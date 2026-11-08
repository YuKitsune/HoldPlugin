namespace HoldPlugin;

/// <summary>
///     Stores the aircraft that left a hold, but did not cross the hold point again (callsign to
///     hold point name). For these aircraft, the ETO of the hold point is calculated from the
///     radar position. All access uses one lock, so you can use this class from all threads.
/// </summary>
public class HoldExitRepository
{
    readonly object _lock = new();

    readonly Dictionary<string, string> _exits = new();

    public bool Contains(string callsign)
    {
        lock (_lock)
        {
            return _exits.ContainsKey(callsign);
        }
    }

    public bool TryGet(string callsign, out string holdPoint)
    {
        lock (_lock)
        {
            return _exits.TryGetValue(callsign, out holdPoint);
        }
    }

    public void Set(string callsign, string holdPoint)
    {
        lock (_lock)
        {
            _exits[callsign] = holdPoint;
        }
    }

    public void Remove(string callsign)
    {
        lock (_lock)
        {
            _exits.Remove(callsign);
        }
    }

    /// <summary>
    ///     Remove the exits for the callsigns that are not in <paramref name="callsigns"/>.
    /// </summary>
    public void RemoveAllExcept(IReadOnlyCollection<string> callsigns)
    {
        lock (_lock)
        {
            foreach (var callsign in _exits.Keys.ToArray())
            {
                if (!callsigns.Contains(callsign))
                    _exits.Remove(callsign);
            }
        }
    }
}
