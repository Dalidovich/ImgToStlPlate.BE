using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using ImgToStlPlate.API.Models;

namespace ImgToStlPlate.API.Services;

public interface IImageProcessingService
{
    Task<Image<Rgba32>> CropAndConvertToBw(IFormFile image, CropSelection selection, string orientation, bool fillSpace, bool invert);
    Task<Image<Rgba32>> Denoise(Image<Rgba32> bwImage, int intensity);
}
