using YetAnotherProxyManager.Models;
using YetAnotherProxyManager.Services;
using YetAnotherProxyManager.Services.Filtering;

namespace YetAnotherProxyManager.Middleware;

public class RequestFilterMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ConfigurationService _configService;
    private readonly FilterRuleEvaluator _filterEvaluator;
    private readonly ILogger<RequestFilterMiddleware> _logger;

    public RequestFilterMiddleware(
        RequestDelegate next,
        ConfigurationService configService,
        FilterRuleEvaluator filterEvaluator,
        ILogger<RequestFilterMiddleware> logger)
    {
        _next = next;
        _configService = configService;
        _filterEvaluator = filterEvaluator;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var settings = _configService.GetSettings();
        var port = context.Connection.LocalPort;

        // Don't filter management UI traffic
        if (port == settings.ManagementPort)
        {
            await _next(context);
            return;
        }

        var host = context.Request.Host.Host;
        var clientIp = GetClientIp(context);

        // Find matching route
        var route = _configService.GetEnabledHttpRoutes()
            .FirstOrDefault(r => r.HttpConfig?.Hosts?.Contains(host, StringComparer.OrdinalIgnoreCase) == true);

        if (route == null)
        {
            // No matching route found, let it pass through (YARP will handle the error)
            await _next(context);
            return;
        }

        // Check if route has filter configuration
        var filterConfig = _configService.GetFilterConfigurationByRoute(route.Id);

        if (filterConfig == null || !filterConfig.Enabled)
        {
            // No filtering configured for this route
            await _next(context);
            return;
        }

        // Evaluate filter rules
        var result = await _filterEvaluator.EvaluateAsync(filterConfig, clientIp, context.Request.Headers);

        if (result.Action == FilterAction.Deny)
        {
            _logger.LogInformation(
                "Request blocked by filter: Route={RouteName}, ClientIP={ClientIp}, Reason={Reason}",
                route.Name, clientIp, result.Reason);

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "text/plain";
            await context.Response.WriteAsync("Access denied");
            return;
        }

        // Allow the request
        await _next(context);
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
        return context.Connection.RemoteIpAddress?.ToString() ?? "0.0.0.0";
    }
}

public static class RequestFilterMiddlewareExtensions
{
    public static IApplicationBuilder UseRequestFiltering(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<RequestFilterMiddleware>();
    }
}
