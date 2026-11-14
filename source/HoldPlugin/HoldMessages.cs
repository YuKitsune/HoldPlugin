namespace HoldPlugin;

/// <summary>
/// The messages that other plugins (for example, the buttons of the Eurocat Menus plugin) use to control the Hold plugin.
/// Send these messages to the mailbox <see cref="Plugin.MailboxName"/>. The plugin ignores the payload.
/// </summary>
public static class HoldMessages
{
    /// <summary>
    /// Opens or closes the Hold Setup window.
    /// </summary>
    public const string ToggleSetupWindow = "dev.eoinmotherway.hold.toggle-setup-window";

    /// <summary>
    /// The plugin sends this message to all plugins when the Hold Setup window opens. The payload is an empty JSON
    /// object.
    /// </summary>
    public const string SetupWindowOpened = "dev.eoinmotherway.hold.setup-window-opened";

    /// <summary>
    /// The plugin sends this message to all plugins when the Hold Setup window closes. The payload is an empty JSON
    /// object.
    /// </summary>
    public const string SetupWindowClosed = "dev.eoinmotherway.hold.setup-window-closed";
}
