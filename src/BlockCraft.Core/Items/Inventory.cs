namespace BlockCraft.Core.Items;

/// <summary>One stack of matching items. A zero count represents an empty slot.</summary>
public readonly record struct ItemStack(ushort ItemId, int Count)
{
    /// <summary>An empty stack value.</summary>
    public static ItemStack Empty => default;
    /// <summary>Whether this stack contains no items.</summary>
    public bool IsEmpty => Count <= 0 || ItemId == 0;
}

/// <summary>Fixed-slot inventory with deterministic stacking and removal rules.</summary>
public sealed class Inventory
{
    private readonly ItemStack[] _slots;
    private readonly ItemRegistry _items;

    /// <summary>Creates an inventory with the requested slot count.</summary>
    public Inventory(ItemRegistry items, int slotCount = 36)
    {
        _items = items ?? throw new ArgumentNullException(nameof(items));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(slotCount);
        _slots = new ItemStack[slotCount];
    }

    /// <summary>Number of inventory slots.</summary>
    public int Count => _slots.Length;
    /// <summary>Reads a slot by index.</summary>
    public ItemStack this[int index] => _slots[index];
    /// <summary>Returns an immutable snapshot of all slots.</summary>
    public IReadOnlyList<ItemStack> Snapshot() => Array.AsReadOnly((ItemStack[])_slots.Clone());

    /// <summary>Adds as many items as possible, returning the number that did not fit.</summary>
    public int Add(ushort itemId, int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        if (count == 0) return 0;
        var definition = _items[itemId];
        var remaining = count;
        for (var i = 0; i < _slots.Length && remaining > 0; i++)
        {
            var stack = _slots[i];
            if (stack.IsEmpty || stack.ItemId != itemId || stack.Count >= definition.MaxStack) continue;
            var moved = Math.Min(remaining, definition.MaxStack - stack.Count);
            _slots[i] = stack with { Count = stack.Count + moved };
            remaining -= moved;
        }
        for (var i = 0; i < _slots.Length && remaining > 0; i++)
        {
            if (!_slots[i].IsEmpty) continue;
            var moved = Math.Min(remaining, definition.MaxStack);
            _slots[i] = new ItemStack(itemId, moved);
            remaining -= moved;
        }
        return remaining;
    }

    /// <summary>Returns whether the inventory contains at least the requested quantity.</summary>
    public bool Contains(ushort itemId, int count)
    {
        if (count < 0) return false;
        return _slots.Where(stack => stack.ItemId == itemId).Sum(stack => stack.Count) >= count;
    }

    /// <summary>Removes the requested quantity atomically, returning false if it is unavailable.</summary>
    public bool Remove(ushort itemId, int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        if (!Contains(itemId, count)) return false;
        var remaining = count;
        for (var i = 0; i < _slots.Length && remaining > 0; i++)
        {
            var stack = _slots[i];
            if (stack.ItemId != itemId || stack.IsEmpty) continue;
            var removed = Math.Min(remaining, stack.Count);
            var left = stack.Count - removed;
            _slots[i] = left == 0 ? ItemStack.Empty : stack with { Count = left };
            remaining -= removed;
        }
        return true;
    }

    /// <summary>Swaps two slots.</summary>
    public void Swap(int first, int second)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(first);
        ArgumentOutOfRangeException.ThrowIfNegative(second);
        if (first >= _slots.Length || second >= _slots.Length) throw new ArgumentOutOfRangeException(nameof(first));
        (_slots[first], _slots[second]) = (_slots[second], _slots[first]);
    }

    /// <summary>Replaces a slot with a validated stack.</summary>
    public void Set(int index, ItemStack stack)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        if (index >= _slots.Length) throw new ArgumentOutOfRangeException(nameof(index));
        if (stack.Count < 0) throw new ArgumentException("Stack count cannot be negative.", nameof(stack));
        if (!stack.IsEmpty && stack.Count > _items[stack.ItemId].MaxStack)
            throw new ArgumentException("Stack count exceeds the item's limit.", nameof(stack));
        _slots[index] = stack.IsEmpty ? ItemStack.Empty : stack;
    }
}
