using YetAnotherProxyManager.Data;
using YetAnotherProxyManager.Models;

namespace YetAnotherProxyManager.Services;

public class ServiceManager
{
    private readonly LiteDbRepository _repository;
    private readonly ILogger<ServiceManager> _logger;

    public event Action? ServicesChanged;

    public ServiceManager(LiteDbRepository repository, ILogger<ServiceManager> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    // Services
    public IEnumerable<Service> GetAllServices() => _repository.GetAllServices();

    public IEnumerable<Service> GetEnabledServices() => _repository.GetEnabledServices();

    public Service? GetService(Guid id) => _repository.GetService(id);

    public Service? GetServiceByName(string name) => _repository.GetServiceByName(name);

    public IEnumerable<Service> GetServicesBySource(ServiceSource source) =>
        _repository.GetServicesBySource(source);

    public void SaveService(Service service)
    {
        _repository.UpsertService(service);
        _logger.LogInformation("Service saved: {Name} ({Source})", service.Name, service.Source);
        NotifyServicesChanged();
    }

    public bool DeleteService(Guid id)
    {
        var result = _repository.DeleteService(id);
        if (result)
        {
            _logger.LogInformation("Service deleted: {Id}", id);
            NotifyServicesChanged();
        }
        return result;
    }

    /// <summary>
    /// Resolves a service endpoint to its full address (e.g., "http://10.0.0.3:32400")
    /// </summary>
    public string? ResolveServiceEndpoint(Guid serviceId, Guid endpointId)
    {
        var service = GetService(serviceId);
        if (service == null || !service.Enabled)
            return null;

        var endpoint = service.Endpoints.FirstOrDefault(e => e.Id == endpointId);
        if (endpoint == null || !endpoint.Enabled)
            return null;

        return BuildEndpointAddress(service, endpoint);
    }

    /// <summary>
    /// Resolves an UpstreamServer's address, handling service references if set
    /// </summary>
    public string ResolveUpstreamAddress(UpstreamServer upstream)
    {
        // If no service reference, return the direct address
        if (!upstream.ServiceId.HasValue || !upstream.ServiceEndpointId.HasValue)
            return upstream.Address;

        // Try to resolve from service
        var resolved = ResolveServiceEndpoint(upstream.ServiceId.Value, upstream.ServiceEndpointId.Value);
        return resolved ?? upstream.Address; // Fallback to stored address if service not found
    }

    /// <summary>
    /// Gets all routes that depend on a specific service
    /// </summary>
    public IEnumerable<ProxyRoute> GetRoutesUsingService(Guid serviceId, IEnumerable<ProxyRoute> allRoutes)
    {
        return allRoutes.Where(r =>
            r.HttpConfig?.Upstreams.Any(u => u.ServiceId == serviceId) == true);
    }

    /// <summary>
    /// Gets all routes that depend on a specific service endpoint
    /// </summary>
    public IEnumerable<ProxyRoute> GetRoutesUsingEndpoint(Guid serviceId, Guid endpointId, IEnumerable<ProxyRoute> allRoutes)
    {
        return allRoutes.Where(r =>
            r.HttpConfig?.Upstreams.Any(u =>
                u.ServiceId == serviceId && u.ServiceEndpointId == endpointId) == true);
    }

    /// <summary>
    /// Checks if a service can be safely deleted (no routes depend on it)
    /// </summary>
    public (bool CanDelete, List<string> DependentRouteNames) CanDeleteService(Guid serviceId, IEnumerable<ProxyRoute> allRoutes)
    {
        var dependentRoutes = GetRoutesUsingService(serviceId, allRoutes).ToList();
        return (dependentRoutes.Count == 0, dependentRoutes.Select(r => r.Name).ToList());
    }

    /// <summary>
    /// Gets the service and endpoint names for display purposes
    /// </summary>
    public (string? ServiceName, string? EndpointName) GetServiceEndpointNames(Guid? serviceId, Guid? endpointId)
    {
        if (!serviceId.HasValue)
            return (null, null);

        var service = GetService(serviceId.Value);
        if (service == null)
            return (null, null);

        string? endpointName = null;
        if (endpointId.HasValue)
        {
            var endpoint = service.Endpoints.FirstOrDefault(e => e.Id == endpointId.Value);
            endpointName = endpoint?.Name;
        }

        return (service.Name, endpointName);
    }

    private string BuildEndpointAddress(Service service, ServiceEndpoint endpoint)
    {
        var protocol = endpoint.Protocol.ToLowerInvariant();
        var baseAddress = service.BaseAddress.TrimEnd('/');
        var port = endpoint.Port;
        var pathPrefix = endpoint.PathPrefix?.TrimStart('/');

        var address = $"{protocol}://{baseAddress}:{port}";
        if (!string.IsNullOrEmpty(pathPrefix))
        {
            address += $"/{pathPrefix}";
        }

        return address;
    }

    private void NotifyServicesChanged()
    {
        try
        {
            ServicesChanged?.Invoke();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error notifying services change");
        }
    }
}
