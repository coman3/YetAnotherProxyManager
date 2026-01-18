using System.Text.RegularExpressions;
using YetAnotherProxyManager.Models;

namespace YetAnotherProxyManager.Services.Filtering;

public class FilterRuleEvaluator
{
    private readonly IpRangeService _ipRangeService;
    private readonly GeoLocationService _geoLocationService;
    private readonly ILogger<FilterRuleEvaluator> _logger;

    public FilterRuleEvaluator(
        IpRangeService ipRangeService,
        GeoLocationService geoLocationService,
        ILogger<FilterRuleEvaluator> logger)
    {
        _ipRangeService = ipRangeService;
        _geoLocationService = geoLocationService;
        _logger = logger;
    }

    public async Task<FilterEvaluationResult> EvaluateAsync(
        FilterConfiguration config,
        string clientIp,
        IHeaderDictionary? headers = null)
    {
        if (!config.Enabled)
        {
            return new FilterEvaluationResult(FilterAction.Allow, "Filtering disabled");
        }

        // Evaluate rule groups in priority order
        var sortedGroups = config.RuleGroups
            .Where(g => g.Enabled)
            .OrderByDescending(g => g.Priority)
            .ToList();

        foreach (var group in sortedGroups)
        {
            var groupResult = await EvaluateGroupAsync(group, clientIp, headers);

            if (groupResult.Matched)
            {
                _logger.LogDebug("Filter rule group '{GroupName}' matched for IP {ClientIp}, action: {Action}",
                    group.Name, clientIp, group.Action);

                return new FilterEvaluationResult(group.Action, $"Matched rule group: {group.Name}");
            }
        }

        // No groups matched, use default action
        return new FilterEvaluationResult(config.DefaultAction, "Default action (no rules matched)");
    }

    private async Task<GroupEvaluationResult> EvaluateGroupAsync(
        FilterRuleGroup group,
        string clientIp,
        IHeaderDictionary? headers)
    {
        if (group.Rules.Count == 0)
        {
            return new GroupEvaluationResult(false);
        }

        var ruleResults = new List<bool>();

        foreach (var rule in group.Rules)
        {
            var result = await EvaluateRuleAsync(rule, clientIp, headers);
            ruleResults.Add(rule.Negate ? !result : result);
        }

        // Apply logic operator
        var matched = group.Operator switch
        {
            LogicOperator.And => ruleResults.All(r => r),
            LogicOperator.Or => ruleResults.Any(r => r),
            _ => ruleResults.All(r => r)
        };

        return new GroupEvaluationResult(matched);
    }

    private async Task<bool> EvaluateRuleAsync(
        FilterRule rule,
        string clientIp,
        IHeaderDictionary? headers)
    {
        return rule.Type switch
        {
            FilterRuleType.IpSingle => EvaluateIpSingle(rule, clientIp),
            FilterRuleType.IpRange => EvaluateIpRange(rule, clientIp),
            FilterRuleType.IpCidr => EvaluateIpCidr(rule, clientIp),
            FilterRuleType.IpPredefined => EvaluateIpPredefined(rule, clientIp),
            FilterRuleType.GeoCountry => await EvaluateGeoCountryAsync(rule, clientIp),
            FilterRuleType.GeoContinent => await EvaluateGeoContinentAsync(rule, clientIp),
            FilterRuleType.TimeBased => EvaluateTimeBased(rule),
            FilterRuleType.Header => EvaluateHeader(rule, headers),
            _ => false
        };
    }

    private bool EvaluateIpSingle(FilterRule rule, string clientIp)
    {
        if (rule.IpSingleConfig == null)
            return false;

        return _ipRangeService.MatchesSingleIp(clientIp, rule.IpSingleConfig.IpAddress);
    }

    private bool EvaluateIpRange(FilterRule rule, string clientIp)
    {
        if (rule.IpRangeConfig == null)
            return false;

        return _ipRangeService.IsInRange(clientIp, rule.IpRangeConfig.StartIp, rule.IpRangeConfig.EndIp);
    }

    private bool EvaluateIpCidr(FilterRule rule, string clientIp)
    {
        if (rule.IpCidrConfig == null)
            return false;

        return _ipRangeService.IsInCidr(clientIp, rule.IpCidrConfig.Cidr);
    }

    private bool EvaluateIpPredefined(FilterRule rule, string clientIp)
    {
        if (rule.IpPredefinedConfig == null)
            return false;

        return rule.IpPredefinedConfig.Rule switch
        {
            PredefinedIpRule.LocalOnly => _ipRangeService.IsLocalNetworkIp(clientIp),
            PredefinedIpRule.PrivateOnly => _ipRangeService.IsPrivateIp(clientIp),
            PredefinedIpRule.PublicOnly => _ipRangeService.IsPublicIp(clientIp),
            _ => false
        };
    }

    private async Task<bool> EvaluateGeoCountryAsync(FilterRule rule, string clientIp)
    {
        if (rule.GeoCountryConfig == null || rule.GeoCountryConfig.CountryCodes.Count == 0)
            return false;

        try
        {
            var geoInfo = await _geoLocationService.GetLocationAsync(clientIp);
            if (geoInfo == null || string.IsNullOrEmpty(geoInfo.CountryCode))
                return false;

            return rule.GeoCountryConfig.CountryCodes
                .Any(c => c.Equals(geoInfo.CountryCode, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to lookup geo location for IP {ClientIp}", clientIp);
            return false;
        }
    }

    private async Task<bool> EvaluateGeoContinentAsync(FilterRule rule, string clientIp)
    {
        if (rule.GeoContinentConfig == null || rule.GeoContinentConfig.ContinentCodes.Count == 0)
            return false;

        try
        {
            var geoInfo = await _geoLocationService.GetLocationAsync(clientIp);
            if (geoInfo == null || string.IsNullOrEmpty(geoInfo.ContinentCode))
                return false;

            return rule.GeoContinentConfig.ContinentCodes
                .Any(c => c.Equals(geoInfo.ContinentCode, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to lookup geo location for IP {ClientIp}", clientIp);
            return false;
        }
    }

    private bool EvaluateTimeBased(FilterRule rule)
    {
        if (rule.TimeBasedConfig == null)
            return false;

        var config = rule.TimeBasedConfig;
        var now = DateTime.UtcNow;

        // Apply timezone if specified
        if (!string.IsNullOrEmpty(config.TimeZone))
        {
            try
            {
                var tz = TimeZoneInfo.FindSystemTimeZoneById(config.TimeZone);
                now = TimeZoneInfo.ConvertTimeFromUtc(now, tz);
            }
            catch (TimeZoneNotFoundException)
            {
                _logger.LogWarning("Unknown timezone: {TimeZone}", config.TimeZone);
            }
        }

        // Check day of week
        if (config.DaysOfWeek.Count > 0 && !config.DaysOfWeek.Contains(now.DayOfWeek))
        {
            return false;
        }

        // Check time range
        if (config.StartTime.HasValue && config.EndTime.HasValue)
        {
            var currentTime = TimeOnly.FromDateTime(now);

            if (config.StartTime.Value <= config.EndTime.Value)
            {
                // Normal range (e.g., 9:00 - 17:00)
                if (currentTime < config.StartTime.Value || currentTime > config.EndTime.Value)
                    return false;
            }
            else
            {
                // Overnight range (e.g., 22:00 - 06:00)
                if (currentTime < config.StartTime.Value && currentTime > config.EndTime.Value)
                    return false;
            }
        }

        // Check date range
        var currentDate = DateOnly.FromDateTime(now);
        if (config.StartDate.HasValue && currentDate < config.StartDate.Value)
            return false;
        if (config.EndDate.HasValue && currentDate > config.EndDate.Value)
            return false;

        return true;
    }

    private bool EvaluateHeader(FilterRule rule, IHeaderDictionary? headers)
    {
        if (rule.HeaderConfig == null || headers == null)
            return false;

        var config = rule.HeaderConfig;

        if (!headers.TryGetValue(config.HeaderName, out var values))
            return false;

        // If no value specified, just check for header existence
        if (string.IsNullOrEmpty(config.HeaderValue))
            return true;

        var headerValue = values.ToString();

        if (config.UseRegex)
        {
            try
            {
                return Regex.IsMatch(headerValue, config.HeaderValue, RegexOptions.IgnoreCase);
            }
            catch (ArgumentException)
            {
                _logger.LogWarning("Invalid regex pattern in header rule: {Pattern}", config.HeaderValue);
                return false;
            }
        }
        else
        {
            return headerValue.Equals(config.HeaderValue, StringComparison.OrdinalIgnoreCase);
        }
    }
}

public record FilterEvaluationResult(FilterAction Action, string Reason);
public record GroupEvaluationResult(bool Matched);
