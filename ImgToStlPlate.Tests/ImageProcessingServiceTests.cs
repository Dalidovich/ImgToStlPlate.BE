using ImgToStlPlate.API.Models;
using ImgToStlPlate.API.Services;
using ImgToStlPlate.Tests.Support;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ImgToStlPlate.Tests;

public class ImageProcessingServiceTests
{
    private const int Hole = -1;
    private const int Dark = 1;
    private const int Light = 0;

    private readonly ImageProcessingService _service = new();

    [Fact]
    public async Task RotationLeavesTransparentCornersThatBecomeHoleCells()
    {
        using var source = TestImages.Create(16, 16, (_, _) => TestImages.Black);

        using var result = await Convert(source, rotationDegrees: 30);

        Assert.Equal(0, result[0, 0].A);
        Assert.Equal(0, result[result.Width - 1, 0].A);
        Assert.Equal(0, result[0, result.Height - 1].A);
        Assert.Equal(0, result[result.Width - 1, result.Height - 1].A);

        int width = result.Width;
        int height = result.Height;
        var matrix = _service.BuildHeightMatrix(result, width, height);

        Assert.Equal(Hole, matrix[0, 0]);
        Assert.Equal(Hole, matrix[0, width - 1]);
        Assert.Equal(Hole, matrix[height - 1, 0]);
        Assert.Equal(Hole, matrix[height - 1, width - 1]);
    }

    [Fact]
    public async Task TransparentSourcePixelsStayTransparentThroughTheThresholdPass()
    {
        using var source = TestImages.Create(3, 1, (x, _) => x switch
        {
            0 => TestImages.Black,
            1 => TestImages.White,
            _ => TestImages.Transparent
        });

        using var result = await Convert(source);

        Assert.Equal(255, result[0, 0].A);
        Assert.Equal(255, result[1, 0].A);
        Assert.Equal(0, result[2, 0].A);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 255)]
    public async Task InvertDecidesWhichLuminanceBecomesSolid(bool invert, byte expectedForBlackSource)
    {
        using var source = TestImages.Create(2, 1, (x, _) => x == 0 ? TestImages.Black : TestImages.White);

        using var result = await Convert(source, invert: invert);

        Assert.Equal(expectedForBlackSource, result[0, 0].R);
        Assert.Equal((byte)(255 - expectedForBlackSource), result[1, 0].R);
        Assert.Equal(255, result[0, 0].A);
        Assert.Equal(255, result[1, 0].A);
    }

    [Fact]
    public async Task FillSpaceTurnsLightPixelsIntoHoles()
    {
        using var source = TestImages.Create(2, 1, (x, _) => x == 0 ? TestImages.Black : TestImages.White);

        using var result = await Convert(source, fillSpace: true);

        Assert.Equal(0, result[0, 0].R);
        Assert.Equal(255, result[0, 0].A);
        Assert.Equal(0, result[1, 0].A);
    }

    [Theory]
    [InlineData(ModelOrientation.Horizontal)]
    [InlineData(ModelOrientation.Vertical)]
    public async Task OrientationRotatesWithoutMirroring(string orientation)
    {
        const int width = 4;
        const int height = 3;
        using var source = TestImages.Create(width, height, IsSolidPixel);

        using var converted = await Convert(source, orientation: orientation);
        var matrix = _service.BuildHeightMatrix(converted, converted.Width, converted.Height);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var (row, col) = ModelCell(orientation, x, y, width, height);
                int expected = IsSolidPixel(x, y) == TestImages.Black ? Dark : Light;

                Assert.Equal(expected, matrix[row, col]);
            }
        }
    }

    private static Rgba32 IsSolidPixel(int x, int y) =>
        (x, y) is (0, 0) or (1, 0) or (0, 1) or (3, 2) ? TestImages.Black : TestImages.White;

    private static (int Row, int Col) ModelCell(string orientation, int x, int y, int width, int height) =>
        ModelOrientation.IsVertical(orientation)
            ? (width - 1 - x, height - 1 - y)
            : (height - 1 - y, x);

    private Task<Image<Rgba32>> Convert(
        Image<Rgba32> source,
        string orientation = ModelOrientation.Horizontal,
        bool fillSpace = false,
        bool invert = false,
        double rotationDegrees = 0) =>
        _service.CropAndConvertToBw(
            TestImages.AsFormFile(source),
            new CropSelection { X = 0, Y = 0, Width = int.MaxValue / 2, Height = int.MaxValue / 2 },
            orientation,
            fillSpace,
            invert,
            rotationDegrees);
}
