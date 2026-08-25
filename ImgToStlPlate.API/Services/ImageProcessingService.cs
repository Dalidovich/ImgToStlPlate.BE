using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using ImgToStlPlate.API.Imaging;
using ImgToStlPlate.API.Models;

namespace ImgToStlPlate.API.Services;

public class ImageProcessingService : IImageProcessingService
{
    private const byte Transparent = 0;
    private const byte Light = 1;
    private const byte Dark = 2;
    private const int DenoiseRadius = 3;
    private const int MaxFlipThreshold = 970;
    private const int MinFlipThreshold = 500;

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

    public Task Denoise(Image<Rgba32> bwImage, int intensity, CancellationToken cancellationToken)
    {
        if (intensity <= 0)
        {
            return Task.CompletedTask;
        }

        const int radius = DenoiseRadius;
        int flipThresholdPerMille = MaxFlipThreshold
            - (intensity - 1) * (MaxFlipThreshold - MinFlipThreshold) / 99;

        int w = bwImage.Width;
        int h = bwImage.Height;
        var kinds = ClassifyPixels(bwImage);

        var columnOpaque = new int[w];
        var columnDark = new int[w];

        for (int y = 0; y <= Math.Min(radius, h - 1); y++)
        {
            AccumulateRow(kinds, columnOpaque, columnDark, w, y, 1);
        }

        bwImage.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < h; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int windowOpaque = 0;
                int windowDark = 0;
                for (int x = 0; x <= Math.Min(radius, w - 1); x++)
                {
                    windowOpaque += columnOpaque[x];
                    windowDark += columnDark[x];
                }

                var row = accessor.GetRowSpan(y);
                int rowOffset = y * w;

                for (int x = 0; x < w; x++)
                {
                    byte kind = kinds[rowOffset + x];
                    if (kind != Transparent && windowOpaque > 0)
                    {
                        bool isDark = kind == Dark;
                        int opposite = isDark ? windowOpaque - windowDark : windowDark;
                        bool flip = isDark
                            ? opposite * 1000 >= flipThresholdPerMille * windowOpaque
                            : opposite * 1000 > flipThresholdPerMille * windowOpaque;

                        if (flip)
                        {
                            byte value = isDark ? (byte)255 : (byte)0;
                            ref Rgba32 px = ref row[x];
                            px = new Rgba32(value, value, value, px.A);
                        }
                    }

                    int leaving = x - radius;
                    if (leaving >= 0)
                    {
                        windowOpaque -= columnOpaque[leaving];
                        windowDark -= columnDark[leaving];
                    }

                    int entering = x + radius + 1;
                    if (entering < w)
                    {
                        windowOpaque += columnOpaque[entering];
                        windowDark += columnDark[entering];
                    }
                }

                int leavingRow = y - radius;
                if (leavingRow >= 0)
                {
                    AccumulateRow(kinds, columnOpaque, columnDark, w, leavingRow, -1);
                }

                int enteringRow = y + radius + 1;
                if (enteringRow < h)
                {
                    AccumulateRow(kinds, columnOpaque, columnDark, w, enteringRow, 1);
                }
            }
        });

        return Task.CompletedTask;
    }

    public int[,] BuildHeightMatrix(Image<Rgba32> bwImage, int targetWidth, int targetHeight)
    {
        bwImage.Mutate(ctx => ctx
            .Resize(targetWidth, targetHeight, KnownResamplers.Bicubic)
            .Flip(FlipMode.Vertical));

        int w = bwImage.Width;
        int h = bwImage.Height;
        var matrix = new int[h, w];

        bwImage.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < h; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < w; x++)
                {
                    ref Rgba32 px = ref row[x];
                    if (px.A < 128)
                        matrix[y, x] = -1;
                    else if (px.R < 128)
                        matrix[y, x] = 1;
                    else
                        matrix[y, x] = 0;
                }
            }
        });

        return matrix;
    }

    private static byte[] ClassifyPixels(Image<Rgba32> image)
    {
        int w = image.Width;
        var kinds = new byte[w * image.Height];

        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                int rowOffset = y * w;
                for (int x = 0; x < row.Length; x++)
                {
                    ref Rgba32 px = ref row[x];
                    kinds[rowOffset + x] = px.A == 0
                        ? Transparent
                        : px.R < 128 ? Dark : Light;
                }
            }
        });

        return kinds;
    }

    private static void AccumulateRow(byte[] kinds, int[] columnOpaque, int[] columnDark, int width, int y, int sign)
    {
        int rowOffset = y * width;
        for (int x = 0; x < width; x++)
        {
            byte kind = kinds[rowOffset + x];
            if (kind == Transparent)
            {
                continue;
            }

            columnOpaque[x] += sign;
            if (kind == Dark)
            {
                columnDark[x] += sign;
            }
        }
    }
}
