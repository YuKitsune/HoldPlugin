using CommunityToolkit.Mvvm.Messaging;
using HoldPlugin.Contracts;
using MediatR;
using vatSysToolbox;
using vatSysToolbox.Windows;

namespace HoldPlugin.Handlers;

/// <summary>
///     Open the hold lists that have holds, and tell the open hold lists to show the holds again.
/// </summary>
public class RefreshHoldsHandler(HoldWindows holdWindows, WorkQueue workQueue) : INotificationHandler<RefreshHoldsCommand>
{
    public Task Handle(RefreshHoldsCommand notification, CancellationToken cancellationToken)
    {
        // The hold list view models are not MediatR handlers.
        WeakReferenceMessenger.Default.Send(notification);

        workQueue.Enqueue(async () => holdWindows.EnsureHoldWindowsAreOpen());
        return Task.CompletedTask;
    }
}
