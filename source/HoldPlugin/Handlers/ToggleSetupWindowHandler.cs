using HoldPlugin.Contracts;
using MediatR;
using vatSysToolbox;
using vatSysToolbox.MediatR;

namespace HoldPlugin.Handlers;

// The messenger receives the messages on a background thread. HoldWindows opens the window on the GUI thread.
[PluginMessageHandler(HoldMessages.ToggleSetupWindow)]
public class ToggleSetupWindowHandler(HoldWindows holdWindows, IErrorReporter errorReporter)
    : INotificationHandler<ToggleSetupWindowNotification>
{
    public Task Handle(ToggleSetupWindowNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            holdWindows.ToggleHoldSetup();
        }
        catch (Exception ex)
        {
            errorReporter.ReportError(ex);
        }

        return Task.CompletedTask;
    }
}
