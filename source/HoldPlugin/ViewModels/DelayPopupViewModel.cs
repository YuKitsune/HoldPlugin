using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using HoldPlugin.Contracts;

namespace HoldPlugin.ViewModels;

public partial class DelayPopupViewModel(string callsign, string waypointName, TimeSpan? delay) : ObservableObject
{
    [ObservableProperty] string _waypointName = waypointName;
    [ObservableProperty] string _delayDuration = FormatDuration(delay);

    /// <summary>
    ///     Set the delay at the waypoint in the remarks of the flight plan. A delay of zero
    ///     (e.g. 0000) removes the delay.
    /// </summary>
    [RelayCommand]
    void Submit()
    {
        var newDelay = ParseDuration(DelayDuration);
        if (newDelay is null || newDelay < TimeSpan.Zero)
            return;

        WeakReferenceMessenger.Default.Send(new SetHoldDelayCommand(callsign, WaypointName, newDelay.Value));
    }

    // The format is HHMM.
    static string FormatDuration(TimeSpan? duration)
    {
        if (duration is null || duration.Value <= TimeSpan.Zero)
        {
            return string.Empty;
        }

        // The times can have seconds. Round to the nearest minute.
        var totalMinutes = (int)Math.Round(duration.Value.TotalMinutes);
        return $"{totalMinutes / 60:00}{totalMinutes % 60:00}";
    }

    // The format is HHMM. One or two digits are minutes only. Returns null if the format is not correct.
    static TimeSpan? ParseDuration(string durationString)
    {
        if (string.IsNullOrEmpty(durationString))
        {
            return null;
        }

        if (durationString.Length <= 2 && int.TryParse(durationString, out var minutes))
        {
            return TimeSpan.FromMinutes(minutes);
        }

        if (durationString.Length <= 2)
        {
            return null;
        }

        var minutesPart = durationString.Substring(durationString.Length - 2, 2);
        var hoursPart = durationString.Substring(0, durationString.Length - 2);
        if (!int.TryParse(minutesPart, out minutes) || !int.TryParse(hoursPart, out var hours))
        {
            return null;
        }

        var totalMinutes = minutes + hours * 60;
        return TimeSpan.FromMinutes(totalMinutes);
    }
}
