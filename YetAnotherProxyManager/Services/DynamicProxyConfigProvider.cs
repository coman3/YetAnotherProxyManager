using Microsoft.Extensions.Primitives;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;
using YetAnotherProxyManager.Models;

namespace YetAnotherProxyManager.Services;

public class DynamicProxyConfigProvider : IProxyConfigProvider, IDisposable
{
    private readonly ConfigurationService _configService;
    private readonly ServiceManager _serviceManager;
    private readonly ILogger<DynamicProxyConfigProvider> _logger;
    private CancellationTokenSource _changeTokenSource = new();
    private IProxyConfig _config;

    public DynamicProxyConfigProvider(
        ConfigurationService configService,
        ServiceManager serviceManager,
        ILogger<DynamicProxyConfigProvider> logger)
    {
        _configService = configService;
        _serviceManager = serviceManager;
        _logger = logger;
        _config = BuildConfig();

        _configService.ConfigurationChanged += OnConfigurationChanged;
    }

    public IProxyConfig GetConfig() => _config;

    private void OnConfigurationChanged()
    {
        _logger.LogInformation("Configuration changed, rebuilding YARP config");
        var oldTokenSource = _changeTokenSource;
        _changeTokenSource = new CancellationTokenSource();
        _config = BuildConfig();
        oldTokenSource.Cancel();
        oldTokenSource.Dispose();
    }

    private IProxyConfig BuildConfig()
    {
        var routes = new List<RouteConfig>();
        var clusters = new List<ClusterConfig>();

        foreach (var route in _configService.GetEnabledHttpRoutes())
        {
            if (route.HttpConfig == null) continue;

            var routeId = $"route-{route.Id}";
            var clusterId = $"cluster-{route.Id}";

            var match = new RouteMatch
            {
                Hosts = route.HttpConfig.Hosts.Count > 0 ? route.HttpConfig.Hosts : null,
                Path = route.HttpConfig.PathPrefix != null ? $"{route.HttpConfig.PathPrefix}/{{**catch-all}}" : "{**catch-all}"
            };

            var routeConfig = new RouteConfig
            {
                RouteId = routeId,
                ClusterId = clusterId,
                Match = match
            };

            routes.Add(routeConfig);

            var destinations = new Dictionary<string, DestinationConfig>();
            for (int i = 0; i < route.HttpConfig.Upstreams.Count; i++)
            {
                var upstream = route.HttpConfig.Upstreams[i];
                if (!upstream.Enabled) continue;

                // Resolve address from service if referenced, otherwise use direct address
                var address = _serviceManager.ResolveUpstreamAddress(upstream);
                if (string.IsNullOrEmpty(address))
                {
                    _logger.LogWarning("Skipping upstream {Index} for route {Name}: unable to resolve address",
                        i, route.Name);
                    continue;
                }

                destinations[$"dest-{i}"] = new DestinationConfig
                {
                    Address = address
                };
            }

            var clusterConfig = new ClusterConfig
            {
                ClusterId = clusterId,
                Destinations = destinations,
                LoadBalancingPolicy = route.HttpConfig.LoadBalancing switch
                {
                    LoadBalancingPolicy.RoundRobin => "RoundRobin",
                    LoadBalancingPolicy.Random => "Random",
                    LoadBalancingPolicy.LeastRequests => "LeastRequests",
                    LoadBalancingPolicy.PowerOfTwoChoices => "PowerOfTwoChoices",
                    _ => "RoundRobin"
                }
            };

            if (route.HttpConfig.TimeoutSeconds.HasValue)
            {
                clusterConfig = clusterConfig with
                {
                    HttpRequest = new ForwarderRequestConfig
                    {
                        ActivityTimeout = TimeSpan.FromSeconds(route.HttpConfig.TimeoutSeconds.Value)
                    }
                };
            }

            clusters.Add(clusterConfig);
        }

        _logger.LogInformation("Built YARP config with {RouteCount} routes and {ClusterCount} clusters",
            routes.Count, clusters.Count);

        return new InMemoryConfig(routes, clusters, new CancellationChangeToken(_changeTokenSource.Token));
    }

    public void Dispose()
    {
        _configService.ConfigurationChanged -= OnConfigurationChanged;
        _changeTokenSource.Dispose();
    }
}

internal class InMemoryConfig : IProxyConfig
{
    public InMemoryConfig(IReadOnlyList<RouteConfig> routes, IReadOnlyList<ClusterConfig> clusters, IChangeToken changeToken)
    {
        Routes = routes;
        Clusters = clusters;
        ChangeToken = changeToken;
    }

    public IReadOnlyList<RouteConfig> Routes { get; }
    public IReadOnlyList<ClusterConfig> Clusters { get; }
    public IChangeToken ChangeToken { get; }
}
