using System.Numerics;
using BlockCraft.Core.Blocks;

namespace BlockCraft.Core.World;

/// <summary>A merged, axis-aligned face emitted by the chunk greedy mesher.</summary>
public readonly record struct MeshQuad(Vector3 P0, Vector3 P1, Vector3 P2, Vector3 P3, ushort BlockId, Vector3 Normal);

/// <summary>Greedy face meshing that merges adjacent coplanar faces of the same block type.</summary>
public static class GreedyMesher
{
    /// <summary>
    /// Builds greedy quads for one chunk. The block accessor receives local coordinates and may
    /// receive -1 or 16 at horizontal chunk boundaries so neighboring chunks can hide shared faces.
    /// </summary>
    public static IReadOnlyList<MeshQuad> Build(Func<int, int, int, ushort> getBlock, BlockRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(getBlock);
        ArgumentNullException.ThrowIfNull(registry);
        var result = new List<MeshQuad>();
        var dimensions = new[] { Chunk.Width, Chunk.Height, Chunk.Depth };
        var mask = new FaceCell[Chunk.Width * Chunk.Height];

        for (var axis = 0; axis < 3; axis++)
        {
            var u = (axis + 1) % 3;
            var v = (axis + 2) % 3;
            var position = new int[3];
            for (position[axis] = -1; position[axis] < dimensions[axis]; position[axis]++)
            {
                Array.Clear(mask);
                var index = 0;
                for (position[v] = 0; position[v] < dimensions[v]; position[v]++)
                for (position[u] = 0; position[u] < dimensions[u]; position[u]++)
                {
                    var a = Read(getBlock, axis, position, 0);
                    var b = Read(getBlock, axis, position, 1);
                    var aSolid = a != 0 && registry[a].Solid;
                    var bSolid = b != 0 && registry[b].Solid;
                    if (aSolid && (b == 0 || !registry[b].OccludesFaces)) mask[index] = new FaceCell(a, 1);
                    else if (bSolid && (a == 0 || !registry[a].OccludesFaces)) mask[index] = new FaceCell(b, -1);
                    index++;
                }

                index = 0;
                for (var j = 0; j < dimensions[v]; j++)
                for (var i = 0; i < dimensions[u];)
                {
                    var cell = mask[index];
                    if (cell.BlockId == 0) { i++; index++; continue; }

                    var width = 1;
                    while (i + width < dimensions[u] && mask[index + width] == cell) width++;
                    var height = 1;
                    var canGrow = true;
                    while (j + height < dimensions[v] && canGrow)
                    {
                        for (var k = 0; k < width; k++)
                            if (mask[index + k + height * dimensions[u]] != cell) { canGrow = false; break; }
                        if (canGrow) height++;
                    }

                    position[u] = i; position[v] = j;
                    var point = new[] { position[0], position[1], position[2] };
                    point[axis]++;
                    var origin = new Vector3(point[0], point[1], point[2]);
                    var uValues = new float[3]; uValues[u] = width;
                    var vValues = new float[3]; vValues[v] = height;
                    var normalValues = new float[3]; normalValues[axis] = cell.Sign;
                    var du = new Vector3(uValues[0], uValues[1], uValues[2]);
                    var dv = new Vector3(vValues[0], vValues[1], vValues[2]);
                    var p0 = origin; var p1 = origin + du; var p2 = origin + du + dv; var p3 = origin + dv;
                    if (cell.Sign < 0) (p1, p3) = (p3, p1);
                    result.Add(new MeshQuad(p0, p1, p2, p3, cell.BlockId,
                        new Vector3(normalValues[0], normalValues[1], normalValues[2])));

                    for (var row = 0; row < height; row++)
                    for (var column = 0; column < width; column++)
                        mask[index + column + row * dimensions[u]] = default;
                    i += width; index += width;
                }
            }
        }
        return result;
    }

    private static ushort Read(Func<int, int, int, ushort> accessor, int axis, int[] position, int offset)
    {
        var x = position[0]; var y = position[1]; var z = position[2];
        if (axis == 0) x += offset;
        else if (axis == 1) y += offset;
        else z += offset;
        return accessor(x, y, z);
    }

    private readonly record struct FaceCell(ushort BlockId, int Sign);
}
