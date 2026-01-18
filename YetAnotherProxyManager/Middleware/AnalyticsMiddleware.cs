using System.Diagnostics;
using YetAnotherProxyManager.Models;
using YetAnotherProxyManager.Services;

namespace YetAnotherProxyManager.Middleware;

public class AnalyticsMiddleware
{
    private readonly RequestDelegate _next;
    private readonly AnalyticsService _analyticsService;
    private readonly ConfigurationService _configService;
    private readonly ILogger<AnalyticsMiddleware> _logger;

    public AnalyticsMiddleware(
        RequestDelegate next,
        AnalyticsService analyticsService,
        ConfigurationService configService,
        ILogger<AnalyticsMiddleware> logger)
    {
        _next = next;
        _analyticsService = analyticsService;
        _configService = configService;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var settings = _configService.GetSettings();
        var port = context.Connection.LocalPort;

        // Only track proxy traffic, not management UI
        if (port == settings.ManagementPort)
        {
            await _next(context);
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        var request = new RequestAnalytics
        {
            Timestamp = DateTime.UtcNow,
            Path = context.Request.Path.ToString(),
            Method = context.Request.Method,
            Host = context.Request.Host.Host,
            ClientIp = GetClientIp(context),
            IsSecure = context.Request.IsHttps,
            UserAgent = context.Request.Headers.UserAgent.ToString(),
            Referer = context.Request.Headers.Referer.ToString(),
            RequestSize = context.Request.ContentLength ?? 0
        };

        // Find matching route name
        var route = _configService.GetEnabledHttpRoutes()
            .FirstOrDefault(r => r.HttpConfig?.Hosts?.Contains(request.Host, StringComparer.OrdinalIgnoreCase) == true);
        request.RouteName = route?.Name ?? "Unknown";

        try
        {
            await _next(context);
        }
        finally
        {
            stopwatch.Stop();
            request.ResponseTimeMs = stopwatch.ElapsedMilliseconds;
            request.StatusCode = context.Response.StatusCode;

            // Try to get response size
            if (context.Response.ContentLength.HasValue)
            {
                request.ResponseSize = context.Response.ContentLength.Value;
            }

            // Record analytics asynchronously (fire and forget)
            _ = Task.Run(async () =>
            {
                try
                {
                    await _analyticsService.RecordRequestAsync(request);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to record analytics");
                }
            });
        }
    }

    private static string GetClientIp(HttpContext context)
    {
        // Check for forwarded headers first (reverse proxy scenario)
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            // Take the first IP in the chain (original client)
            var ips = forwardedFor.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (ips.Length > 0)
            {
                return ips[0];
            }
        }

        // Check X-Real-IP header
        var realIp = context.Request.Headers["X-Real-IP"].FirstOrDefault();
        if (!string.IsNullOrEmpty(realIp))
        {
            return realIp;
        }

        // Fall back to connection remote IP
        return context.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
    }
}

public static class AnalyticsMiddlewareExtensions
{
    public static IApplicationBuilder UseAnalytics(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<AnalyticsMiddleware>();
    }
}
