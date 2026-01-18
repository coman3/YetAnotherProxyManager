using System.Collections.Concurrent;
using YetAnotherProxyManager.Models;

namespace YetAnotherProxyManager.Services;

public class AnalyticsService
{
    private readonly ILogger<AnalyticsService> _logger;
    private readonly GeoLocationService _geoService;
    private readonly RequestAnalytics[] _buffer;
    private readonly object _lock = new();
    private long _writeIndex;
    private long _totalRequests;

    private const int MaxRequests = 1_000_000;

    public event Action<RequestAnalytics>? OnRequestReceived;

    public AnalyticsService(ILogger<AnalyticsService> logger, GeoLocationService geoService)
    {
        _logger = logger;
        _geoService = geoService;
        _buffer = new RequestAnalytics[MaxRequests];
    }

    public async Task RecordRequestAsync(RequestAnalytics request)
    {
        // Get geolocation asynchronously
        if (!string.IsNullOrEmpty(request.ClientIp) && !IsPrivateIp(request.ClientIp))
        {
            request.Location = await _geoService.GetLocationAsync(request.ClientIp);
        }

        lock (_lock)
        {
            var index = _writeIndex % MaxRequests;
            _buffer[index] = request;
            _writeIndex++;
            Interlocked.Increment(ref _totalRequests);
        }

        // Notify subscribers (for real-time updates)
        try
        {
            OnRequestReceived?.Invoke(request);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error notifying request subscribers");
        }
    }

    public long TotalRequests => Interlocked.Read(ref _totalRequests);

    public IEnumerable<RequestAnalytics> GetRecentRequests(int count = 100)
    {
        var results = new List<RequestAnalytics>();
        lock (_lock)
        {
            var start = Math.Max(0, _writeIndex - count);
            for (long i = _writeIndex - 1; i >= start && i >= 0; i--)
            {
                var index = i % MaxRequests;
                var request = _buffer[index];
                if (request != null)
                {
                    results.Add(request);
                }
            }
        }
        return results;
    }

    public IEnumerable<RequestAnalytics> GetRequestsWithLocation(int count = 1000)
    {
        var results = new List<RequestAnalytics>();
        lock (_lock)
        {
            var start = Math.Max(0, _writeIndex - MaxRequests);
            for (long i = _writeIndex - 1; i >= start && results.Count < count; i--)
            {
                var index = i % MaxRequests;
                var request = _buffer[index];
                if (request?.Location != null)
                {
                    results.Add(request);
                }
            }
        }
        return results;
    }

    public AnalyticsSummary GetSummary()
    {
        var now = DateTime.UtcNow;
        var oneMinuteAgo = now.AddMinutes(-1);
        var oneHourAgo = now.AddHours(-1);

        var summary = new AnalyticsSummary
        {
            TotalRequests = TotalRequests
        };

        var responseTimes = new List<long>();
        var requestsByHost = new Dictionary<string, long>();
        var requestsByPath = new Dictionary<string, long>();
        var requestsByCountry = new Dictionary<string, long>();
        var requestsByStatus = new Dictionary<int, long>();

        lock (_lock)
        {
            var start = Math.Max(0, _writeIndex - MaxRequests);
            for (long i = _writeIndex - 1; i >= start; i--)
            {
                var index = i % MaxRequests;
                var request = _buffer[index];
                if (request == null) continue;

                if (request.Timestamp >= oneMinuteAgo)
                    summary.RequestsLastMinute++;

                if (request.Timestamp >= oneHourAgo)
                    summary.RequestsLastHour++;

                responseTimes.Add(request.ResponseTimeMs);

                // Aggregate by host
                if (!string.IsNullOrEmpty(request.Host))
                {
                    requestsByHost.TryGetValue(request.Host, out var hostCount);
                    requestsByHost[request.Host] = hostCount + 1;
                }

                // Aggregate by path (top level only)
                var pathKey = GetTopLevelPath(request.Path);
                requestsByPath.TryGetValue(pathKey, out var pathCount);
                requestsByPath[pathKey] = pathCount + 1;

                // Aggregate by country
                var country = request.Location?.Country ?? "Unknown";
                requestsByCountry.TryGetValue(country, out var countryCount);
                requestsByCountry[country] = countryCount + 1;

                // Aggregate by status code
                requestsByStatus.TryGetValue(request.StatusCode, out var statusCount);
                requestsByStatus[request.StatusCode] = statusCount + 1;
            }
        }

        summary.AverageResponseTimeMs = responseTimes.Count > 0 ? responseTimes.Average() : 0;
        summary.RequestsByHost = requestsByHost.OrderByDescending(x => x.Value).Take(10).ToDictionary(x => x.Key, x => x.Value);
        summary.RequestsByPath = requestsByPath.OrderByDescending(x => x.Value).Take(10).ToDictionary(x => x.Key, x => x.Value);
        summary.RequestsByCountry = requestsByCountry.OrderByDescending(x => x.Value).Take(10).ToDictionary(x => x.Key, x => x.Value);
        summary.RequestsByStatusCode = requestsByStatus.OrderBy(x => x.Key).ToDictionary(x => x.Key, x => x.Value);
        summary.RecentRequests = GetRecentRequests(20).ToList();

        return summary;
    }

    public TimeSeriesData GetTimeSeries(
        int seconds = 60,
        TimeSeriesGrouping groupBy = TimeSeriesGrouping.None,
        string? filterCountry = null,
        string? filterHost = null,
        string? filterPath = null)
    {
        var now = DateTime.UtcNow;
        var startTime = now.AddSeconds(-seconds);

        var result = new TimeSeriesData
        {
            StartTime = startTime,
            EndTime = now,
            IntervalSeconds = 1,
            GroupBy = groupBy
        };

        // Initialize time buckets
        var buckets = new Dictionary<DateTime, Dictionary<string, int>>();
        for (int i = 0; i < seconds; i++)
        {
            var bucketTime = startTime.AddSeconds(i);
            bucketTime = new DateTime(bucketTime.Year, bucketTime.Month, bucketTime.Day,
                bucketTime.Hour, bucketTime.Minute, bucketTime.Second, DateTimeKind.Utc);
            buckets[bucketTime] = new Dictionary<string, int>();
        }

        lock (_lock)
        {
            var start = Math.Max(0, _writeIndex - MaxRequests);
            for (long i = _writeIndex - 1; i >= start; i--)
            {
                var index = i % MaxRequests;
                var request = _buffer[index];
                if (request == null) continue;

                // Skip if before our time window
                if (request.Timestamp < startTime) break;

                // Apply filters
                if (!string.IsNullOrEmpty(filterCountry) &&
                    !string.Equals(request.Location?.Country, filterCountry, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!string.IsNullOrEmpty(filterHost) &&
                    !string.Equals(request.Host, filterHost, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!string.IsNullOrEmpty(filterPath) &&
                    !request.Path.StartsWith(filterPath, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Get bucket time (truncate to second)
                var bucketTime = new DateTime(request.Timestamp.Year, request.Timestamp.Month, request.Timestamp.Day,
                    request.Timestamp.Hour, request.Timestamp.Minute, request.Timestamp.Second, DateTimeKind.Utc);

                if (!buckets.ContainsKey(bucketTime)) continue;

                // Get group key
                var groupKey = groupBy switch
                {
                    TimeSeriesGrouping.Country => request.Location?.Country ?? "Unknown",
                    TimeSeriesGrouping.Host => request.Host ?? "Unknown",
                    TimeSeriesGrouping.Path => GetTopLevelPath(request.Path),
                    TimeSeriesGrouping.StatusCode => request.StatusCode.ToString(),
                    _ => "Total"
                };

                buckets[bucketTime].TryGetValue(groupKey, out var count);
                buckets[bucketTime][groupKey] = count + 1;
            }
        }

        // Convert to series format
        var allGroups = buckets.Values.SelectMany(b => b.Keys).Distinct().ToList();

        foreach (var group in allGroups)
        {
            var series = new TimeSeriesItem
            {
                Name = group,
                DataPoints = buckets
                    .OrderBy(b => b.Key)
                    .Select(b => new DataPoint
                    {
                        Timestamp = b.Key,
                        Value = b.Value.TryGetValue(group, out var v) ? v : 0
                    })
                    .ToList()
            };
            result.Series.Add(series);
        }

        // If no data, add empty "Total" series
        if (result.Series.Count == 0)
        {
            result.Series.Add(new TimeSeriesItem
            {
                Name = "Total",
                DataPoints = buckets
                    .OrderBy(b => b.Key)
                    .Select(b => new DataPoint { Timestamp = b.Key, Value = 0 })
                    .ToList()
            });
        }

        return result;
    }

    public List<string> GetDistinctCountries()
    {
        var countries = new HashSet<string>();
        lock (_lock)
        {
            var start = Math.Max(0, _writeIndex - MaxRequests);
            for (long i = _writeIndex - 1; i >= start; i--)
            {
                var index = i % MaxRequests;
                var request = _buffer[index];
                if (request?.Location?.Country != null)
                {
                    countries.Add(request.Location.Country);
                }
            }
        }
        return countries.OrderBy(c => c).ToList();
    }

    public List<string> GetDistinctHosts()
    {
        var hosts = new HashSet<string>();
        lock (_lock)
        {
            var start = Math.Max(0, _writeIndex - MaxRequests);
            for (long i = _writeIndex - 1; i >= start; i--)
            {
                var index = i % MaxRequests;
                var request = _buffer[index];
                if (!string.IsNullOrEmpty(request?.Host))
                {
                    hosts.Add(request.Host);
                }
            }
        }
        return hosts.OrderBy(h => h).ToList();
    }

    public List<string> GetDistinctPaths()
    {
        var paths = new HashSet<string>();
        lock (_lock)
        {
            var start = Math.Max(0, _writeIndex - MaxRequests);
            for (long i = _writeIndex - 1; i >= start; i--)
            {
                var index = i % MaxRequests;
                var request = _buffer[index];
                if (!string.IsNullOrEmpty(request?.Path))
                {
                    paths.Add(GetTopLevelPath(request.Path));
                }
            }
        }
        return paths.OrderBy(p => p).ToList();
    }

    private static string GetTopLevelPath(string path)
    {
        if (string.IsNullOrEmpty(path) || path == "/")
            return "/";

        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? "/" + parts[0] : "/";
    }

    private static bool IsPrivateIp(string ip)
    {
        if (string.IsNullOrEmpty(ip)) return true;
        if (ip == "::1" || ip == "127.0.0.1") return true;

        // Check for private IP ranges
        if (ip.StartsWith("10.") || ip.StartsWith("192.168.") || ip.StartsWith("172.16.") ||
            ip.StartsWith("172.17.") || ip.StartsWith("172.18.") || ip.StartsWith("172.19.") ||
            ip.StartsWith("172.20.") || ip.StartsWith("172.21.") || ip.StartsWith("172.22.") ||
            ip.StartsWith("172.23.") || ip.StartsWith("172.24.") || ip.StartsWith("172.25.") ||
            ip.StartsWith("172.26.") || ip.StartsWith("172.27.") || ip.StartsWith("172.28.") ||
            ip.StartsWith("172.29.") || ip.StartsWith("172.30.") || ip.StartsWith("172.31."))
        {
            return true;
        }

        return false;
    }
}
