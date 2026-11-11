using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using vatsys;

namespace HoldPlugin;

public record DelayRemark(string WaypointName, TimeSpan Delay);

/// <summary>
///     Read and write the delays in the remarks of a flight plan. The format is
///     DLE/[waypoint][HHMM], where HHMM is the length of the delay (e.g. DLE/TARAL0030). The
///     remarks can have more than one delay.
/// </summary>
public static class DelayRemarks
{
    static readonly Regex DelayRegex = new(@"\bDLE/(\w+?)(\d{2})(\d{2})\b");

    static readonly FieldInfo NetworkInstanceField = AccessTools.Field(typeof(Network), "Instance");
    static readonly MethodInfo SendFlightPlanChangeMethod = AccessTools.Method(typeof(Network), "SendFlightPlanChange");

    /// <summary>
    ///     Find all the delays in the remarks, in the sequence of the remarks. This does not match
    ///     the waypoints to the route.
    /// </summary>
    public static DelayRemark[] Parse(FDP2.FDR fdr)
    {
        return DelayRegex.Matches(fdr.Remarks ?? string.Empty)
            .Cast<Match>()
            .Select(ToDelayRemark)
            .ToArray();
    }

    public static bool HasDelay(FDP2.FDR fdr, string waypointName) =>
        Parse(fdr).Any(d => d.WaypointName == waypointName);

    /// <summary>
    ///     Replace the delay at the waypoint in the remarks, or add a delay to the end of the remarks.
    /// </summary>
    public static void Set(FDP2.FDR fdr, string waypointName, TimeSpan delay)
    {
        var totalMinutes = (int)Math.Round(delay.TotalMinutes);
        var delaySection = $"DLE/{waypointName}{totalMinutes / 60:00}{totalMinutes % 60:00}";

        var remarks = fdr.Remarks ?? string.Empty;
        var match = FindMatch(remarks, waypointName);
        var newRemarks = match is not null
            ? remarks.Remove(match.Index, match.Length).Insert(match.Index, delaySection)
            : $"{remarks} {delaySection}".Trim();

        SetRemarks(fdr, newRemarks);
    }

    /// <summary>
    ///     Remove the delay at the waypoint from the remarks. Keep the other parts of the remarks.
    /// </summary>
    public static void Remove(FDP2.FDR fdr, string waypointName)
    {
        var remarks = fdr.Remarks ?? string.Empty;
        var match = FindMatch(remarks, waypointName);
        if (match is null)
            return;

        var newRemarks = Regex.Replace(remarks.Remove(match.Index, match.Length), @"\s{2,}", " ").Trim();
        SetRemarks(fdr, newRemarks);
    }

    static Match? FindMatch(string remarks, string waypointName) =>
        DelayRegex.Matches(remarks)
            .Cast<Match>()
            .FirstOrDefault(m => ToDelayRemark(m).WaypointName == waypointName);

    static DelayRemark ToDelayRemark(Match match)
    {
        var hours = int.Parse(match.Groups[2].Value);
        var minutes = int.Parse(match.Groups[3].Value);
        return new DelayRemark(match.Groups[1].Value, TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes));
    }

    // vatSys has no public method that changes only the remarks. FDP2.ModifyFDR parses the route
    // again, and the route then loses its ATOs. Thus set the remarks, then send the flight plan
    // change to the network. vatSys sends it only when the controller has permission.
    static void SetRemarks(FDP2.FDR fdr, string remarks)
    {
        fdr.Remarks = remarks;

        var network = NetworkInstanceField.GetValue(null);
        SendFlightPlanChangeMethod.Invoke(network, [fdr]);
    }
}
