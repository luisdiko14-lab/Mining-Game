using System.Text.Json;

namespace BlockCraft.Core.Items;

/// <summary>One furnace transformation from an input item into an output stack.</summary>
public sealed record SmeltingRecipe(ushort InputItemId, ItemStack Result, float DurationSeconds);

/// <summary>Validated lookup of smelting recipes keyed by input item id.</summary>
public sealed class SmeltingRecipeRegistry
{
    private readonly Dictionary<ushort, SmeltingRecipe> _byInput;
    private SmeltingRecipeRegistry(IEnumerable<SmeltingRecipe> recipes)
    {
        _byInput = [];
        foreach (var recipe in recipes)
        {
            if (recipe.DurationSeconds <= 0f || !float.IsFinite(recipe.DurationSeconds) || recipe.Result.IsEmpty)
                throw new InvalidDataException("Smelting recipes need a positive duration and non-empty output.");
            if (!_byInput.TryAdd(recipe.InputItemId, recipe))
                throw new InvalidDataException($"Duplicate smelting input id {recipe.InputItemId}.");
        }
    }
    /// <summary>Number of loaded smelting recipes.</summary>
    public int Count => _byInput.Count;
    /// <summary>Finds a recipe by input item id.</summary>
    public bool TryGet(ushort inputItemId, out SmeltingRecipe recipe) => _byInput.TryGetValue(inputItemId, out recipe!);
    /// <summary>Loads smelting recipes from a JSON file.</summary>
    public static SmeltingRecipeRegistry LoadFromFile(string path, ItemRegistry items)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return LoadFromJson(File.ReadAllText(path), items);
    }
    /// <summary>Loads smelting recipes from JSON text and resolves item names.</summary>
    public static SmeltingRecipeRegistry LoadFromJson(string json, ItemRegistry items)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentNullException.ThrowIfNull(items);
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Smelting recipe file root must be an array.");
        var recipes = new List<SmeltingRecipe>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            var inputName = element.GetProperty("input").GetString() ?? throw new InvalidDataException("Smelting input must be an item name.");
            var outputName = element.GetProperty("output").GetString() ?? throw new InvalidDataException("Smelting output must be an item name.");
            var inputId = items.GetId(inputName);
            var outputId = items.GetId(outputName);
            var count = element.GetProperty("count").GetInt32();
            var duration = element.GetProperty("durationSeconds").GetSingle();
            if (count < 1 || count > items[outputId].MaxStack)
                throw new InvalidDataException($"Smelting result '{outputName}' has an invalid stack size.");
            recipes.Add(new SmeltingRecipe(inputId, new ItemStack(outputId, count), duration));
        }
        return new SmeltingRecipeRegistry(recipes);
    }
}

/// <summary>Persistable state of the three furnace slots and active timers.</summary>
public sealed record FurnaceSnapshot(ItemStack Input, ItemStack Fuel, ItemStack Output,
    float BurnRemainingSeconds, float BurnDurationSeconds, float SmeltProgressSeconds);

/// <summary>One persistent furnace with fuel consumption, smelting progress, and output capacity.</summary>
public sealed class FurnaceState
{
    private readonly ItemRegistry _items;
    private readonly SmeltingRecipeRegistry _recipes;
    /// <summary>Creates an empty furnace using validated registries.</summary>
    public FurnaceState(ItemRegistry items, SmeltingRecipeRegistry recipes)
    {
        _items = items ?? throw new ArgumentNullException(nameof(items));
        _recipes = recipes ?? throw new ArgumentNullException(nameof(recipes));
    }
    /// <summary>Input slot.</summary>
    public ItemStack Input { get; private set; }
    /// <summary>Fuel slot.</summary>
    public ItemStack Fuel { get; private set; }
    /// <summary>Output slot.</summary>
    public ItemStack Output { get; private set; }
    /// <summary>Remaining active fuel time in seconds.</summary>
    public float BurnRemainingSeconds { get; private set; }
    /// <summary>Full duration of the currently burning fuel item.</summary>
    public float BurnDurationSeconds { get; private set; }
    /// <summary>Current smelting progress in seconds.</summary>
    public float SmeltProgressSeconds { get; private set; }

    /// <summary>Moves matching items into the input slot and returns any remainder.</summary>
    public ItemStack InsertInput(ItemStack stack)
    {
        var slot = Input;
        var rest = Insert(ref slot, stack);
        Input = slot;
        return rest;
    }

    /// <summary>Moves burnable items into the fuel slot and returns any remainder.</summary>
    public ItemStack InsertFuel(ItemStack stack)
    {
        var slot = Fuel;
        var rest = Insert(ref slot, stack, requireFuel: true);
        Fuel = slot;
        return rest;
    }

    /// <summary>Moves output into inventory and retains any overflow in the furnace.</summary>
    public bool TakeOutput(Inventory inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        if (Output.IsEmpty) return false;
        var overflow = inventory.Add(Output.ItemId, Output.Count);
        var accepted = Output.Count - overflow;
        if (accepted == 0) return false;
        Output = overflow == 0 ? ItemStack.Empty : Output with { Count = overflow };
        return true;
    }

    /// <summary>Advances fuel burning and smelting using elapsed real-time seconds.</summary>
    public void Update(float elapsedSeconds)
    {
        if (!float.IsFinite(elapsedSeconds) || elapsedSeconds <= 0f) return;
        var remaining = Math.Min(elapsedSeconds, 3600f);
        while (remaining > 0f)
        {
            var ready = TryGetReadyRecipe(out var recipe);
            if (BurnRemainingSeconds <= 0f)
            {
                if (!ready || Fuel.IsEmpty) return;
                var fuelDefinition = _items[Fuel.ItemId];
                if (fuelDefinition.BurnTimeSeconds <= 0f) return;
                BurnDurationSeconds = fuelDefinition.BurnTimeSeconds;
                BurnRemainingSeconds = BurnDurationSeconds;
                Fuel = Fuel.Count == 1 ? ItemStack.Empty : Fuel with { Count = Fuel.Count - 1 };
            }
            var step = Math.Min(remaining, BurnRemainingSeconds);
            remaining -= step;
            BurnRemainingSeconds -= step;
            if (!ready) { SmeltProgressSeconds = 0f; continue; }
            SmeltProgressSeconds += step;
            while (SmeltProgressSeconds >= recipe.DurationSeconds && CanAcceptOutput(recipe.Result))
            {
                Input = Input.Count == 1 ? ItemStack.Empty : Input with { Count = Input.Count - 1 };
                Output = Output.IsEmpty ? recipe.Result : Output with { Count = Output.Count + recipe.Result.Count };
                SmeltProgressSeconds -= recipe.DurationSeconds;
                if (Input.IsEmpty || !_recipes.TryGet(Input.ItemId, out recipe))
                {
                    SmeltProgressSeconds = 0f;
                    break;
                }
            }
        }
    }

    /// <summary>Restores a snapshot after validating slots, fuel type, and timers.</summary>
    public void Restore(FurnaceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ValidateStack(snapshot.Input);
        ValidateStack(snapshot.Fuel);
        ValidateStack(snapshot.Output);
        if (!snapshot.Fuel.IsEmpty && _items[snapshot.Fuel.ItemId].BurnTimeSeconds <= 0f)
            throw new InvalidDataException("Saved furnace fuel slot contains a non-fuel item.");
        if (!float.IsFinite(snapshot.BurnRemainingSeconds) || snapshot.BurnRemainingSeconds < 0f ||
            !float.IsFinite(snapshot.BurnDurationSeconds) || snapshot.BurnDurationSeconds < 0f ||
            !float.IsFinite(snapshot.SmeltProgressSeconds) || snapshot.SmeltProgressSeconds < 0f)
            throw new InvalidDataException("Saved furnace timers are invalid.");
        Input = snapshot.Input;
        Fuel = snapshot.Fuel;
        Output = snapshot.Output;
        BurnRemainingSeconds = snapshot.BurnRemainingSeconds;
        BurnDurationSeconds = snapshot.BurnDurationSeconds;
        SmeltProgressSeconds = snapshot.SmeltProgressSeconds;
    }

    /// <summary>Creates a value snapshot suitable for world-save serialization.</summary>
    public FurnaceSnapshot CreateSnapshot() =>
        new(Input, Fuel, Output, BurnRemainingSeconds, BurnDurationSeconds, SmeltProgressSeconds);

    private bool TryGetReadyRecipe(out SmeltingRecipe recipe)
    {
        if (!Input.IsEmpty && _recipes.TryGet(Input.ItemId, out recipe!) && CanAcceptOutput(recipe.Result))
            return true;
        recipe = null!;
        return false;
    }

    private ItemStack Insert(ref ItemStack slot, ItemStack stack, bool requireFuel = false)
    {
        if (stack.IsEmpty) return ItemStack.Empty;
        var definition = _items[stack.ItemId];
        if (requireFuel && definition.BurnTimeSeconds <= 0f) return stack;
        if (!slot.IsEmpty && slot.ItemId != stack.ItemId) return stack;
        var capacity = definition.MaxStack - (slot.IsEmpty ? 0 : slot.Count);
        var moved = Math.Min(capacity, stack.Count);
        if (moved <= 0) return stack;
        slot = new ItemStack(stack.ItemId, (slot.IsEmpty ? 0 : slot.Count) + moved);
        return moved == stack.Count ? ItemStack.Empty : stack with { Count = stack.Count - moved };
    }

    private bool CanAcceptOutput(ItemStack result) => Output.IsEmpty
        ? result.Count <= _items[result.ItemId].MaxStack
        : Output.ItemId == result.ItemId && Output.Count + result.Count <= _items[result.ItemId].MaxStack;

    private void ValidateStack(ItemStack stack)
    {
        if (stack.Count < 0) throw new InvalidDataException("Saved furnace item count is negative.");
        if (!stack.IsEmpty && stack.Count > _items[stack.ItemId].MaxStack)
            throw new InvalidDataException("Saved furnace stack exceeds its maximum size.");
    }
}
