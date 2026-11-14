using HoldPlugin.Contracts;
using MediatR;
using vatsys;
using vatSysToolbox;
using vatSysToolbox.Windows;

namespace HoldPlugin.Handlers;

public class SelectedTrackChangedHandler(
    HoldRepository holdRepository,
    HoldService holdService,
    WorkQueue workQueue,
    IErrorReporter errorReporter)
    : INotificationHandler<SelectedTrackChangedNotification>
{
    public Task Handle(SelectedTrackChangedNotification notification, CancellationToken cancellationToken)
    {
        workQueue.Enqueue(async () =>
        {
            try
            {
                var selectedCallsign = MMI.SelectedTrack?.GetFDR()?.Callsign;
                foreach (var hold in holdRepository.GetHolds())
                {
                    holdRepository.Update(hold.Callsign, h => h with { IsDesignated = h.Callsign == selectedCallsign });
                }

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
