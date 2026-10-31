using System.Text.Json.Serialization;
using Microsoft.AspNetCore.HostFiltering;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.EventLog;
using RigForge.Application;
using RigForge.Host.Daemon;
using RigForge.Host.Daemon.Api;
using RigForge.Host.Daemon.Hubs;
using RigForge.Host.Daemon.Security;
using RigForge.Host.Daemon.Services;
using RigForge.Infrastructure.CrossPlatform;
using RigForge.Infrastructure.Linux;
using RigForge.Infrastructure.Windows;

// Slim builder: no IIS integration, no HTTPS/certificate plumbing, fewer default services → smaller footprint.
// Content root = binary folder: services and logon tasks start with System32 as the working directory.
var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory });

builder.Services.AddWindowsService(options => options.ServiceName = "RigForge");
builder.Services.AddSystemd();
if (OperatingSystem.IsWindows())
{
    // Without a console (logon task) warnings and errors still land in the Windows Application log.
    builder.Logging.AddEventLog().AddFilter<EventLogLoggerProvider>(level => level >= LogLevel.Warning);
}

builder.Services.Configure<DaemonOptions>(builder.Configuration.GetSection(DaemonOptions.SectionName));
builder.Services.Configure<RenderingOptions>(builder.Configuration.GetSection(RenderingOptions.SectionName));
builder.Services.Configure<TelemetryOptions>(builder.Configuration.GetSection(TelemetryOptions.SectionName));
builder.Services.Configure<CoolingOptions>(builder.Configuration.GetSection(CoolingOptions.SectionName));
builder.Services.Configure<LcdOptions>(builder.Configuration.GetSection(LcdOptions.SectionName));

builder.Services.AddRigForgeApplication();
builder.Services.AddRigForgeCrossPlatformInfrastructure(builder.Configuration);
if (OperatingSystem.IsWindows())
{
    builder.Services.AddRigForgeWindowsPlatform(builder.Configuration);
}
else if (OperatingSystem.IsLinux())
{
    builder.Services.AddRigForgeLinuxPlatform(builder.Configuration);
}

// The daemon listens on loopback only; host filtering also blocks DNS-rebinding attempts.
builder.Services.Configure<HostFilteringOptions>(options =>
    options.AllowedHosts = (builder.Configuration["AllowedHosts"] ?? "localhost;127.0.0.1").Split(';', StringSplitOptions.RemoveEmptyEntries));

var allowedOrigins = builder.Configuration.GetSection($"{DaemonOptions.SectionName}:{nameof(DaemonOptions.AllowedOrigins)}").Get<string[]>()
    ?? new DaemonOptions().AllowedOrigins;
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services
    .AddSignalR(options =>
    {
        options.MaximumReceiveMessageSize = 512 * 1024;
        options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    })
    .AddJsonProtocol(options => options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddSingleton<ClientRegistry>();
builder.Services.AddSingleton<SystemStateBuilder>();

// Hosted services start in this order and stop in reverse: devices are detected before the loops start
// and released only after every loop has handed its hardware back (fan curves restored, LCD on firmware view).
builder.Services.AddHostedService<HardwareLifecycleService>();
builder.Services.AddHostedService<TelemetryWorker>();
builder.Services.AddHostedService<FanControlWorker>();
builder.Services.AddHostedService<EffectRenderWorker>();
builder.Services.AddHostedService<LcdRenderWorker>();
builder.Services.AddHostedService<UiBroadcastService>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseHostFiltering();
app.UseMiddleware<LocalOriginGuardMiddleware>();
app.UseCors();

// The production UI build (ui/ → src/Host.Daemon/wwwroot) is served by the daemon itself.
var hasUi = app.Environment.WebRootFileProvider is not NullFileProvider;
if (hasUi)
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}

app.MapHub<ControlHub>("/hubs/control");
app.MapRigForgeApi();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

if (hasUi)
{
    app.MapFallbackToFile("index.html");
}

app.Run();
