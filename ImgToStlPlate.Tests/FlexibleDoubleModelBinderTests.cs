using System.Globalization;
using ImgToStlPlate.API.Binding;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Primitives;

namespace ImgToStlPlate.Tests;

public class FlexibleDoubleModelBinderTests
{
    [Theory]
    [InlineData("2.5", 2.5)]
    [InlineData("2,5", 2.5)]
    [InlineData("100.75", 100.75)]
    [InlineData("100,75", 100.75)]
    [InlineData("3", 3.0)]
    [InlineData("-1.5", -1.5)]
    [InlineData("-1,5", -1.5)]
    [InlineData("0.1", 0.1)]
    [InlineData("0,1", 0.1)]
    [InlineData(" 2.5 ", 2.5)]
    [InlineData("1e2", 100.0)]
    public void ParsesBothDecimalSeparators(string text, double expected)
    {
        Assert.True(FlexibleNumber.TryParseDouble(text, out double value));
        Assert.Equal(expected, value, 10);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("abc")]
    [InlineData("1.2.3")]
    [InlineData("1,2,3")]
    [InlineData("1.2,3")]
    public void RejectsValuesThatAreNotSingleNumbers(string? text)
    {
        Assert.False(FlexibleNumber.TryParseDouble(text, out _));
    }

    [Theory]
    [InlineData("ru-RU")]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    public void BindsDotAndCommaRegardlessOfCurrentCulture(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo(culture);

        try
        {
            Assert.Equal(2.5, BindValue("2.5"));
            Assert.Equal(2.5, BindValue("2,5"));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public async Task ReportsAModelErrorForAnUnparsableValue()
    {
        var context = CreateContext("not-a-number");

        await new FlexibleDoubleModelBinder().BindModelAsync(context);

        Assert.False(context.Result.IsModelSet);
        Assert.False(context.ModelState.IsValid);
    }

    [Fact]
    public async Task LeavesTheResultUnsetWhenTheFieldIsAbsent()
    {
        var context = CreateContext(null);

        await new FlexibleDoubleModelBinder().BindModelAsync(context);

        Assert.False(context.Result.IsModelSet);
    }

    private static double BindValue(string text)
    {
        var context = CreateContext(text);

        new FlexibleDoubleModelBinder().BindModelAsync(context).GetAwaiter().GetResult();

        Assert.True(context.Result.IsModelSet);
        return Assert.IsType<double>(context.Result.Model);
    }

    private static DefaultModelBindingContext CreateContext(string? value)
    {
        var provider = new EmptyModelMetadataProvider();

        return new DefaultModelBindingContext
        {
            ModelName = "value",
            ModelState = new ModelStateDictionary(),
            ModelMetadata = provider.GetMetadataForType(typeof(double)),
            ValueProvider = value is null
                ? new SimpleValueProvider()
                : new SimpleValueProvider { { "value", value } }
        };
    }

    private sealed class SimpleValueProvider : Dictionary<string, StringValues>, IValueProvider
    {
        public bool ContainsPrefix(string prefix) => ContainsKey(prefix);

        public ValueProviderResult GetValue(string key) =>
            TryGetValue(key, out var value)
                ? new ValueProviderResult(value, CultureInfo.InvariantCulture)
                : ValueProviderResult.None;
    }
}
