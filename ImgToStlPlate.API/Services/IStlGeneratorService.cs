namespace ImgToStlPlate.API.Services;

public interface IStlGeneratorService
{
    byte[] GenerateStl(int[,] matrix, double thickness, double mmPerPixel, double zeroToHalfRatio);
}
