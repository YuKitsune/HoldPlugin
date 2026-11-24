using MediatR;
using vatsys;

namespace HoldPlugin.Contracts;

public record DesignateAircraftCommand(string Callsign) : IRequest;
public record CancelHoldCommand(string Callsign) : IRequest;
public record OpenClearedLevelMenuCommand(string Callsign) : IRequest;
public record OpenHoldExitMenuCommand(string Callsign) : IRequest;
public record ChangeGlobalOpsCommand(string Callsign, string GlobalOps) : IRequest;
public record OpenDelayPopupCommand(string Callsign, string WaypointName, TimeSpan? Delay) : IRequest;
public record SetHoldDelayCommand(string Callsign, string WaypointName, TimeSpan Delay) : IRequest;

public record HoldKeyPressedCommand(string Callsign, Coordinate Position, double DistanceTolerance) : IRequest;
// If ExitTime is null, the hold has the default length. ExitTime is UTC.
public record InitiateHoldCommand(string Callsign, string WaypointName, DateTime? ExitTime = null) : IRequest;

// The hold list view models also receive this notification through WeakReferenceMessenger.
public record RefreshHoldsCommand : INotification;
public record HoldSlotsUpdatedCommand;

public record HoldPointAddedCommand(int Index, string PointName) : IRequest;
public record HoldPointRemovedCommand(string PointName) : IRequest;

// Other plugins send this message to the mailbox of the plugin. See HoldMessages.ToggleSetupWindow.
public record ToggleSetupWindowNotification : INotification;

// Other plugins send this message to the mailbox of the plugin. See HoldMessages.InitiateHold.
public record InitiateHoldNotification(string Callsign, string WaypointName, DateTime? ExitTime) : INotification;

// Other plugins send this message to the mailbox of the plugin. See HoldMessages.TerminateHold.
public record TerminateHoldNotification(string Callsign) : INotification;

// The vatSys events.
public record RadarTrackUpdatedNotification(RDP.RadarTrack RadarTrack) : INotification;
public record SelectedTrackChangedNotification : INotification;
public record FDRsChangedNotification : INotification;
public record FDRPropertyChangedNotification(FDP2.FDR FDR, string? PropertyName) : INotification;
