namespace HoldPlugin;

/// <summary>
/// The messages that other plugins (for example, the buttons of the Eurocat Menus plugin) use to control the Hold plugin.
/// Send these messages to the mailbox <see cref="Plugin.MailboxName"/>. The plugin ignores the payload, unless
/// the description of the message gives a payload.
/// </summary>
public static class HoldMessages
{
    /// <summary>
    /// Opens or closes the Hold Setup window.
    /// </summary>
    public const string ToggleSetupWindow = "dev.eoinmotherway.hold.toggle-setup-window";

    /// <summary>
    /// Initiates a hold at a waypoint. The payload is a JSON object with these properties:
    /// <list type="bullet">
    /// <item><c>Callsign</c>: The callsign of the aircraft.</item>
    /// <item><c>WaypointName</c>: The name of the waypoint in the flight plan route.</item>
    /// <item><c>ExitTime</c> (optional): The UTC exit time of the hold, for example <c>"2026-11-24T12:45:00Z"</c>.
    /// If the payload does not have an exit time, the hold length is 10 minutes from the arrival time at the
    /// waypoint.</item>
    /// </list>
    /// A hold at a different waypoint moves to this waypoint.
    /// </summary>
    public const string InitiateHold = "dev.eoinmotherway.hold.initiate-hold";

    /// <summary>
    /// Terminates the hold of an aircraft. The payload is a JSON object with this property:
    /// <list type="bullet">
    /// <item><c>Callsign</c>: The callsign of the aircraft.</item>
    /// </list>
    /// If the aircraft does not hold, the plugin does nothing.
    /// </summary>
    public const string TerminateHold = "dev.eoinmotherway.hold.terminate-hold";

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
