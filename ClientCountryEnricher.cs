using MaxMind.GeoIP2;
using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;

namespace UpdaterMirror;

/// <summary>
/// Extensions for enriching log events with client country information
/// </summary>
public static class ClientInfoLoggerConfigurationExtensions
{
    /// <summary>
    /// Enrich log events with the client country information
    /// </summary>
    /// <param name="enrichmentConfiguration">The logger enrichment configuration</param>
    /// <param name="accountId">The MaxMind account id</param>
    /// <param name="licenseKey">The MaxMind license key</param>
    /// <param name="headerName">The header name to use for the client IP address</param>
    /// <returns>The logger configuration enriched with the client country information</returns>
    public static LoggerConfiguration WithClientCountry(
        this LoggerEnrichmentConfiguration enrichmentConfiguration,
        int accountId,
        string licenseKey,
        string headerName
    )
    {
        if (enrichmentConfiguration == null)
            throw new ArgumentNullException(nameof(enrichmentConfiguration));

        return enrichmentConfiguration.With(new ClientCountryEnricher(headerName, accountId, licenseKey));
    }
}

/// <summary>
/// Enriches log events with the client country information.
/// This is mostly based on Serilog's built-in `ClientIpEnricher`
/// </summary>
public class ClientCountryEnricher : ILogEventEnricher
{
    /// <summary>
    /// The property name for the client country
    /// </summary>
    private const string CountryPropertyName = "ClientCountry";
    /// <summary>
    /// The key for the country item in the HTTP context
    /// </summary>
    private const string CountryItemKey = "Serilog_ClientCountry";

    /// <summary>
    /// The header key to use for the client IP address
    /// </summary>
    private readonly string _forwardHeaderKey;
    /// <summary>
    /// The HTTP context accessor
    /// </summary>
    private readonly IHttpContextAccessor _contextAccessor;
    /// <summary>
    /// The MaxMind web service client
    /// </summary>
    private readonly WebServiceClient _webserviceClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="ClientCountryEnricher"/> class
    /// </summary>
    /// <param name="forwardHeaderKey">The header key to use for the client IP address</param>
    /// <param name="accountId">The MaxMind account id</param>
    /// <param name="licenseKey">The MaxMind license key</param>
    public ClientCountryEnricher(string forwardHeaderKey, int accountId, string licenseKey)
        : this(forwardHeaderKey, new WebServiceClient(accountId, licenseKey, host: "geolite.info"), new HttpContextAccessor())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ClientCountryEnricher"/> class
    /// </summary>
    /// <param name="forwardHeaderKey">The header key to use for the client IP address</param>
    /// <param name="webServiceClient">The MaxMind web service client</param>
    /// <param name="contextAccessor">The HTTP context accessor</param>
    internal ClientCountryEnricher(string forwardHeaderKey, WebServiceClient webServiceClient, IHttpContextAccessor contextAccessor)
    {
        _forwardHeaderKey = string.IsNullOrWhiteSpace(forwardHeaderKey)
            ? "x-forwarded-for"
            : forwardHeaderKey;

        _contextAccessor = contextAccessor;
        _webserviceClient = webServiceClient;
    }

    /// <summary>
    /// Enriches the log event with the client country information
    /// </summary>
    /// <param name="logEvent">The log event to enrich</param>
    /// <param name="propertyFactory">The log event property factory</param>
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var httpContext = _contextAccessor.HttpContext;
        if (httpContext == null)
            return;

        if (httpContext.Items[CountryItemKey] is LogEventProperty logEventProperty)
        {
            logEvent.AddPropertyIfAbsent(logEventProperty);
            return;
        }

        var ipAddress = GetIpAddress();
        var country = "unknown";
        if (!string.IsNullOrWhiteSpace(ipAddress))
            try { country = _webserviceClient.Country(ipAddress).Country.IsoCode; }
            catch (Exception ex)
            {
                country = "error";
                var errorProperty = new LogEventProperty(CountryPropertyName + "Error", new ScalarValue(ex.Message));
                logEvent.AddPropertyIfAbsent(errorProperty);
            }

        var countryProperty = new LogEventProperty(CountryPropertyName, new ScalarValue(country));
        httpContext.Items.Add(CountryItemKey, countryProperty);
        logEvent.AddPropertyIfAbsent(countryProperty);
    }

    /// <summary>
    /// Gets the client IP address
    /// </summary>
    private string? GetIpAddress()
    {
        var ipAddress = _contextAccessor.HttpContext?.Request?.Headers[_forwardHeaderKey].FirstOrDefault();

        return !string.IsNullOrEmpty(ipAddress)
            ? GetIpAddressFromProxy(ipAddress)
            : _contextAccessor.HttpContext?.Connection?.RemoteIpAddress?.ToString();
    }

    /// <summary>
    /// Gets the IP address from the proxy list
    /// </summary>
    private string GetIpAddressFromProxy(string proxifiedIpList)
    {
        var addresses = proxifiedIpList.Split(',');
        return addresses.Length == 0 ? string.Empty : addresses[0].Trim();
    }

}
