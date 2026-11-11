using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using HoldPlugin.Contracts;

namespace HoldPlugin.ViewModels;

public partial class DelayPopupViewModel(string callsign, string waypointName, DateTime arrivalTime) : ObservableObject
{
    [ObservableProperty] string _waypointName = waypointName;
    [ObservableProperty] string _delayDuration = string.Empty;

    /// <summary>
    ///     Initiate a hold at the waypoint, with an exit time of the arrival time plus the delay.
    ///     If the aircraft already holds at the waypoint, change the exit time.
    /// </summary>
    [RelayCommand]
    void Submit()
    {
        var delay = ParseDuration(DelayDuration);
        if (delay <= TimeSpan.Zero)
            return;

        WeakReferenceMessenger.Default.Send(new SetHoldDelayCommand(callsign, WaypointName, arrivalTime + delay));
    }

    // The format is HHMM. One or two digits are minutes only.
    static TimeSpan ParseDuration(string durationString)
    {
        if (string.IsNullOrEmpty(durationString))
        {
            return TimeSpan.Zero;
        }

        if (durationString.Length <= 2 && int.TryParse(durationString, out var minutes))
        {
            return TimeSpan.FromMinutes(minutes);
        }

        if (durationString.Length <= 2)
        {
            return TimeSpan.Zero;
        }

        var minutesPart = durationString.Substring(durationString.Length - 2, 2);
        var hoursPart = durationString.Substring(0, durationString.Length - 2);
        if (!int.TryParse(minutesPart, out minutes) || !int.TryParse(hoursPart, out var hours))
        {
            return TimeSpan.Zero;
        }

        var totalMinutes = minutes + hours * 60;
        return TimeSpan.FromMinutes(totalMinutes);
    }
}
