using System.Buffers.Binary;

namespace ImgToStlPlate.Tests.Support;

public readonly record struct Vec3(double X, double Y, double Z);

public readonly record struct GridVertex(long X, long Y, long Z) : IComparable<GridVertex>
{
    public int CompareTo(GridVertex other)
    {
        int byX = X.CompareTo(other.X);
        if (byX != 0) return byX;

        int byY = Y.CompareTo(other.Y);
        return byY != 0 ? byY : Z.CompareTo(other.Z);
    }
}

public readonly record struct StlTriangle(Vec3 Normal, Vec3 A, Vec3 B, Vec3 C)
{
    public IReadOnlyList<Vec3> Vertices => new[] { A, B, C };
}

public sealed class StlMesh
{
    private const int HeaderLength = 80;
    private const int RecordLength = 50;

    public required int DeclaredTriangleCount { get; init; }

    public required IReadOnlyList<StlTriangle> Triangles { get; init; }

    public required int RecordCount { get; init; }

    public static StlMesh Parse(byte[] bytes)
    {
        Assert.True(bytes.Length >= HeaderLength + 4, "STL payload is shorter than a binary STL header.");

        int declared = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(HeaderLength, 4));
        int payload = bytes.Length - HeaderLength - 4;
        Assert.Equal(0, payload % RecordLength);

        int records = payload / RecordLength;
        var triangles = new List<StlTriangle>(records);

        for (int i = 0; i < records; i++)
        {
            int offset = HeaderLength + 4 + i * RecordLength;
            triangles.Add(new StlTriangle(
                ReadVec3(bytes, offset),
                ReadVec3(bytes, offset + 12),
                ReadVec3(bytes, offset + 24),
                ReadVec3(bytes, offset + 36)));
        }

        return new StlMesh
        {
            DeclaredTriangleCount = declared,
            Triangles = triangles,
            RecordCount = records
        };
    }

    public double Volume()
    {
        double total = 0;

        foreach (var triangle in Triangles)
        {
            var a = triangle.A;
            var b = triangle.B;
            var c = triangle.C;

            double cx = b.Y * c.Z - b.Z * c.Y;
            double cy = b.Z * c.X - b.X * c.Z;
            double cz = b.X * c.Y - b.Y * c.X;

            total += a.X * cx + a.Y * cy + a.Z * cz;
        }

        return total / 6.0;
    }

    public static GridVertex Snap(Vec3 vertex) => new(
        (long)Math.Round(vertex.X * 1000.0),
        (long)Math.Round(vertex.Y * 1000.0),
        (long)Math.Round(vertex.Z * 1000.0));

    public static (GridVertex Low, GridVertex High) UndirectedEdge(Vec3 from, Vec3 to)
    {
        var a = Snap(from);
        var b = Snap(to);
        return a.CompareTo(b) <= 0 ? (a, b) : (b, a);
    }

    private static Vec3 ReadVec3(byte[] bytes, int offset) => new(
        BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(offset, 4)),
        BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(offset + 4, 4)),
        BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(offset + 8, 4)));
}
