using System.Security.Cryptography.X509Certificates;
using Certes;
using Certes.Acme;
using YetAnotherProxyManager.Models;

namespace YetAnotherProxyManager.Services;

public class AcmeCertificateService : BackgroundService
{
    private readonly ConfigurationService _configService;
    private readonly ILogger<AcmeCertificateService> _logger;
    private readonly Dictionary<string, AcmeChallenge> _pendingChallenges = new();
    private readonly object _challengeLock = new();

    private static readonly Uri LetsEncryptStaging = WellKnownServers.LetsEncryptStagingV2;
    private static readonly Uri LetsEncryptProduction = WellKnownServers.LetsEncryptV2;

    public AcmeCertificateService(ConfigurationService configService, ILogger<AcmeCertificateService> logger)
    {
        _configService = configService;
        _logger = logger;
    }

    public AcmeChallenge? GetPendingChallenge(string token)
    {
        lock (_challengeLock)
        {
            return _pendingChallenges.TryGetValue(token, out var challenge) ? challenge : null;
        }
    }

    public async Task<(bool Success, string? Error)> RequestCertificateAsync(List<string> domains, CancellationToken cancellationToken = default)
    {
        var settings = _configService.GetSettings();

        if (string.IsNullOrEmpty(settings.AcmeEmail))
        {
            return (false, "ACME email not configured. Please set it in Settings.");
        }

        try
        {
            var acmeServer = settings.UseAcmeStaging ? LetsEncryptStaging : LetsEncryptProduction;
            _logger.LogInformation("Requesting certificate for {Domains} from {Server}",
                string.Join(", ", domains), acmeServer);

            AcmeContext acme;
            if (settings.AcmeAccountKey != null)
            {
                var accountKey = KeyFactory.FromDer(settings.AcmeAccountKey);
                acme = new AcmeContext(acmeServer, accountKey);
                await acme.Account();
            }
            else
            {
                acme = new AcmeContext(acmeServer);
                await acme.NewAccount(settings.AcmeEmail, true);

                settings.AcmeAccountKey = acme.AccountKey.ToDer();
                _configService.SaveSettings(settings);
            }

            var order = await acme.NewOrder(domains);
            var authorizations = await order.Authorizations();

            foreach (var authz in authorizations)
            {
                var challenge = await authz.Http();
                if (challenge == null)
                {
                    return (false, "HTTP-01 challenge not available");
                }

                var keyAuthz = challenge.KeyAuthz;
                var token = challenge.Token;

                lock (_challengeLock)
                {
                    _pendingChallenges[token] = new AcmeChallenge
                    {
                        Token = token,
                        KeyAuthorization = keyAuthz,
                        ExpiresAt = DateTime.UtcNow.AddMinutes(10)
                    };
                }

                _logger.LogInformation("Challenge registered for token: {Token}", token);

                var challengeResult = await challenge.Validate();

                var maxAttempts = 30;
                for (int i = 0; i < maxAttempts; i++)
                {
                    await Task.Delay(2000, cancellationToken);
                    challengeResult = await challenge.Resource();

                    if (challengeResult.Status == Certes.Acme.Resource.ChallengeStatus.Valid)
                        break;

                    if (challengeResult.Status == Certes.Acme.Resource.ChallengeStatus.Invalid)
                    {
                        return (false, $"Challenge validation failed: {challengeResult.Error?.Detail}");
                    }
                }

                lock (_challengeLock)
                {
                    _pendingChallenges.Remove(token);
                }

                if (challengeResult.Status != Certes.Acme.Resource.ChallengeStatus.Valid)
                {
                    return (false, "Challenge validation timed out");
                }
            }

            var privateKey = KeyFactory.NewKey(KeyAlgorithm.RS256);
            var certChain = await order.Generate(new CsrInfo
            {
                CommonName = domains[0]
            }, privateKey);

            var pfxBuilder = certChain.ToPfx(privateKey);
            var pfxPassword = Guid.NewGuid().ToString("N");
            var pfxData = pfxBuilder.Build(domains[0], pfxPassword);

            var x509 = X509CertificateLoader.LoadPkcs12(pfxData, pfxPassword);

            var cert = new Certificate
            {
                Name = domains[0],
                Type = CertificateType.LetsEncrypt,
                Domains = domains,
                IssuedAt = x509.NotBefore.ToUniversalTime(),
                ExpiresAt = x509.NotAfter.ToUniversalTime(),
                PfxData = pfxData,
                PfxPassword = pfxPassword,
                AutoRenew = true
            };

            _configService.SaveCertificate(cert);
            _logger.LogInformation("Certificate issued for {Domains}, expires {ExpiresAt}",
                string.Join(", ", domains), cert.ExpiresAt);

            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to request certificate for {Domains}", string.Join(", ", domains));
            return (false, ex.Message);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ACME Certificate Service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromHours(12), stoppingToken);
                await RenewExpiringCertificatesAsync(stoppingToken);
                CleanupExpiredChallenges();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in certificate renewal loop");
            }
        }
    }

    private async Task RenewExpiringCertificatesAsync(CancellationToken cancellationToken)
    {
        var certsToRenew = _configService.GetCertificatesNeedingRenewal().ToList();

        foreach (var cert in certsToRenew)
        {
            if (cert.Type != CertificateType.LetsEncrypt)
                continue;

            _logger.LogInformation("Renewing certificate: {Name}", cert.Name);

            cert.LastRenewalAttempt = DateTime.UtcNow;
            var result = await RequestCertificateAsync(cert.Domains, cancellationToken);

            if (!result.Success)
            {
                cert.LastRenewalError = result.Error;
                _configService.SaveCertificate(cert);
            }
        }
    }

    private void CleanupExpiredChallenges()
    {
        lock (_challengeLock)
        {
            var expiredTokens = _pendingChallenges
                .Where(kvp => kvp.Value.ExpiresAt < DateTime.UtcNow)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var token in expiredTokens)
            {
                _pendingChallenges.Remove(token);
            }
        }
    }
}
