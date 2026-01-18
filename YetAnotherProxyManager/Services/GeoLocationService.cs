using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using YetAnotherProxyManager.Models;

namespace YetAnotherProxyManager.Services;

public class GeoLocationService : IDisposable
{
    private readonly ILogger<GeoLocationService> _logger;
    private readonly HttpClient _httpClient;
    private readonly ConcurrentDictionary<string, GeoLocation?> _cache = new();
    private readonly ConcurrentDictionary<string, DateTime> _cacheExpiry = new();
    private readonly SemaphoreSlim _rateLimiter = new(5, 5); // Max 5 concurrent requests

    private ServerLocation? _serverLocation;
    private DateTime _serverLocationExpiry = DateTime.MinValue;

    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(24);
    private static readonly TimeSpan RateLimitDelay = TimeSpan.FromMilliseconds(100); // ip-api allows 45 req/min

    public GeoLocationService(ILogger<GeoLocationService> logger)
    {
        _logger = logger;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
    }

    public async Task<GeoLocation?> GetLocationAsync(string ip)
    {
        if (string.IsNullOrEmpty(ip))
            return null;

        // Check cache first
        if (_cache.TryGetValue(ip, out var cached))
        {
            if (_cacheExpiry.TryGetValue(ip, out var expiry) && expiry > DateTime.UtcNow)
            {
                return cached;
            }
        }

        await _rateLimiter.WaitAsync();
        try
        {
            await Task.Delay(RateLimitDelay); // Rate limiting

            // Using ip-api.com (free for non-commercial, 45 requests/minute)
            var response = await _httpClient.GetFromJsonAsync<IpApiResponse>(
                $"http://ip-api.com/json/{ip}?fields=status,country,countryCode,continent,continentCode,region,city,lat,lon,isp");

            if (response?.Status == "success")
            {
                var location = new GeoLocation
                {
                    Latitude = response.Lat,
                    Longitude = response.Lon,
                    City = response.City,
                    Region = response.Region,
                    Country = response.Country,
                    CountryCode = response.CountryCode,
                    ContinentCode = response.ContinentCode,
                    Isp = response.Isp
                };

                _cache[ip] = location;
                _cacheExpiry[ip] = DateTime.UtcNow.Add(CacheDuration);

                return location;
            }

            // Cache null result to avoid repeated lookups
            _cache[ip] = null;
            _cacheExpiry[ip] = DateTime.UtcNow.AddMinutes(5);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to get geolocation for IP: {Ip}", ip);
            return null;
        }
        finally
        {
            _rateLimiter.Release();
        }
    }

    public async Task<ServerLocation> GetServerLocationAsync()
    {
        if (_serverLocation != null && _serverLocationExpiry > DateTime.UtcNow)
        {
            return _serverLocation;
        }

        try
        {
            // Get WAN IP using multiple services for reliability
            var wanIp = await GetWanIpAsync();

            if (!string.IsNullOrEmpty(wanIp))
            {
                var location = await GetLocationAsync(wanIp);

                _serverLocation = new ServerLocation
                {
                    WanIp = wanIp,
                    Latitude = location?.Latitude ?? 0,
                    Longitude = location?.Longitude ?? 0,
                    City = location?.City,
                    Country = location?.Country
                };
            }
            else
            {
                _serverLocation = new ServerLocation
                {
                    WanIp = "Unknown",
                    Latitude = 0,
                    Longitude = 0
                };
            }

            _serverLocationExpiry = DateTime.UtcNow.AddHours(1);
            return _serverLocation;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get server location");
            return _serverLocation ?? new ServerLocation { WanIp = "Unknown" };
        }
    }

    private async Task<string?> GetWanIpAsync()
    {
        var services = new[]
        {
            "https://api.ipify.org",
            "https://icanhazip.com",
            "https://ifconfig.me/ip"
        };

        foreach (var service in services)
        {
            try
            {
                var ip = await _httpClient.GetStringAsync(service);
                ip = ip.Trim();
                if (!string.IsNullOrEmpty(ip) && ip.Length < 50) // Basic validation
                {
                    return ip;
                }
            }
            catch
            {
                // Try next service
            }
        }

        return null;
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _rateLimiter.Dispose();
    }

    private class IpApiResponse
    {
        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("country")]
        public string? Country { get; set; }

        [JsonPropertyName("countryCode")]
        public string? CountryCode { get; set; }

        [JsonPropertyName("continent")]
        public string? Continent { get; set; }

        [JsonPropertyName("continentCode")]
        public string? ContinentCode { get; set; }

        [JsonPropertyName("region")]
        public string? Region { get; set; }

        [JsonPropertyName("city")]
        public string? City { get; set; }

        [JsonPropertyName("lat")]
        public double Lat { get; set; }

        [JsonPropertyName("lon")]
        public double Lon { get; set; }

        [JsonPropertyName("isp")]
        public string? Isp { get; set; }
    }
}
