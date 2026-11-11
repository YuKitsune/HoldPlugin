using HoldPlugin.ViewModels;

namespace HoldPlugin;

public record HoldItem(
    string Callsign,
    string HoldPoint,
    DateTime? HoldEntryTime,
    DateTime HoldExitTime,
    bool IsDesignated,
    int Level,
    IClearedFlightLevel ClearedFlightLevel,
    bool RvsmApproved,
    string GlobalOps,
    HoldItemState State,
    // The length of the hold from the remarks. The exit time is the arrival time plus the delay.
    TimeSpan Delay);
