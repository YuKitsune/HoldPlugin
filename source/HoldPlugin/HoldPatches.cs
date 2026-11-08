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
    ///     The aircraft leaves the hold now. Remove the position reports that vatSys made during
    ///     the hold, and calculate the ETOs again from the hold point.
    /// </summary>
    static void ResetEstimatesFromHoldPoint(FDP2.FDR fdr, FDP2.FDR.ExtractedRoute.Segment holdSegment)
    {
        // The aircraft did not get to the hold point. The ETOs from vatSys are correct.
        if (holdSegment.ATO == DateTime.MaxValue)
            return;

        RemovePositionReportsAfter(fdr, holdSegment.ATO);

        var radarTrack = fdr.CoupledTrack;
        if (radarTrack is null || radarTrack.GroundSpeed < MinimumGroundSpeed)
        {
            // There is no radar position. Thus the aircraft leaves the hold point now.
            holdSegment.ATONoEvent = DateTime.UtcNow;
        }
        else
        {
            // The aircraft must fly back to the hold point. Remove the ATO, so that vatSys sets
            // it again when the aircraft crosses the hold point. Until then, use a PETO from the
            // radar position. vatSys does not change the ETO of a PETO when it calculates the ETOs.
            var distance = Conversions.CalculateDistance(radarTrack.LatLong, holdSegment.Intersection.LatLong);
            holdSegment.ATONoEvent = DateTime.MaxValue;
            holdSegment.IsPETO = true;
            holdSegment.ETONoEvent = DateTime.UtcNow + TimeSpan.FromHours(distance / radarTrack.GroundSpeed);
        }

        // The seed time is not used. CalculateETOs uses the ATO or the PETO of the hold point.
        fdr.ParsedRoute.CalculateETOs(DateTime.UtcNow, fdr.ParsedRoute.IndexOf(holdSegment));
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
    }

    static bool TryGetHoldPointIndex(FDP2.FDR? fdr, out HoldPatchState holdState, out int holdPointIndex)
    {
        holdPointIndex = -1;
        holdState = null!;

        if (fdr is null || !Holds.TryGetValue(fdr.Callsign, out holdState))
            return false;

        var holdSegment = Plugin.FindHoldWaypoint(fdr, holdState.HoldPoint);
        if (holdSegment is null)
            return false;

        holdPointIndex = fdr.ParsedRoute.IndexOf(holdSegment);
        return holdPointIndex != -1;
    }

    /// <summary>
    ///     Set the ETO of the hold point to the hold exit time. Then calculate the ETOs of the
    ///     segments after the hold point from the hold exit time.
    /// </summary>
    [HarmonyPatch(typeof(FDP2.FDR.ExtractedRoute), nameof(FDP2.FDR.ExtractedRoute.CalculateETOs))]
    static class CalculateETOsPatch
    {
        static void Postfix(FDP2.FDR.ExtractedRoute __instance)
        {
            try
            {
                if (!TryGetHoldPointIndex(__instance.Parent, out var holdState, out var holdPointIndex))
                    return;

                var route = __instance.ToList();
                route[holdPointIndex].ETONoEvent = holdState.ExitTime;

                var eto = holdState.ExitTime;
                for (var i = holdPointIndex + 1; i < route.Count; i++)
                {
                    var segment = route[i];
                    if (!segment.IsPETO)
                        segment.ETONoEvent = eto + segment.EET;

                    eto = segment.ETONoEvent;
                }

                __instance.Refresh();
                __instance.FirePropertyChanged("ETO");
            }
            catch (Exception ex)
            {
                Plugin.AddError(ex);
            }
        }
    }

    /// <summary>
    ///     Do not let the overflown index go past the hold point. This is necessary when the hold
    ///     exit time is in the past, but the aircraft is still in the hold.
    /// </summary>
    [HarmonyPatch(typeof(FDP2.FDR.ExtractedRoute), "SetOverflownIndex")]
    static class SetOverflownIndexPatch
    {
        static void Postfix(FDP2.FDR.ExtractedRoute __instance)
        {
            try
            {
                if (!TryGetHoldPointIndex(__instance.Parent, out _, out var holdPointIndex))
                    return;

                if (__instance.OverflownIndex <= holdPointIndex)
                    return;

                __instance.OverflownIndex = holdPointIndex;
                __instance.FirePropertyChanged("OverflownIndex");
            }
            catch (Exception ex)
            {
                Plugin.AddError(ex);
            }
        }
    }

    /// <summary>
    ///     Do not show a RAM alert for an aircraft in a hold.
    /// </summary>
    [HarmonyPatch(typeof(RDP), "CheckForRAM", typeof(RDP.RadarTrack))]
    static class CheckForRAMPatch
    {
        static bool Prefix(RDP.RadarTrack rt, ref bool __result)
        {
            if (rt.CoupledFDR is null || !Holds.ContainsKey(rt.CoupledFDR.Callsign))
                return true;

            __result = false;
            return false;
        }
    }
}
