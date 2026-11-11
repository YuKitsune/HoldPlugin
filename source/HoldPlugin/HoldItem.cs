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
    // The delay from the remarks. When it has a value, the exit time is the arrival time plus the delay.
    TimeSpan? Delay);
