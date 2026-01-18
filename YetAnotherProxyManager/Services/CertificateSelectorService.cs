using System.Security.Cryptography.X509Certificates;

namespace YetAnotherProxyManager.Services;

public class CertificateSelectorService
{
    private readonly ConfigurationService _configService;
    private readonly ILogger<CertificateSelectorService> _logger;
    private readonly Dictionary<string, X509Certificate2> _certificateCache = new();
    private readonly object _cacheLock = new();

    public CertificateSelectorService(ConfigurationService configService, ILogger<CertificateSelectorService> logger)
    {
        _configService = configService;
        _logger = logger;

        _configService.ConfigurationChanged += RefreshCache;
        RefreshCache();
    }

    public X509Certificate2? SelectCertificate(string? hostname)
    {
        if (string.IsNullOrEmpty(hostname))
            return null;

        hostname = hostname.ToLowerInvariant();

        lock (_cacheLock)
        {
            if (_certificateCache.TryGetValue(hostname, out var cert))
                return cert;

            // Try wildcard match
            var parts = hostname.Split('.');
            if (parts.Length >= 2)
            {
                var wildcard = "*." + string.Join(".", parts.Skip(1));
                if (_certificateCache.TryGetValue(wildcard, out cert))
                    return cert;
            }
        }

        return null;
    }

    private void RefreshCache()
    {
        lock (_cacheLock)
        {
            foreach (var cert in _certificateCache.Values)
            {
                cert.Dispose();
            }
            _certificateCache.Clear();

            foreach (var certificate in _configService.GetAllCertificates())
            {
                if (certificate.PfxData == null || string.IsNullOrEmpty(certificate.PfxPassword))
                    continue;

                try
                {
                    var x509 = X509CertificateLoader.LoadPkcs12(certificate.PfxData, certificate.PfxPassword,
                        X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet);

                    foreach (var domain in certificate.Domains)
                    {
                        _certificateCache[domain.ToLowerInvariant()] = x509;
                    }

                    _logger.LogInformation("Loaded certificate for domains: {Domains}",
                        string.Join(", ", certificate.Domains));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to load certificate: {Name}", certificate.Name);
                }
            }
        }
    }
}
