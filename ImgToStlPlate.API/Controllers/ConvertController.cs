using System.Globalization;
using System.Text.Json;
using ImgToStlPlate.API.Imaging;
using ImgToStlPlate.API.Models;
using ImgToStlPlate.API.Services;
using Microsoft.AspNetCore.Mvc;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Png;

namespace ImgToStlPlate.API.Controllers;

[ApiController]
[Route("api/convert")]
public class ConvertController : ControllerBase
{
    private const string GenericFailureDetail = "The request could not be processed.";

    private static readonly JsonSerializerOptions SelectionJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

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
    public async Task<IActionResult> ToBlackAndWhite(
        [FromForm] IFormFile image,
        [FromForm] string selection,
        [FromForm] string orientation,
        [FromForm] bool fillSpace,
        [FromForm] bool invert,
        [FromForm] double rotationDegrees)
    {
        try
        {
            if (image == null || image.Length == 0)
                return BadRequest(new ProblemDetails { Detail = "Image file is required." });

            if (!ModelOrientation.IsValid(orientation))
                return BadRequest(new ProblemDetails { Detail = OrientationDetail });

            if (string.IsNullOrWhiteSpace(selection))
                return BadRequest(new ProblemDetails { Detail = "Selection data is required." });

            CropSelection? cropSelection;
            try
            {
                cropSelection = JsonSerializer.Deserialize<CropSelection>(selection, SelectionJsonOptions);
            }
            catch (JsonException)
            {
                return BadRequest(new ProblemDetails { Detail = "Selection data is not valid JSON." });
            }

            if (cropSelection == null)
                return BadRequest(new ProblemDetails { Detail = "Invalid selection data." });

            if (cropSelection.X < 0 || cropSelection.Y < 0)
                return BadRequest(new ProblemDetails { Detail = "Selection offset must not be negative." });

            if (cropSelection.Width <= 0 || cropSelection.Height <= 0)
                return BadRequest(new ProblemDetails { Detail = "Selection width and height must be positive." });

            if (double.IsNaN(rotationDegrees) || double.IsInfinity(rotationDegrees))
                return BadRequest(new ProblemDetails { Detail = "Rotation must be a finite number." });

            _logger.LogInformation("Converting image to B&W: orientation={O}, fill={F}, invert={I}, rotation={R}",
                orientation, fillSpace, invert, rotationDegrees);

            using var result = await _imageService.CropAndConvertToBw(
                image, cropSelection,
                orientation, fillSpace, invert, rotationDegrees);

            using var ms = new MemoryStream();
            await result.SaveAsync(ms, new PngEncoder());

            return File(ms.ToArray(), "image/png");
        }
        catch (ImageValidationException ex)
        {
            return BadRequest(new ProblemDetails { Detail = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error converting to B&W");
            return Problem(detail: GenericFailureDetail);
        }
    }

    [HttpPost("denoise")]
    public async Task<IActionResult> Denoise([FromForm] IFormFile bwImage, [FromForm] int intensity)
    {
        try
        {
            if (bwImage == null || bwImage.Length == 0)
                return BadRequest(new ProblemDetails { Detail = "Image file is required." });

            if (intensity < 0 || intensity > 100)
                return BadRequest(new ProblemDetails { Detail = "Intensity must be between 0 and 100." });

            _logger.LogInformation("Denoising image with intensity={I}", intensity);

            using var img = await SafeImageLoader.LoadAsync(bwImage);

            await _imageService.Denoise(img, intensity, HttpContext.RequestAborted);

            using var ms = new MemoryStream();
            await img.SaveAsync(ms, new PngEncoder());

            return File(ms.ToArray(), "image/png");
        }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
        {
            return new StatusCodeResult(StatusCodes.Status499ClientClosedRequest);
        }
        catch (ImageValidationException ex)
        {
            return BadRequest(new ProblemDetails { Detail = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error denoising image");
            return Problem(detail: GenericFailureDetail);
        }
    }

    [HttpPost("to-stl")]
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

            if (!ModelOrientation.IsValid(orientation))
                return BadRequest(new ProblemDetails { Detail = OrientationDetail });

            if (!IsWithin(thickness, AppConstants.MinThicknessMm, AppConstants.MaxThicknessMm))
                return BadRequest(new ProblemDetails
                {
                    Detail = string.Format(CultureInfo.InvariantCulture,
                        "Thickness must be between {0} and {1} mm.",
                        AppConstants.MinThicknessMm, AppConstants.MaxThicknessMm)
                });

            if (!IsWithin(modelWidth, AppConstants.MinModelDimensionMm, AppConstants.MaxModelDimensionMm) ||
                !IsWithin(modelHeight, AppConstants.MinModelDimensionMm, AppConstants.MaxModelDimensionMm))
            {
                return BadRequest(new ProblemDetails
                {
                    Detail = string.Format(CultureInfo.InvariantCulture,
                        "Model dimensions must be between {0} and {1} mm.",
                        AppConstants.MinModelDimensionMm, AppConstants.MaxModelDimensionMm)
                });
            }

            int targetWidth = (int)Math.Round(modelWidth / AppConstants.MmPerPixel);
            int targetHeight = (int)Math.Round(modelHeight / AppConstants.MmPerPixel);

            if (targetWidth <= 0 || targetHeight <= 0)
                return BadRequest(new ProblemDetails { Detail = "Model dimensions are too small to produce a model." });

            if ((long)targetWidth * targetHeight > AppConstants.MaxModelPixels)
                return BadRequest(new ProblemDetails
                {
                    Detail = string.Format(CultureInfo.InvariantCulture,
                        "The requested model exceeds the limit of {0} pixels.",
                        AppConstants.MaxModelPixels)
                });

            _logger.LogInformation("Generating STL: {W}x{H}mm, thickness={T}mm", modelWidth, modelHeight, thickness);

            using var img = await SafeImageLoader.LoadAsync(bwImage);

            img.Mutate(ctx => ctx
                .Resize(targetWidth, targetHeight, KnownResamplers.Bicubic)
                .Flip(FlipMode.Vertical));

            var pixels = img.Frames.RootFrame;

            int w = img.Width;
            int h = img.Height;
            var matrix = new int[h, w];

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    var px = pixels[x, y];
                    if (px.A < 128)
                        matrix[y, x] = -1;
                    else if (px.R < 128)
                        matrix[y, x] = 1;
                    else
                        matrix[y, x] = 0;
                }
            }

            var stlBytes = _stlService.GenerateStl(matrix, thickness, AppConstants.MmPerPixel, AppConstants.WhitePixelThicknessRatio);

            return File(stlBytes, "model/stl", "model.stl");
        }
        catch (ImageValidationException ex)
        {
            return BadRequest(new ProblemDetails { Detail = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating STL");
            return Problem(detail: GenericFailureDetail);
        }
    }

    private static string OrientationDetail =>
        $"Orientation must be '{ModelOrientation.Horizontal}' or '{ModelOrientation.Vertical}'.";

    private static bool IsWithin(double value, double min, double max) => value >= min && value <= max;
}
