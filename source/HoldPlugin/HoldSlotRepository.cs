namespace HoldPlugin;

/// <summary>
///     Stores the hold point slots. All access uses one lock, so you can use this class from all
///     threads. The read methods return copies, so callers cannot change the stored data directly.
/// </summary>
public class HoldSlotRepository
{
    readonly object _lock = new();

    readonly IHoldPointDescriptor[] _slots =
    [
        new Unallocated(),
        new Unallocated(),
        new Unallocated(),
        new Unallocated()
    ];

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
    ///     Put the hold point in an empty slot. Does nothing if a slot already has the hold point,
    ///     or if no slot is empty.
    /// </summary>
    /// <returns><c>true</c> if a slot was allocated, otherwise <c>false</c>.</returns>
    public bool TryAutoAllocateSlot(string holdPointName)
    {
        lock (_lock)
        {
            if (_slots.Any(d => d.Matches(holdPointName)))
                return false;

            var emptySlotIndex = Array.FindIndex(_slots, d => d is Unallocated);
            if (emptySlotIndex == -1)
                return false;

            _slots[emptySlotIndex] = new AutoAllocated(holdPointName);
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

    /// <summary>
    ///     Remove the hold point from its slot, only if the slot was allocated automatically.
    /// </summary>
    /// <returns><c>true</c> if the slot was freed, otherwise <c>false</c>.</returns>
    public bool TryFreeAutoAllocatedSlot(string holdPointName)
    {
        lock (_lock)
        {
            var index = Array.FindIndex(_slots, d => d is AutoAllocated && d.Matches(holdPointName));
            if (index == -1)
                return false;

            _slots[index] = new Unallocated();
            return true;
        }
    }
}
