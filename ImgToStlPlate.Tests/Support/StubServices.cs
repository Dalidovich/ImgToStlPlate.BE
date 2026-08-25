using ImgToStlPlate.API.Models;
using Microsoft.AspNetCore.Http;
using ImgToStlPlate.API.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ImgToStlPlate.Tests.Support;

public sealed class StubImageProcessingService : IImageProcessingService
{
    public Exception? ThrowOnConvert { get; init; }

    public Task<Image<Rgba32>> CropAndConvertToBw(
        IFormFile image,
        CropSelection selection,
        string orientation,
        bool fillSpace,
        bool invert,
        double rotationDegrees)
    {
        if (ThrowOnConvert is not null)
            throw ThrowOnConvert;

        return Task.FromResult(new Image<Rgba32>(1, 1));
    }

    public Task Denoise(Image<Rgba32> bwImage, int intensity, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public int[,] BuildHeightMatrix(Image<Rgba32> bwImage, int targetWidth, int targetHeight) =>
        new int[targetHeight, targetWidth];
}

public sealed class StubStlGeneratorService : IStlGeneratorService
{
    public byte[] GenerateStl(int[,] matrix, double thickness, double mmPerPixel, double whitePixelThicknessRatio) =>
        new byte[84];
}
