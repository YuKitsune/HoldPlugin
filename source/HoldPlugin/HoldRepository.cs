namespace HoldPlugin;

/// <summary>
///     Stores the active holds and the hold point slots. All access uses one lock, so you can
///     use this class from all threads. The read methods return copies, and hold items are
///     immutable, so callers cannot change the stored data directly.
/// </summary>
public class HoldRepository
{
    readonly object _lock = new();

    readonly List<HoldItem> _holds = [];

    readonly IHoldPointDescriptor[] _slots =
    [
        new Unallocated(),
        new Unallocated(),
        new Unallocated(),
        new Unallocated()
    ];

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

    /// <summary>
    ///     Add the hold. If no slot has the hold point, put the hold point in an empty slot.
    /// </summary>
    /// <returns><c>true</c> if a slot was allocated, otherwise <c>false</c>.</returns>
    public bool Add(HoldItem hold)
    {
        lock (_lock)
        {
            _holds.Add(hold);

            if (_slots.Any(d => d.Matches(hold.HoldPoint)))
                return false;

            var emptySlotIndex = Array.FindIndex(_slots, d => d is Unallocated);
            if (emptySlotIndex == -1)
                return false;

            _slots[emptySlotIndex] = new AutoAllocated(hold.HoldPoint);
            return true;
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
    ///     Remove the hold for the callsign. If the hold point has an automatically allocated
    ///     slot and no other holds use it, free the slot.
    /// </summary>
    /// <returns><c>true</c> if the hold was removed, otherwise <c>false</c>.</returns>
    public bool TryRemove(string callsign, out bool slotFreed)
    {
        lock (_lock)
        {
            slotFreed = false;

            var hold = _holds.FirstOrDefault(h => h.Callsign == callsign);
            if (hold is null)
                return false;

            _holds.Remove(hold);

            if (_holds.Any(h => h.HoldPoint == hold.HoldPoint))
                return true;

            var slotIndex = Array.FindIndex(_slots, d => d is AutoAllocated && d.Matches(hold.HoldPoint));
            if (slotIndex == -1)
                return true;

            _slots[slotIndex] = new Unallocated();
            slotFreed = true;
            return true;
        }
    }

    public IHoldPointDescriptor[] GetSlots()
    {
        lock (_lock)
        {
            return _slots.ToArray();
        }
    }

    /// <summary>
    ///     Put the hold point in the slot at <paramref name="index"/>. Does nothing if a slot
    ///     already has the hold point.
    /// </summary>
    /// <returns><c>true</c> if the slot was allocated, otherwise <c>false</c>.</returns>
    public bool TryAllocateSlot(int index, string holdPointName)
    {
        lock (_lock)
        {
            if (_slots.Any(d => d.Matches(holdPointName)))
                return false;

            _slots[index] = new ManuallyAllocated(holdPointName);
            return true;
        }
    }

    /// <summary>
    ///     Remove the hold point from its slot.
    /// </summary>
    /// <returns><c>true</c> if the slot was freed, otherwise <c>false</c>.</returns>
    public bool TryFreeSlot(string holdPointName)
    {
        lock (_lock)
        {
            var index = Array.FindIndex(_slots, d => d.Matches(holdPointName));
            if (index == -1)
                return false;

            _slots[index] = new Unallocated();
            return true;
        }
    }
}
