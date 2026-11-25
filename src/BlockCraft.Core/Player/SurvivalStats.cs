namespace BlockCraft.Core.Player;

/// <summary>Survival health, hunger, food saturation, regeneration, and starvation rules.</summary>
public sealed class SurvivalStats
{
    private float _exhaustion;
    private float _regenerationClock;
    private float _starvationClock;

    /// <summary>Maximum health points.</summary>
    public const float MaximumHealth = 20f;
    /// <summary>Maximum hunger points.</summary>
    public const float MaximumHunger = 20f;
    /// <summary>Current health points.</summary>
    public float Health { get; private set; } = MaximumHealth;
    /// <summary>Current hunger points.</summary>
    public float Hunger { get; private set; } = MaximumHunger;
    /// <summary>Current food saturation, consumed before hunger points.</summary>
    public float Saturation { get; private set; } = 5f;
    /// <summary>Whether the player is alive.</summary>
    public bool IsAlive => Health > 0f;

    /// <summary>Applies damage while the player is alive.</summary>
    public void Damage(float amount)
    {
        ValidateNonNegative(amount, nameof(amount));
        if (IsAlive)
            Health = Math.Max(0f, Health - amount);
    }

    /// <summary>Restores health up to the maximum.</summary>
    public void Heal(float amount)
    {
        ValidateNonNegative(amount, nameof(amount));
        if (IsAlive)
            Health = Math.Min(MaximumHealth, Health + amount);
    }

    /// <summary>Adds exhaustion; saturation is consumed before hunger.</summary>
    public void AddExhaustion(float amount)
    {
        ValidateNonNegative(amount, nameof(amount));
        _exhaustion += amount;
        while (_exhaustion >= 4f)
        {
            _exhaustion -= 4f;
            if (Saturation > 0f)
                Saturation = Math.Max(0f, Saturation - 1f);
            else
                Hunger = Math.Max(0f, Hunger - 1f);
        }
    }

    /// <summary>Consumes food and restores hunger and saturation.</summary>
    public bool ConsumeFood(int hungerPoints, float saturationPoints = 1f)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(hungerPoints);
        ValidateNonNegative(saturationPoints, nameof(saturationPoints));
        if (!IsAlive || Hunger >= MaximumHunger)
            return false;
        Hunger = Math.Min(MaximumHunger, Hunger + hungerPoints);
        Saturation = Math.Min(Hunger, Saturation + saturationPoints);
        return true;
    }

    /// <summary>Restores validated survival values from a saved world.</summary>
    public void Restore(float health, float hunger, float saturation)
    {
        ValidateNonNegative(health, nameof(health));
        ValidateNonNegative(hunger, nameof(hunger));
        ValidateNonNegative(saturation, nameof(saturation));
        Health = Math.Clamp(health, 0f, MaximumHealth);
        Hunger = Math.Clamp(hunger, 0f, MaximumHunger);
        Saturation = Math.Clamp(saturation, 0f, Hunger);
        _exhaustion = 0f;
        _regenerationClock = 0f;
        _starvationClock = 0f;
    }

    /// <summary>Applies fall damage above a three-block safe fall distance.</summary>
    public void ApplyFallDamage(float fallDistance)
    {
        ValidateNonNegative(fallDistance, nameof(fallDistance));
        if (fallDistance > 3f)
            Damage((fallDistance - 3f) * 2f);
    }

    /// <summary>Advances hunger, natural regeneration, and starvation timers.</summary>
    public void Update(float elapsedSeconds, bool moving, bool sprinting)
    {
        if (!float.IsFinite(elapsedSeconds) || elapsedSeconds <= 0f || !IsAlive)
            return;

        if (moving)
            AddExhaustion(elapsedSeconds * (sprinting ? 0.025f : 0.005f));

        if (Hunger >= 18f && Health < MaximumHealth)
        {
            _regenerationClock += elapsedSeconds;
            while (_regenerationClock >= 4f && Health < MaximumHealth && Hunger >= 18f)
            {
                _regenerationClock -= 4f;
                Heal(1f);
                AddExhaustion(6f);
            }
        }
        else
        {
            _regenerationClock = 0f;
        }

        if (Hunger <= 0f)
        {
            _starvationClock += elapsedSeconds;
            while (_starvationClock >= 4f && Health > 1f)
            {
                _starvationClock -= 4f;
                Damage(1f);
            }
        }
        else
        {
            _starvationClock = 0f;
        }
    }

    private static void ValidateNonNegative(float value, string parameterName)
    {
        if (!float.IsFinite(value) || value < 0f)
            throw new ArgumentOutOfRangeException(parameterName, "Value must be finite and non-negative.");
    }
}
