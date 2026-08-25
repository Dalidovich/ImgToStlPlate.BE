using ImgToStlPlate.API.Services;
using ImgToStlPlate.Tests.Support;

namespace ImgToStlPlate.Tests;

public class StlGeneratorServiceTests
{
    private const double Thickness = 2.0;
    private const double MmPerPixel = 0.4;
    private const double WhiteRatio = 0.5;

    private readonly StlGeneratorService _service = new();

    public static TheoryData<string, int[,]> Shapes() => new()
    {
        { "step", new[,] { { 1, 1 }, { 1, 0 } } },
        { "random64", RandomMatrix(64, 64, seed: 20250825) }
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    public void MeshIsManifoldApartFromDiagonalPinches(string name, int[,] matrix)
    {
        var mesh = Generate(matrix);
        var pinches = Field(matrix).DiagonalPinchEdges();
        var undirected = new Dictionary<(GridVertex, GridVertex), int>();
        var directed = new Dictionary<(GridVertex, GridVertex), int>();

        foreach (var triangle in mesh.Triangles)
        {
            var vertices = triangle.Vertices;
            for (int i = 0; i < 3; i++)
            {
                var from = vertices[i];
                var to = vertices[(i + 1) % 3];

                var undirectedKey = StlMesh.UndirectedEdge(from, to);
                undirected[undirectedKey] = undirected.GetValueOrDefault(undirectedKey) + 1;

                var directedKey = (StlMesh.Snap(from), StlMesh.Snap(to));
                directed[directedKey] = directed.GetValueOrDefault(directedKey) + 1;
            }
        }

        var wrongFanIn = undirected
            .Where(pair => pair.Value != (pinches.Contains(pair.Key) ? 4 : 2))
            .ToList();
        Assert.True(wrongFanIn.Count == 0, wrongFanIn.Count == 0
            ? string.Empty
            : $"{name}: {wrongFanIn.Count} edges are shared by the wrong number of triangles, "
              + $"first is {Describe(wrongFanIn[0], pinches)}.");

        var wrongWinding = directed
            .Where(pair => pair.Value != (pinches.Contains(StlMesh.UndirectedEdge(Unsnap(pair.Key.Item1), Unsnap(pair.Key.Item2))) ? 2 : 1))
            .ToList();
        Assert.True(wrongWinding.Count == 0,
            $"{name}: {wrongWinding.Count} directed edges are traversed the wrong number of times.");

        foreach (var pinch in pinches)
            Assert.True(undirected.ContainsKey(pinch), $"{name}: expected pinch edge {pinch} is missing from the mesh.");
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void MeshHasNoCoincidentTriangles(string name, int[,] matrix)
    {
        var mesh = Generate(matrix);
        var seen = new HashSet<(GridVertex, GridVertex, GridVertex)>();

        foreach (var triangle in mesh.Triangles)
        {
            var key = triangle.Vertices
                .Select(StlMesh.Snap)
                .OrderBy(vertex => vertex)
                .ToArray();

            Assert.True(seen.Add((key[0], key[1], key[2])),
                $"{name}: two triangles share the same three vertices.");
        }
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void HeaderTriangleCountMatchesRecordCount(string name, int[,] matrix)
    {
        var mesh = Generate(matrix);

        Assert.True(mesh.RecordCount > 0, $"{name}: the mesh has no triangles.");
        Assert.Equal(mesh.RecordCount, mesh.DeclaredTriangleCount);
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void EveryNormalIsUnitLength(string name, int[,] matrix)
    {
        var mesh = Generate(matrix);

        foreach (var triangle in mesh.Triangles)
        {
            var n = triangle.Normal;
            double length = Math.Sqrt(n.X * n.X + n.Y * n.Y + n.Z * n.Z);
            Assert.True(Math.Abs(length - 1.0) < 1e-4,
                $"{name}: normal ({n.X}, {n.Y}, {n.Z}) has length {length}.");
        }
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void VolumeMatchesTheSumOfCellVolumes(string name, int[,] matrix)
    {
        var mesh = Generate(matrix);
        double expected = Field(matrix).TotalVolume();

        Assert.True(Math.Abs(mesh.Volume() - expected) < expected * 1e-4,
            $"{name}: mesh volume {mesh.Volume()} differs from the analytic volume {expected}.");
    }

    [Fact]
    public void EmptyMatrixProducesAnEmptyMesh()
    {
        var matrix = new int[8, 8];
        for (int row = 0; row < 8; row++)
            for (int col = 0; col < 8; col++)
                matrix[row, col] = -1;

        var mesh = Generate(matrix);

        Assert.Equal(0, mesh.DeclaredTriangleCount);
        Assert.Equal(0, mesh.RecordCount);
    }

    [Fact]
    public void ZeroSizedMatrixDoesNotThrow()
    {
        var mesh = Generate(new int[0, 0]);

        Assert.Equal(0, mesh.RecordCount);
    }

    private StlMesh Generate(int[,] matrix) =>
        StlMesh.Parse(_service.GenerateStl(matrix, Thickness, MmPerPixel, WhiteRatio));

    private static HeightField Field(int[,] matrix) =>
        new(matrix, Thickness, MmPerPixel, WhiteRatio);

    private static Vec3 Unsnap(GridVertex vertex) =>
        new(vertex.X / 1000.0, vertex.Y / 1000.0, vertex.Z / 1000.0);

    private static string Describe(
        KeyValuePair<(GridVertex Low, GridVertex High), int> edge,
        HashSet<(GridVertex Low, GridVertex High)> pinches) =>
        $"{edge.Key} shared by {edge.Value} triangles (pinch: {pinches.Contains(edge.Key)})";

    private static int[,] RandomMatrix(int rows, int cols, int seed)
    {
        var random = new Random(seed);
        var matrix = new int[rows, cols];

        for (int row = 0; row < rows; row++)
            for (int col = 0; col < cols; col++)
                matrix[row, col] = random.Next(-1, 2);

        return matrix;
    }
}
