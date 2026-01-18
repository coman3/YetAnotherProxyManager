using YetAnotherProxyManager.Services;

namespace YetAnotherProxyManager.Middleware;

public class AcmeChallengeMiddleware
{
    private readonly RequestDelegate _next;
    private readonly AcmeCertificateService _acmeService;
    private readonly ILogger<AcmeChallengeMiddleware> _logger;

    private const string ChallengePath = "/.well-known/acme-challenge/";

    public AcmeChallengeMiddleware(RequestDelegate next, AcmeCertificateService acmeService, ILogger<AcmeChallengeMiddleware> logger)
    {
        _next = next;
        _acmeService = acmeService;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/.well-known/acme-challenge"))
        {
            var token = context.Request.Path.Value?.Substring(ChallengePath.Length);

            if (!string.IsNullOrEmpty(token))
            {
                var challenge = _acmeService.GetPendingChallenge(token);

                if (challenge != null)
                {
                    _logger.LogInformation("Responding to ACME challenge for token: {Token}", token);
                    context.Response.ContentType = "text/plain";
                    await context.Response.WriteAsync(challenge.KeyAuthorization);
                    return;
                }
            }

            _logger.LogWarning("ACME challenge not found for token: {Token}", token);
            context.Response.StatusCode = 404;
            return;
        }

        await _next(context);
    }
}

public static class AcmeChallengeMiddlewareExtensions
{
    public static IApplicationBuilder UseAcmeChallenge(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<AcmeChallengeMiddleware>();
    }
}
