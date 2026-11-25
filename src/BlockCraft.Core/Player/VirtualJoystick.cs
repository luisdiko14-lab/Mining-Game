using System.Numerics;

namespace BlockCraft.Core.Player;

/// <summary>Radial virtual joystick with a fixed anchor, clamped travel, and a configurable dead zone.</summary>
public sealed class VirtualJoystick
{
    private Vector2 _center;
    private Vector2 _knobPosition;
    private Vector2 _axis;
    private Vector2 _lastPointerPosition;
    private int? _pointerId;

    /// <summary>Creates a virtual joystick at a screen-space anchor.</summary>
    public VirtualJoystick(Vector2 center, float radius, float deadZone = 0.14f)
    {
        ValidateRadius(radius);
        ValidateDeadZone(deadZone);
        _center = center;
        Radius = radius;
        DeadZone = deadZone;
        _knobPosition = center;
    }

    /// <summary>Screen-space center of the joystick base.</summary>
    public Vector2 Center => _center;
    /// <summary>Current screen-space center of the movable thumb.</summary>
    public Vector2 KnobPosition => _knobPosition;
    /// <summary>Maximum thumb travel radius in screen units.</summary>
    public float Radius { get; private set; }
    /// <summary>Fraction of travel ignored to prevent accidental movement.</summary>
    public float DeadZone { get; }
    /// <summary>Whether a pointer currently owns the joystick.</summary>
    public bool IsActive => _pointerId.HasValue;
    /// <summary>Identifier of the active pointer, or null when released.</summary>
    public int? PointerId => _pointerId;
    /// <summary>Analog movement where positive Y means forward.</summary>
    public Vector2 Axis => _axis;

    /// <summary>Updates the screen anchor and radius after a resolution or orientation change.</summary>
    public void Configure(Vector2 center, float radius)
    {
        ValidateRadius(radius);
        _center = center;
        Radius = radius;
        if (_pointerId.HasValue)
            UpdatePointer(_pointerId.Value, _lastPointerPosition);
        else
            ResetThumb();
    }

    /// <summary>Attempts to capture a new pointer when it begins inside the joystick hit area.</summary>
    public bool TryBegin(int pointerId, Vector2 position)
    {
        if (_pointerId.HasValue || Vector2.DistanceSquared(position, _center) > Radius * Radius * 2.25f)
            return false;
        _pointerId = pointerId;
        _lastPointerPosition = position;
        UpdateThumb(position);
        return true;
    }

    /// <summary>Moves the active pointer. Pointers that do not own the stick are ignored.</summary>
    public bool UpdatePointer(int pointerId, Vector2 position)
    {
        if (_pointerId != pointerId)
            return false;
        _lastPointerPosition = position;
        UpdateThumb(position);
        return true;
    }

    /// <summary>Releases the owning pointer and snaps the thumb and movement axes to zero.</summary>
    public bool Release(int pointerId)
    {
        if (_pointerId != pointerId)
            return false;
        _pointerId = null;
        _axis = Vector2.Zero;
        ResetThumb();
        return true;
    }

    /// <summary>Cancels any active pointer, resetting the joystick to its neutral position.</summary>
    public void Reset()
    {
        _pointerId = null;
        _axis = Vector2.Zero;
        ResetThumb();
    }

    private void UpdateThumb(Vector2 position)
    {
        var offset = position - _center;
        var distance = offset.Length();
        if (distance > Radius)
            offset = Vector2.Normalize(offset) * Radius;

        _knobPosition = _center + offset;
        var magnitude = Math.Min(1f, distance / Radius);
        if (magnitude <= DeadZone)
        {
            _axis = Vector2.Zero;
            return;
        }

        var adjustedMagnitude = (magnitude - DeadZone) / (1f - DeadZone);
        var direction = distance > 0f ? offset / Math.Min(distance, Radius) : Vector2.Zero;
        _axis = new Vector2(direction.X, -direction.Y) * adjustedMagnitude;
    }

    private void ResetThumb() => _knobPosition = _center;

    private static void ValidateRadius(float radius)
    {
        if (!float.IsFinite(radius) || radius <= 0f)
            throw new ArgumentOutOfRangeException(nameof(radius), "Joystick radius must be finite and positive.");
    }

    private static void ValidateDeadZone(float deadZone)
    {
        if (!float.IsFinite(deadZone) || deadZone is < 0f or >= 1f)
            throw new ArgumentOutOfRangeException(nameof(deadZone), "Dead zone must be in the range [0, 1).");
    }
}
