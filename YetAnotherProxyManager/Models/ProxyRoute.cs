namespace YetAnotherProxyManager.Models;

public enum RouteType
{
    Http,
    Tcp,
    Udp
}

public enum SslMode
{
    None,
    LetsEncrypt,
    Custom
}

public enum LoadBalancingPolicy
{
    RoundRobin,
    Random,
    LeastRequests,
    PowerOfTwoChoices
}

public class ProxyRoute
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public RouteType Type { get; set; } = RouteType.Http;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public HttpRouteConfig? HttpConfig { get; set; }
    public StreamRouteConfig? StreamConfig { get; set; }
}

public class HttpRouteConfig
{
    public List<string> Hosts { get; set; } = new();
    public string? PathPrefix { get; set; }
    public List<UpstreamServer> Upstreams { get; set; } = new();
    public LoadBalancingPolicy LoadBalancing { get; set; } = LoadBalancingPolicy.RoundRobin;
    public SslMode SslMode { get; set; } = SslMode.None;
    public Guid? CustomCertificateId { get; set; }
    public bool ForceHttps { get; set; }
    public Dictionary<string, string> RequestHeaders { get; set; } = new();
    public Dictionary<string, string> ResponseHeaders { get; set; } = new();
    public int? TimeoutSeconds { get; set; }
}

public class UpstreamServer
{
    public string Address { get; set; } = string.Empty;
    public int Weight { get; set; } = 1;
    public bool Enabled { get; set; } = true;
}

public class StreamRouteConfig
{
    public int ListenPort { get; set; }
    public string UpstreamHost { get; set; } = string.Empty;
    public int UpstreamPort { get; set; }
    public int? TimeoutSeconds { get; set; }
    public int BufferSize { get; set; } = 8192;
}
