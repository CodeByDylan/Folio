using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Folio.Api.Infrastructure;
using Folio.Api.Options;
using Folio.Ingestion;
using Folio.Ingestion.GitHub;
using Loom.Handlers;
using Loom.Results;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;
using Slice = Folio.Api.Features.Refresh.TriggerRefresh;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddTelemetry();

builder.Services.AddOptions<GitHubOptions>()
    .BindConfiguration(GitHubOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<RefreshOptions>()
    .BindConfiguration(RefreshOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<SnapshotStoreOptions>()
    .BindConfiguration(SnapshotStoreOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<ApiOptions>()
    .BindConfiguration(ApiOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();

builder.Services.AddSingleton<SnapshotProvider>();
builder.Services.AddSingleton<RefreshReporter>();
builder.Services.AddSingleton<RefreshGate<Result<Slice.Response>>>();
builder.Services.AddSingleton<FolioMetrics>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddFolioHealthChecks();
builder.Services.AddFolioIngestion(provider =>
{
    GitHubOptions github = provider.GetRequiredService<IOptions<GitHubOptions>>().Value;
    RefreshOptions refresh = provider.GetRequiredService<IOptions<RefreshOptions>>().Value;
    SnapshotStoreOptions store = provider.GetRequiredService<IOptions<SnapshotStoreOptions>>().Value;

    return new IngestionSettings(
        github.Token,
        new FetchSettings(
            github.CentralRepository,
            github.CentralRef,
            refresh.FetchConcurrency,
            refresh.MinimumRateLimitBudget,
            refresh.MaxFileBytes,
            refresh.MaxFileCount,
            refresh.MaxTotalBytes),
        store.Mode is SnapshotStoreMode.Redis ? SnapshotStoreKind.Redis : SnapshotStoreKind.File,
        store.FilePath,
        store.RedisConnectionString);
});
builder.Services.AddHostedService<RefreshService>();

builder.Services
    .AddLoomHandlers(chain => chain.WithLogging())
    .AddHandler<Folio.Api.Features.Site.GetSite.Handler,
        Folio.Api.Features.Site.GetSite.Request,
        Folio.Api.Features.Site.GetSite.Response>()
    .AddHandler<Folio.Api.Features.Pages.GetPage.Handler,
        Folio.Api.Features.Pages.GetPage.Request,
        Folio.Api.Features.Pages.GetPage.Response>()
    .AddHandler<Folio.Api.Features.Projects.ListProjects.Handler,
        Folio.Api.Features.Projects.ListProjects.Request,
        Folio.Api.Features.Projects.ListProjects.Response>()
    .AddHandler<Folio.Api.Features.Projects.GetProject.Handler,
        Folio.Api.Features.Projects.GetProject.Request,
        Folio.Api.Features.Projects.GetProject.Response>()
    .AddHandler<Folio.Api.Features.Diagnostics.GetDiagnostics.Handler,
        Folio.Api.Features.Diagnostics.GetDiagnostics.Request,
        Folio.Api.Features.Diagnostics.GetDiagnostics.Response>()
    .AddHandler<Folio.Api.Features.Refresh.TriggerRefresh.Handler,
        Folio.Api.Features.Refresh.TriggerRefresh.Request,
        Folio.Api.Features.Refresh.TriggerRefresh.Response>();

builder.Services
    .AddAuthentication(ApiKeyAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationHandler.SchemeName,
        configureOptions: null);

builder.Services.AddAuthorization();

builder.Services.AddRateLimiter(limiter =>
{
    limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        ApiOptions options = context.RequestServices
            .GetRequiredService<IOptionsMonitor<ApiOptions>>().CurrentValue;

        return RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = options.RateLimitPermits,
                Window = options.RateLimitWindow,
            });
    });
});

builder.Services.AddCors();
builder.Services.AddOptions<CorsOptions>().Configure<IOptions<ApiOptions>>((cors, api) =>
    cors.AddDefaultPolicy(policy =>
        _ = policy.WithOrigins([.. api.Value.AllowedOrigins])
            .AllowAnyHeader()
            .WithMethods("GET", "POST")
            .WithExposedHeaders("ETag", "Last-Modified")));

builder.Services.ConfigureHttpJsonOptions(json =>
{
    json.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    json.SerializerOptions.TypeInfoResolverChain.Insert(0, FolioJsonContext.Default);
});

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi(options =>
{
    options.CreateSchemaReferenceId = SchemaIds.For;
    _ = options.AddSchemaTransformer<SchemaFacts>();
});

builder.Services.Configure<ForwardedHeadersOptions>(forwarded =>
{
    forwarded.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

    // The immediate proxy is trusted by deployment; without clearing these the default single-hop
    // restriction drops the header inside most container networks.
    forwarded.KnownIPNetworks.Clear();
    forwarded.KnownProxies.Clear();
});

WebApplication app = builder.Build();

// So the rate limiter partitions on the real client, not the proxy — only when a proxy is trusted.
if (app.Services.GetRequiredService<IOptions<ApiOptions>>().Value.TrustForwardedHeaders)
{
    app.UseForwardedHeaders();
}

// Without these an unhandled exception is a bodyless 500 and an unmatched route a bodyless 404.
app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthEndpoints();

if (app.Environment.IsDevelopment())
{
    _ = app.MapOpenApi();
    _ = app.MapScalarApiReference();
}

_ = app.MapGroup("/v1").RequireAuthorization().MapFolio();

await app.RunAsync();

/// <summary>Exposed so the test host can reference this assembly.</summary>
public sealed partial class Program;
