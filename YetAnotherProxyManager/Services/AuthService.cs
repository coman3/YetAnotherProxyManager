using System.Security.Cryptography;

namespace YetAnotherProxyManager.Services;

public class AuthService
{
    private readonly ConfigurationService _configService;
    private readonly ILogger<AuthService> _logger;
    private readonly HashSet<string> _validSessions = new();
    private readonly object _lock = new();

    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 100000;

    public AuthService(ConfigurationService configService, ILogger<AuthService> logger)
    {
        _configService = configService;
        _logger = logger;
    }

    public bool IsPasswordConfigured()
    {
        var settings = _configService.GetSettings();
        return !string.IsNullOrEmpty(settings.PasswordHash);
    }

    public bool SetPassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
        {
            _logger.LogWarning("Password must be at least 8 characters");
            return false;
        }

        var hash = HashPassword(password);
        var settings = _configService.GetSettings();
        settings.PasswordHash = hash;
        _configService.SaveSettings(settings);

        _logger.LogInformation("Password has been set");
        return true;
    }

    public bool ValidatePassword(string password)
    {
        var settings = _configService.GetSettings();
        if (string.IsNullOrEmpty(settings.PasswordHash))
            return false;

        return VerifyPassword(password, settings.PasswordHash);
    }

    public string CreateSession()
    {
        var sessionId = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        lock (_lock)
        {
            _validSessions.Add(sessionId);
        }
        _logger.LogInformation("New session created");
        return sessionId;
    }

    public bool ValidateSession(string? sessionId)
    {
        if (string.IsNullOrEmpty(sessionId))
            return false;

        lock (_lock)
        {
            return _validSessions.Contains(sessionId);
        }
    }

    public void InvalidateSession(string sessionId)
    {
        lock (_lock)
        {
            _validSessions.Remove(sessionId);
        }
        _logger.LogInformation("Session invalidated");
    }

    public void InvalidateAllSessions()
    {
        lock (_lock)
        {
            _validSessions.Clear();
        }
        _logger.LogInformation("All sessions invalidated");
    }

    private static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);

        var result = new byte[SaltSize + HashSize];
        Buffer.BlockCopy(salt, 0, result, 0, SaltSize);
        Buffer.BlockCopy(hash, 0, result, SaltSize, HashSize);

        return Convert.ToBase64String(result);
    }

    private static bool VerifyPassword(string password, string storedHash)
    {
        try
        {
            var hashBytes = Convert.FromBase64String(storedHash);
            if (hashBytes.Length != SaltSize + HashSize)
                return false;

            var salt = new byte[SaltSize];
            Buffer.BlockCopy(hashBytes, 0, salt, 0, SaltSize);

            var storedHashPart = new byte[HashSize];
            Buffer.BlockCopy(hashBytes, SaltSize, storedHashPart, 0, HashSize);

            var computedHash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);

            return CryptographicOperations.FixedTimeEquals(computedHash, storedHashPart);
        }
        catch
        {
            return false;
        }
    }
}
