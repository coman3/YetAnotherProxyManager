namespace YetAnotherProxyManager.Models;

public enum FilterAction
{
    Allow,
    Deny
}

public enum LogicOperator
{
    And,
    Or
}

public enum FilterRuleType
{
    IpSingle,
    IpRange,
    IpCidr,
    IpPredefined,
    GeoCountry,
    GeoContinent,
    TimeBased,
    Header
}

public enum PredefinedIpRule
{
    LocalOnly,
    PrivateOnly,
    PublicOnly
}

public class FilterConfiguration
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RouteId { get; set; }
    public bool Enabled { get; set; } = true;
    public List<FilterRuleGroup> RuleGroups { get; set; } = new();
    public FilterAction DefaultAction { get; set; } = FilterAction.Allow;
}

public class FilterRuleGroup
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public int Priority { get; set; }
    public LogicOperator Operator { get; set; } = LogicOperator.And;
    public FilterAction Action { get; set; } = FilterAction.Allow;
    public List<FilterRule> Rules { get; set; } = new();
    public bool Enabled { get; set; } = true;
}

public class FilterRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public FilterRuleType Type { get; set; }
    public bool Negate { get; set; }

    // Type-specific configurations (only one should be set based on Type)
    public IpSingleConfig? IpSingleConfig { get; set; }
    public IpRangeConfig? IpRangeConfig { get; set; }
    public IpCidrConfig? IpCidrConfig { get; set; }
    public IpPredefinedConfig? IpPredefinedConfig { get; set; }
    public GeoCountryConfig? GeoCountryConfig { get; set; }
    public GeoContinentConfig? GeoContinentConfig { get; set; }
    public TimeBasedConfig? TimeBasedConfig { get; set; }
    public HeaderConfig? HeaderConfig { get; set; }
}

public class IpSingleConfig
{
    public string IpAddress { get; set; } = string.Empty;
}

public class IpRangeConfig
{
    public string StartIp { get; set; } = string.Empty;
    public string EndIp { get; set; } = string.Empty;
}

public class IpCidrConfig
{
    public string Cidr { get; set; } = string.Empty;
}

public class IpPredefinedConfig
{
    public PredefinedIpRule Rule { get; set; }
}

public class GeoCountryConfig
{
    public List<string> CountryCodes { get; set; } = new();
}

public class GeoContinentConfig
{
    public List<string> ContinentCodes { get; set; } = new();
}

public class TimeBasedConfig
{
    public List<DayOfWeek> DaysOfWeek { get; set; } = new();
    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string? TimeZone { get; set; }
}

public class HeaderConfig
{
    public string HeaderName { get; set; } = string.Empty;
    public string? HeaderValue { get; set; }
    public bool UseRegex { get; set; }
}
