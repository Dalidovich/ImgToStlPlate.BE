using System.Globalization;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;

namespace ImgToStlPlate.API.Imaging;

public static class SafeImageLoader
{
    private static readonly DecoderOptions Options = new() { MaxFrames = 1 };

    public static async Task<Image<Rgba32>> LoadAsync(IFormFile file)
    {
        try
        {
            using (var probeStream = file.OpenReadStream())
            {
                var info = await Image.IdentifyAsync(Options, probeStream);
                EnsureWithinLimits(info.Width, info.Height);
            }

            using var stream = file.OpenReadStream();
            return await Image.LoadAsync<Rgba32>(Options, stream);
        }
        catch (ImageFormatException)
        {
            throw new ImageValidationException("The uploaded file is not a supported image.");
        }
    }

    private static void EnsureWithinLimits(int width, int height)
    {
        if (width > AppConstants.MaxSourceImageDimension || height > AppConstants.MaxSourceImageDimension)
        {
            throw new ImageValidationException(string.Format(CultureInfo.InvariantCulture,
                "Image dimensions must not exceed {0} pixels per side.",
                AppConstants.MaxSourceImageDimension));
        }

        if ((long)width * height > AppConstants.MaxSourceImagePixels)
        {
            throw new ImageValidationException(string.Format(CultureInfo.InvariantCulture,
                "Image must not exceed {0} pixels in total.",
                AppConstants.MaxSourceImagePixels));
        }
    }
}
