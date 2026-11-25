using System.Numerics;
using BlockCraft.Core.Blocks;
using BlockCraft.Core.World;

namespace BlockCraft.Core.Player;

/// <summary>First-person player movement and voxel collision state.</summary>
public sealed class PlayerController
{
    private const float Radius = 0.30f;
    private const float Height = 1.80f;
    private readonly BlockRegistry _blocks;

    /// <summary>Creates a player controller bound to the block registry.</summary>
    public PlayerController(BlockRegistry blocks, Vector3 spawnPosition)
    {
        _blocks = blocks ?? throw new ArgumentNullException(nameof(blocks));
        Position = spawnPosition;
    }

    /// <summary>Player feet position in world coordinates.</summary>
    public Vector3 Position { get; private set; }
    /// <summary>Current velocity in blocks per second.</summary>
    public Vector3 Velocity { get; private set; }
    /// <summary>Horizontal view rotation in radians.</summary>
    public float Yaw { get; set; }
    /// <summary>Vertical view rotation in radians.</summary>
    public float Pitch { get; set; }
    /// <summary>Whether the player is touching the ground.</summary>
    public bool IsGrounded { get; private set; }
    /// <summary>Creative flight mode.</summary>
    public bool IsFlying { get; set; }

    /// <summary>Updates movement, gravity, and axis-separated voxel collision.</summary>
    public void Update(ChunkManager world, Vector2 input, float elapsedSeconds, bool jump, bool sprint, bool sneak)
    {
        ArgumentNullException.ThrowIfNull(world);
        var dt = Math.Clamp(elapsedSeconds, 0f, 0.05f);
        var forward = new Vector3(-MathF.Sin(Yaw), 0, -MathF.Cos(Yaw));
        var right = new Vector3(MathF.Cos(Yaw), 0, -MathF.Sin(Yaw));
        var wish = forward * input.Y + right * input.X;
        if (wish.LengthSquared() > 1f) wish = Vector3.Normalize(wish);
        var speed = sneak ? 2f : sprint ? 8.5f : 5f;

        if (IsFlying)
        {
            Position += wish * speed * dt;
            Position += Vector3.UnitY * ((jump ? 1f : 0f) - (sneak ? 1f : 0f)) * speed * dt;
            Velocity = Vector3.Zero; IsGrounded = false;
            return;
        }

        Velocity = new Vector3(wish.X * speed, Velocity.Y, wish.Z * speed);
        if (jump && IsGrounded) { Velocity = new Vector3(Velocity.X, 8.6f, Velocity.Z); IsGrounded = false; }
        Velocity = new Vector3(Velocity.X, Math.Max(-48f, Velocity.Y - 24f * dt), Velocity.Z);
        var movement = Velocity * dt;
        MoveAxis(world, movement.X, 0);
        MoveAxis(world, movement.Y, 1);
        MoveAxis(world, movement.Z, 2);
    }

    /// <summary>Places the player at a new position and clears movement state.</summary>
    public void Teleport(Vector3 position) { Position = position; Velocity = Vector3.Zero; IsGrounded = false; }

    private void MoveAxis(ChunkManager world, float amount, int axis)
    {
        if (amount == 0) return;
        var candidate = Position;
        if (axis == 0) candidate.X += amount;
        else if (axis == 1) candidate.Y += amount;
        else candidate.Z += amount;
        if (!Collides(world, candidate)) { Position = candidate; return; }

        var low = 0f; var high = 1f;
        for (var i = 0; i < 12; i++)
        {
            var middle = (low + high) * 0.5f;
            var probe = Vector3.Lerp(Position, candidate, middle);
            if (Collides(world, probe)) high = middle; else low = middle;
        }
        Position = Vector3.Lerp(Position, candidate, Math.Max(0, low - 0.0001f));
        if (axis == 1)
        {
            if (amount < 0) IsGrounded = true;
            Velocity = new Vector3(Velocity.X, 0, Velocity.Z);
        }
    }

    private bool Collides(ChunkManager world, Vector3 feet)
    {
        var minX = (int)MathF.Floor(feet.X - Radius); var maxX = (int)MathF.Floor(feet.X + Radius);
        var minY = (int)MathF.Floor(feet.Y + 0.001f); var maxY = (int)MathF.Floor(feet.Y + Height - 0.001f);
        var minZ = (int)MathF.Floor(feet.Z - Radius); var maxZ = (int)MathF.Floor(feet.Z + Radius);
        for (var y = minY; y <= maxY; y++)
        for (var z = minZ; z <= maxZ; z++)
        for (var x = minX; x <= maxX; x++)
        {
            var block = world.GetBlock(x, y, z);
            if (block != 0 && _blocks[block].Solid) return true;
        }
        return false;
    }
}

/// <summary>Voxel DDA ray casting from a camera origin.</summary>
public static class BlockRaycaster
{
    /// <summary>Finds the first solid block hit along a ray, or null when nothing is hit.</summary>
    public static RayHit? Cast(ChunkManager world, BlockRegistry blocks, Vector3 origin, Vector3 direction, float maxDistance)
    {
        ArgumentNullException.ThrowIfNull(world); ArgumentNullException.ThrowIfNull(blocks);
        if (maxDistance <= 0) throw new ArgumentOutOfRangeException(nameof(maxDistance));
        if (direction.LengthSquared() < 0.000001f) return null;
        direction = Vector3.Normalize(direction);
        var x = (int)MathF.Floor(origin.X); var y = (int)MathF.Floor(origin.Y); var z = (int)MathF.Floor(origin.Z);
        var previous = new BlockPos(x, y, z);
        var stepX = Math.Sign(direction.X); var stepY = Math.Sign(direction.Y); var stepZ = Math.Sign(direction.Z);
        var deltaX = stepX == 0 ? float.PositiveInfinity : MathF.Abs(1f / direction.X);
        var deltaY = stepY == 0 ? float.PositiveInfinity : MathF.Abs(1f / direction.Y);
        var deltaZ = stepZ == 0 ? float.PositiveInfinity : MathF.Abs(1f / direction.Z);
        var maxX = InitialBoundary(origin.X, direction.X, x, stepX);
        var maxY = InitialBoundary(origin.Y, direction.Y, y, stepY);
        var maxZ = InitialBoundary(origin.Z, direction.Z, z, stepZ);
        var distance = 0f;
        while (distance <= maxDistance)
        {
            var id = world.GetBlock(x, y, z);
            if (id != 0 && blocks[id].Solid) return new RayHit(new BlockPos(x, y, z), previous, distance);
            previous = new BlockPos(x, y, z);
            if (maxX < maxY && maxX < maxZ) { x += stepX; distance = maxX; maxX += deltaX; }
            else if (maxY < maxZ) { y += stepY; distance = maxY; maxY += deltaY; }
            else { z += stepZ; distance = maxZ; maxZ += deltaZ; }
        }
        return null;
    }

    private static float InitialBoundary(float origin, float direction, int cell, int step)
    {
        if (step == 0) return float.PositiveInfinity;
        var boundary = step > 0 ? cell + 1f : cell;
        return (boundary - origin) / direction;
    }
}

/// <summary>Block target and preceding replaceable position hit by a block ray.</summary>
public readonly record struct RayHit(BlockPos Block, BlockPos Previous, float Distance);
