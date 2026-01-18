namespace YetAnotherProxyManager.Models;

public class RequestAnalytics
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Path { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public string ClientIp { get; set; } = string.Empty;
    public bool IsSecure { get; set; }
    public int StatusCode { get; set; }
    public long ResponseTimeMs { get; set; }
    public string UserAgent { get; set; } = string.Empty;
    public string Referer { get; set; } = string.Empty;
    public long RequestSize { get; set; }
    public long ResponseSize { get; set; }
    public string RouteName { get; set; } = string.Empty;

    // Geolocation data
    public GeoLocation? Location { get; set; }
}

public class GeoLocation
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string? City { get; set; }
    public string? Region { get; set; }
    public string? Country { get; set; }
    public string? CountryCode { get; set; }
    public string? ContinentCode { get; set; }
    public string? Isp { get; set; }
}

public class AnalyticsSummary
{
    public long TotalRequests { get; set; }
    public long RequestsLastMinute { get; set; }
    public long RequestsLastHour { get; set; }
    public double AverageResponseTimeMs { get; set; }
    public Dictionary<string, long> RequestsByHost { get; set; } = new();
    public Dictionary<string, long> RequestsByPath { get; set; } = new();
    public Dictionary<string, long> RequestsByCountry { get; set; } = new();
    public Dictionary<int, long> RequestsByStatusCode { get; set; } = new();
    public List<RequestAnalytics> RecentRequests { get; set; } = new();
}

public class ServerLocation
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }
    public string WanIp { get; set; } = string.Empty;
}

public enum TimeSeriesGrouping
{
    None,
    Country,
    Host,
    Path,
    StatusCode
}

public class TimeSeriesData
{
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public int IntervalSeconds { get; set; }
    public TimeSeriesGrouping GroupBy { get; set; }
    public List<TimeSeriesItem> Series { get; set; } = new();
}

public class TimeSeriesItem
{
    public string Name { get; set; } = string.Empty;
    public List<DataPoint> DataPoints { get; set; } = new();
}

public class DataPoint
{
    public DateTime Timestamp { get; set; }
    public int Value { get; set; }
}
