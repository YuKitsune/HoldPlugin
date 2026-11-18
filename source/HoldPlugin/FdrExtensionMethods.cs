using vatsys;

namespace HoldPlugin;

public static class FdrExtensionMethods
{
    // The name of the waypoint in the flight plan route, and in the delays in the remarks. For a
    // lat/long point, this is e.g. 3330S15100E, but the strip shows 33 15+.
    public static string GetFlightPlanName(this FDP2.FDR.ExtractedRoute.Segment segment) =>
        Conversions.ConvertWaypointNameToFlightPlan(segment);

    public static FDP2.FDR.ExtractedRoute.Segment? GetNextWaypoint(this FDP2.FDR fdr) =>
        fdr.ParsedRoute.ToList()
            .Skip(Math.Max(fdr.ParsedRoute.OverflownIndex + 1, 0))
            .FirstOrDefault(s => s.Type == FDP2.FDR.ExtractedRoute.Segment.SegmentTypes.WAYPOINT);

    public static FDP2.FDR.ExtractedRoute.Segment[] GetWaypointsAhead(this FDP2.FDR fdr) =>
        fdr.ParsedRoute.ToList()
            .Skip(Math.Max(fdr.ParsedRoute.OverflownIndex + 1, 0))
            .Where(s => s.Type == FDP2.FDR.ExtractedRoute.Segment.SegmentTypes.WAYPOINT)
            .ToArray();

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

        // The aircraft leaves the previous waypoint after the delay at it.
        if (fdr.GetActiveDelays().TryGetValue(holdPointIndex - 1, out var delay))
            previousTime += delay;

        arrivalTime = previousTime + holdSegment.EET;
        return true;
    }

    // The delays (DLE) in the remarks that change the ETOs, by route index. vatSys calculates the
    // ETOs from the last ATO, thus the times up to the last ATO already contain the delays before
    // it. If the route has the waypoint more than one time, the delay is at the first one.
    public static Dictionary<int, TimeSpan> GetActiveDelays(this FDP2.FDR fdr)
    {
        var activeDelays = new Dictionary<int, TimeSpan>();

        var delays = DelayRemarks.ParseDelays(fdr).ToList();
        if (delays.Count == 0)
            return activeDelays;

        var route = fdr.ParsedRoute.ToList();
        var startIndex = Math.Max(route.FindLastIndex(s => s.ATO != DateTime.MaxValue), 0);
        for (var i = startIndex; i < route.Count && delays.Count > 0; i++)
        {
            if (route[i].Type != FDP2.FDR.ExtractedRoute.Segment.SegmentTypes.WAYPOINT)
                continue;

            var waypointName = route[i].GetFlightPlanName();
            var delay = delays.FirstOrDefault(d => d.WaypointName == waypointName);
            if (delay is null)
                continue;

            activeDelays[i] = delay.Delay;
            delays.Remove(delay);
        }

        return activeDelays;
    }

    // Find the waypoint with the name in the flight plan route. Search forward from the next
    // waypoint first. Then search backward from the last overflown waypoint.
    public static FDP2.FDR.ExtractedRoute.Segment? FindWaypoint(this FDP2.FDR fdr, string flightPlanName)
    {
        var route = fdr.ParsedRoute.ToList();
        var nextIndex = Math.Max(fdr.ParsedRoute.OverflownIndex + 1, 0);

        var waypointsAhead = route
            .Skip(nextIndex)
            .Where(s => s.Type == FDP2.FDR.ExtractedRoute.Segment.SegmentTypes.WAYPOINT)
            .ToArray();

        var waypointsBehind = route
            .Take(nextIndex)
            .Where(s => s.Type == FDP2.FDR.ExtractedRoute.Segment.SegmentTypes.WAYPOINT)
            .Reverse()
            .ToArray();
        
        return waypointsAhead.FirstOrDefault(s => s.GetFlightPlanName() == flightPlanName)
            ?? waypointsBehind.FirstOrDefault(s => s.GetFlightPlanName() == flightPlanName);
    }

    // Find the hold-point waypoint on the route by its intersection name (HoldItem.HoldPoint).
    // Search forward from the next waypoint first. Then search backward from the last overflown
    // waypoint, because the aircraft can pass the hold point (e.g. a handoff is accepted after
    // overflight). The backward search starts at the last overflown waypoint, so that it does not
    // match a waypoint at the start of the route before a nearer one.
    // Lat/long waypoints near each other can have the same name (e.g. 33 15+). The forward search
    // must not start at the last overflown waypoint. If it does, the hold key finds the reroute
    // waypoint at the aircraft position, and not the hold point after it.
    public static FDP2.FDR.ExtractedRoute.Segment? FindHoldWaypoint(this FDP2.FDR fdr, string holdPointPrefix)
    {
        var route = fdr.ParsedRoute.ToList();
        var nextIndex = Math.Max(fdr.ParsedRoute.OverflownIndex + 1, 0);

        var waypointsAhead = route
            .Skip(nextIndex)
            .Where(s => s.Type == FDP2.FDR.ExtractedRoute.Segment.SegmentTypes.WAYPOINT)
            .ToArray();

        var match = FindMatchingWaypoint(waypointsAhead, holdPointPrefix);
        if (match is not null)
            return match;

        var waypointsBehind = route
            .Take(nextIndex)
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
