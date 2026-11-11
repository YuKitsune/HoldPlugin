using HoldPlugin.Contracts;
using MediatR;
using vatSysToolbox;
using vatSysToolbox.Windows;

namespace HoldPlugin.Handlers;

public class HoldPointRemovedHandler(
    HoldRepository holdRepository,
    HoldSlotRepository holdSlotRepository,
    HoldService holdService,
    HoldWindows holdWindows,
    WindowManager windowManager,
    WorkQueue workQueue)
    : IRequestHandler<HoldPointRemovedCommand>
{
    public Task Handle(HoldPointRemovedCommand request, CancellationToken cancellationToken)
    {
        workQueue.Enqueue(async () =>
        {
            if (!holdSlotRepository.TryFreeSlot(request.PointName))
                return;

            // Close the window for this hold point
            windowManager.CloseWindow(WindowKeys.HoldFor(request.PointName));

            // Check if any aircraft were holding at this point
            var holdsAtRemovedPoint = holdRepository.GetHolds().Where(h => h.HoldPoint == request.PointName).ToArray();

            // If aircraft exist, they need to be shown in OTHER window
            if (holdsAtRemovedPoint.Any())
            {
                holdService.RefreshHolds();
                holdWindows.EnsureHoldWindowsAreOpen();
            }
        });

        return Task.CompletedTask;
    }
}
