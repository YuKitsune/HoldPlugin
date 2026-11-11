using System.Collections;
using System.Reflection;
using CommunityToolkit.Mvvm.Messaging;
using HarmonyLib;
using HoldPlugin.Contracts;
using vatsys;
using vatSysToolbox;

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

    /// <summary>
    ///     Add the delays (DLE) in the remarks and the hold to the ETOs. A delay does not change
    ///     the ETO of its waypoint, thus vatSys moves the aircraft past the waypoint as usual. The
    ///     ETOs after the waypoint start at the ETO plus the delay. In a hold, set the ETO of the
    ///     hold point to the hold exit time. When the hold has a delay from the remarks, the hold
    ///     exit time is the arrival time plus the delay.
    /// </summary>
    [HarmonyPatch(typeof(FDP2.FDR.ExtractedRoute), nameof(FDP2.FDR.ExtractedRoute.CalculateETOs))]
    static class CalculateETOsPatch
    {
        static void Postfix(FDP2.FDR.ExtractedRoute __instance, int indexPosition)
        {
            try
            {
                var fdr = __instance.Parent;
                var delays = fdr.GetActiveDelays();
                var hasHold = TryGetHoldPointIndex(fdr, out var hold, out var holdPointIndex);
                if (!hasHold && delays.Count == 0)
                    return;

                // vatSys calculates the ETOs from indexPosition. In a hold, the hold point can be before it.
                var segments = __instance.ToList();
                var startIndex = Math.Max(0, Math.Min(indexPosition, segments.Count - 1));
                if (hasHold)
                    startIndex = Math.Min(startIndex, holdPointIndex);

                // The time that the aircraft leaves the previous segment.
                var departureTime = DateTime.MaxValue;
                for (var i = startIndex; i < segments.Count; i++)
                {
                    var segment = segments[i];
                    if (i > startIndex && !segment.IsPETO && segment.ATO == DateTime.MaxValue)
                    {
                        if (departureTime == DateTime.MaxValue)
                            break;

                        segment.ETONoEvent = departureTime + segment.EET;
                    }

                    if (hasHold && i == holdPointIndex)
                    {
                        departureTime = GetHoldExitTime(fdr, hold, segment);
                        segment.ETONoEvent = departureTime;
                        continue;
                    }

                    var arrivalTime = segment.ATO != DateTime.MaxValue ? segment.ATO : segment.ETO;
                    departureTime = arrivalTime != DateTime.MaxValue && delays.TryGetValue(i, out var delay)
                        ? arrivalTime + delay
                        : arrivalTime;
                }

                __instance.Refresh();
                __instance.FirePropertyChanged("ETO");
            }
            catch (Exception ex)
            {
                _errorReporter.ReportError(ex);
            }
        }

        static DateTime GetHoldExitTime(FDP2.FDR fdr, HoldItem hold, FDP2.FDR.ExtractedRoute.Segment holdSegment)
        {
            if (!fdr.TryGetHoldArrivalTime(holdSegment, hold.HoldEntryTime, out var arrivalTime))
                return hold.HoldExitTime;

            var holdExitTime = arrivalTime + hold.Delay;
            if (holdExitTime != hold.HoldExitTime)
                _holds.Update(fdr.Callsign, h => h with { HoldExitTime = holdExitTime });

            return holdExitTime;
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
    ///     aircraft crossed the hold point, record the hold entry time. The hold continues until
    ///     the controller cancels it, also after the exit time.
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
                    {
                        _holds.Update(fdr.Callsign, h => h with { HoldEntryTime = h.HoldEntryTime ?? DateTime.UtcNow });

                        // Calculate the ETOs again. With a delay, the hold exit time is now the
                        // hold entry time plus the delay.
                        FDP2.Process(fdr);
                    }

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
    ///     Show the waypoint of a delay (DLE) or a hold two times on the strip. The first time
    ///     shows the arrival time (ETO or ATO). The second time shows the departure time: the
    ///     arrival time plus the delay, or the hold exit time. When the aircraft is in the hold,
    ///     the strip starts at the hold point.
    /// </summary>
    [HarmonyPatch]
    static class PaintStripItemPatch
    {
        // Identifies the arrival copy of the waypoint on the strip.
        public static readonly object ArrivalSegmentTag = new();

        static MethodBase TargetMethod() => AccessTools.Method("vatsys.StripPanelDX:PaintStripItem");

        // The strip route contains only the waypoints, starting at the last overflown waypoint.
        static void Prefix(FDP2.FDR fdr, ref IList<FDP2.FDR.ExtractedRoute.Segment> route)
        {
            try
            {
                var stripRoute = route.ToList();
                var hold = _holds.Find(fdr.Callsign);
                var holdSegment = hold is not null ? fdr.FindHoldWaypoint(hold.HoldPoint) : null;

                var isChanged = AddDelays(fdr, stripRoute, holdSegment);
                if (hold is not null && holdSegment is not null)
                    isChanged |= AddHold(fdr, stripRoute, hold, holdSegment);

                if (isChanged)
                    route = stripRoute;
            }
            catch (Exception ex)
            {
                _errorReporter.ReportError(ex);
            }
        }

        // Returns true if the strip route changed. At the hold point, the hold wins over the delay.
        static bool AddDelays(
            FDP2.FDR fdr,
            List<FDP2.FDR.ExtractedRoute.Segment> stripRoute,
            FDP2.FDR.ExtractedRoute.Segment? holdSegment)
        {
            var isChanged = false;
            foreach (var activeDelay in fdr.GetActiveDelays())
            {
                var segment = fdr.ParsedRoute[activeDelay.Key];
                if (segment == holdSegment)
                    continue;

                var stripIndex = stripRoute.IndexOf(segment);
                if (stripIndex == -1)
                    continue;

                var arrivalTime = segment.ATO != DateTime.MaxValue ? segment.ATO : segment.ETO;
                if (arrivalTime == DateTime.MaxValue)
                    continue;

                var arrivalSegment = CreateStripSegment(fdr, segment, segment.ETO, segment.ATO);
                arrivalSegment.Tag = ArrivalSegmentTag;

                stripRoute[stripIndex] = CreateStripSegment(fdr, segment, arrivalTime + activeDelay.Value, DateTime.MaxValue);
                stripRoute.Insert(stripIndex, arrivalSegment);
                isChanged = true;
            }

            return isChanged;
        }

        // Returns true if the strip route changed.
        static bool AddHold(
            FDP2.FDR fdr,
            List<FDP2.FDR.ExtractedRoute.Segment> stripRoute,
            HoldItem hold,
            FDP2.FDR.ExtractedRoute.Segment holdSegment)
        {
            var holdPointIndex = stripRoute.IndexOf(holdSegment);
            if (holdPointIndex == -1)
                return false;

            if (!fdr.TryGetHoldArrivalTime(holdSegment, hold.HoldEntryTime, out var arrivalTime))
                return false;

            // After the aircraft gets to the hold point, the arrival time is an ATO.
            var arrivalSegment = hold.HoldEntryTime is null
                ? CreateStripSegment(fdr, holdSegment, arrivalTime, DateTime.MaxValue)
                : CreateStripSegment(fdr, holdSegment, DateTime.MaxValue, arrivalTime);
            arrivalSegment.Tag = ArrivalSegmentTag;

            // The hold point has an ATO when the hold started after the aircraft crossed the
            // hold point. The strip shows the ATO before the ETO, thus the exit time must go on
            // a copy of the hold point.
            var exitSegment = holdSegment.ATO == DateTime.MaxValue
                ? holdSegment
                : CreateStripSegment(fdr, holdSegment, hold.HoldExitTime, DateTime.MaxValue);

            stripRoute[holdPointIndex] = exitSegment;
            stripRoute.Insert(holdPointIndex, arrivalSegment);

            // vatSys starts the strip at the last overflown waypoint. SetOverflownIndexPatch
            // does not let the hold point become overflown, thus remove the waypoints before it.
            if (hold.HoldEntryTime is not null)
                stripRoute.RemoveRange(0, holdPointIndex);

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
    ///     For a delay (DLE) or a hold, add "A" to the arrival time of the waypoint on the strip,
    ///     and "D" to the departure time. PaintStripItemPatch puts the arrival copy of the
    ///     waypoint in the route.
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
    ///     Open the delay popup when the user right-clicks the ETO of a waypoint with a delay (DLE)
    ///     in the remarks. Do nothing for the other waypoints. Do not open it when the controller
    ///     does not have permission to change the flight plan.
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

                if (!fdr.HavePermission)
                    return;

                var delay = GetCurrentDelay(fdr, segment);
                if (delay is null)
                    return;

                WeakReferenceMessenger.Default.Send(new OpenDelayPopupCommand(fdr.Callsign, segment.GetFlightPlanName(), delay));
            }
            catch (Exception ex)
            {
                _errorReporter.ReportError(ex);
            }
        }

        // The popup changes the delay from the pilot (DLE) at the waypoint in the remarks.
        static TimeSpan? GetCurrentDelay(FDP2.FDR fdr, FDP2.FDR.ExtractedRoute.Segment segment)
        {
            var waypointName = segment.GetFlightPlanName();
            return DelayRemarks.Parse(fdr)
                .FirstOrDefault(d => d.Type == DelayType.Delay && d.WaypointName == waypointName)
                ?.Delay;
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
    ///     Do not show a RAM alert for an aircraft in a hold, or for an aircraft with the hold
    ///     point as the next waypoint.
    /// </summary>
    [HarmonyPatch(typeof(RDP), "CheckForRAM", typeof(RDP.RadarTrack))]
    static class CheckForRAMPatch
    {
        static bool Prefix(RDP.RadarTrack rt, ref bool __result)
        {
            var fdr = rt.CoupledFDR;
            if (fdr is null)
                return true;

            var hold = _holds.Find(fdr.Callsign);
            if (hold is null)
                return true;

            if (hold.HoldEntryTime is null && fdr.GetNextWaypoint() != fdr.FindHoldWaypoint(hold.HoldPoint))
                return true;

            __result = false;
            return false;
        }
    }
}
