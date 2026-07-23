using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using ImgToStlPlate.API.Models;

namespace ImgToStlPlate.API.Services;

public class ImageProcessingService : IImageProcessingService
{
    public async Task<Image<Rgba32>> CropAndConvertToBw(
        IFormFile image,
        CropSelection selection,
        string orientation,
        bool fillSpace,
        bool invert,
        double rotationDegrees)
    {
        using var stream = image.OpenReadStream();
        var img = await Image.LoadAsync<Rgba32>(stream);

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

        // Convert to grayscale then threshold
        img.Mutate(ctx =>
        {
            ctx.Grayscale();
            ctx.BinaryThreshold(0.5f);
        });

        // Invert if requested (swap black/white for non-transparent pixels)
        if (invert)
        {
            var pixels = img.Frames.RootFrame;
            for (int y = 0; y < img.Height; y++)
            {
                for (int x = 0; x < img.Width; x++)
                {
                    var px = pixels[x, y];
                    if (px.A > 0)
                    {
                        // Binary threshold means pixels are either 0 or 255
                        byte val = (byte)(px.R < 128 ? 255 : 0);
                        pixels[x, y] = new Rgba32(val, val, val, 255);
                    }
                }
            }
        }

        // Handle orientation: vertical means bottom of model = left side of image
        if (string.Equals(orientation, "vertical", StringComparison.OrdinalIgnoreCase))
        {
            img.Mutate(ctx => ctx.Rotate(RotateMode.Rotate90));
        }
        // horizontal: bottom = bottom of image, no rotation needed

        // Fill space: replace white with transparency
        if (fillSpace)
        {
            var pixels = img.Frames.RootFrame;
            for (int y = 0; y < img.Height; y++)
            {
                for (int x = 0; x < img.Width; x++)
                {
                    var px = pixels[x, y];
                    if (px.A > 0 && px.R >= 128)
                    {
                        pixels[x, y] = new Rgba32(255, 255, 255, 0);
                    }
                }
            }
        }

        return img;
    }

    public Task<Image<Rgba32>> Denoise(Image<Rgba32> bwImage, int intensity)
    {
        int radius = (int)Math.Round(intensity / 100.0 * 3.0);
        if (radius == 0)
        {
            return Task.FromResult(bwImage);
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

        return Task.FromResult(bwImage);
    }
}
