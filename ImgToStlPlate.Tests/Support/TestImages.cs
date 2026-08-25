using Microsoft.AspNetCore.Http;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace ImgToStlPlate.Tests.Support;

public static class TestImages
{
    public static readonly Rgba32 Black = new(0, 0, 0, 255);
    public static readonly Rgba32 White = new(255, 255, 255, 255);
    public static readonly Rgba32 Transparent = new(0, 0, 0, 0);

    public static Image<Rgba32> Create(int width, int height, Func<int, int, Rgba32> pixel)
    {
        var image = new Image<Rgba32>(width, height);

        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                image[x, y] = pixel(x, y);

        return image;
    }

    public static IFormFile AsFormFile(Image<Rgba32> image, string name = "image.png")
    {
        var stream = new MemoryStream();
        image.Save(stream, new PngEncoder());
        stream.Position = 0;

        return new FormFile(stream, 0, stream.Length, "image", name)
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/png"
        };
    }

    public static IFormFile EmptyFormFile(string name = "image.png") =>
        new FormFile(new MemoryStream(), 0, 0, "image", name)
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/png"
        };
}
