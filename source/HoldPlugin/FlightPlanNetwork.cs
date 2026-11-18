using System.Reflection;
using HarmonyLib;
using vatsys;

namespace HoldPlugin;

public static class FlightPlanNetwork
{
    static readonly FieldInfo NetworkInstanceField = AccessTools.Field(typeof(Network), "Instance");
    static readonly MethodInfo SendFlightPlanChangeMethod = AccessTools.Method(typeof(Network), "SendFlightPlanChange");

    /// <summary>
    ///     Send the flight plan to the network, the same as vatSys does after a change. vatSys
    ///     sends it only when the controller has permission. The method is internal in vatSys.
    /// </summary>
    public static void SendFlightPlanChange(FDP2.FDR fdr)
    {
        var network = NetworkInstanceField.GetValue(null);
        SendFlightPlanChangeMethod.Invoke(network, [fdr]);
    }
}
