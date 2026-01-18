using YetAnotherProxyManager.Data;
using YetAnotherProxyManager.Models;

namespace YetAnotherProxyManager.Services;

public class ConfigurationService
{
    private readonly LiteDbRepository _repository;
    private readonly ILogger<ConfigurationService> _logger;
    private ServiceManager? _serviceManager;

    public event Action? ConfigurationChanged;

    public ConfigurationService(LiteDbRepository repository, ILogger<ConfigurationService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Wires up the ServiceManager to propagate its changes through ConfigurationChanged
    /// </summary>
    public void SetServiceManager(ServiceManager serviceManager)
    {
        _serviceManager = serviceManager;
        _serviceManager.ServicesChanged += () => NotifyConfigurationChanged();
    }

    public ServiceManager? ServiceManager => _serviceManager;

    // Routes
    public IEnumerable<ProxyRoute> GetAllRoutes() => _repository.GetAllRoutes();

    public IEnumerable<ProxyRoute> GetEnabledRoutes() => _repository.GetEnabledRoutes();

    public IEnumerable<ProxyRoute> GetEnabledHttpRoutes() =>
        _repository.GetEnabledRoutes().Where(r => r.Type == RouteType.Http);

    public IEnumerable<ProxyRoute> GetEnabledTcpRoutes() =>
        _repository.GetEnabledRoutes().Where(r => r.Type == RouteType.Tcp);

    public IEnumerable<ProxyRoute> GetEnabledUdpRoutes() =>
        _repository.GetEnabledRoutes().Where(r => r.Type == RouteType.Udp);

    public ProxyRoute? GetRoute(Guid id) => _repository.GetRoute(id);

    public void SaveRoute(ProxyRoute route)
    {
        _repository.UpsertRoute(route);
        _logger.LogInformation("Route saved: {Name} ({Type})", route.Name, route.Type);
        NotifyConfigurationChanged();
    }

    public bool DeleteRoute(Guid id)
    {
        var result = _repository.DeleteRoute(id);
        if (result)
        {
            _logger.LogInformation("Route deleted: {Id}", id);
            NotifyConfigurationChanged();
        }
        return result;
    }

    // Certificates
    public IEnumerable<Certificate> GetAllCertificates() => _repository.GetAllCertificates();

    public Certificate? GetCertificate(Guid id) => _repository.GetCertificate(id);

    public Certificate? GetCertificateByDomain(string domain) => _repository.GetCertificateByDomain(domain);

    public IEnumerable<Certificate> GetCertificatesNeedingRenewal() =>
        _repository.GetCertificatesNeedingRenewal(GetSettings().CertificateRenewalDays);

    public void SaveCertificate(Certificate cert)
    {
        _repository.UpsertCertificate(cert);
        _logger.LogInformation("Certificate saved: {Name}", cert.Name);
        NotifyConfigurationChanged();
    }

    public bool DeleteCertificate(Guid id)
    {
        var result = _repository.DeleteCertificate(id);
        if (result)
        {
            _logger.LogInformation("Certificate deleted: {Id}", id);
            NotifyConfigurationChanged();
        }
        return result;
    }

    // Filter Configurations
    public IEnumerable<FilterConfiguration> GetAllFilterConfigurations() =>
        _repository.GetAllFilterConfigurations();

    public FilterConfiguration? GetFilterConfiguration(Guid id) =>
        _repository.GetFilterConfiguration(id);

    public FilterConfiguration? GetFilterConfigurationByRoute(Guid routeId) =>
        _repository.GetFilterConfigurationByRoute(routeId);

    public void SaveFilterConfiguration(FilterConfiguration config)
    {
        _repository.UpsertFilterConfiguration(config);
        _logger.LogInformation("Filter configuration saved for route: {RouteId}", config.RouteId);
        NotifyConfigurationChanged();
    }

    public bool DeleteFilterConfiguration(Guid id)
    {
        var result = _repository.DeleteFilterConfiguration(id);
        if (result)
        {
            _logger.LogInformation("Filter configuration deleted: {Id}", id);
            NotifyConfigurationChanged();
        }
        return result;
    }

    public bool DeleteFilterConfigurationByRoute(Guid routeId)
    {
        var result = _repository.DeleteFilterConfigurationByRoute(routeId);
        if (result)
        {
            _logger.LogInformation("Filter configuration deleted for route: {RouteId}", routeId);
            NotifyConfigurationChanged();
        }
        return result;
    }

    // Settings
    public AppSettings GetSettings() => _repository.GetSettings();

    public void SaveSettings(AppSettings settings)
    {
        _repository.SaveSettings(settings);
        _logger.LogInformation("Settings saved");
        NotifyConfigurationChanged();
    }

    private void NotifyConfigurationChanged()
    {
        try
        {
            ConfigurationChanged?.Invoke();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error notifying configuration change");
        }
    }
}
