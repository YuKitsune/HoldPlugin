using HoldPlugin.Contracts;
using MediatR;
using vatsys;
using vatSysToolbox;
using vatSysToolbox.Windows;

namespace HoldPlugin.Handlers;

public class CancelHoldHandler(
    HoldRepository holdRepository,
    HoldService holdService,
    WorkQueue workQueue,
    IErrorReporter errorReporter)
    : IRequestHandler<CancelHoldCommand>
{
    public Task Handle(CancelHoldCommand request, CancellationToken cancellationToken)
    {
        workQueue.Enqueue(async () =>
        {
            try
            {
                var hold = holdRepository.Find(request.Callsign);
                if (hold == null)
                    return;

                var fdr = FDP2.GetFDRs.FirstOrDefault(f => f.Callsign == hold.Callsign);
                holdService.CancelHold(fdr, hold);

                holdService.RefreshHolds();
            }
            catch (Exception ex)
            {
                errorReporter.ReportError(ex);
            }
        });

        return Task.CompletedTask;
    }
}
