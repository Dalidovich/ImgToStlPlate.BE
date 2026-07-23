using System.Text.Json;
using ImgToStlPlate.API.Models;
using ImgToStlPlate.API.Services;
using Microsoft.AspNetCore.Mvc;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Png;

namespace ImgToStlPlate.API.Controllers;

[ApiController]
[Route("api/convert")]
public class ConvertController : ControllerBase
{
    private readonly IImageProcessingService _imageService;
    private readonly IStlGeneratorService _stlService;
    private readonly ILogger<ConvertController> _logger;

    public ConvertController(
        IImageProcessingService imageService,
        IStlGeneratorService stlService,
        ILogger<ConvertController> logger)
    {
        _imageService = imageService;
        _stlService = stlService;
        _logger = logger;
    }

    [HttpPost("to-bw")]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> ToBlackAndWhite(
        [FromForm] IFormFile image,
        [FromForm] string selection,
        [FromForm] string orientation,
        [FromForm] bool fillSpace,
        [FromForm] bool invert)
    {
        try
        {
            if (image == null || image.Length == 0)
                return BadRequest(new ProblemDetails { Detail = "Image file is required." });

            var cropSelection = JsonSerializer.Deserialize<CropSelection>(selection, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (cropSelection == null)
                return BadRequest(new ProblemDetails { Detail = "Invalid selection data." });

            _logger.LogInformation("Converting image to B&W: orientation={O}, fill={F}, invert={I}",
                orientation, fillSpace, invert);

            var result = await _imageService.CropAndConvertToBw(
                image, cropSelection,
                orientation, fillSpace, invert);

            var ms = new MemoryStream();
            await result.SaveAsync(ms, new PngEncoder());
            ms.Position = 0;
            result.Dispose();

            return File(ms.ToArray(), "image/png");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error converting to B&W");
            return Problem($"Error processing image: {ex.Message}");
        }
    }

    [HttpPost("denoise")]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> Denoise([FromForm] IFormFile bwImage, [FromForm] int intensity)
    {
        try
        {
            if (bwImage == null || bwImage.Length == 0)
                return BadRequest(new ProblemDetails { Detail = "Image file is required." });

            if (intensity < 0 || intensity > 100)
                return BadRequest(new ProblemDetails { Detail = "Intensity must be between 0 and 100." });

            _logger.LogInformation("Denoising image with intensity={I}", intensity);

            using var stream = bwImage.OpenReadStream();
            var img = await Image.LoadAsync<Rgba32>(stream);

            var result = await _imageService.Denoise(img, intensity);

            var ms = new MemoryStream();
            await result.SaveAsync(ms, new PngEncoder());
            ms.Position = 0;
            result.Dispose();

            return File(ms.ToArray(), "image/png");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error denoising image");
            return Problem($"Error denoising image: {ex.Message}");
        }
    }

        [HttpPost("to-stl")]
        [DisableRequestSizeLimit]
        public async Task<IActionResult> ToStl(
            [FromForm] IFormFile bwImage,
            [FromForm] double thickness,
            [FromForm] double modelWidth,
            [FromForm] double modelHeight,
            [FromForm] string orientation)
        {
            try
            {
                if (bwImage == null || bwImage.Length == 0)
                    return BadRequest(new ProblemDetails { Detail = "Image file is required." });

                if (thickness <= 0)
                    return BadRequest(new ProblemDetails { Detail = "Thickness must be positive." });

                if (modelWidth <= 0 || modelHeight <= 0)
                    return BadRequest(new ProblemDetails { Detail = "Model dimensions must be positive." });

                _logger.LogInformation("Generating STL: {W}x{H}mm, thickness={T}mm", modelWidth, modelHeight, thickness);

                using var stream = bwImage.OpenReadStream();
                var img = await Image.LoadAsync<Rgba32>(stream);

                int targetWidth = (int)Math.Round(modelWidth / AppConstants.MmPerPixel);
                int targetHeight = (int)Math.Round(modelHeight / AppConstants.MmPerPixel);
                img.Mutate(ctx => ctx.Resize(targetWidth, targetHeight, KnownResamplers.Bicubic));

                if (string.Equals(orientation, "horizontal", StringComparison.OrdinalIgnoreCase))
                {
                    img.Mutate(ctx => ctx.Flip(FlipMode.Vertical));
                }

                var pixels = img.Frames.RootFrame;

            int w = img.Width;
            int h = img.Height;
            var matrix = new int[h, w];

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    var px = pixels[x, y];
                    if (px.A == 0)
                        matrix[y, x] = -1;
                    else if (px.R < 128)
                        matrix[y, x] = 1;
                    else
                        matrix[y, x] = 0;
                }
            }

            var stlBytes = _stlService.GenerateStl(matrix, thickness, AppConstants.MmPerPixel, AppConstants.ZeroToHalfRatio);

            return File(stlBytes, "model/stl", "model.stl");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating STL");
            return Problem($"Error generating STL: {ex.Message}");
        }
    }
}
