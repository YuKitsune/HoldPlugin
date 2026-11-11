using HoldPlugin.Contracts;
using MediatR;
using vatsys;
using vatSysToolbox;
using vatSysToolbox.Windows;

namespace HoldPlugin.Handlers;

/// <summary>
///     Initiate a hold with the default delay at the waypoint. A hold at a different waypoint
///     moves to this waypoint. Do nothing if the aircraft already holds at this waypoint.
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
        workQueue.Enqueue(async () =>
        {
            try
            {
                var fdr = FDP2.GetFDRs.FirstOrDefault(f => f.Callsign == request.Callsign);
                if (fdr is null)
                    return;

                var hold = holdRepository.Find(request.Callsign);
                if (hold is not null && fdr.FindHoldWaypoint(hold.HoldPoint)?.GetFlightPlanName() == request.WaypointName)
                    return;

                holdService.SetDelay(fdr, DelayType.Hold, request.WaypointName, HoldService.DefaultHoldDuration);
            }
            catch (Exception ex)
            {
                errorReporter.ReportError(ex);
            }
        });

        return Task.CompletedTask;
    }
}
