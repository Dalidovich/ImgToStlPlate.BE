namespace ImgToStlPlate.Tests.Support;

public sealed class HeightField
{
    private readonly double[,] _heights;
    private readonly double _mmPerPixel;
    private readonly double _centerX;
    private readonly double _centerY;

    public HeightField(int[,] matrix, double thickness, double mmPerPixel, double whitePixelThicknessRatio)
    {
        Rows = matrix.GetLength(0);
        Cols = matrix.GetLength(1);
        _mmPerPixel = mmPerPixel;
        _heights = new double[Rows, Cols];

        for (int row = 0; row < Rows; row++)
        {
            for (int col = 0; col < Cols; col++)
            {
                _heights[row, col] = matrix[row, col] switch
                {
                    1 => thickness,
                    0 => thickness * whitePixelThicknessRatio,
                    _ => 0.0
                };
            }
        }

        (_centerX, _centerY) = SolidBoundsCenter();
    }

    public int Rows { get; }

    public int Cols { get; }

    public double Height(int row, int col) => _heights[row, col];

    public double TotalVolume()
    {
        double cellArea = _mmPerPixel * _mmPerPixel;
        double total = 0;

        for (int row = 0; row < Rows; row++)
            for (int col = 0; col < Cols; col++)
                total += cellArea * _heights[row, col];

        return total;
    }

    public double[] Levels()
    {
        var levels = new SortedSet<double> { 0.0 };

        for (int row = 0; row < Rows; row++)
            for (int col = 0; col < Cols; col++)
                if (_heights[row, col] > 0)
                    levels.Add(_heights[row, col]);

        return levels.ToArray();
    }

    public HashSet<(GridVertex Low, GridVertex High)> DiagonalPinchEdges()
    {
        var pinches = new HashSet<(GridVertex, GridVertex)>();
        var levels = Levels();

        for (int band = 0; band + 1 < levels.Length; band++)
        {
            double solidAbove = levels[band + 1];

            for (int row = 1; row < Rows; row++)
            {
                for (int col = 1; col < Cols; col++)
                {
                    bool topLeft = _heights[row - 1, col - 1] >= solidAbove;
                    bool topRight = _heights[row - 1, col] >= solidAbove;
                    bool bottomLeft = _heights[row, col - 1] >= solidAbove;
                    bool bottomRight = _heights[row, col] >= solidAbove;

                    bool mainDiagonal = topLeft && bottomRight && !topRight && !bottomLeft;
                    bool antiDiagonal = topRight && bottomLeft && !topLeft && !bottomRight;

                    if (!mainDiagonal && !antiDiagonal)
                        continue;

                    double x = col * _mmPerPixel - _centerX;
                    double y = row * _mmPerPixel - _centerY;
                    pinches.Add(StlMesh.UndirectedEdge(
                        new Vec3(x, y, levels[band]),
                        new Vec3(x, y, levels[band + 1])));
                }
            }
        }

        return pinches;
    }

    private (double X, double Y) SolidBoundsCenter()
    {
        int minRow = int.MaxValue, minCol = int.MaxValue, maxRow = int.MinValue, maxCol = int.MinValue;

        for (int row = 0; row < Rows; row++)
        {
            for (int col = 0; col < Cols; col++)
            {
                if (_heights[row, col] <= 0) continue;

                minRow = Math.Min(minRow, row);
                minCol = Math.Min(minCol, col);
                maxRow = Math.Max(maxRow, row);
                maxCol = Math.Max(maxCol, col);
            }
        }

        if (minRow == int.MaxValue)
            return (0, 0);

        return (
            (minCol + maxCol + 1) * _mmPerPixel / 2.0,
            (minRow + maxRow + 1) * _mmPerPixel / 2.0);
    }
}
