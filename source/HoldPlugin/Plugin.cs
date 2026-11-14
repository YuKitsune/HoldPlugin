using System.ComponentModel.Composition;
using System.Windows.Forms;
using HoldPlugin.Contracts;
using HoldPlugin.Patches;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using vatsys;
using vatsys.Plugin;
using vatSysToolbox;
using vatSysToolbox.Extensions;
using vatSysToolbox.MediatR;
using vatSysToolbox.Messaging;

namespace HoldPlugin;

// TODO: Add DLE detection, and Delay menu
// TODO: Smaller label when in the hold
// TODO: Clean up

[Export(typeof(IPlugin))]
public class Plugin : IPlugin
{
#if DEBUG
    public const string Name = "Hold Plugin - Debug";
#else
    public const string Name = "Hold Plugin";
#endif

    /// <summary>
    /// The name that other plugins use to send messages to this plugin.
    /// </summary>
    public const string MailboxName = "dev.eoinmotherway.hold";

    readonly IMediator _mediator;
    readonly HoldWindows _holdWindows;

    string IPlugin.Name => Name;

    public Plugin()
    {
        var serviceProvider = new ServiceCollection()
            .AddLogging()
            .AddVatSysToolbox(Name, MailboxName)
            .AddMediatR(c => c.RegisterServicesFromAssemblyContaining<Plugin>())
            .AddSingleton<HoldRepository>()
            .AddSingleton<HoldSlotRepository>()
            .AddSingleton<KeySettings>()
            .AddSingleton<HoldService>()
            .AddSingleton<HoldWindows>()
            .AddSingleton<FdrSubscriptions>()
            .BuildServiceProvider();

        _mediator = serviceProvider.GetRequiredService<IMediator>();
        _holdWindows = serviceProvider.GetRequiredService<HoldWindows>();

        var errorReporter = serviceProvider.GetRequiredService<IErrorReporter>();
        HoldKeyPatches.Initialize(serviceProvider.GetRequiredService<KeySettings>(), errorReporter, _mediator);
        HoldPatches.Apply(Name, serviceProvider.GetRequiredService<HoldRepository>(), errorReporter, _mediator);

        MMI.SelectedTrackChanged += (_, _) => _ = _mediator.Publish(new SelectedTrackChangedNotification());
        FDP2.FDRsChanged += (_, _) => _ = _mediator.Publish(new FDRsChangedNotification());

        CreateMenuItems();

        var messenger = serviceProvider.GetRequiredService<IPluginMessenger>();
        messenger.SubscribeHandlers(typeof(Plugin).Assembly, _mediator, new JsonMessageSerializer());
        messenger.Start();
    }

    void CreateMenuItems()
    {
        var menuItem = new CustomToolStripMenuItem(
            CustomToolStripMenuItemWindowType.Main,
            CustomToolStripMenuItemCategory.Tools,
            new ToolStripMenuItem("Hold Setup"));
        menuItem.Item.Click += (s, e) => _holdWindows.OpenHoldSetup();

        MMI.AddCustomMenuItem(menuItem);
    }

    public void OnFDRUpdate(FDP2.FDR updated)
    {
        // Unused
        // Hold detection is handled by FDRPropertyChangedHandler for immediate response
    }

    public void OnRadarTrackUpdate(RDP.RadarTrack updated)
    {
        _ = _mediator.Publish(new RadarTrackUpdatedNotification(updated));
    }
}
