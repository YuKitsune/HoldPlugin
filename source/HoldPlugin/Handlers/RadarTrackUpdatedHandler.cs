using HoldPlugin.Contracts;
using MediatR;
using vatSysToolbox;
using vatSysToolbox.Windows;

namespace HoldPlugin.Handlers;

public class RadarTrackUpdatedHandler(
    HoldRepository holdRepository,
    HoldService holdService,
    WorkQueue workQueue,
    IErrorReporter errorReporter)
    : INotificationHandler<RadarTrackUpdatedNotification>
{
    public Task Handle(RadarTrackUpdatedNotification notification, CancellationToken cancellationToken)
    {
        var updated = notification.RadarTrack;

        workQueue.Enqueue(async () =>
        {
            try
            {
                if (updated.CoupledFDR is null)
                    return;

                var holdItem = holdRepository.Find(updated.CoupledFDR.Callsign);
                if (holdItem is null)
                    return;

                // vatSys sends a radar update for each track every few seconds. Refresh the hold lists only when the
                // level changes, because each refresh posts to the UI thread.
                if (holdItem.Level == updated.CorrectedAltitude)
                    return;

                holdRepository.Update(holdItem.Callsign, h => h with { Level = updated.CorrectedAltitude });
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
