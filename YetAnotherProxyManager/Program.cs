using MudBlazor.Services;
using Serilog;
using Yarp.ReverseProxy.Configuration;
using YetAnotherProxyManager.Components;
using YetAnotherProxyManager.Data;
using YetAnotherProxyManager.Middleware;
using YetAnotherProxyManager.Services;
using YetAnotherProxyManager.Services.Filtering;

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .WriteTo.Console()
    .WriteTo.File("logs/proxymanager-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

const string ManagementBasePath = "/.proxy-manager";

try
{
    Log.Information("Starting YetAnotherProxyManager");

    var builder = WebApplication.CreateBuilder(args);

    // Use Serilog
    builder.Host.UseSerilog();

    // Get data path from config or use default
    var dataPath = builder.Configuration.GetValue<string>("DataPath") ?? "data";
    Directory.CreateDirectory(dataPath);

    // Register LiteDB repository as singleton
    var repository = new LiteDbRepository(dataPath);
    builder.Services.AddSingleton(repository);

    // Load settings from database
    var settings = repository.GetSettings();

    // Register core services
    builder.Services.AddSingleton<ConfigurationService>();
    builder.Services.AddSingleton<ServiceManager>();
    builder.Services.AddSingleton<AuthService>();
    builder.Services.AddSingleton<CertificateSelectorService>();
    builder.Services.AddSingleton<AcmeCertificateService>();
    builder.Services.AddSingleton<RouteValidationService>();
    builder.Services.AddSingleton<IProxyConfigProvider, DynamicProxyConfigProvider>();

    // Register filtering services
    builder.Services.AddSingleton<IpRangeService>();
    builder.Services.AddSingleton<FilterRuleEvaluator>();

    // Register analytics services
    builder.Services.AddSingleton<GeoLocationService>();
    builder.Services.AddSingleton<AnalyticsService>();

    // Register TCP/UDP forwarding services as singletons and hosted services
    builder.Services.AddSingleton<TcpForwarderService>();
    builder.Services.AddSingleton<UdpForwarderService>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<TcpForwarderService>());
    builder.Services.AddHostedService(sp => sp.GetRequiredService<UdpForwarderService>());
    builder.Services.AddHostedService(sp => sp.GetRequiredService<AcmeCertificateService>());

    // Configure Kestrel for multi-port listening
    builder.WebHost.ConfigureKestrel((context, serverOptions) =>
    {
        var certSelector = serverOptions.ApplicationServices.GetService<CertificateSelectorService>();

        // HTTP proxy port (80)
        serverOptions.ListenAnyIP(settings.HttpPort, listenOptions =>
        {
            listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1AndHttp2;
        });

        // HTTPS proxy port (443)
        serverOptions.ListenAnyIP(settings.HttpsPort, listenOptions =>
        {
            listenOptions.UseHttps(httpsOptions =>
            {
                httpsOptions.ServerCertificateSelector = (connectionContext, hostName) =>
                {
                    return certSelector?.SelectCertificate(hostName);
                };
            });
            listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1AndHttp2;
        });
    });

    // Add YARP reverse proxy
    builder.Services.AddReverseProxy();

    // Add Blazor Server
    builder.Services.AddRazorComponents()
        .AddInteractiveServerComponents();

    // Add MudBlazor
    builder.Services.AddMudServices();

    // Add HTTP context accessor for session management
    builder.Services.AddHttpContextAccessor();

    var app = builder.Build();

    // Wire up ServiceManager with ConfigurationService
    var configService = app.Services.GetRequiredService<ConfigurationService>();
    var serviceManager = app.Services.GetRequiredService<ServiceManager>();
    configService.SetServiceManager(serviceManager);

    // ACME challenge middleware
    app.UseAcmeChallenge();

    // ============================================================
    // Management UI - under /.proxy-manager
    // ============================================================
    app.MapWhen(
        context => context.Request.Path.StartsWithSegments(ManagementBasePath),
        managementApp =>
        {
            managementApp.UsePathBase(ManagementBasePath);
            managementApp.UseStaticFiles();
            managementApp.UseRouting();
            managementApp.UseAntiforgery();
            managementApp.UseEndpoints(endpoints =>
            {
                endpoints.MapRazorComponents<App>()
                    .AddInteractiveServerRenderMode();
            });
        });

    // ============================================================
    // Proxy traffic - YARP only, no interference
    // ============================================================
    app.UseConditionalHttpsRedirect();
    app.UseAnalytics();
    app.UseRequestFiltering();
    app.UseRouting();
    app.MapReverseProxy();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
