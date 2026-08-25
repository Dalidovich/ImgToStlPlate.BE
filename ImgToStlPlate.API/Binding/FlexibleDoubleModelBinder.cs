using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace ImgToStlPlate.API.Binding;

public class FlexibleDoubleModelBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        ArgumentNullException.ThrowIfNull(bindingContext);

        var valueProviderResult = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);

        if (valueProviderResult == ValueProviderResult.None)
            return Task.CompletedTask;

        bindingContext.ModelState.SetModelValue(bindingContext.ModelName, valueProviderResult);

        string? value = valueProviderResult.FirstValue;

        if (string.IsNullOrWhiteSpace(value))
        {
            if (bindingContext.ModelType == typeof(double?))
                bindingContext.Result = ModelBindingResult.Success(null);

            return Task.CompletedTask;
        }

        if (FlexibleNumber.TryParseDouble(value, out double parsed))
        {
            bindingContext.Result = ModelBindingResult.Success(parsed);
        }
        else
        {
            bindingContext.ModelState.TryAddModelError(
                bindingContext.ModelName,
                string.Format(CultureInfo.InvariantCulture, "The value '{0}' is not a valid number.", value));
        }

        return Task.CompletedTask;
    }
}
