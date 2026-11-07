using System.Collections.Concurrent;
using HarmonyLib;
using vatsys;

namespace HoldPlugin;

/// <summary>
///     The hold data that the Harmony patches use. The patches run on vatSys threads, thus this
///     data is kept in a thread-safe dictionary.
/// </summary>
public record HoldPatchState(string HoldPoint, DateTime ExitTime);

/// <summary>
///     Harmony patches that stop vatSys from moving an aircraft along its route while the
///     aircraft is in a hold.
/// </summary>
public static class HoldPatches
{
    static readonly ConcurrentDictionary<string, HoldPatchState> Holds = new();

    public static void Apply(string harmonyId)
    {
        var harmony = new Harmony(harmonyId);
        harmony.PatchAll(typeof(HoldPatches).Assembly);
    }

    /// <summary>
    ///     Update the hold data from the active holds. Process the FDR again when its hold data
    ///     changes, so that vatSys calculates the ETOs again.
    /// </summary>
    public static void Sync(IReadOnlyCollection<HoldItem> activeHolds)
    {
        var expectedStates = activeHolds.ToDictionary(
            h => h.Callsign,
            h => new HoldPatchState(h.HoldPoint, h.HoldExitTime ?? h.DefaultHoldExitTime));

        foreach (var callsign in Holds.Keys.ToArray())
        {
            if (expectedStates.ContainsKey(callsign))
                continue;

            Holds.TryRemove(callsign, out _);
            ProcessFDR(callsign);
        }

        foreach (var expectedState in expectedStates)
        {
            if (Holds.TryGetValue(expectedState.Key, out var currentState) && currentState == expectedState.Value)
                continue;

            Holds[expectedState.Key] = expectedState.Value;
            ProcessFDR(expectedState.Key);
        }
    }

    static void ProcessFDR(string callsign)
    {
        var fdr = FDP2.GetFDRs.FirstOrDefault(f => f.Callsign == callsign);
        if (fdr is null)
            return;

        FDP2.Process(fdr);
    }

    static bool TryGetHoldPointIndex(FDP2.FDR? fdr, out HoldPatchState holdState, out int holdPointIndex)
    {
        holdPointIndex = -1;
        holdState = null!;

        if (fdr is null || !Holds.TryGetValue(fdr.Callsign, out holdState))
            return false;

        var holdSegment = Plugin.FindHoldWaypoint(fdr, holdState.HoldPoint);
        if (holdSegment is null)
            return false;

        holdPointIndex = fdr.ParsedRoute.IndexOf(holdSegment);
        return holdPointIndex != -1;
    }

    /// <summary>
    ///     Set the ETO of the hold point to the hold exit time. Then calculate the ETOs of the
    ///     segments after the hold point from the hold exit time.
    /// </summary>
    [HarmonyPatch(typeof(FDP2.FDR.ExtractedRoute), nameof(FDP2.FDR.ExtractedRoute.CalculateETOs))]
    static class CalculateETOsPatch
    {
        static void Postfix(FDP2.FDR.ExtractedRoute __instance)
        {
            try
            {
                if (!TryGetHoldPointIndex(__instance.Parent, out var holdState, out var holdPointIndex))
                    return;

                var route = __instance.ToList();
                route[holdPointIndex].ETONoEvent = holdState.ExitTime;

                var eto = holdState.ExitTime;
                for (var i = holdPointIndex + 1; i < route.Count; i++)
                {
                    var segment = route[i];
                    if (!segment.IsPETO)
                        segment.ETONoEvent = eto + segment.EET;

                    eto = segment.ETONoEvent;
                }

                __instance.Refresh();
                __instance.FirePropertyChanged("ETO");
            }
            catch (Exception ex)
            {
                Plugin.AddError(ex);
            }
        }
    }

    /// <summary>
    ///     Do not let the overflown index go past the hold point. This is necessary when the hold
    ///     exit time is in the past, but the aircraft is still in the hold.
    /// </summary>
    [HarmonyPatch(typeof(FDP2.FDR.ExtractedRoute), "SetOverflownIndex")]
    static class SetOverflownIndexPatch
    {
        static void Postfix(FDP2.FDR.ExtractedRoute __instance)
        {
            try
            {
                if (!TryGetHoldPointIndex(__instance.Parent, out _, out var holdPointIndex))
                    return;

                if (__instance.OverflownIndex <= holdPointIndex)
                    return;

                __instance.OverflownIndex = holdPointIndex;
                __instance.FirePropertyChanged("OverflownIndex");
            }
            catch (Exception ex)
            {
                Plugin.AddError(ex);
            }
        }
    }

    /// <summary>
    ///     Do not show a RAM alert for an aircraft in a hold.
    /// </summary>
    [HarmonyPatch(typeof(RDP), "CheckForRAM", typeof(RDP.RadarTrack))]
    static class CheckForRAMPatch
    {
        static bool Prefix(RDP.RadarTrack rt, ref bool __result)
        {
            if (rt.CoupledFDR is null || !Holds.ContainsKey(rt.CoupledFDR.Callsign))
                return true;

            __result = false;
            return false;
        }
    }
}
