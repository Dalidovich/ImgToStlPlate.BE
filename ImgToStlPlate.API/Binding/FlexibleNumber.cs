using System.Globalization;

namespace ImgToStlPlate.API.Binding;

public static class FlexibleNumber
{
    private const NumberStyles Styles = NumberStyles.Float;

    public static bool TryParseDouble(string? text, out double value)
    {
        value = 0;

        if (string.IsNullOrWhiteSpace(text))
            return false;

        if (double.TryParse(text, Styles, CultureInfo.InvariantCulture, out value))
            return true;

        int firstComma = text.IndexOf(',');
        bool hasSingleComma = firstComma >= 0 && text.IndexOf(',', firstComma + 1) < 0;

        if (hasSingleComma && !text.Contains('.'))
            return double.TryParse(text.Replace(',', '.'), Styles, CultureInfo.InvariantCulture, out value);

        value = 0;
        return false;
    }
}
