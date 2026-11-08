using System.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using vatsys;

namespace HoldPlugin.Contracts;

public record DesignateAircraftCommand(string Callsign);
public record CancelHoldCommand(string Callsign);
public record OpenClearedLevelMenuCommand(string Callsign);
public record OpenHoldExitMenuCommand(string Callsign);
public record ChangeGlobalOpsCommand(string Callsign, string GlobalOps);

public record RefreshHoldsCommand;
public record HoldSlotsUpdatedCommand;
public record RemoveHoldItemCommand(string Callsign);

public record HoldPointAddedCommand(int Index, string PointName);
public record HoldPointRemovedCommand(string PointName);
