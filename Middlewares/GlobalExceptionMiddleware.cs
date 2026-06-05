using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using System.Net;
using System.Text.Json;
using SantiyeAPI.Exceptions;

namespace SantiyeAPI.Middlewares;

public class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;

    public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/problem+json";

        var statusCode = exception switch
        {
            NotFoundException => HttpStatusCode.NotFound,           // 404
            BusinessException => HttpStatusCode.BadRequest,         // 400
            InvalidOperationException => HttpStatusCode.BadRequest, // 400
            _ => HttpStatusCode.InternalServerError                 // 500
        };

        context.Response.StatusCode = (int)statusCode;

        var problemDetails = new ProblemDetails
        {
            Status = context.Response.StatusCode,
            Instance = context.Request.Path,
            Title = statusCode == HttpStatusCode.InternalServerError ? "Sunucu Hatası" : "İşlem Hatası",
            Detail = exception.Message
        };

        problemDetails.Extensions["traceId"] = context.TraceIdentifier;

        if (statusCode == HttpStatusCode.InternalServerError)
            _logger.LogError(exception, "Şantiyede Teknik Arıza!");
        else
            _logger.LogWarning("İş Kuralı İhlali: {Message}", exception.Message);

        await context.Response.WriteAsync(JsonSerializer.Serialize(problemDetails));
    }
}