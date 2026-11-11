using CommunityToolkit.Mvvm.Messaging;
using HoldPlugin.Contracts;
using MediatR;
using vatSysToolbox;
using vatSysToolbox.Windows;

namespace HoldPlugin.Handlers;

/// <summary>
///     Open the hold lists that have holds, and tell the open hold lists to show the holds again.
/// </summary>
public class RefreshHoldsHandler(HoldWindows holdWindows, WorkQueue workQueue, IErrorReporter errorReporter)
    : INotificationHandler<RefreshHoldsCommand>
{
    public Task Handle(RefreshHoldsCommand notification, CancellationToken cancellationToken)
    {
        try
        {
            // The hold list view models are not MediatR handlers.
            WeakReferenceMessenger.Default.Send(notification);
        }
        catch (Exception ex)
        {
            errorReporter.ReportError(ex);
        }

        workQueue.Enqueue(async () =>
        {
            try
            {
                holdWindows.EnsureHoldWindowsAreOpen();
            }
            catch (Exception ex)
            {
                errorReporter.ReportError(ex);
            }
        });

        return Task.CompletedTask;
    }
}
