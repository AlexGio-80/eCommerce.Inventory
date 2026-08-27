using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Serilog.Context;

namespace eCommerce.Inventory.Api.Middleware;

public class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;
    private const string CorrelationIdHeader = "X-Correlation-ID";

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers[CorrelationIdHeader].FirstOrDefault()
            ?? Activity.Current?.Id
            ?? Guid.NewGuid().ToString("N")[..12];

        context.Response.Headers[CorrelationIdHeader] = correlationId;
        _logger.LogDebug("Set CorrelationId header to: {CorrelationId}", correlationId);

        using (LogContext.PushProperty("CorrelationId", correlationId))
        using (LogContext.PushProperty("TraceId", Activity.Current?.TraceId.ToString()))
        using (LogContext.PushProperty("SpanId", Activity.Current?.SpanId.ToString()))
        {
            await _next(context);
        }
    }
}

public static class CorrelationIdMiddlewareExtensions
{
    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<CorrelationIdMiddleware>();
    }
}