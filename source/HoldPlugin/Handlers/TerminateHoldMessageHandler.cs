using HoldPlugin.Contracts;
using MediatR;
using vatSysToolbox;
using vatSysToolbox.MediatR;

namespace HoldPlugin.Handlers;

// Other plugins send this message. CancelHoldHandler terminates the hold on the work queue.
[PluginMessageHandler(HoldMessages.TerminateHold)]
public class TerminateHoldMessageHandler(IMediator mediator, IErrorReporter errorReporter)
    : INotificationHandler<TerminateHoldNotification>
{
    public async Task Handle(TerminateHoldNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            await mediator.Send(new CancelHoldCommand(notification.Callsign), cancellationToken);
        }
        catch (Exception ex)
        {
            errorReporter.ReportError(ex);
        }
    }
}
