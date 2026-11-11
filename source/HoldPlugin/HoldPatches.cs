using System.Collections;
using System.Reflection;
using CommunityToolkit.Mvvm.Messaging;
using HarmonyLib;
using HoldPlugin.Contracts;
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
    ///     exit time is in the past, but the aircraft is still in the hold. The graphical route
    ///     starts at the aircraft position and goes to the segment after the overflown index, thus
    ///     it shows the hold point.
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
    ///     In a hold, show the hold point two times on the strip. The first time shows the arrival
    ///     time (ETO or ATO). The second time shows the hold exit time. When the aircraft is in the
    ///     hold, the strip starts at the hold point.
    /// </summary>
    [HarmonyPatch]
    static class PaintStripItemPatch
    {
        // Identifies the arrival copy of the hold point on the strip.
        public static readonly object ArrivalSegmentTag = new();

        static MethodBase TargetMethod() => AccessTools.Method("vatsys.StripPanelDX:PaintStripItem");

        // The strip route contains only the waypoints, starting at the last overflown waypoint.
        static void Prefix(FDP2.FDR fdr, ref IList<FDP2.FDR.ExtractedRoute.Segment> route)
        {
            try
            {
                var hold = _holds.Find(fdr.Callsign);
                if (hold is null)
                    return;

                var holdSegment = fdr.FindHoldWaypoint(hold.HoldPoint);
                if (holdSegment is null)
                    return;

                var holdPointIndex = route.IndexOf(holdSegment);
                if (holdPointIndex == -1)
                    return;

                if (!TryGetArrivalTime(fdr, hold, holdSegment, out var arrivalEto, out var arrivalAto))
                    return;

                var arrivalSegment = CreateStripSegment(fdr, holdSegment, arrivalEto, arrivalAto);
                arrivalSegment.Tag = ArrivalSegmentTag;

                // The hold point has an ATO when the hold started after the aircraft crossed the
                // hold point. The strip shows the ATO before the ETO, thus the exit time must go on
                // a copy of the hold point.
                var exitSegment = holdSegment.ATO == DateTime.MaxValue
                    ? holdSegment
                    : CreateStripSegment(fdr, holdSegment, hold.HoldExitTime, DateTime.MaxValue);

                var stripRoute = route.ToList();
                stripRoute[holdPointIndex] = exitSegment;
                stripRoute.Insert(holdPointIndex, arrivalSegment);

                // vatSys starts the strip at the last overflown waypoint. SetOverflownIndexPatch
                // does not let the hold point become overflown, thus remove the waypoints before it.
                if (hold.HoldEntryTime is not null)
                    stripRoute.RemoveRange(0, holdPointIndex);

                route = stripRoute;
            }
            catch (Exception ex)
            {
                _errorReporter.ReportError(ex);
            }
        }

        // Before the aircraft gets to the hold point, the arrival ETO is the time at the previous
        // segment plus the EET to the hold point. CalculateETOsPatch does not change the segments
        // before the hold point.
        public static bool TryGetArrivalTime(
            FDP2.FDR fdr,
            HoldItem hold,
            FDP2.FDR.ExtractedRoute.Segment holdSegment,
            out DateTime eto,
            out DateTime ato)
        {
            eto = DateTime.MaxValue;
            ato = DateTime.MaxValue;

            if (hold.HoldEntryTime is not null)
            {
                ato = hold.HoldEntryTime.Value;
                return true;
            }

            var holdPointIndex = fdr.ParsedRoute.IndexOf(holdSegment);
            if (holdPointIndex < 1)
                return false;

            var previousSegment = fdr.ParsedRoute[holdPointIndex - 1];
            var previousTime = previousSegment.ATO != DateTime.MaxValue ? previousSegment.ATO : previousSegment.ETO;
            if (previousTime == DateTime.MaxValue)
                return false;

            eto = previousTime + holdSegment.EET;
            return true;
        }

        // Make a segment for the strip only. It is not in the route of the FDR.
        static FDP2.FDR.ExtractedRoute.Segment CreateStripSegment(
            FDP2.FDR fdr,
            FDP2.FDR.ExtractedRoute.Segment holdSegment,
            DateTime eto,
            DateTime ato)
        {
            return new FDP2.FDR.ExtractedRoute.Segment(fdr.ParsedRoute)
            {
                Type = FDP2.FDR.ExtractedRoute.Segment.SegmentTypes.WAYPOINT,
                Intersection = holdSegment.Intersection,
                EET = holdSegment.EET,
                Distance = holdSegment.Distance,
                PCLNoEvent = holdSegment.PCL,
                ETONoEvent = eto,
                ATONoEvent = ato
            };
        }
    }

    /// <summary>
    ///     In a hold, add "A" to the arrival time of the hold point on the strip, and "D" to the
    ///     exit time. PaintStripItemPatch puts the arrival copy of the hold point in the route.
    /// </summary>
    [HarmonyPatch]
    static class GetItemTextPatch
    {
        static MethodBase TargetMethod() => AccessTools.Method("vatsys.StripPanelDX:GetItemText");

        static void Postfix(StripItem item, IList<FDP2.FDR.ExtractedRoute.Segment> rte, ref string __result)
        {
            try
            {
                if (item.Type != StripItemType.ETOP || !item.PointIndexSpecified || item.PointIndex >= rte.Count)
                    return;

                if (rte[item.PointIndex].Tag == PaintStripItemPatch.ArrivalSegmentTag)
                {
                    __result += " A";
                    return;
                }

                if (item.PointIndex > 0 && rte[item.PointIndex - 1].Tag == PaintStripItemPatch.ArrivalSegmentTag)
                    __result += " D";
            }
            catch (Exception ex)
            {
                _errorReporter.ReportError(ex);
            }
        }
    }

    /// <summary>
    ///     Open the delay popup when the user right-clicks the ETO of a waypoint on the strip.
    /// </summary>
    [HarmonyPatch]
    static class StripMouseUpPatch
    {
        static readonly FieldInfo ClickspotsField = AccessTools.Field("vatsys.StripPanelDX:clickspots");

        static readonly Type ClickspotType = AccessTools.TypeByName("vatsys.Clickspot");
        static readonly FieldInfo AreaField = AccessTools.Field(ClickspotType, "Area");
        static readonly FieldInfo TypeMouseLeftField = AccessTools.Field(ClickspotType, "TypeMouseLeft");
        static readonly FieldInfo IdField = AccessTools.Field(ClickspotType, "Id");
        static readonly FieldInfo ValueField = AccessTools.Field(ClickspotType, "Value");

        static readonly MethodInfo AreaContainsMethod = AccessTools.Method(AreaField.FieldType, "Contains", [typeof(float), typeof(float)]);
        static readonly PropertyInfo SourceFdrProperty = AccessTools.Property("vatsys.Strip:SourceFDR");

        static MethodBase TargetMethod() => AccessTools.Method("vatsys.StripPanelDX:OnMouseUp");

        static void Prefix(object __instance, System.Windows.Forms.MouseEventArgs e)
        {
            try
            {
                if (e.Button != System.Windows.Forms.MouseButtons.Right)
                    return;

                var clickspot = FindClickspot(__instance, e.X, e.Y);
                if (clickspot is null)
                    return;

                // The strip uses Strip_ETO only for the ETO items.
                if ((MMI.ClickspotTypes)TypeMouseLeftField.GetValue(clickspot) != MMI.ClickspotTypes.Strip_ETO)
                    return;

                if (ValueField.GetValue(clickspot) is not FDP2.FDR.ExtractedRoute.Segment segment)
                    return;

                if (SourceFdrProperty.GetValue(IdField.GetValue(clickspot)) is not FDP2.FDR fdr)
                    return;

                if (!TryGetArrivalTimeAndDelay(fdr, segment, out var arrivalTime, out var delay))
                    return;

                WeakReferenceMessenger.Default.Send(new OpenDelayPopupCommand(fdr.Callsign, segment.Intersection.Name, arrivalTime, delay));
            }
            catch (Exception ex)
            {
                _errorReporter.ReportError(ex);
            }
        }

        // On the hold entry point and the hold exit point, the arrival time is the arrival time at
        // the hold point, and the delay is the current hold duration. On other waypoints, there is
        // no delay.
        static bool TryGetArrivalTimeAndDelay(
            FDP2.FDR fdr,
            FDP2.FDR.ExtractedRoute.Segment segment,
            out DateTime arrivalTime,
            out TimeSpan? delay)
        {
            delay = null;

            var hold = _holds.Find(fdr.Callsign);
            if (hold is null || segment.Intersection.Name != hold.HoldPoint)
            {
                arrivalTime = segment.ATO != DateTime.MaxValue ? segment.ATO : segment.ETO;
                return arrivalTime != DateTime.MaxValue;
            }

            arrivalTime = DateTime.MaxValue;

            var holdSegment = fdr.FindHoldWaypoint(hold.HoldPoint);
            if (holdSegment is null)
                return false;

            if (!PaintStripItemPatch.TryGetArrivalTime(fdr, hold, holdSegment, out var arrivalEto, out var arrivalAto))
                return false;

            arrivalTime = arrivalAto != DateTime.MaxValue ? arrivalAto : arrivalEto;
            delay = hold.HoldExitTime - arrivalTime;
            return true;
        }

        // Find the clickspot at the mouse position, the same as vatSys does.
        static object? FindClickspot(object stripPanel, int x, int y)
        {
            var clickspots = (IEnumerable)ClickspotsField.GetValue(stripPanel);
            foreach (var clickspot in clickspots)
            {
                var area = AreaField.GetValue(clickspot);
                if ((bool)AreaContainsMethod.Invoke(area, [(float)x, (float)y]))
                    return clickspot;
            }

            return null;
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
