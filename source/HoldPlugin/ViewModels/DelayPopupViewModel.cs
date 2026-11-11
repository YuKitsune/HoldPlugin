using CommunityToolkit.Mvvm.ComponentModel;

namespace HoldPlugin.ViewModels;

public partial class DelayPopupViewModel : ObservableObject
{
    [ObservableProperty] string _waypointName;
    [ObservableProperty] string _delayDuration;

    TimeSpan ParseDuration(string durationString)
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
        
        var minutesPart = durationString.Substring(durationString.Length - 2, durationString.Length);
        var hoursPart = durationString.Substring(0, durationString.Length - 2);
        if (!int.TryParse(minutesPart, out minutes) || !int.TryParse(hoursPart, out var hours))
        {
            return TimeSpan.Zero;
        }

        var totalMinutes = minutes + hours * 60;
        return TimeSpan.FromMinutes(totalMinutes);
    }
}