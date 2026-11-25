using System.Numerics;
using BlockCraft.Core.Player;
using Xunit;

namespace BlockCraft.Tests;

public sealed class VirtualJoystickTests
{
    [Fact]
    public void PointerOutsideHitAreaCannotCaptureTheStick()
    {
        var joystick = new VirtualJoystick(new Vector2(100, 500), 80);
        Assert.False(joystick.TryBegin(4, new Vector2(300, 500)));
        Assert.False(joystick.IsActive);
    }

    [Fact]
    public void DragProducesAnalogForwardAndSidewaysMovement()
    {
        var joystick = new VirtualJoystick(new Vector2(100, 500), 80);
        Assert.True(joystick.TryBegin(1, new Vector2(100, 500)));
        Assert.True(joystick.UpdatePointer(1, new Vector2(100, 420)));
        Assert.Equal(1f, joystick.Axis.Y, 3);
        Assert.Equal(0f, joystick.Axis.X, 3);
        Assert.True(joystick.UpdatePointer(1, new Vector2(180, 500)));
        Assert.Equal(1f, joystick.Axis.X, 3);
        Assert.Equal(0f, joystick.Axis.Y, 3);
    }

    [Fact]
    public void ThumbTravelAndOutputAreClampedToTheJoystickRadius()
    {
        var joystick = new VirtualJoystick(Vector2.Zero, 50);
        joystick.TryBegin(2, Vector2.Zero);
        joystick.UpdatePointer(2, new Vector2(100, -100));

        Assert.Equal(50f, joystick.KnobPosition.Length(), 3);
        Assert.InRange(joystick.Axis.Length(), 0.99f, 1.42f);
    }

    [Fact]
    public void DeadZonePreventsTinyMovement()
    {
        var joystick = new VirtualJoystick(Vector2.Zero, 100, 0.2f);
        joystick.TryBegin(3, Vector2.Zero);
        joystick.UpdatePointer(3, new Vector2(10, -10));
        Assert.Equal(Vector2.Zero, joystick.Axis);
    }

    [Fact]
    public void ReleasingTheOwningPointerSnapsStickBackToCenter()
    {
        var joystick = new VirtualJoystick(new Vector2(90, 300), 60);
        joystick.TryBegin(7, new Vector2(90, 300));
        joystick.UpdatePointer(7, new Vector2(90, 240));
        Assert.NotEqual(Vector2.Zero, joystick.Axis);

        Assert.True(joystick.Release(7));
        Assert.False(joystick.IsActive);
        Assert.Equal(Vector2.Zero, joystick.Axis);
        Assert.Equal(joystick.Center, joystick.KnobPosition);
    }

    [Fact]
    public void UnrelatedPointerCannotMoveOrReleaseActiveStick()
    {
        var joystick = new VirtualJoystick(Vector2.Zero, 60);
        joystick.TryBegin(1, Vector2.Zero);
        Assert.False(joystick.UpdatePointer(2, new Vector2(0, -60)));
        Assert.False(joystick.Release(2));
        Assert.True(joystick.IsActive);
    }

    [Fact]
    public void ReconfiguringTheScreenAnchorKeepsAnActiveThumbInBounds()
    {
        var joystick = new VirtualJoystick(Vector2.Zero, 50);
        joystick.TryBegin(1, new Vector2(20, -20));
        joystick.Configure(new Vector2(100, 200), 30);
        Assert.True(Vector2.Distance(joystick.Center, joystick.KnobPosition) <= 30.001f);
    }
}
