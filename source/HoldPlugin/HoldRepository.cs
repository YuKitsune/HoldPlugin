namespace HoldPlugin;

/// <summary>
///     Stores the active holds, and the completed holds. All access uses one lock, so you can
///     use this class from all threads. The read methods return copies, and hold items are
///     immutable, so callers cannot change the stored data directly.
/// </summary>
public class HoldRepository
{
    readonly object _lock = new();

    readonly List<HoldItem> _holds = [];

    // The hold points that the aircraft left at the end of a hold with a delay from the remarks.
    // The delay stays in the remarks, thus the plugin must not start the hold again.
    readonly HashSet<(string Callsign, string HoldPoint)> _completedHolds = [];

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

    /// <summary>
    ///     Remove the hold for the callsign, and remember that the aircraft left its hold point.
    /// </summary>
    /// <returns><c>true</c> if the hold was removed, otherwise <c>false</c>.</returns>
    public bool TryComplete(string callsign)
    {
        lock (_lock)
        {
            var hold = _holds.FirstOrDefault(h => h.Callsign == callsign);
            if (hold is null)
                return false;

            _holds.Remove(hold);
            _completedHolds.Add((callsign, hold.HoldPoint));
            return true;
        }
    }

    public bool IsCompleted(string callsign, string holdPoint)
    {
        lock (_lock)
        {
            return _completedHolds.Contains((callsign, holdPoint));
        }
    }

    /// <summary>
    ///     Forget the completed holds of the callsign at the hold points that are not in
    ///     <paramref name="holdPointsToKeep"/>, e.g. when the delay was removed from the remarks.
    /// </summary>
    public void ForgetCompletedHolds(string callsign, IEnumerable<string> holdPointsToKeep)
    {
        lock (_lock)
        {
            var keep = holdPointsToKeep.ToHashSet();
            _completedHolds.RemoveWhere(c => c.Callsign == callsign && !keep.Contains(c.HoldPoint));
        }
    }
}
