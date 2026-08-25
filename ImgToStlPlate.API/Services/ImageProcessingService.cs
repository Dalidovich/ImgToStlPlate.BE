using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using ImgToStlPlate.API.Imaging;
using ImgToStlPlate.API.Models;

namespace ImgToStlPlate.API.Services;

public class ImageProcessingService : IImageProcessingService
{
    private static readonly Rgba32 Hole = new(255, 255, 255, 0);

    public async Task<Image<Rgba32>> CropAndConvertToBw(
        IFormFile image,
        CropSelection selection,
        string orientation,
        bool fillSpace,
        bool invert,
        double rotationDegrees)
    {
        var img = await SafeImageLoader.LoadAsync(image);

        if (Math.Abs(rotationDegrees) > 0.01)
        {
            img.Mutate(ctx => ctx.Rotate((float)rotationDegrees));
        }

        int cropX = Math.Max(0, selection.X);
        int cropY = Math.Max(0, selection.Y);
        int cropW = Math.Min(selection.Width, img.Width - cropX);
        int cropH = Math.Min(selection.Height, img.Height - cropY);

        if (cropW > 0 && cropH > 0)
        {
            img.Mutate(ctx => ctx.Crop(new Rectangle(cropX, cropY, cropW, cropH)));
        }

        ApplyBinaryPass(img, invert, fillSpace);

        if (ModelOrientation.IsVertical(orientation))
        {
            img.Mutate(ctx => ctx.Rotate(RotateMode.Rotate90));
        }

        return img;
    }

    private static void ApplyBinaryPass(Image<Rgba32> img, bool invert, bool fillSpace)
    {
        img.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    ref Rgba32 px = ref row[x];

                    if (px.A == 0)
                    {
                        px = Hole;
                        continue;
                    }

                    bool isBlack = Luminance(px) < 128;
                    if (invert)
                    {
                        isBlack = !isBlack;
                    }

                    if (!isBlack && fillSpace)
                    {
                        px = Hole;
                        continue;
                    }

                    byte value = isBlack ? (byte)0 : (byte)255;
                    px = new Rgba32(value, value, value, px.A);
                }
            }
        });
    }

    private static int Luminance(Rgba32 px) =>
        (px.R * 2126 + px.G * 7152 + px.B * 722) / 10000;

    public Task Denoise(Image<Rgba32> bwImage, int intensity)
    {
        int radius = (int)Math.Round(intensity / 100.0 * 3.0);
        if (radius == 0)
        {
            return Task.CompletedTask;
        }

        int w = bwImage.Width;
        int h = bwImage.Height;

        // Clone the entire image so we can read from original while writing to clone
        using var clone = bwImage.Clone();
        var srcPixels = bwImage.Frames.RootFrame;
        var dstPixels = clone.Frames.RootFrame;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                var center = srcPixels[x, y];
                if (center.A == 0) continue;

                var neighbors = new List<byte>();

                for (int dy = -radius; dy <= radius; dy++)
                {
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        int nx = x + dx;
                        int ny = y + dy;
                        if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;

                        var nb = srcPixels[nx, ny];
                        if (nb.A > 0)
                        {
                            neighbors.Add(nb.R);
                        }
                    }
                }

                if (neighbors.Count > 0)
                {
                    neighbors.Sort();
                    byte median = neighbors[neighbors.Count / 2];
                    dstPixels[x, y] = new Rgba32(median, median, median, center.A);
                }
            }
        }

        // Copy result back into the original image frame
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                srcPixels[x, y] = dstPixels[x, y];
            }
        }

        return Task.CompletedTask;
    }
}
