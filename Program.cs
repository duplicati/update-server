using System.Net;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Serilog;
using UpdaterMirror;

var appconfig = ApplicationConfig.Load();

var builder = WebApplication.CreateBuilder(args);
var logConfiguration = new LoggerConfiguration()
    .Enrich.WithHttpRequestId()
    .Enrich.FromLogContext();

foreach (var header in appconfig.CustomLogHeaders.Split(";"))
    logConfiguration = logConfiguration.Enrich.WithRequestHeader(header);

if (!string.IsNullOrWhiteSpace(appconfig.MaxmindLicenseKey) && appconfig.MaxmindAccountId > 0)
    logConfiguration = logConfiguration.Enrich.WithClientCountry(appconfig.MaxmindAccountId, appconfig.MaxmindLicenseKey, appconfig.MaxmindIpHeader);

logConfiguration = logConfiguration
    .WriteTo.Console();

builder.Host.UseSerilog();
builder.Services.AddHttpContextAccessor();

if (!string.IsNullOrWhiteSpace(appconfig.SeqLogUrl))
    logConfiguration = logConfiguration.WriteTo.Seq(appconfig.SeqLogUrl, apiKey: appconfig.SeqLogApiKey, queueSizeLimit: 1000);

Log.Logger = logConfiguration
    .CreateLogger();


var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Error");

// Support LetsEncrypt
var le_path = Path.Combine(Directory.GetCurrentDirectory(), @".well-known");
if (Directory.Exists(le_path))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(le_path),
        RequestPath = new PathString("/.well-known"),
        ServeUnknownFileTypes = true // serve extensionless file
    });
}

var filetypeMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
    {".zip", "application/x-zip" },
    {".tar", "application/x-tar" },
    {".tgz", "application/gzip" },
    {".deb", "application/x-debian-package" },
    {".dmg", "application/x-apple-diskimage" },
    {".rpm", "application/x-redhat-package-manager" },
    {".spk", "application/octet-stream" },
    {".pkg", "application/octet-stream" },
    {".manifest", "application/octet-stream" },
    {".sig", "application/octet-stream" },
    {".asc", "text/plain" },
    {".js ","application/javascript" },
    {".json", "application/json" },
    {".msi", "application/x-msi" },
};

app.MapGet("/robots.txt", async ctx =>
{
    // Move along, nothing to see here
    ctx.Response.ContentType = "text/plain";
    await ctx.Response.WriteAsync(@"User-agent: *\r\nDisallow: /", ctx.RequestAborted);
});

if (!string.IsNullOrWhiteSpace(appconfig.RootRedirect))
    app.MapGet("/", ctx =>
    {
        ctx.Response.Redirect(appconfig.RootRedirect, true);
        return Task.CompletedTask;
    });

var cacheManager = new CacheManager(appconfig.PrimaryStorage, appconfig.CachePath, appconfig.MaxNotFound, appconfig.MaxSize, appconfig.ValidityPeriod, appconfig.KeepForeverRegex);

if (!string.IsNullOrWhiteSpace(appconfig.ManualExpireApiKey))
    app.MapPost("/reload", async ctx =>
    {
        if (!ctx.Request.Headers.ContainsKey("X-API-KEY") || ctx.Request.Headers["X-API-KEY"].FirstOrDefault() != appconfig.ManualExpireApiKey)
        {
            ctx.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            return;
        }

        var keys = await System.Text.Json.JsonSerializer.DeserializeAsync<IEnumerable<string>>(ctx.Request.Body, cancellationToken: ctx.RequestAborted)
            ?? Array.Empty<string>();
        cacheManager.ForceExpire(keys.Where(x => !string.IsNullOrWhiteSpace(x)).ToHashSet());
    });

Action<HttpContext>? customLog = string.IsNullOrWhiteSpace(appconfig.CustomLog)
    ? null
    : customLog = (c) => Log.Information(appconfig.CustomLog, c.Request.Host, c.Request.Path, c.Response.StatusCode, c.Response.ContentLength, c.Request, c.Response);

var cacheControlPrivate = new Microsoft.Net.Http.Headers.CacheControlHeaderValue
{
    Private = true,
    MaxAge = null,
    NoCache = true,
    NoStore = true
};

var cacheControlPublic = new Microsoft.Net.Http.Headers.CacheControlHeaderValue
{
    Public = true,
    MaxAge = appconfig.ValidityPeriod.Add(TimeSpan.FromSeconds(-1))
};

var fileProvider = new RemoteAccessFileProvider(cacheManager);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = fileProvider,
    ContentTypeProvider = new FileExtensionContentTypeProvider(filetypeMappings),
    DefaultContentType = "application/octet-stream",
    RequestPath = new PathString(""),
    ServeUnknownFileTypes = true,
    OnPrepareResponse = (context) =>
    {
        customLog?.Invoke(context.Context);

        var headers = context.Context.Response.GetTypedHeaders();
        if (appconfig.NoCacheRegex != null && appconfig.NoCacheRegex.IsMatch(context.File.Name))
        {
            headers.CacheControl = cacheControlPrivate;
        }
        else
        {
            headers.CacheControl = cacheControlPublic;
        }
    }
});


if (!string.IsNullOrWhiteSpace(appconfig.NotFoundHtmlKey) || !string.IsNullOrWhiteSpace(appconfig.IndexHtmlKey))
    app.Use(async (context, next) =>
    {
        await next();

        if (context.Response.StatusCode == 404)
        {
            if (!string.IsNullOrWhiteSpace(appconfig.IndexHtmlKey) && appconfig.IndexHtmlRegex != null && appconfig.IndexHtmlRegex.IsMatch(context.Request.Path))
            {
                var path = context.Request.Path.ToString();
                var lastSegment = path.Split('/').Last();

                if (!lastSegment.Contains('.') && lastSegment.Length > 0 && !path.EndsWith('/'))
                {
                    context.Response.Redirect($"{path.TrimEnd('/')}/", true);
                    return;
                }

                if (!lastSegment.Contains('.') || lastSegment.Equals("index.html") || lastSegment.Equals("index.htm") || lastSegment.Equals("default.html") || lastSegment.Equals("default.htm"))
                {
                    var accessItem = await fileProvider.GetRemoteAccessItem(appconfig.IndexHtmlKey);
                    if (accessItem != null && accessItem.Exists)
                    {
                        context.Response.ContentType = "text/html";
                        context.Response.StatusCode = 200;
                        using var fs = accessItem.CreateReadStream();
                        await fs.CopyToAsync(context.Response.Body, context.RequestAborted);
                        return;
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(appconfig.NotFoundHtmlKey))
            {
                var accessItem = await fileProvider.GetRemoteAccessItem(appconfig.NotFoundHtmlKey);
                if (accessItem != null && accessItem.Exists)
                {
                    context.Response.ContentType = "text/html";
                    using var fs = accessItem.CreateReadStream();
                    await fs.CopyToAsync(context.Response.Body, context.RequestAborted);
                    return;
                }
            }
        }
    });

try
{
    app.Run();
}
catch (Exception ex)
{
    Log.Error(ex, $"Terminating due to exception");
}
finally
{
    Log.CloseAndFlush();
}

