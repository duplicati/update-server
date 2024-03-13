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
/// <param name="SeqLogUrl">Url for Seq log destination</param>
/// <param name="SeqLogApiKey">Optional API key for logging to Seq</param>
public record ApplicationConfig(
    string PrimaryStorage,
    string? TestFilesStorage,
    string CachePath,
    int MaxNotFound,
    long MaxSize,
    TimeSpan ValidityPeriod,
    string SeqLogUrl,
    string SeqLogApiKey
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
            Environment.GetEnvironmentVariable(SeqApiKeyEnvKey) ?? string.Empty
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
            'h' => 60*60,
            'd' => 24*60*60,
            'w' => 7*24*60*60,
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
