using System.Text.RegularExpressions;
using vatsys;

namespace HoldPlugin;

// DLE in the remarks. The pilot gives the delay.
public record DelayRemark(string WaypointName, TimeSpan Delay);

// HLD in the remarks. The controller gives the hold.
public record HoldRemark(string WaypointName, DateTime ExitTime);

/// <summary>
///     Read and write the delays and the holds in the remarks of a flight plan. The format is
///     DLE/[waypoint][HHMM] for a delay, where HHMM is the length of the delay (e.g. DLE/TARAL0030),
///     and HLD/[waypoint][HHMM] for a hold, where HHMM is the UTC exit time (e.g. HLD/TARAL1245).
///     The remarks can have more than one delay, but only one hold. The waypoint names are the
///     names in the flight plan route (see <see cref="FdrExtensionMethods.GetFlightPlanName"/>).
/// </summary>
public static class DelayRemarks
{
    static readonly Regex DelayRegex = new(@"\bDLE/(\w+?)(\d{2})(\d{2})\b");
    static readonly Regex HoldRegex = new(@"\bHLD/(\w+?)(\d{2})(\d{2})\b");

    // The remarks have the exit time without the date. The exit time is the time nearest to now.
    static readonly TimeSpan HalfDay = TimeSpan.FromHours(12);

    /// <summary>
    ///     Find all the delays in the remarks, in the sequence of the remarks. This does not match
    ///     the waypoints to the route.
    /// </summary>
    public static DelayRemark[] ParseDelays(FDP2.FDR fdr)
    {
        return DelayRegex.Matches(fdr.Remarks ?? string.Empty)
            .Cast<Match>()
            .Select(ToDelayRemark)
            .ToArray();
    }

    /// <summary>
    ///     Find the hold in the remarks. Returns null if the remarks have no hold, or if the exit
    ///     time is not a valid time.
    /// </summary>
    public static HoldRemark? ParseHold(FDP2.FDR fdr)
    {
        var match = HoldRegex.Match(fdr.Remarks ?? string.Empty);
        return match.Success ? ToHoldRemark(match) : null;
    }

    public static bool HasHold(FDP2.FDR fdr, string waypointName) =>
        ParseHold(fdr)?.WaypointName == waypointName;

    /// <summary>
    ///     Replace the delay at the waypoint in the remarks, or add it to the end of the remarks.
    /// </summary>
    public static void SetDelay(FDP2.FDR fdr, string waypointName, TimeSpan delay)
    {
        var totalMinutes = (int)Math.Round(delay.TotalMinutes);
        var delaySection = $"DLE/{waypointName}{totalMinutes / 60:00}{totalMinutes % 60:00}";

        var remarks = fdr.Remarks ?? string.Empty;
        var match = DelayRegex.Matches(remarks)
            .Cast<Match>()
            .FirstOrDefault(m => ToDelayRemark(m).WaypointName == waypointName);

        SetRemarks(fdr, Replace(remarks, match, delaySection));
    }

    /// <summary>
    ///     Replace the hold in the remarks, or add it to the end of the remarks. The remarks have
    ///     only one hold, thus a hold replaces the hold at any waypoint.
    /// </summary>
    public static void SetHold(FDP2.FDR fdr, string waypointName, DateTime exitTime)
    {
        var holdSection = $"HLD/{waypointName}{exitTime:HHmm}";

        var remarks = fdr.Remarks ?? string.Empty;
        var match = HoldRegex.Match(remarks);

        SetRemarks(fdr, Replace(remarks, match.Success ? match : null, holdSection));
    }

    /// <summary>
    ///     Remove the delay at the waypoint from the remarks. Keep the other parts of the remarks.
    /// </summary>
    public static void RemoveDelay(FDP2.FDR fdr, string waypointName)
    {
        var remarks = fdr.Remarks ?? string.Empty;
        var match = DelayRegex.Matches(remarks)
            .Cast<Match>()
            .FirstOrDefault(m => ToDelayRemark(m).WaypointName == waypointName);

        Remove(fdr, remarks, match);
    }

    /// <summary>
    ///     Remove the hold at the waypoint from the remarks. Keep the other parts of the remarks.
    /// </summary>
    public static void RemoveHold(FDP2.FDR fdr, string waypointName)
    {
        var remarks = fdr.Remarks ?? string.Empty;
        var match = HoldRegex.Match(remarks);
        if (!match.Success || match.Groups[1].Value != waypointName)
            return;

        Remove(fdr, remarks, match);
    }

    static string Replace(string remarks, Match? match, string section) =>
        match is not null
            ? remarks.Remove(match.Index, match.Length).Insert(match.Index, section)
            : $"{remarks} {section}".Trim();

    static void Remove(FDP2.FDR fdr, string remarks, Match? match)
    {
        if (match is null)
            return;

        var newRemarks = Regex.Replace(remarks.Remove(match.Index, match.Length), @"\s{2,}", " ").Trim();
        SetRemarks(fdr, newRemarks);
    }

    static DelayRemark ToDelayRemark(Match match)
    {
        var hours = int.Parse(match.Groups[2].Value);
        var minutes = int.Parse(match.Groups[3].Value);
        return new DelayRemark(match.Groups[1].Value, TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes));
    }

    static HoldRemark? ToHoldRemark(Match match)
    {
        var hours = int.Parse(match.Groups[2].Value);
        var minutes = int.Parse(match.Groups[3].Value);
        if (hours > 23 || minutes > 59)
            return null;

        var now = DateTime.UtcNow;
        var exitTime = now.Date + TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes);
        if (exitTime - now > HalfDay)
            exitTime = exitTime.AddDays(-1);
        else if (now - exitTime > HalfDay)
            exitTime = exitTime.AddDays(1);

        return new HoldRemark(match.Groups[1].Value, exitTime);
    }

    // vatSys has no public method that changes only the remarks. FDP2.ModifyFDR parses the route
    // again, and the route then loses its ATOs. Thus set the remarks, then send the flight plan
    // change to the network.
    static void SetRemarks(FDP2.FDR fdr, string remarks)
    {
        fdr.Remarks = remarks;
        FlightPlanNetwork.SendFlightPlanChange(fdr);
    }
}
