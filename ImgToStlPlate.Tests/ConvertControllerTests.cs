using System.Text.Json;
using ImgToStlPlate.API.Controllers;
using ImgToStlPlate.API.Models;
using ImgToStlPlate.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace ImgToStlPlate.Tests;

public class ConvertControllerTests
{
    private const string SecretFailure = "Object reference not set at StlGeneratorService.Internals";

    private static readonly string ValidSelection =
        JsonSerializer.Serialize(new CropSelection { X = 0, Y = 0, Width = 10, Height = 10 });

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.05)]
    [InlineData(50.1)]
    [InlineData(double.NaN)]
    public async Task ToStlRejectsThicknessOutsideTheAllowedRange(double thickness)
    {
        var controller = CreateController();

        var result = await controller.ToStl(ValidImage(), thickness, 30, 60, ModelOrientation.Horizontal);

        AssertBadRequest(result);
    }

    [Theory]
    [InlineData(0.5, 60)]
    [InlineData(600, 60)]
    [InlineData(30, 0.5)]
    [InlineData(30, 600)]
    [InlineData(double.NaN, 60)]
    public async Task ToStlRejectsModelDimensionsOutsideTheAllowedRange(double modelWidth, double modelHeight)
    {
        var controller = CreateController();

        var result = await controller.ToStl(ValidImage(), 2, modelWidth, modelHeight, ModelOrientation.Horizontal);

        AssertBadRequest(result);
    }

    [Theory]
    [InlineData("diagonal")]
    [InlineData("")]
    [InlineData("HORIZONTALLY")]
    public async Task ToStlRejectsUnknownOrientation(string orientation)
    {
        var controller = CreateController();

        var result = await controller.ToStl(ValidImage(), 2, 30, 60, orientation);

        AssertBadRequest(result);
    }

    [Theory]
    [InlineData("diagonal")]
    [InlineData("")]
    public async Task ToBlackAndWhiteRejectsUnknownOrientation(string orientation)
    {
        var controller = CreateController();

        var result = await controller.ToBlackAndWhite(ValidImage(), ValidSelection, orientation, false, false, 0);

        AssertBadRequest(result);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("[1, 2, 3]")]
    [InlineData("\"selection\"")]
    public async Task ToBlackAndWhiteRejectsMalformedSelectionJson(string selection)
    {
        var controller = CreateController();

        var result = await controller.ToBlackAndWhite(
            ValidImage(), selection, ModelOrientation.Horizontal, false, false, 0);

        AssertBadRequest(result);
    }

    [Fact]
    public async Task ToBlackAndWhiteDoesNotLeakExceptionTextOnFailure()
    {
        var controller = CreateController(new InvalidOperationException(SecretFailure));

        var result = await controller.ToBlackAndWhite(
            ValidImage(), ValidSelection, ModelOrientation.Horizontal, false, false, 0);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, objectResult.StatusCode);

        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.DoesNotContain(SecretFailure, problem.Detail);
        Assert.DoesNotContain("StlGeneratorService", problem.Detail ?? string.Empty);
        Assert.DoesNotContain("Exception", problem.Detail ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertBadRequest(IActionResult result)
    {
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);

        Assert.False(string.IsNullOrWhiteSpace(problem.Detail));
    }

    private static IFormFile ValidImage() =>
        TestImages.AsFormFile(TestImages.Create(4, 4, (_, _) => TestImages.Black));

    private static ConvertController CreateController(Exception? imageServiceFailure = null) =>
        new(
            new StubImageProcessingService { ThrowOnConvert = imageServiceFailure },
            new StubStlGeneratorService(),
            NullLogger<ConvertController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            ProblemDetailsFactory = new StubProblemDetailsFactory()
        };
}
