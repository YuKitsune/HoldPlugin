using HoldPlugin.Contracts;
using MediatR;
using vatsys;
using vatSysToolbox;
using vatSysToolbox.Windows;

namespace HoldPlugin.Handlers;

/// <summary>
///     Initiate a hold at the waypoint. A hold at a different waypoint moves to this waypoint.
///     With an exit time, set the exit time of the hold. Without an exit time, use the default
///     length, and do nothing if the aircraft already holds at this waypoint.
/// </summary>
public class InitiateHoldHandler(
    HoldRepository holdRepository,
    HoldService holdService,
    WorkQueue workQueue,
    IErrorReporter errorReporter)
    : IRequestHandler<InitiateHoldCommand>
{
    public Task Handle(InitiateHoldCommand request, CancellationToken cancellationToken)
    {
        workQueue.Enqueue(async _ =>
        {
            try
            {
                var fdr = FDP2.GetFDRs.FirstOrDefault(f => f.Callsign == request.Callsign);
                if (fdr is null)
                    return;

                if (request.ExitTime is not null)
                {
                    // Another plugin can send a waypoint that is not on the route.
                    if (fdr.FindWaypoint(request.WaypointName) is null)
                        return;

                    holdService.SetHold(fdr, request.WaypointName, request.ExitTime.Value);
                    return;
                }

                var hold = holdRepository.Find(request.Callsign);
                if (hold is not null && fdr.FindHoldWaypoint(hold.HoldPoint)?.GetFlightPlanName() == request.WaypointName)
                    return;

                holdService.SetDefaultHold(fdr, request.WaypointName);
            }
            catch (Exception ex)
            {
                errorReporter.ReportError(ex);
            }
        });

        return Task.CompletedTask;
    }
}
