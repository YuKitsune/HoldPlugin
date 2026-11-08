using System.Collections.Concurrent;
using HarmonyLib;
using vatsys;

namespace HoldPlugin;

/// <summary>
///     The hold data that the Harmony patches use. The patches run on vatSys threads, thus this
///     data is kept in a thread-safe dictionary.
/// </summary>
public record HoldPatchState(string HoldPoint, DateTime ExitTime, DateTime DefaultExitTime);

/// <summary>
///     Harmony patches that stop vatSys from moving an aircraft along its route while the
///     aircraft is in a hold.
/// </summary>
public static class HoldPatches
{
    static readonly ConcurrentDictionary<string, HoldPatchState> Holds = new();

    // The aircraft that left a hold, but did not cross the hold point again (callsign to hold
    // point name). For these aircraft, the ETO of the hold point is calculated from the radar
    // position.
    static readonly ConcurrentDictionary<string, string> Exits = new();

    // vatSys does not calculate an estimate from radar below this ground speed (knots).
    const double MinimumGroundSpeed = 10;

    public static void Apply(string harmonyId)
    {
        var harmony = new Harmony(harmonyId);
        harmony.PatchAll(typeof(HoldPatches).Assembly);
    }

    public static bool TryGet(string callsign, out HoldPatchState holdState)
    {
        return Holds.TryGetValue(callsign, out holdState);
    }

    /// <summary>
    ///     Set the hold data for the FDR. When the hold data changes, process the FDR again, so
    ///     that vatSys calculates the ETOs again.
    /// </summary>
    public static void Set(FDP2.FDR fdr, HoldPatchState holdState)
    {
        if (Holds.TryGetValue(fdr.Callsign, out var currentState) && currentState == holdState)
            return;

        Holds[fdr.Callsign] = holdState;
        Exits.TryRemove(fdr.Callsign, out _);
        FDP2.Process(fdr);
    }

    /// <summary>
    ///     Remove the hold data for the FDR. Then request an APR, so that vatSys calculates the
    ///     ETOs again from the current position of the aircraft.
    /// </summary>
    public static void Remove(FDP2.FDR fdr)
    {
        // Find the hold point before the hold data is removed.
        TryGetHoldPointIndex(fdr, out _, out var holdPointIndex);

        if (!Holds.TryRemove(fdr.Callsign, out _))
            return;

        if (holdPointIndex != -1)
            ResetEstimatesFromHoldPoint(fdr, fdr.ParsedRoute[holdPointIndex]);

        // RequestAPR does nothing when the FDR has no coupled track.
        if (fdr.CoupledTrack is null)
            FDP2.Process(fdr);
        else
            RDP.RequestAPR(fdr);
    }

    /// <summary>
    ///     Process the FDR again while the aircraft flies back to the hold point, so that the ETO
    ///     of the hold point follows the radar position.
    /// </summary>
    public static void UpdateExitEstimate(FDP2.FDR fdr)
    {
        if (!Exits.ContainsKey(fdr.Callsign))
            return;

        FDP2.Process(fdr);
    }

    /// <summary>
    ///     The aircraft leaves the hold now. Remove the position reports that vatSys made during
    ///     the hold, and calculate the ETOs again from the hold point.
    /// </summary>
    static void ResetEstimatesFromHoldPoint(FDP2.FDR fdr, FDP2.FDR.ExtractedRoute.Segment holdSegment)
    {
        // The aircraft did not get to the hold point. The ETOs from vatSys are correct.
        if (holdSegment.ATO == DateTime.MaxValue)
            return;

        RemovePositionReportsAfter(fdr, holdSegment.ATO);

        if (!TryEstimateFromRadar(fdr, holdSegment, out _))
        {
            // There is no radar position. Thus the aircraft leaves the hold point now.
            holdSegment.ATONoEvent = DateTime.UtcNow;
        }
        else
        {
            // The aircraft must fly back to the hold point. Remove the ATO, so that vatSys sets
            // it again when the aircraft crosses the hold point. Until then, the ETO of the hold
            // point is calculated from the radar position.
            holdSegment.ATONoEvent = DateTime.MaxValue;
            Exits[fdr.Callsign] = holdSegment.Intersection.Name;
        }

        fdr.ParsedRoute.CalculateETOs(DateTime.UtcNow, fdr.ParsedRoute.IndexOf(holdSegment));
    }

    // Calculate the ETO of the hold point from the radar position and the ground speed, the same
    // as vatSys does for an APR.
    static bool TryEstimateFromRadar(FDP2.FDR fdr, FDP2.FDR.ExtractedRoute.Segment holdSegment, out DateTime eto)
    {
        eto = DateTime.MaxValue;

        var radarTrack = fdr.CoupledTrack;
        if (radarTrack is null || radarTrack.GroundSpeed < MinimumGroundSpeed)
            return false;

        var distance = Conversions.CalculateDistance(radarTrack.LatLong, holdSegment.Intersection.LatLong);
        eto = DateTime.UtcNow + TimeSpan.FromHours(distance / radarTrack.GroundSpeed);
        return true;
    }

    // The aircraft crossed the hold point when the hold point, or a segment after it, has an ATO.
    // A segment after the hold point gets an ATO when vatSys finds the aircraft on the route after
    // the hold point.
    static bool HasCrossedHoldPoint(FDP2.FDR.ExtractedRoute route, int holdPointIndex)
    {
        return route.Skip(holdPointIndex).Any(s => s.ATO != DateTime.MaxValue);
    }

    // Remove the APR segments with an ATO after the given time. Add the EET and the distance of
    // each removed segment to the next segment, the same as vatSys does.
    static void RemovePositionReportsAfter(FDP2.FDR fdr, DateTime time)
    {
        foreach (var segment in fdr.ParsedRoute.ToList())
        {
            if (segment.Type != FDP2.FDR.ExtractedRoute.Segment.SegmentTypes.APRUPR)
                continue;

            if (segment.ATO == DateTime.MaxValue || segment.ATO <= time)
                continue;

            var index = fdr.ParsedRoute.IndexOf(segment);
            if (index < 1 || index > fdr.ParsedRoute.Count - 2)
                continue;

            var nextSegment = fdr.ParsedRoute[index + 1];
            nextSegment.EET += segment.EET;
            nextSegment.Distance += segment.Distance;
            fdr.ParsedRoute.Remove(segment);
        }
    }

    /// <summary>
    ///     Remove the hold data for the FDRs that vatSys does not have anymore.
    /// </summary>
    public static void RemoveAllExcept(IReadOnlyCollection<string> callsigns)
    {
        foreach (var callsign in Holds.Keys.ToArray())
        {
            if (!callsigns.Contains(callsign))
                Holds.TryRemove(callsign, out _);
        }

        foreach (var callsign in Exits.Keys.ToArray())
        {
            if (!callsigns.Contains(callsign))
                Exits.TryRemove(callsign, out _);
        }
    }

    static bool TryGetHoldPointIndex(FDP2.FDR? fdr, out HoldPatchState holdState, out int holdPointIndex)
    {
        holdPointIndex = -1;
        holdState = null!;

        if (fdr is null || !Holds.TryGetValue(fdr.Callsign, out holdState))
            return false;

        holdPointIndex = FindHoldPointIndex(fdr, holdState.HoldPoint);
        return holdPointIndex != -1;
    }

    static bool TryGetExitHoldPointIndex(FDP2.FDR? fdr, out int holdPointIndex)
    {
        holdPointIndex = -1;

        if (fdr is null || !Exits.TryGetValue(fdr.Callsign, out var holdPoint))
            return false;

        holdPointIndex = FindHoldPointIndex(fdr, holdPoint);
        return holdPointIndex != -1;
    }

    static int FindHoldPointIndex(FDP2.FDR fdr, string holdPoint)
    {
        var holdSegment = Plugin.FindHoldWaypoint(fdr, holdPoint);
        if (holdSegment is null)
            return -1;

        return fdr.ParsedRoute.IndexOf(holdSegment);
    }

    // Set the ETO of the hold point, then calculate the ETOs of the segments after the hold point
    // from it.
    static void SetETOsFromHoldPoint(FDP2.FDR.ExtractedRoute route, int holdPointIndex, DateTime holdPointEto)
    {
        var segments = route.ToList();
        segments[holdPointIndex].ETONoEvent = holdPointEto;

        var eto = holdPointEto;
        for (var i = holdPointIndex + 1; i < segments.Count; i++)
        {
            var segment = segments[i];
            if (!segment.IsPETO)
                segment.ETONoEvent = eto + segment.EET;

            eto = segment.ETONoEvent;
        }

        route.Refresh();
        route.FirePropertyChanged("ETO");
    }

    /// <summary>
    ///     In a hold, set the ETO of the hold point to the hold exit time. After a hold, until the
    ///     aircraft crosses the hold point, set the ETO of the hold point from the radar position.
    ///     Then calculate the ETOs of the segments after the hold point from it.
    /// </summary>
    [HarmonyPatch(typeof(FDP2.FDR.ExtractedRoute), nameof(FDP2.FDR.ExtractedRoute.CalculateETOs))]
    static class CalculateETOsPatch
    {
        static void Postfix(FDP2.FDR.ExtractedRoute __instance)
        {
            try
            {
                var fdr = __instance.Parent;

                if (TryGetHoldPointIndex(fdr, out var holdState, out var holdPointIndex))
                {
                    SetETOsFromHoldPoint(__instance, holdPointIndex, holdState.ExitTime);
                    return;
                }

                if (fdr is null || !Exits.ContainsKey(fdr.Callsign))
                    return;

                // The ETOs from vatSys are correct when the aircraft crossed the hold point, or
                // when the hold point is not on the route anymore.
                if (!TryGetExitHoldPointIndex(fdr, out var exitHoldPointIndex) ||
                    HasCrossedHoldPoint(__instance, exitHoldPointIndex))
                {
                    Exits.TryRemove(fdr.Callsign, out _);
                    return;
                }

                if (TryEstimateFromRadar(fdr, __instance[exitHoldPointIndex], out var eto))
                    SetETOsFromHoldPoint(__instance, exitHoldPointIndex, eto);
            }
            catch (Exception ex)
            {
                Plugin.AddError(ex);
            }
        }
    }

    /// <summary>
    ///     In a hold, do not let the overflown index go past the hold point. This is necessary
    ///     when the hold exit time is in the past, but the aircraft is still in the hold.
    ///     After a hold, do not mark the hold point as overflown until the aircraft crosses it.
    /// </summary>
    [HarmonyPatch(typeof(FDP2.FDR.ExtractedRoute), "SetOverflownIndex")]
    static class SetOverflownIndexPatch
    {
        static void Postfix(FDP2.FDR.ExtractedRoute __instance)
        {
            try
            {
                int maximumOverflownIndex;
                if (TryGetHoldPointIndex(__instance.Parent, out _, out var holdPointIndex))
                    maximumOverflownIndex = holdPointIndex;
                else if (TryGetExitHoldPointIndex(__instance.Parent, out var exitHoldPointIndex) &&
                         !HasCrossedHoldPoint(__instance, exitHoldPointIndex))
                    maximumOverflownIndex = exitHoldPointIndex - 1;
                else
                    return;

                if (__instance.OverflownIndex <= maximumOverflownIndex)
                    return;

                __instance.OverflownIndex = maximumOverflownIndex;
                __instance.FirePropertyChanged("OverflownIndex");
            }
            catch (Exception ex)
            {
                Plugin.AddError(ex);
            }
        }
    }

    /// <summary>
    ///     Do not show a RAM alert for an aircraft in a hold, or for an aircraft that flies back to
    ///     the hold point after a hold.
    /// </summary>
    [HarmonyPatch(typeof(RDP), "CheckForRAM", typeof(RDP.RadarTrack))]
    static class CheckForRAMPatch
    {
        static bool Prefix(RDP.RadarTrack rt, ref bool __result)
        {
            if (rt.CoupledFDR is null)
                return true;

            var callsign = rt.CoupledFDR.Callsign;
            if (!Holds.ContainsKey(callsign) && !Exits.ContainsKey(callsign))
                return true;

            __result = false;
            return false;
        }
    }
}
