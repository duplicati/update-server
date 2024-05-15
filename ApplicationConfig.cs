using System.Text.RegularExpressions;

namespace UpdaterMirror;

/// <summary>
/// The application configuration options
/// </summary>
/// <param name="PrimaryStorage">The primary storage url</param>
/// <param name="SecondaryStorage">The secondary storage url</param>
/// <param name="TestFilesStorage">The storage url for test files</param>
/// <param name="CachePath">Path to on-disk cache</param>
/// <param name="MaxNotFound">The maximum not-found (404) entries in cache</param>
/// <param name="MaxSize">The maximum size of the on-disk cached items</param>
/// <param name="ValidityPeriod">The item validity period</param>
/// <param name="SeqLogUrl">Url for Seq log destination</param>
/// <param name="SeqLogApiKey">Optional API key for logging to Seq</param>
/// <param name="RootRedirect">Redirect url for the root</param>
/// <param name="ManualExpireApiKey">API key for manually expiring items</param>
/// <param name="KeepForeverRegex">Expression to selectively disable expiration</param>
/// <param name="NoCacheRegex">Expression to selectively disable caching</param>
/// <param name="CustomLog">Custom log template</param>
/// <param name="CustomLogHeaders">Custom log headers</param>
/// <param name="MaxmindAccountId">The account id for maxmind GeoIP</param>
/// <param name="MaxmindLicenseKey">The license key for maxmind GeoIP</param>
/// <param name="MaxmindIpHeader">The header to use for maxmind GeoIP</param>
/// <param name="NotFoundHtmlKey">The key for the not-found html file</param>
/// <param name="IndexHtmlKey">The key for the index html file</param>
/// <param name="IndexHtmlRegex">The regex to apply to the index html file</param>
public record ApplicationConfig(
    string PrimaryStorage,
    string? TestFilesStorage,
    string CachePath,
    int MaxNotFound,
    long MaxSize,
    TimeSpan ValidityPeriod,
    string SeqLogUrl,
    string SeqLogApiKey,
    string RootRedirect,
    string ManualExpireApiKey,
    Regex? KeepForeverRegex,
    Regex? NoCacheRegex,
    string? CustomLog,
    string CustomLogHeaders,
    int MaxmindAccountId,
    string MaxmindLicenseKey,
    string MaxmindIpHeader,
    string NotFoundHtmlKey,
    string IndexHtmlKey,
    Regex? IndexHtmlRegex
)
{
    /// <summary>
    /// The environment key for the storage for primary files
    /// </summary>
    private const string PrimaryEnvKey = "PRIMARY";

    /// <summary>
    /// The environment key for the storage for test files
    /// </summary>
    private const string TestFilesEnvKey = "TESTFILES";

    /// <summary>
    /// The environment key for the storage of locally cached files
    /// </summary>
    private const string CachePathEnvKey = "CACHEPATH";

    /// <summary>
    /// The environment key for the maximum not-found entries in the cache
    /// </summary>
    private const string MaxNotFoundEnvKey = "MAX_NOT_FOUND";
    /// <summary>
    /// The environment key for the maximum size of the cache
    /// </summary>
    private const string MaxSizeEnvKey = "MAX_SIZE";
    /// <summary>
    /// The time entries are cached
    /// </summary>
    private const string ValidityPeriodEnvKey = "CACHE_TIME";

    /// <summary>
    /// The environment key for the Seq logging Url
    /// </summary>
    private const string SeqUrlEnvKey = "SEQ_URL";

    /// <summary>
    /// The environment key for the Seq API key
    /// </summary>
    private const string SeqApiKeyEnvKey = "SEQ_APIKEY";

    /// <summary>
    /// The environment key for url to redirect to when accessing the root
    /// </summary>
    private const string RootRedirectEnvKey = "REDIRECT";

    /// <summary>
    /// The environment key for manually expiring items
    /// </summary>
    private const string ManualExpireApiKeyEnvKey = "APIKEY";

    /// <summary>
    /// The environment key for finding manually expiring items
    /// </summary>
    private const string KeepForeverRegexEnvKey = "KEEP_FOREVER_REGEX";

    /// <summary>
    /// The environement key for toggling caching of items
    /// </summary>
    private const string NoCacheRegexEnvKey = "NO_CACHE_REGEX";

    /// <summary>
    /// The environment key for a custom log template
    /// </summary>
    private const string CustomLogEnvKey = "CUSTOM_LOG";

    /// <summary>
    /// The environment key for a custom log template
    /// </summary>
    private const string CustomLogHeadersEnvKey = "CUSTOM_LOG_HEADERS";

    /// <summary>
    /// The environment key for Maxmind Geoip account id
    /// </summary>
    private const string MaxmindAccountIdEnvKey = "MAXMIND_ACCOUNT_ID";

    /// <summary>
    /// The environment key for Maxmind Geoip license key
    /// </summary>
    private const string MaxmindLicenseEnvKey = "MAXMIND_LICENSE_KEY";

    /// <summary>
    /// The environment key for the IP header to use for Maxmind Geoip
    /// </summary>
    private const string MaxmindIpHeaderEnvKey = "MAXMIND_IP_HEADER";

    /// <summary>
    /// The environment key for the not-found html file
    /// </summary>
    private const string NotFoundHtmlKeyEnvKey = "NOTFOUND_HTML";

    /// <summary>
    /// The environment key for the index html file
    /// </summary>
    private const string IndexHtmlKeyEnvKey = "INDEX_HTML";

    /// <summary>
    /// The environment key for when to apply the index html regex
    /// </summary>
    private const string IndexHtmlRegexEnvKey = "INDEX_HTML_REGEX";

    /// <summary>
    /// Loads settings from the environment
    /// </summary>
    /// <returns>A typed instance with settings</returns>
    public static ApplicationConfig Load()
        => new(
            Environment.GetEnvironmentVariable(PrimaryEnvKey) ?? string.Empty,
            Environment.GetEnvironmentVariable(TestFilesEnvKey) ?? string.Empty,

            ExpandEnvPath(Environment.GetEnvironmentVariable(CachePathEnvKey)) ?? string.Empty,

            (int)ParseSize(Environment.GetEnvironmentVariable(MaxNotFoundEnvKey), "10k"),
            ParseSize(Environment.GetEnvironmentVariable(MaxSizeEnvKey), "10m"),
            ParseDuration(Environment.GetEnvironmentVariable(ValidityPeriodEnvKey), "1d"),

            Environment.GetEnvironmentVariable(SeqUrlEnvKey) ?? string.Empty,
            Environment.GetEnvironmentVariable(SeqApiKeyEnvKey) ?? string.Empty,

            Environment.GetEnvironmentVariable(RootRedirectEnvKey) ?? string.Empty,
            Environment.GetEnvironmentVariable(ManualExpireApiKeyEnvKey) ?? string.Empty,

            ParseRegex(Environment.GetEnvironmentVariable(KeepForeverRegexEnvKey)),
            ParseRegex(Environment.GetEnvironmentVariable(NoCacheRegexEnvKey)),

            Environment.GetEnvironmentVariable(CustomLogEnvKey) ?? string.Empty,
            Environment.GetEnvironmentVariable(CustomLogHeadersEnvKey) ?? string.Empty,

            (int)ParseSize(Environment.GetEnvironmentVariable(MaxmindAccountIdEnvKey), "0"),
            Environment.GetEnvironmentVariable(MaxmindLicenseEnvKey) ?? string.Empty,
            Environment.GetEnvironmentVariable(MaxmindIpHeaderEnvKey) ?? string.Empty,

            Environment.GetEnvironmentVariable(NotFoundHtmlKeyEnvKey) ?? string.Empty,
            Environment.GetEnvironmentVariable(IndexHtmlKeyEnvKey) ?? string.Empty,
            ParseRegex(Environment.GetEnvironmentVariable(IndexHtmlRegexEnvKey))
        );

    /// <summary>
    /// Expands environment variables and expands the path of the <paramref name="setting"/> value
    /// </summary>
    /// <param name="setting">The value to expand to a path</param>
    /// <returns>The expanded path</returns>
    private static string? ExpandEnvPath(string? setting)
        => string.IsNullOrEmpty(setting) || setting.StartsWith("base64:")
                ? setting
                : Path.GetFullPath(Environment.ExpandEnvironmentVariables(setting));

    /// <summary>
    /// Creates a new regular expression instance if the value is set
    /// </summary>
    /// <param name="value">The value to use for regex matching</param>
    /// <returns>The matched value</returns>
    private static Regex? ParseRegex(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : new Regex(value, RegexOptions.ExplicitCapture | RegexOptions.Compiled);

    /// <summary>
    /// Parses a boolean string value
    /// </summary>
    /// <param name="value">The value to parse</param>
    /// <param name="defaultValue">The default value, if <paramref name="value"/> is not a boolean</param>
    /// <returns>The parsed value</returns>
    private static bool ParseBool(string? value, bool defaultValue)
        => string.IsNullOrWhiteSpace(value)
            ? defaultValue
            : value.ToLowerInvariant().Trim() switch
            {
                "t" or "true" or "1" or "yes" or "on" => true,
                "f" or "false" or "0" or "no" or "off" => false,
                _ => defaultValue
            };

    /// <summary>
    /// Parses a duration-like string into a value, supporting common s/m/h/d/w suffixes.
    /// </summary>
    /// <param name="value">The value to parse</param>
    /// <param name="defaultValue">The value to use if nothing is set</param>
    /// <returns>The parsed value</returns>
    private static TimeSpan ParseDuration(string? value, string defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value))
            value = defaultValue;

        value = value.Trim();
        var multiplierchar = value.ToLowerInvariant().Last();
        var multiplier = multiplierchar switch
        {
            'm' => 60,
            'h' => 60 * 60,
            'd' => 24 * 60 * 60,
            'w' => 7 * 24 * 60 * 60,
            _ => 0
        };

        if (multiplier > 1 || multiplierchar == 's')
            value = value[..^1];

        return TimeSpan.FromSeconds(long.Parse(value, System.Globalization.NumberStyles.Integer) * multiplier);
    }

    /// <summary>
    /// Parses a size-like string into a value, supporting common b/k/m/g/t/p suffixes.
    /// </summary>
    /// <param name="value">The value to parse</param>
    /// <param name="defaultValue">The value to use if nothing is set</param>
    /// <returns>The parsed value</returns>
    private static long ParseSize(string? value, string defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value))
            value = defaultValue;

        value = value.Trim();
        var multiplierchar = value.ToLowerInvariant().Last();
        var multiplier = multiplierchar switch
        {
            'k' => 1,
            'm' => 2,
            'g' => 3,
            't' => 4,
            'p' => 5,
            _ => 0
        };

        if (multiplier > 0 || multiplierchar == 'b')
            value = value[..^1];

        return long.Parse(value, System.Globalization.NumberStyles.Integer) * (long)Math.Pow(1024, multiplier);
    }
}
