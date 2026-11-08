using HarmonyLib;
using vatsys;

namespace HoldPlugin;

/// <summary>
///     Harmony patches that stop vatSys from moving an aircraft along its route while the
///     aircraft is in a hold.
/// </summary>
public static class HoldPatches
{
    // The patches run on vatSys threads. The repositories are thread-safe.
    static HoldRepository _holds = null!;
    static HoldExitRepository _exits = null!;

    public static void Apply(string harmonyId, HoldRepository holdRepository, HoldExitRepository holdExitRepository)
    {
        _holds = holdRepository;
        _exits = holdExitRepository;

        var harmony = new Harmony(harmonyId);
        harmony.PatchAll(typeof(HoldPatches).Assembly);
    }

    // The aircraft crossed the hold point when the hold point, or a segment after it, has an ATO.
    // A segment after the hold point gets an ATO when vatSys finds the aircraft on the route after
    // the hold point.
    static bool HasCrossedHoldPoint(FDP2.FDR.ExtractedRoute route, int holdPointIndex)
    {
        return route.Skip(holdPointIndex).Any(s => s.ATO != DateTime.MaxValue);
    }

    static bool TryGetHoldPointIndex(FDP2.FDR? fdr, out HoldItem hold, out int holdPointIndex)
    {
        holdPointIndex = -1;
        hold = null!;

        if (fdr is null)
            return false;

        var foundHold = _holds.Find(fdr.Callsign);
        if (foundHold is null)
            return false;

        hold = foundHold;
        holdPointIndex = FindHoldPointIndex(fdr, hold.HoldPoint);
        return holdPointIndex != -1;
    }

    static bool TryGetExitHoldPointIndex(FDP2.FDR? fdr, out int holdPointIndex)
    {
        holdPointIndex = -1;

        if (fdr is null || !_exits.TryGet(fdr.Callsign, out var holdPoint))
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

                if (TryGetHoldPointIndex(fdr, out var hold, out var holdPointIndex))
                {
                    SetETOsFromHoldPoint(__instance, holdPointIndex, hold.HoldExitTime);
                    return;
                }

                if (fdr is null || !_exits.Contains(fdr.Callsign))
                    return;

                // The ETOs from vatSys are correct when the aircraft crossed the hold point, or
                // when the hold point is not on the route anymore.
                if (!TryGetExitHoldPointIndex(fdr, out var exitHoldPointIndex) ||
                    HasCrossedHoldPoint(__instance, exitHoldPointIndex))
                {
                    _exits.Remove(fdr.Callsign);
                    return;
                }

                if (Plugin.TryEstimateFromRadar(fdr, __instance[exitHoldPointIndex], out var eto))
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
            if (_holds.Find(callsign) is null && !_exits.Contains(callsign))
                return true;

            __result = false;
            return false;
        }
    }
}
