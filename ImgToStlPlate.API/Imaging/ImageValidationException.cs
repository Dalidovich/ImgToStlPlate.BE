namespace ImgToStlPlate.API.Imaging;

public class ImageValidationException : Exception
{
    public ImageValidationException(string message) : base(message)
    {
    }
}
