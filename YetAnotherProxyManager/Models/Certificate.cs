namespace YetAnotherProxyManager.Models;

public enum CertificateType
{
    LetsEncrypt,
    Custom
}

public class Certificate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public CertificateType Type { get; set; }
    public List<string> Domains { get; set; } = new();
    public DateTime? IssuedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public byte[]? PfxData { get; set; }
    public string? PfxPassword { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastRenewalAttempt { get; set; }
    public string? LastRenewalError { get; set; }
    public bool AutoRenew { get; set; } = true;
}
