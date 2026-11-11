using HoldPlugin.Contracts;
using MediatR;
using vatsys;
using vatSysToolbox;
using vatSysToolbox.Windows;

namespace HoldPlugin.Handlers;

public class SetHoldDelayHandler(HoldService holdService, WorkQueue workQueue, IErrorReporter errorReporter)
    : IRequestHandler<SetHoldDelayCommand>
{
    public Task Handle(SetHoldDelayCommand request, CancellationToken cancellationToken)
    {
        workQueue.Enqueue(async () =>
        {
            try
            {
                var fdr = FDP2.GetFDRs.FirstOrDefault(f => f.Callsign == request.Callsign);
                if (fdr is null)
                    return;

                if (request.Delay == TimeSpan.Zero)
                    holdService.RemoveDelay(fdr, request.WaypointName);
                else
                    holdService.SetDelay(fdr, DelayType.Delay, request.WaypointName, request.Delay);
            }
            catch (Exception ex)
            {
                errorReporter.ReportError(ex);
            }
        });

        return Task.CompletedTask;
    }
}
