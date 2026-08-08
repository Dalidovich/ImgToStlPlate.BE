using ImgToStlPlate.API.Services;

namespace ImgToStlPlate.API;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddSingleton<IImageProcessingService, ImageProcessingService>();
        builder.Services.AddSingleton<IStlGeneratorService, StlGeneratorService>();

        builder.Services.AddControllers();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();

        builder.Services.AddCors(options =>
        {
            options.AddPolicy("AllowAngular", policy =>
            {
                policy.WithOrigins("http://localhost:4200")
                      .AllowAnyHeader()
                      .AllowAnyMethod();
            });
        });

        builder.Services.AddLogging();

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Limits.MaxRequestBodySize = 52428800;
        });

        var app = builder.Build();

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseCors("AllowAngular");
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.MapControllers();
        app.MapFallbackToFile("index.html");

        app.Run();
    }
}
