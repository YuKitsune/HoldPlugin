using vatsys;

namespace HoldPlugin;

public static class FdrExtensionMethods
{
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
