using HoldPlugin.Contracts;
using MediatR;
using vatsys;
using vatSysToolbox;
using vatSysToolbox.Windows;

namespace HoldPlugin.Handlers;

/// <summary>
///     If the aircraft has a hold, cancel the hold. Otherwise, initiate a hold with the
///     default delay at the waypoint near the position. If no waypoint ahead is near the
///     position, route the aircraft direct to the position, and hold there.
/// </summary>
public class HoldKeyPressedHandler(
    HoldRepository holdRepository,
    HoldService holdService,
    WorkQueue workQueue,
    IErrorReporter errorReporter)
    : IRequestHandler<HoldKeyPressedCommand>
{
    // The hold duration that the hold key gives.
    static readonly TimeSpan DefaultHoldDuration = TimeSpan.FromMinutes(10);

    public Task Handle(HoldKeyPressedCommand request, CancellationToken cancellationToken)
    {
        workQueue.Enqueue(async () =>
        {
            try
            {
                var fdr = FDP2.GetFDRs.FirstOrDefault(f => f.Callsign == request.Callsign);
                if (fdr is null)
                    return;

                var hold = holdRepository.Find(request.Callsign);
                if (hold is not null)
                {
                    holdService.CancelHold(fdr, hold);
                    holdService.RefreshHolds();
                    return;
                }

                if (!fdr.HavePermission)
                    return;

                var holdPointName = FindWaypointAhead(fdr, request.Position, request.DistanceTolerance)?.GetFlightPlanName()
                    ?? RerouteDirectTo(fdr, request.Position, request.DistanceTolerance);
                if (holdPointName is null)
                    return;

                holdService.SetDelay(fdr, DelayType.Hold, holdPointName, DefaultHoldDuration);
            }
            catch (Exception ex)
            {
                errorReporter.ReportError(ex);
            }
        });

        return Task.CompletedTask;
    }

    // Find the nearest waypoint ahead of the aircraft that is in the distance tolerance of the position.
    static FDP2.FDR.ExtractedRoute.Segment? FindWaypointAhead(FDP2.FDR fdr, Coordinate position, double distanceTolerance) =>
        fdr.GetWaypointsAhead()
            .Select(s => (Segment: s, Distance: Conversions.CalculateDistance(s.Intersection.LatLong, position)))
            .Where(s => s.Distance < distanceTolerance)
            .OrderBy(s => s.Distance)
            .Select(s => s.Segment)
            .FirstOrDefault();

    /// <summary>
    ///     Route the aircraft direct to the position, then to the waypoint ahead that is nearest to
    ///     the position. This is the same as the graphical reroute tool, when the reroute starts at
    ///     the aircraft (see MMI.FinishGraphicReroute). Returns the name of the new waypoint at the
    ///     position in the flight plan route, or null if there is no waypoint ahead.
    /// </summary>
    static string? RerouteDirectTo(FDP2.FDR fdr, Coordinate position, double distanceTolerance)
    {
        var rejoinWaypoint = fdr.GetWaypointsAhead()
            .OrderBy(s => Conversions.CalculateDistance(s.Intersection.LatLong, position))
            .FirstOrDefault();
        if (rejoinWaypoint is null)
            return null;

        var newRoute = new FDP2.FDR.ExtractedRoute(fdr);

        var aircraftPosition = fdr.GetLocation();
        var startSegment = new FDP2.FDR.ExtractedRoute.Segment(newRoute)
        {
            Type = FDP2.FDR.ExtractedRoute.Segment.SegmentTypes.REROUTE,
            Intersection = new Airspace2.Intersection(Conversions.ConvertToStripLatLong(aircraftPosition), aircraftPosition)
        };
        startSegment.Intersection.FullName = Conversions.ConvertToFlightplanLatLong(aircraftPosition);
        startSegment.ATO = DateTime.UtcNow;
        startSegment.PCL = fdr.PRL != -1 || fdr.CFLUpper == -1
            ? (fdr.PRL == -1 ? fdr.RFL : fdr.PRL)
            : fdr.CFLUpper;
        newRoute.Add(startSegment);

        // vatSys uses a named waypoint near the position, if there is one.
        var holdIntersection = Airspace2.FindIntersection(position, distanceTolerance)
            ?? new Airspace2.Intersection(Conversions.ConvertToStripLatLong(position), position);
        var holdSegment = new FDP2.FDR.ExtractedRoute.Segment(newRoute)
        {
            Type = FDP2.FDR.ExtractedRoute.Segment.SegmentTypes.REROUTE,
            Intersection = holdIntersection
        };
        newRoute.Add(holdSegment);

        FDP2.ModifyRoute(fdr, newRoute, null, rejoinWaypoint);
        FlightPlanNetwork.SendFlightPlanChange(fdr);

        // Make a position report at the aircraft position. The hold point is then the next waypoint.
        var routeStartSegment = fdr.ParsedRoute.FirstOrDefault(s => s.Intersection.Name == startSegment.Intersection.Name);
        if (routeStartSegment is not null)
            fdr.TaskQueue.QueueTask(() => FDP2.PositionReport(fdr, routeStartSegment, DateTime.UtcNow, true));

        return holdSegment.GetFlightPlanName();
    }
}
