using YetAnotherProxyManager.Services;

namespace YetAnotherProxyManager.Middleware;

public class HttpsRedirectMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ConfigurationService _configService;
    private readonly ILogger<HttpsRedirectMiddleware> _logger;

    public HttpsRedirectMiddleware(RequestDelegate next, ConfigurationService configService, ILogger<HttpsRedirectMiddleware> logger)
    {
        _next = next;
        _configService = configService;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.IsHttps)
        {
            await _next(context);
            return;
        }

        var host = context.Request.Host.Host;
        var route = _configService.GetEnabledHttpRoutes()
            .FirstOrDefault(r => r.HttpConfig?.Hosts.Contains(host, StringComparer.OrdinalIgnoreCase) == true);

        if (route?.HttpConfig?.ForceHttps == true)
        {
            var settings = _configService.GetSettings();
            var httpsHost = settings.HttpsPort == 443
                ? host
                : $"{host}:{settings.HttpsPort}";

            var redirectUrl = $"https://{httpsHost}{context.Request.Path}{context.Request.QueryString}";
            _logger.LogDebug("Redirecting to HTTPS: {Url}", redirectUrl);
            context.Response.Redirect(redirectUrl, permanent: true);
            return;
        }

        await _next(context);
    }
}

public static class HttpsRedirectMiddlewareExtensions
{
    public static IApplicationBuilder UseConditionalHttpsRedirect(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<HttpsRedirectMiddleware>();
    }
}
