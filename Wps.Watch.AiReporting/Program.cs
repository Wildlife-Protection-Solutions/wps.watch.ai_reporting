using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using Wps.Watch.AiReporting.Authorization;
using Wps.Watch.AiReporting.Chat;
using Wps.Watch.AiReporting.Data;
using Wps.Watch.AiReporting.Discovery;
using Wps.Watch.AiReporting.Mcp;
using Wps.Watch.AiReporting.Reports;
using Wps.Watch.AiReporting.Reports.Definitions;

// Default DOTNET_ENVIRONMENT for local dev. Generic Host (MCP mode) reads this
// variable specifically — it doesn't fall back to ASPNETCORE_ENVIRONMENT the way
// WebApplication does, so without this fallback an MCP launch from the Inspector
// or Claude Desktop would run as "Production" and the DevAuth guardrail below
// would refuse to start. Inherits from ASPNETCORE_ENVIRONMENT when present
// (web mode sets it via launchSettings.json), otherwise defaults to Development.
// Deployed environments set DOTNET_ENVIRONMENT explicitly via the host config.
if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")))
{
    var aspnet = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
    Environment.SetEnvironmentVariable(
        "DOTNET_ENVIRONMENT",
        string.IsNullOrEmpty(aspnet) ? "Development" : aspnet);
}

if (args.Length > 0 && args[0] == "mcp")
{
    // ───────────── MCP stdio server mode ─────────────
    // stdout is the JSON-RPC channel; logging must go to stderr.
    var mcpBuilder = Host.CreateApplicationBuilder(args);

    mcpBuilder.Logging.ClearProviders();
    mcpBuilder.Logging.AddConsole(opts =>
    {
        opts.LogToStandardErrorThreshold = LogLevel.Trace;
    });

    ConfigureCommonServices(mcpBuilder.Services, mcpBuilder.Configuration, isMcpMode: true);

    mcpBuilder.Services
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithToolsFromAssembly();

    var mcpHost = mcpBuilder.Build();
    AssertDevAuthSafeForEnvironment(mcpHost.Services, mcpHost.Services.GetRequiredService<IHostEnvironment>());
    await mcpHost.RunAsync();
    return;
}

// ───────────── Web API mode (default) ─────────────
var webBuilder = WebApplication.CreateBuilder(args);

ConfigureCommonServices(webBuilder.Services, webBuilder.Configuration, isMcpMode: false);

webBuilder.Services.AddControllers();
webBuilder.Services.AddEndpointsApiExplorer();
webBuilder.Services.AddSwaggerGen();

// "Ask AI Reports" chat surface (web mode only). Calls the Claude Messages API
// over HttpClient to translate plain-English questions into guarded SELECTs.
webBuilder.Services.Configure<AnthropicOptions>(
    webBuilder.Configuration.GetSection(AnthropicOptions.SectionName));
webBuilder.Services.AddHttpClient("anthropic", c => c.Timeout = TimeSpan.FromSeconds(120));
webBuilder.Services.AddScoped<ChatService>();

var app = webBuilder.Build();
AssertDevAuthSafeForEnvironment(app.Services, app.Environment);

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// Serve the AI Reports front end (Vite build output in wwwroot). Static assets and
// the SPA fallback sit ahead of the auth shim — only /api/* needs the per-request
// user context. The fallback is a no-op until `npm run build` has populated wwwroot.
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseMiddleware<DevAuthMiddleware>();
app.UseAuthorization();
app.MapControllers();
app.MapFallbackToFile("index.html");

app.Run();

static void ConfigureCommonServices(IServiceCollection services, IConfiguration config, bool isMcpMode)
{
    var connectionString = config.GetConnectionString("WpsReplica")
        ?? throw new InvalidOperationException(
            "Connection string 'WpsReplica' is not configured. " +
            "Set ConnectionStrings:WpsReplica in appsettings.json or via the " +
            "ConnectionStrings__WpsReplica environment variable.");

    services.AddDbContext<WpsReplicaDbContext>(opts =>
    {
        opts.UseSqlServer(connectionString, sql =>
        {
            sql.CommandTimeout(15);
            sql.EnableRetryOnFailure(maxRetryCount: 3);
        });
        opts.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
    });

    services.Configure<DevAuthOptions>(config.GetSection(DevAuthOptions.SectionName));

    if (isMcpMode)
    {
        // No HTTP middleware in stdio mode — the dev user is configured once at
        // startup and shared for the process lifetime.
        services.AddSingleton<IUserContextAccessor>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<DevAuthOptions>>().Value;
            var accessor = new UserContextAccessor();
            accessor.Set(new UserContext
            {
                UserId = opts.UserId,
                IsSystemAdmin = opts.IsSystemAdmin,
                OrganizationIds = opts.OrganizationIds.AsReadOnly(),
                RoleNames = opts.RoleNames.AsReadOnly(),
            });
            return accessor;
        });
    }
    else
    {
        services.AddScoped<IUserContextAccessor, UserContextAccessor>();
    }

    // Admin-tier reports (AdminReport operation — system admins only).
    services.AddSingleton<IReportDefinition, RegionSiteTotalsReport>();
    services.AddSingleton<IReportDefinition, CameraInventoryReport>();
    services.AddSingleton<IReportDefinition, DeployedCameraBatteryReport>();
    services.AddSingleton<IReportDefinition, PhotoCountByCameraLastMonthReport>();
    services.AddSingleton<IReportDefinition, PhotoCountByCameraRangeReport>();
    services.AddSingleton<IReportDefinition, SimContractRenewalReport>();
    services.AddSingleton<IReportDefinition, SimPrepaidDataReport>();
    services.AddSingleton<IReportDefinition, DeploymentHealthStatusReport>();

    // User-tier reports (UserReport operation — orgadmin/viewer/etc.).
    services.AddSingleton<IReportDefinition, CameraBatteryLevelUserReport>();
    services.AddSingleton<IReportDefinition, CameraDeploymentDetailReport>();
    services.AddSingleton<IReportDefinition, PhotoCountByCameraUserReport>();

    services.AddSingleton<IReportCatalog, ReportCatalog>();
    services.AddScoped<ReportRunner>();

    // Free-form discovery surface (Rung 1: internal team, devqa).
    services.Configure<DiscoveryOptions>(config.GetSection(DiscoveryOptions.SectionName));
    services.AddScoped<DiscoveryQueryRunner>();
}

// Fail-closed guardrail: refuses to start if the dev auth shim is enabled
// outside the Development environment. Without this, a deployed service that
// inherited the committed appsettings.json defaults would silently treat every
// request as system-admin from `dev-user` — a serious security footgun.
static void AssertDevAuthSafeForEnvironment(IServiceProvider services, IHostEnvironment env)
{
    var devAuth = services.GetRequiredService<IOptions<DevAuthOptions>>().Value;
    if (devAuth.Enabled && !env.IsDevelopment())
    {
        throw new InvalidOperationException(
            $"DevAuth.Enabled=true is not permitted outside the Development environment " +
            $"(current environment: {env.EnvironmentName}). The dev auth shim bypasses real " +
            "authentication; if this is a deployed environment, set DevAuth:Enabled=false " +
            "in appsettings.{Environment}.json or via the DevAuth__Enabled environment variable, " +
            "and configure real JWT validation.");
    }
}
