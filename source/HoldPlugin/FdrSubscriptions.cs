using System.ComponentModel;
using HoldPlugin.Contracts;
using MediatR;
using vatsys;

namespace HoldPlugin;

/// <summary>
///     Publishes a <see cref="FDRPropertyChangedNotification"/> when a property of an FDR changes.
///     Call the methods from the work queue.
/// </summary>
public class FdrSubscriptions(IMediator mediator)
{
    readonly Dictionary<string, FDP2.FDR> _subscribedFDRs = new();

    public FDP2.FDR[] SubscribeToAllFDRs()
    {
        // Unsubscribe from all previous FDRs (snapshot to avoid enumeration issues)
        foreach (var fdr in _subscribedFDRs.Values.ToArray())
        {
            fdr.PropertyChanged -= OnFDRPropertyChanged;
        }

        _subscribedFDRs.Clear();

        // Subscribe to all current FDRs (snapshot to avoid enumeration issues)
        foreach (var fdr in FDP2.GetFDRs.ToArray())
        {
            _subscribedFDRs[fdr.Callsign] = fdr;
            fdr.PropertyChanged += OnFDRPropertyChanged;
        }

        return _subscribedFDRs.Values.ToArray();
    }

    void OnFDRPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not FDP2.FDR fdr)
            return;

        _ = mediator.Publish(new FDRPropertyChangedNotification(fdr, e.PropertyName));
    }
}
