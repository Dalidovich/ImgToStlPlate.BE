using Microsoft.AspNetCore.Mvc;

namespace ImgToStlPlate.API.Middleware;

public class RequestSizeLimitMiddleware
{
    private readonly RequestDelegate _next;

    public RequestSizeLimitMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.ContentLength > AppConstants.MaxRequestBodyBytes)
        {
            await WritePayloadTooLargeAsync(context);
            return;
        }

        try
        {
            await _next(context);
        }
        catch (BadHttpRequestException ex)
            when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge && !context.Response.HasStarted)
        {
            await WritePayloadTooLargeAsync(context);
        }
    }

    private static Task WritePayloadTooLargeAsync(HttpContext context)
    {
        context.Response.Clear();
        context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
        context.Response.ContentType = "application/problem+json";

        return context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = StatusCodes.Status413PayloadTooLarge,
            Detail = "The uploaded content exceeds the maximum allowed request size."
        });
    }
}
