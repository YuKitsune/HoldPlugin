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
                if (holdItem is not null)
                {
                    holdRepository.Update(holdItem.Callsign, h => h with { Level = updated.CorrectedAltitude });
                    holdService.RefreshHolds();
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
