using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace ImgToStlPlate.API.Binding;

public class FlexibleDoubleModelBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var type = context.Metadata.ModelType;

        return type == typeof(double) || type == typeof(double?)
            ? new FlexibleDoubleModelBinder()
            : null;
    }
}
