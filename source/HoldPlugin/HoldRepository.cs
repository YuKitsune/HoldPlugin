namespace HoldPlugin;

/// <summary>
///     Stores the active holds. All access uses one lock, so you can use this class from all
///     threads. The read methods return copies, and hold items are immutable, so callers cannot
///     change the stored data directly.
/// </summary>
public class HoldRepository
{
    readonly object _lock = new();

    readonly List<HoldItem> _holds = [];

    public HoldItem[] GetHolds()
    {
        lock (_lock)
        {
            return _holds.ToArray();
        }
    }

    public HoldItem? Find(string callsign)
    {
        lock (_lock)
        {
            return _holds.FirstOrDefault(h => h.Callsign == callsign);
        }
    }

    public void Add(HoldItem hold)
    {
        lock (_lock)
        {
            _holds.Add(hold);
        }
    }

    /// <summary>
    ///     Replace the hold for the callsign with the result of <paramref name="update"/>.
    ///     Does nothing if there is no hold for the callsign.
    /// </summary>
    public void Update(string callsign, Func<HoldItem, HoldItem> update)
    {
        lock (_lock)
        {
            var index = _holds.FindIndex(h => h.Callsign == callsign);
            if (index == -1)
                return;

            _holds[index] = update(_holds[index]);
        }
    }

    /// <summary>
    ///     Remove the hold for the callsign.
    /// </summary>
    /// <returns><c>true</c> if the hold was removed, otherwise <c>false</c>.</returns>
    public bool TryRemove(string callsign)
    {
        lock (_lock)
        {
            return _holds.RemoveAll(h => h.Callsign == callsign) > 0;
        }
    }
}
