using System.Reflection;
using HarmonyLib;
using vatsys;

namespace HoldPlugin;

/// <summary>
///     Harmony patches that stop vatSys from moving an aircraft along its route while the
///     aircraft is in a hold.
/// </summary>
public static class HoldPatches
{
    // The patches run on vatSys threads. The hold repository is thread-safe.
    static HoldRepository _holds = null!;
    static IErrorReporter _errorReporter = null!;

    public static void Apply(string harmonyId, HoldRepository holdRepository, IErrorReporter errorReporter)
    {
        _holds = holdRepository;
        _errorReporter = errorReporter;

        var harmony = new Harmony(harmonyId);
        harmony.PatchAll(typeof(HoldPatches).Assembly);
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

    static int FindHoldPointIndex(FDP2.FDR fdr, string holdPoint)
    {
        var holdSegment = fdr.FindHoldWaypoint(holdPoint);
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
    ///     In a hold, set the ETO of the hold point to the hold exit time. Then calculate the ETOs
    ///     of the segments after the hold point from it.
    /// </summary>
    [HarmonyPatch(typeof(FDP2.FDR.ExtractedRoute), nameof(FDP2.FDR.ExtractedRoute.CalculateETOs))]
    static class CalculateETOsPatch
    {
        static void Postfix(FDP2.FDR.ExtractedRoute __instance)
        {
            try
            {
                var fdr = __instance.Parent;

                if (TryGetHoldPointIndex(__instance.Parent, out var hold, out var holdPointIndex))
                    SetETOsFromHoldPoint(__instance, holdPointIndex, hold.HoldExitTime);
            }
            catch (Exception ex)
            {
                _errorReporter.ReportError(ex);
            }
        }
    }

    /// <summary>
    ///     In a hold, do not mark the hold point as overflown. This is necessary when the hold
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

                var maximumOverflownIndex = holdPointIndex - 1;
                if (__instance.OverflownIndex <= maximumOverflownIndex)
                    return;

                __instance.OverflownIndex = maximumOverflownIndex;
                __instance.FirePropertyChanged("OverflownIndex");
            }
            catch (Exception ex)
            {
                _errorReporter.ReportError(ex);
            }
        }
    }

    /// <summary>
    ///     In a hold, do not let vatSys set ATOs for the hold point, or for the segments after it.
    ///     vatSys can set ATOs for the segments before the hold point. When vatSys finds that the
    ///     aircraft crossed the hold point, record the hold entry time.
    /// </summary>
    [HarmonyPatch(typeof(RDP), "AutomaticPositionReport")]
    static class AutomaticPositionReportPatch
    {
        static readonly MethodInfo CheckForRAMMethod = AccessTools.Method(
            typeof(RDP),
            "CheckForRAM",
            [
                typeof(RDP.RadarTrack),
                typeof(bool),
                typeof(List<FDP2.FDR.ExtractedRoute.Segment>),
                typeof(int),
                typeof(int).MakeByRefType()
            ]);

        static bool Prefix(RDP.RadarTrack rt, FDP2.FDR.ExtractedRoute.Segment? segment)
        {
            try
            {
                var fdr = rt.CoupledFDR;
                if (fdr is null || !TryGetHoldPointIndex(fdr, out _, out var holdPointIndex))
                    return true;

                // vatSys found that the aircraft crossed a waypoint.
                if (segment is not null)
                {
                    var segmentIndex = fdr.ParsedRoute.IndexOf(segment);
                    if (segmentIndex < holdPointIndex)
                        return true;

                    if (segmentIndex == holdPointIndex)
                        _holds.Update(fdr.Callsign, h => h with { HoldEntryTime = h.HoldEntryTime ?? DateTime.UtcNow });

                    return false;
                }

                // vatSys adds an APR segment before the route index that it finds.
                return FindPositionReportIndex(rt, fdr) <= holdPointIndex;
            }
            catch (Exception ex)
            {
                _errorReporter.ReportError(ex);
                return true;
            }
        }

        // Find the route index that vatSys adds the APR segment before, the same as vatSys does.
        // Returns -1 if vatSys does not add an APR segment.
        static int FindPositionReportIndex(RDP.RadarTrack rt, FDP2.FDR fdr)
        {
            var route = fdr.ParsedRoute.ToList();
            object[] arguments = [rt, false, route, fdr.ParsedRoute.OverflownIndex, -1];

            var isOffRoute = (bool)CheckForRAMMethod.Invoke(null, arguments);
            if (isOffRoute)
                return -1;

            var index = (int)arguments[4];
            if (index < 1)
                return index;

            while (index + 1 < route.Count && route[index].Distance == 0.0)
                index++;

            return index;
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
            if (rt.CoupledFDR is null)
                return true;

            if (_holds.Find(rt.CoupledFDR.Callsign) is null)
                return true;

            __result = false;
            return false;
        }
    }
}
