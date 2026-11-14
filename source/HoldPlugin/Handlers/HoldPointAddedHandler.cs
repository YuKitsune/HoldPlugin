using HoldPlugin.Contracts;
using HoldPlugin.ViewModels;
using MediatR;
using vatSysToolbox;
using vatSysToolbox.Windows;

namespace HoldPlugin.Handlers;

public class HoldPointAddedHandler(
    HoldRepository holdRepository,
    HoldSlotRepository holdSlotRepository,
    HoldService holdService,
    HoldWindows holdWindows,
    WindowManager windowManager,
    WorkQueue workQueue,
    IErrorReporter errorReporter)
    : IRequestHandler<HoldPointAddedCommand>
{
    public Task Handle(HoldPointAddedCommand request, CancellationToken cancellationToken)
    {
        workQueue.Enqueue(async () =>
        {
            try
            {
                holdSlotRepository.TryAllocateSlot(request.Index, request.PointName);

                holdService.RefreshHolds();

                // Check if there are any aircraft that should still be in OTHER
                var allocatedNames = HoldWindows.GetAllocatedHoldPointNames(holdSlotRepository.GetSlots());
                var otherHolds = holdRepository.GetHolds()
                    .Where(h => !allocatedNames.Contains(h.HoldPoint) && h.State != HoldItemState.Unconcerned)
                    .ToArray();

                // Close OTHER window only if no aircraft should be in it
                if (!otherHolds.Any())
                {
                    windowManager.CloseWindow(WindowKeys.HoldOther);
                }

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
