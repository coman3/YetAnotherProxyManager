namespace YetAnotherProxyManager.Models;

public enum ServiceSource
{
    Manual,
    Portainer,
    TrueNAS,
    Docker
}

public class Service
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string BaseAddress { get; set; } = string.Empty;
    public List<ServiceEndpoint> Endpoints { get; set; } = new();
    public ServiceSource Source { get; set; } = ServiceSource.Manual;
    public string? ExternalId { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}

public class ServiceEndpoint
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public int Port { get; set; }
    public string Protocol { get; set; } = "http";
    public string? PathPrefix { get; set; }
    public bool Enabled { get; set; } = true;
}
