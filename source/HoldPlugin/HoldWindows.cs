using System.Windows;
using HoldPlugin.Controls;
using HoldPlugin.ViewModels;
using MediatR;
using vatSysToolbox;
using vatSysToolbox.Messaging;
using vatSysToolbox.Windows;

namespace HoldPlugin;

/// <summary>
///     Opens and closes the Hold Setup window and the hold list windows.
/// </summary>
public class HoldWindows(
    HoldRepository holdRepository,
    HoldSlotRepository holdSlotRepository,
    WindowManager windowManager,
    IGuiInvoker guiInvoker,
    IErrorReporter errorReporter,
    IPluginMessenger messenger,
    IMediator mediator)
{
    // The messages that the plugin sends have no data
    static readonly MessageContent EmptyContent = new("application/json", "{}"u8.ToArray());

    public void OpenHoldSetup()
    {
        windowManager.TryCreateWindow(
            WindowKeys.HoldSetup,
            "Hold Setup",
            windowHandle =>
            {
                windowHandle.Closed += (_, _) => _ = Broadcast(HoldMessages.SetupWindowClosed);
                _ = Broadcast(HoldMessages.SetupWindowOpened);

                var viewModel = new HoldSetupViewModel(holdSlotRepository, guiInvoker, errorReporter, mediator);
                var view = new HoldSetup(viewModel);
                return view;
            },
            canMinimise: false);
    }

    // The window manager opens and closes the window on the UI thread. Thus, you can call this method on any thread.
    public void ToggleHoldSetup()
    {
        if (windowManager.TryGetWindow(WindowKeys.HoldSetup, out var _))
            windowManager.CloseWindow(WindowKeys.HoldSetup);
        else
            OpenHoldSetup();
    }

    // Tells the other plugins (for example, the HLD SET button of the Eurocat Menus plugin) that a window opened or
    // closed
    async Task Broadcast(string messageType)
    {
        try
        {
            await messenger.Broadcast(messageType, EmptyContent);
        }
        catch (Exception ex)
        {
            errorReporter.ReportError(ex, $"Cannot send the message {messageType}");
        }
    }

    public void EnsureHoldWindowsAreOpen()
    {
        var slots = holdSlotRepository.GetSlots();
        var holds = holdRepository.GetHolds();

        foreach (var descriptor in slots)
        {
            var holdPointName = descriptor.GetHoldPointName();

            if (holdPointName is null)
                continue;

            var itemsToDisplay = holds
                .Where(h => h.HoldPoint == holdPointName && h.State != HoldItemState.Unconcerned)
                .ToArray();

            if (itemsToDisplay.Any() && !IsWindowOpen(WindowKeys.HoldFor(holdPointName)))
            {
                windowManager.TryCreateWindow(
                    WindowKeys.HoldFor(holdPointName),
                    $"HOLD {holdPointName} WINDOW",
                    handle =>
                    {
                        var viewModel = new HoldListViewModel(holdPointName, itemsToDisplay, holdRepository, holdSlotRepository, handle, guiInvoker, mediator, errorReporter);
                        var view = new HoldList(viewModel);
                        return view;
                    },
                    sizeToContent: false,
                    canClose: false,
                    canMinimise: false,
                    new Size(480, 150));
            }
        }

        var allocatedNames = GetAllocatedHoldPointNames(slots);
        var otherHolds = holds
            .Where(h => !allocatedNames.Contains(h.HoldPoint) && h.State != HoldItemState.Unconcerned)
            .ToArray();
        if (otherHolds.Any() && !IsWindowOpen(WindowKeys.HoldOther))
        {
            windowManager.TryCreateWindow(
                WindowKeys.HoldOther,
                "HOLD OTHER WINDOW",
                handle =>
                {
                    var viewModel = new HoldListViewModel(string.Empty, otherHolds, holdRepository, holdSlotRepository, handle, guiInvoker, mediator, errorReporter);
                    var view = new HoldList(viewModel);
                    return view;
                },
                sizeToContent: false,
                canClose: false,
                canMinimise: false,
                new Size(480, 150));
        }
    }

    // This method runs at each refresh of the holds. TryCreateWindow posts to the UI thread also when the window is
    // open. Thus, do not call it for a window that is open.
    bool IsWindowOpen(string key) => windowManager.TryGetWindow(key, out _);

    public static HashSet<string> GetAllocatedHoldPointNames(IHoldPointDescriptor[] slots) =>
        slots
            .Select(d => d.GetHoldPointName())
            .Where(n => n is not null)
            .ToHashSet()!;
}
