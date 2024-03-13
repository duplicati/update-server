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

if (!string.IsNullOrWhiteSpace(appconfig.RootRedirect))
    app.MapGet("/", ctx => { 
        ctx.Response.Redirect(appconfig.RootRedirect, true);
        return Task.CompletedTask;
    });

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new RemoteAccessFileProvider(
        new CacheManager(appconfig.PrimaryStorage, appconfig.CachePath, appconfig.MaxNotFound, appconfig.MaxSize, appconfig.ValidityPeriod)
    ),
    ContentTypeProvider = new FileExtensionContentTypeProvider(filetypeMappings),
    DefaultContentType = "application/octet-stream",     
    RequestPath = new PathString(""),
    ServeUnknownFileTypes = true
});

try
{
    app.Run();
}
finally
{
    Log.CloseAndFlush();
}

