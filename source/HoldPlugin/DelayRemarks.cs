using System.Text.RegularExpressions;
using vatsys;

namespace HoldPlugin;

public enum DelayType
{
    // DLE in the remarks. The pilot gives the delay.
    Delay,

    // HLD in the remarks. The controller gives the hold.
    Hold
}

public record DelayRemark(DelayType Type, string WaypointName, TimeSpan Delay);

/// <summary>
///     Read and write the delays and the holds in the remarks of a flight plan. The format is
///     DLE/[waypoint][HHMM] for a delay, and HLD/[waypoint][HHMM] for a hold, where HHMM is the
///     length of the delay (e.g. DLE/TARAL0030). The remarks can have more than one delay, but
///     only one hold.
/// </summary>
public static class DelayRemarks
{
    static readonly Regex DelayRegex = new(@"\b(DLE|HLD)/(\w+?)(\d{2})(\d{2})\b");

    /// <summary>
    ///     Find all the delays and holds in the remarks, in the sequence of the remarks. This does
    ///     not match the waypoints to the route. The waypoint names are the names in the flight
    ///     plan route (see <see cref="FdrExtensionMethods.GetFlightPlanName"/>).
    /// </summary>
    public static DelayRemark[] Parse(FDP2.FDR fdr)
    {
        return DelayRegex.Matches(fdr.Remarks ?? string.Empty)
            .Cast<Match>()
            .Select(ToDelayRemark)
            .ToArray();
    }

    public static bool HasDelay(FDP2.FDR fdr, DelayType type, string waypointName) =>
        Parse(fdr).Any(d => d.Type == type && d.WaypointName == waypointName);

    /// <summary>
    ///     Replace the delay at the waypoint in the remarks, or add it to the end of the remarks.
    ///     The remarks have only one hold, thus a hold replaces the hold at any waypoint.
    /// </summary>
    public static void Set(FDP2.FDR fdr, DelayType type, string waypointName, TimeSpan delay)
    {
        var totalMinutes = (int)Math.Round(delay.TotalMinutes);
        var delaySection = $"{ToPrefix(type)}/{waypointName}{totalMinutes / 60:00}{totalMinutes % 60:00}";

        var remarks = fdr.Remarks ?? string.Empty;
        var match = type == DelayType.Hold
            ? FindHoldMatch(remarks)
            : FindMatch(remarks, type, waypointName);
        var newRemarks = match is not null
            ? remarks.Remove(match.Index, match.Length).Insert(match.Index, delaySection)
            : $"{remarks} {delaySection}".Trim();

        SetRemarks(fdr, newRemarks);
    }

    /// <summary>
    ///     Remove the delay or hold of the type at the waypoint from the remarks. Keep the other
    ///     parts of the remarks.
    /// </summary>
    public static void Remove(FDP2.FDR fdr, DelayType type, string waypointName)
    {
        var remarks = fdr.Remarks ?? string.Empty;
        var match = FindMatch(remarks, type, waypointName);
        if (match is null)
            return;

        var newRemarks = Regex.Replace(remarks.Remove(match.Index, match.Length), @"\s{2,}", " ").Trim();
        SetRemarks(fdr, newRemarks);
    }

    static Match? FindMatch(string remarks, DelayType type, string waypointName) =>
        DelayRegex.Matches(remarks)
            .Cast<Match>()
            .FirstOrDefault(m => ToDelayRemark(m) is var d && d.Type == type && d.WaypointName == waypointName);

    static Match? FindHoldMatch(string remarks) =>
        DelayRegex.Matches(remarks)
            .Cast<Match>()
            .FirstOrDefault(m => ToDelayRemark(m).Type == DelayType.Hold);

    static DelayRemark ToDelayRemark(Match match)
    {
        var type = match.Groups[1].Value == "HLD" ? DelayType.Hold : DelayType.Delay;
        var hours = int.Parse(match.Groups[3].Value);
        var minutes = int.Parse(match.Groups[4].Value);
        return new DelayRemark(type, match.Groups[2].Value, TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes));
    }

    static string ToPrefix(DelayType type) => type == DelayType.Hold ? "HLD" : "DLE";

    // vatSys has no public method that changes only the remarks. FDP2.ModifyFDR parses the route
    // again, and the route then loses its ATOs. Thus set the remarks, then send the flight plan
    // change to the network.
    static void SetRemarks(FDP2.FDR fdr, string remarks)
    {
        fdr.Remarks = remarks;
        FlightPlanNetwork.SendFlightPlanChange(fdr);
    }
}
