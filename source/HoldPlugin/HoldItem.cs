using HoldPlugin.ViewModels;

namespace HoldPlugin;

public class HoldItem
{
    public string Callsign { get; set; }
    public string HoldPoint { get; set; }
    public DateTime HoldEntryTime { get; set; }
    public DateTime? HoldExitTime { get; set; }
    public bool IsDesignated { get; set; }
    public int Level { get; set; }
    public IClearedFlightLevel ClearedFlightLevel { get; set; }
    public bool RvsmApproved { get; set; }
    public string GlobalOps { get; set; }
    public HoldItemState State { get; set; }

    public HoldItem(
        string callsign,
        string holdPoint,
        DateTime holdEntryTime,
        DateTime? holdExitTime,
        bool isDesignated,
        int level,
        IClearedFlightLevel clearedFlightLevel,
        bool rvsmApproved,
        string globalOps,
        HoldItemState state)
    {
        Callsign = callsign;
        HoldPoint = holdPoint;
        HoldEntryTime = holdEntryTime;
        HoldExitTime = holdExitTime;
        IsDesignated = isDesignated;
        Level = level;
        ClearedFlightLevel = clearedFlightLevel;
        RvsmApproved = rvsmApproved;
        GlobalOps = globalOps;
        State = state;
    }
}
