using HoldPlugin.Contracts;
using MediatR;
using vatSysToolbox;
using vatSysToolbox.MediatR;

namespace HoldPlugin.Handlers;

// Other plugins send this message. InitiateHoldHandler initiates the hold on the work queue.
[PluginMessageHandler(HoldMessages.InitiateHold)]
public class InitiateHoldMessageHandler(IMediator mediator, IErrorReporter errorReporter)
    : INotificationHandler<InitiateHoldNotification>
{
    public async Task Handle(InitiateHoldNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            // The remarks have the exit time in UTC. A time without a time zone is UTC.
            var exitTime = notification.ExitTime is { Kind: DateTimeKind.Local } localExitTime
                ? localExitTime.ToUniversalTime()
                : notification.ExitTime;

            await mediator.Send(
                new InitiateHoldCommand(notification.Callsign, notification.WaypointName, exitTime),
                cancellationToken);
        }
        catch (Exception ex)
        {
            errorReporter.ReportError(ex);
        }
    }
}
