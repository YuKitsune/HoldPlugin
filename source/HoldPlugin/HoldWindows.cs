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

    static readonly WindowOptions HoldSetupOptions = new() { CanMinimise = false };

    static readonly WindowOptions HoldListOptions = new()
    {
        Size = WindowSize.Absolute(480, 150),
        CanClose = false,
        CanMinimise = false
    };

    public void OpenHoldSetup() =>
        windowManager.Open(WindowKeys.HoldSetup, "Hold Setup", CreateHoldSetup, HoldSetupOptions);

    // The window manager opens and closes the window on the UI thread. Thus, you can call this method on any thread.
    public void ToggleHoldSetup() =>
        windowManager.Toggle(WindowKeys.HoldSetup, "Hold Setup", CreateHoldSetup, HoldSetupOptions);

    HoldSetup CreateHoldSetup(IWindowHandle windowHandle)
    {
        // The handle has no Closed event. A close that no handler cancels closes the window.
        windowHandle.BeforeClose += (_, e) =>
        {
            if (!e.Cancel)
                Broadcast(HoldMessages.SetupWindowClosed);
        };
        Broadcast(HoldMessages.SetupWindowOpened);

        var viewModel = new HoldSetupViewModel(holdSlotRepository, guiInvoker, errorReporter, mediator);
        return new HoldSetup(viewModel);
    }

    // Tells the other plugins (for example, the HLD SET button of the Eurocat Menus plugin) that a window opened or
    // closed
    void Broadcast(string messageType)
    {
        try
        {
            messenger.Broadcast(messageType, EmptyContent);
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
                windowManager.Open(
                    WindowKeys.HoldFor(holdPointName),
                    $"HOLD {holdPointName} WINDOW",
                    handle =>
                    {
                        var viewModel = new HoldListViewModel(holdPointName, itemsToDisplay, holdRepository, holdSlotRepository, handle, guiInvoker, mediator, errorReporter);
                        return new HoldList(viewModel);
                    },
                    HoldListOptions);
            }
        }

        var allocatedNames = GetAllocatedHoldPointNames(slots);
        var otherHolds = holds
            .Where(h => !allocatedNames.Contains(h.HoldPoint) && h.State != HoldItemState.Unconcerned)
            .ToArray();
        if (otherHolds.Any() && !IsWindowOpen(WindowKeys.HoldOther))
        {
            windowManager.Open(
                WindowKeys.HoldOther,
                "HOLD OTHER WINDOW",
                handle =>
                {
                    var viewModel = new HoldListViewModel(string.Empty, otherHolds, holdRepository, holdSlotRepository, handle, guiInvoker, mediator, errorReporter);
                    return new HoldList(viewModel);
                },
                HoldListOptions);
        }
    }

    // This method runs at each refresh of the holds. Open posts to the UI thread also when the window is open. Thus,
    // do not call it for a window that is open.
    bool IsWindowOpen(string key) => windowManager.IsOpen(key);

    public static HashSet<string> GetAllocatedHoldPointNames(IHoldPointDescriptor[] slots) =>
        slots
            .Select(d => d.GetHoldPointName())
            .Where(n => n is not null)
            .ToHashSet()!;
}
