using ImgToStlPlate.API.Binding;
using ImgToStlPlate.API.Middleware;
using ImgToStlPlate.API.Services;
using Microsoft.AspNetCore.Http.Features;

namespace ImgToStlPlate.API;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddSingleton<IImageProcessingService, ImageProcessingService>();
        builder.Services.AddSingleton<IStlGeneratorService, StlGeneratorService>();

        builder.Services.AddControllers(options =>
        {
            options.ModelBinderProviders.Insert(0, new FlexibleDoubleModelBinderProvider());
        });
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();

        builder.Services.AddLogging();

        builder.Services.Configure<FormOptions>(options =>
        {
            options.MultipartBodyLengthLimit = AppConstants.MaxRequestBodyBytes;
        });

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Limits.MaxRequestBodySize = AppConstants.MaxRequestBodyBytes;
        });

        var app = builder.Build();

        app.UseMiddleware<RequestSizeLimitMiddleware>();

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.MapControllers();
        app.MapFallbackToFile("index.html");

        app.Run();
    }
}
