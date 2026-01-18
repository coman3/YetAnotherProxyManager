using System.Text.RegularExpressions;
using YetAnotherProxyManager.Models;

namespace YetAnotherProxyManager.Services;

public class RouteValidationService
{
    private readonly ConfigurationService _configService;

    public RouteValidationService(ConfigurationService configService)
    {
        _configService = configService;
    }

    public ValidationResult ValidateRoute(ProxyRoute route, bool isNew = false)
    {
        var errors = new List<ValidationError>();
        var warnings = new List<ValidationWarning>();

        // Basic validation
        if (string.IsNullOrWhiteSpace(route.Name))
        {
            errors.Add(new ValidationError("Name", "Route name is required"));
        }
        else
        {
            // Check for duplicate names
            var existingRoute = _configService.GetAllRoutes()
                .FirstOrDefault(r => r.Name.Equals(route.Name, StringComparison.OrdinalIgnoreCase) && r.Id != route.Id);
            if (existingRoute != null)
            {
                errors.Add(new ValidationError("Name", $"A route with the name '{route.Name}' already exists"));
            }
        }

        // Type-specific validation
        switch (route.Type)
        {
            case RouteType.Http:
                ValidateHttpRoute(route, errors, warnings);
                break;
            case RouteType.Tcp:
            case RouteType.Udp:
                ValidateStreamRoute(route, errors, warnings);
                break;
        }

        return new ValidationResult(errors, warnings);
    }

    private void ValidateHttpRoute(ProxyRoute route, List<ValidationError> errors, List<ValidationWarning> warnings)
    {
        if (route.HttpConfig == null)
        {
            errors.Add(new ValidationError("HttpConfig", "HTTP configuration is required for HTTP routes"));
            return;
        }

        var config = route.HttpConfig;

        // Host validation
        if (config.Hosts.Count == 0)
        {
            warnings.Add(new ValidationWarning("Hosts", "No hosts specified - route will match all incoming requests"));
        }
        else
        {
            foreach (var host in config.Hosts)
            {
                if (!IsValidHostname(host))
                {
                    errors.Add(new ValidationError("Hosts", $"Invalid hostname format: '{host}'"));
                }
            }

            // Check for duplicate hosts across routes
            var duplicateHosts = FindDuplicateHosts(config.Hosts, route.Id);
            foreach (var (host, existingRouteName) in duplicateHosts)
            {
                warnings.Add(new ValidationWarning("Hosts",
                    $"Host '{host}' is already used by route '{existingRouteName}'. This may cause routing conflicts."));
            }
        }

        // Upstream validation
        if (config.Upstreams.Count == 0)
        {
            errors.Add(new ValidationError("Upstreams", "At least one upstream server is required"));
        }
        else
        {
            var enabledUpstreams = config.Upstreams.Where(u => u.Enabled).ToList();
            if (enabledUpstreams.Count == 0)
            {
                errors.Add(new ValidationError("Upstreams", "At least one upstream server must be enabled"));
            }

            for (int i = 0; i < config.Upstreams.Count; i++)
            {
                var upstream = config.Upstreams[i];

                // Only validate address if not using service reference
                if (!upstream.ServiceId.HasValue && !upstream.ServiceEndpointId.HasValue)
                {
                    if (string.IsNullOrWhiteSpace(upstream.Address))
                    {
                        errors.Add(new ValidationError($"Upstreams[{i}].Address",
                            "Upstream address is required when not using a service reference"));
                    }
                    else if (!IsValidUpstreamAddress(upstream.Address))
                    {
                        errors.Add(new ValidationError($"Upstreams[{i}].Address",
                            $"Invalid upstream address format: '{upstream.Address}'"));
                    }
                }

                if (upstream.Weight < 1)
                {
                    errors.Add(new ValidationError($"Upstreams[{i}].Weight", "Weight must be at least 1"));
                }
            }
        }

        // SSL validation
        if (config.SslMode == SslMode.Custom && !config.CustomCertificateId.HasValue)
        {
            errors.Add(new ValidationError("CustomCertificateId",
                "A certificate must be selected when using custom SSL mode"));
        }

        if (config.SslMode == SslMode.LetsEncrypt)
        {
            var settings = _configService.GetSettings();
            if (string.IsNullOrWhiteSpace(settings.AcmeEmail))
            {
                warnings.Add(new ValidationWarning("SslMode",
                    "Let's Encrypt requires an ACME email address. Please configure it in Settings."));
            }

            foreach (var host in config.Hosts)
            {
                if (host.StartsWith("*."))
                {
                    warnings.Add(new ValidationWarning("SslMode",
                        $"Wildcard domain '{host}' requires DNS-01 challenge which is not currently supported"));
                }
            }
        }

        // Timeout validation
        if (config.TimeoutSeconds.HasValue && config.TimeoutSeconds.Value < 1)
        {
            errors.Add(new ValidationError("TimeoutSeconds", "Timeout must be at least 1 second"));
        }

        if (config.AdvancedTimeouts != null)
        {
            if (config.AdvancedTimeouts.ConnectTimeoutSeconds.HasValue &&
                config.AdvancedTimeouts.ConnectTimeoutSeconds.Value < 1)
            {
                errors.Add(new ValidationError("AdvancedTimeouts.ConnectTimeoutSeconds",
                    "Connect timeout must be at least 1 second"));
            }
            if (config.AdvancedTimeouts.RequestTimeoutSeconds.HasValue &&
                config.AdvancedTimeouts.RequestTimeoutSeconds.Value < 1)
            {
                errors.Add(new ValidationError("AdvancedTimeouts.RequestTimeoutSeconds",
                    "Request timeout must be at least 1 second"));
            }
            if (config.AdvancedTimeouts.ResponseTimeoutSeconds.HasValue &&
                config.AdvancedTimeouts.ResponseTimeoutSeconds.Value < 1)
            {
                errors.Add(new ValidationError("AdvancedTimeouts.ResponseTimeoutSeconds",
                    "Response timeout must be at least 1 second"));
            }
        }

        // Retry policy validation
        if (config.RetryPolicy?.Enabled == true)
        {
            if (config.RetryPolicy.MaxRetries < 1 || config.RetryPolicy.MaxRetries > 10)
            {
                errors.Add(new ValidationError("RetryPolicy.MaxRetries",
                    "Max retries must be between 1 and 10"));
            }
        }
    }

    private void ValidateStreamRoute(ProxyRoute route, List<ValidationError> errors, List<ValidationWarning> warnings)
    {
        if (route.StreamConfig == null)
        {
            errors.Add(new ValidationError("StreamConfig",
                $"{route.Type} configuration is required for {route.Type} routes"));
            return;
        }

        var config = route.StreamConfig;

        // Port validation
        if (config.ListenPort < 1 || config.ListenPort > 65535)
        {
            errors.Add(new ValidationError("ListenPort", "Listen port must be between 1 and 65535"));
        }
        else
        {
            // Check for port conflicts
            var conflictingRoute = FindPortConflict(config.ListenPort, route.Type, route.Id);
            if (conflictingRoute != null)
            {
                errors.Add(new ValidationError("ListenPort",
                    $"Port {config.ListenPort} is already used by route '{conflictingRoute}'"));
            }
        }

        if (string.IsNullOrWhiteSpace(config.UpstreamHost))
        {
            errors.Add(new ValidationError("UpstreamHost", "Upstream host is required"));
        }

        if (config.UpstreamPort < 1 || config.UpstreamPort > 65535)
        {
            errors.Add(new ValidationError("UpstreamPort", "Upstream port must be between 1 and 65535"));
        }

        if (config.BufferSize < 1024 || config.BufferSize > 1048576)
        {
            warnings.Add(new ValidationWarning("BufferSize",
                "Buffer size is outside recommended range (1KB - 1MB)"));
        }
    }

    private bool IsValidHostname(string hostname)
    {
        if (string.IsNullOrWhiteSpace(hostname))
            return false;

        // Allow wildcard prefix
        if (hostname.StartsWith("*."))
        {
            hostname = hostname[2..];
        }

        // Basic hostname regex (allows subdomains)
        var hostnameRegex = new Regex(
            @"^(?!-)[A-Za-z0-9-]{1,63}(?<!-)(\.[A-Za-z0-9-]{1,63})*$",
            RegexOptions.Compiled);

        return hostnameRegex.IsMatch(hostname);
    }

    private bool IsValidUpstreamAddress(string address)
    {
        // Try to parse as URI
        if (Uri.TryCreate(address, UriKind.Absolute, out var uri))
        {
            return uri.Scheme == "http" || uri.Scheme == "https";
        }

        // Also allow simple host:port format
        var hostPortRegex = new Regex(
            @"^(?:https?://)?[\w\.-]+(?::\d+)?(?:/.*)?$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        return hostPortRegex.IsMatch(address);
    }

    private List<(string Host, string RouteName)> FindDuplicateHosts(List<string> hosts, Guid excludeRouteId)
    {
        var duplicates = new List<(string, string)>();
        var existingRoutes = _configService.GetAllRoutes()
            .Where(r => r.Id != excludeRouteId && r.Type == RouteType.Http && r.HttpConfig != null);

        foreach (var host in hosts)
        {
            foreach (var existingRoute in existingRoutes)
            {
                if (existingRoute.HttpConfig!.Hosts.Any(h =>
                    h.Equals(host, StringComparison.OrdinalIgnoreCase)))
                {
                    duplicates.Add((host, existingRoute.Name));
                }
            }
        }

        return duplicates;
    }

    private string? FindPortConflict(int port, RouteType type, Guid excludeRouteId)
    {
        return _configService.GetAllRoutes()
            .Where(r => r.Id != excludeRouteId && r.Type == type && r.StreamConfig != null)
            .FirstOrDefault(r => r.StreamConfig!.ListenPort == port)
            ?.Name;
    }
}

public class ValidationResult
{
    public List<ValidationError> Errors { get; }
    public List<ValidationWarning> Warnings { get; }
    public bool IsValid => Errors.Count == 0;

    public ValidationResult(List<ValidationError> errors, List<ValidationWarning> warnings)
    {
        Errors = errors;
        Warnings = warnings;
    }
}

public record ValidationError(string Field, string Message);
public record ValidationWarning(string Field, string Message);
