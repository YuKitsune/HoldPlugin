using HoldPlugin.Contracts;
using MediatR;
using vatsys;
using vatSysToolbox;
using vatSysToolbox.Windows;

namespace HoldPlugin.Handlers;

/// <summary>
///     Initiate, adopt, or cancel holds based on the remarks and route state.
///     Tracks all holds regardless of jurisdiction.
/// </summary>
public class FDRPropertyChangedHandler(HoldService holdService, WorkQueue workQueue, IErrorReporter errorReporter)
    : INotificationHandler<FDRPropertyChangedNotification>
{
    // Only process relevant property changes to avoid queue backlog
    static readonly string[] RelevantPropertyNames =
    [
        nameof(FDP2.FDR.Remarks),
        nameof(FDP2.FDR.GlobalOpData),
        nameof(FDP2.FDR.CFLUpper),
        nameof(FDP2.FDR.CFLLower),
        nameof(FDP2.FDR.RVSM),
        nameof(FDP2.FDR.IsTrackedByMe),
        nameof(FDP2.FDR.IsHandoff),
        nameof(FDP2.FDR.HandoffController),
        nameof(FDP2.FDR.ParsedRoute)
    ];

    public Task Handle(FDRPropertyChangedNotification notification, CancellationToken cancellationToken)
    {
        if (!RelevantPropertyNames.Contains(notification.PropertyName))
            return Task.CompletedTask;

        var fdr = notification.FDR;

        workQueue.Enqueue(async () =>
        {
            try
            {
                holdService.SyncHold(fdr);

                // The delays (DLE) in the remarks change the ETOs.
                if (notification.PropertyName == nameof(FDP2.FDR.Remarks))
                    holdService.UpdateEstimates(fdr);
            }
            catch (Exception ex)
            {
                errorReporter.ReportError(ex);
            }
        });

        return Task.CompletedTask;
    }
}
