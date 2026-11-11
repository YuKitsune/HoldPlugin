using HoldPlugin.Contracts;
using MediatR;
using vatSysToolbox.MediatR;

namespace HoldPlugin.Handlers;

// The messenger receives the messages on a background thread. HoldWindows opens the window on the GUI thread.
[PluginMessageHandler(HoldMessages.ToggleSetupWindow)]
public class ToggleSetupWindowHandler(HoldWindows holdWindows) : INotificationHandler<ToggleSetupWindowNotification>
{
    public Task Handle(ToggleSetupWindowNotification notification, CancellationToken cancellationToken)
    {
        holdWindows.ToggleHoldSetup();
        return Task.CompletedTask;
    }
}
