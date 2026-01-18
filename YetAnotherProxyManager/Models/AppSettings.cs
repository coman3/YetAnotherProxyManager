namespace YetAnotherProxyManager.Models;

public class AppSettings
{
    public const string Key = "AppSettings";

    public string? PasswordHash { get; set; }
    public string? AcmeEmail { get; set; }
    public bool UseAcmeStaging { get; set; } = true;
    public byte[]? AcmeAccountKey { get; set; }
    public int HttpPort { get; set; } = 80;
    public int HttpsPort { get; set; } = 443;
    public int ManagementPort { get; set; } = 8080;
    public string DataPath { get; set; } = "data";
    public int CertificateRenewalDays { get; set; } = 30;
    public bool EnableAccessLog { get; set; } = true;
}

public class AcmeChallenge
{
    public string Token { get; set; } = string.Empty;
    public string KeyAuthorization { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
}
