using HoldPlugin.Contracts;
using MediatR;
using vatSysToolbox;
using vatSysToolbox.Windows;

namespace HoldPlugin.Handlers;

public class FDRsChangedHandler(
    FdrSubscriptions fdrSubscriptions,
    HoldService holdService,
    WorkQueue workQueue,
    IErrorReporter errorReporter)
    : INotificationHandler<FDRsChangedNotification>
{
    public Task Handle(FDRsChangedNotification notification, CancellationToken cancellationToken)
    {
        workQueue.Enqueue(async () =>
        {
            try
            {
                var fdrs = fdrSubscriptions.SubscribeToAllFDRs();

                // Sync hold states in case FDR objects were replaced (e.g. after assuming a tag from NONE
                // or accepting a handoff, FDRsChanged fires with a new FDR that already has
                // IsTrackedByMe=true, so no PropertyChanged transition fires for that property).
                foreach (var fdr in fdrs)
                {
                    holdService.SyncHold(fdr);
                }
            }
            catch (Exception ex)
            {
                errorReporter.ReportError(ex);
            }
        });

        return Task.CompletedTask;
    }
}
