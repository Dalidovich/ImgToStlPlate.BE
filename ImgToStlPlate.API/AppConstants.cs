public static class AppConstants
{
    public const double MmPerPixel = 0.4;
    public const double ZeroToHalfRatio = 0.5;

    public const double MinModelDimensionMm = 1.0;
    public const double MaxModelDimensionMm = 500.0;
    public const double MinThicknessMm = 0.1;
    public const double MaxThicknessMm = 50.0;

    public const long MaxModelPixels = 2_000_000;

    public const int MaxSourceImageDimension = 10_000;
    public const long MaxSourceImagePixels = 40_000_000;

    public const long MaxRequestBodyBytes = 52_428_800;
}
