using vatsys;

namespace HoldPlugin;

public static class FdrExtensionMethods
{
    // The name of the waypoint in the flight plan route, and in the delays in the remarks. For a
    // lat/long point, this is e.g. 3330S15100E, but the strip shows 33 15+.
    public static string GetFlightPlanName(this FDP2.FDR.ExtractedRoute.Segment segment) =>
        Conversions.ConvertWaypointNameToFlightPlan(segment);

    // In the hold, the arrival time is the hold entry time. Before the aircraft gets to the hold
    // point, the arrival time is the time at the previous segment plus the EET to the hold point.
    // CalculateETOsPatch does not change the segments before the hold point.
    public static bool TryGetHoldArrivalTime(
        this FDP2.FDR fdr,
        FDP2.FDR.ExtractedRoute.Segment holdSegment,
        DateTime? holdEntryTime,
        out DateTime arrivalTime)
    {
        arrivalTime = DateTime.MaxValue;

        if (holdEntryTime is not null)
        {
            arrivalTime = holdEntryTime.Value;
            return true;
        }

        var holdPointIndex = fdr.ParsedRoute.IndexOf(holdSegment);
        if (holdPointIndex < 1)
            return false;

        var previousSegment = fdr.ParsedRoute[holdPointIndex - 1];
        var previousTime = previousSegment.ATO != DateTime.MaxValue ? previousSegment.ATO : previousSegment.ETO;
        if (previousTime == DateTime.MaxValue)
            return false;

        arrivalTime = previousTime + holdSegment.EET;
        return true;
    }

    // Find the hold-point waypoint on the route. Search forward from the current waypoint first.
    // Then search backward from the current waypoint, because the aircraft can pass the hold
    // point (e.g. aircraft is established in the hold, or a handoff is accepted after
    // overflight). The backward search starts at the current waypoint, so that it does not match
    // a waypoint at the start of the route before a nearer one.
    public static FDP2.FDR.ExtractedRoute.Segment? FindHoldWaypoint(this FDP2.FDR fdr, string holdPointPrefix)
    {
        var route = fdr.ParsedRoute.ToList();
        var currentIndex = Math.Max(fdr.ParsedRoute.OverflownIndex, 0);

        var waypointsAhead = route
            .Skip(currentIndex)
            .Where(s => s.Type == FDP2.FDR.ExtractedRoute.Segment.SegmentTypes.WAYPOINT)
            .ToArray();

        var match = FindMatchingWaypoint(waypointsAhead, holdPointPrefix);
        if (match is not null)
            return match;

        var waypointsBehind = route
            .Take(currentIndex)
            .Where(s => s.Type == FDP2.FDR.ExtractedRoute.Segment.SegmentTypes.WAYPOINT)
            .Reverse()
            .ToArray();

        return FindMatchingWaypoint(waypointsBehind, holdPointPrefix);

        static FDP2.FDR.ExtractedRoute.Segment? FindMatchingWaypoint(
            FDP2.FDR.ExtractedRoute.Segment[] waypoints,
            string holdPointPrefix)
        {
            var match = waypoints.FirstOrDefault(s => s.Intersection.Name == holdPointPrefix);

            if (match is null && holdPointPrefix.Length >= 3)
                match = waypoints.FirstOrDefault(s => s.Intersection.Name.StartsWith(holdPointPrefix));

            return match;
        }
    }
}
