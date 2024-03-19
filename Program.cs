using System.Net;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Serilog;
using UpdaterMirror;

var builder = WebApplication.CreateBuilder(args);
var logConfiguration = new LoggerConfiguration()
    .WriteTo.Console();
builder.Host.UseSerilog();

var appconfig = ApplicationConfig.Load();

if (!string.IsNullOrWhiteSpace(appconfig.SeqLogUrl))
    logConfiguration = logConfiguration.WriteTo.Seq(appconfig.SeqLogUrl, apiKey: appconfig.SeqLogApiKey);

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

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new RemoteAccessFileProvider(cacheManager),
    ContentTypeProvider = new FileExtensionContentTypeProvider(filetypeMappings),
    DefaultContentType = "application/octet-stream",
    RequestPath = new PathString(""),
    ServeUnknownFileTypes = true,
    OnPrepareResponse = (context) =>
    {
        var headers = context.Context.Response.GetTypedHeaders();
        if (appconfig.NoCacheRegex != null && appconfig.NoCacheRegex.IsMatch(context.File.Name))
        {
            headers.CacheControl = new Microsoft.Net.Http.Headers.CacheControlHeaderValue
            {
                Private = true,
                MaxAge = null,
                NoCache = true,
                NoStore = true
            };
        }
        else
        {
            headers.CacheControl = new Microsoft.Net.Http.Headers.CacheControlHeaderValue
            {
                Public = true,
                MaxAge = appconfig.ValidityPeriod.Add(TimeSpan.FromSeconds(-1))
            };
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

