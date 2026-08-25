namespace ImgToStlPlate.API.Models;

public static class ModelOrientation
{
    public const string Horizontal = "horizontal";
    public const string Vertical = "vertical";

    public static bool IsHorizontal(string? value) =>
        string.Equals(value, Horizontal, StringComparison.OrdinalIgnoreCase);

    public static bool IsVertical(string? value) =>
        string.Equals(value, Vertical, StringComparison.OrdinalIgnoreCase);

    public static bool IsValid(string? value) => IsHorizontal(value) || IsVertical(value);
}
