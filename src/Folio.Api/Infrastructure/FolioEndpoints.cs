using Folio.Api.Options;
using Loom.Handlers;
using Loom.Results;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Diagnostics = Folio.Api.Features.Diagnostics.GetDiagnostics;
using Pages = Folio.Api.Features.Pages.GetPage;
using Projects = Folio.Api.Features.Projects;
using Refresh = Folio.Api.Features.Refresh.TriggerRefresh;
using Site = Folio.Api.Features.Site.GetSite;

namespace Folio.Api.Infrastructure;

/// <summary>Maps every route.</summary>
internal static class FolioEndpoints
{
    private static readonly string[] GetHead = ["GET", "HEAD"];

    /// <summary>Maps the versioned API.</summary>
    /// <param name="routes">The group to map into.</param>
    /// <returns>The same group.</returns>
    public static RouteGroupBuilder MapFolio(this RouteGroupBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        _ = routes.MapMethods("/site", GetHead, static async (
            [FromQuery] string? locale,
            SnapshotProvider snapshots,
            IHandler<Site.Request, Site.Response> handler,
            HttpContext context,
            CancellationToken cancellationToken) =>
            await Read(snapshots, locale, context, view => handler.HandleAsync(new Site.Request(view), cancellationToken)))
            .AllowAnonymous()
            .WithName("GetSite")
            .Produces<Site.Response>()
            .Produces(StatusCodes.Status304NotModified)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        _ = routes.MapMethods("/pages/{slug}", GetHead, static async (
            string slug,
            [FromQuery] string? locale,
            SnapshotProvider snapshots,
            IHandler<Pages.Request, Pages.Response> handler,
            HttpContext context,
            CancellationToken cancellationToken) =>
            await Read(snapshots, locale, context, view =>
                handler.HandleAsync(new Pages.Request(view, slug), cancellationToken)))
            .AllowAnonymous()
            .WithName("GetPage")
            .Produces<Pages.Response>()
            .Produces(StatusCodes.Status304NotModified)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        _ = routes.MapMethods("/projects", GetHead, static async (
            [FromQuery] string? locale,
            SnapshotProvider snapshots,
            IHandler<Projects.ListProjects.Request, Projects.ListProjects.Response> handler,
            HttpContext context,
            CancellationToken cancellationToken) =>
            await Read(snapshots, locale, context, view =>
                handler.HandleAsync(new Projects.ListProjects.Request(view), cancellationToken)))
            .AllowAnonymous()
            .WithName("ListProjects")
            .Produces<Projects.ListProjects.Response>()
            .Produces(StatusCodes.Status304NotModified)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        _ = routes.MapMethods("/projects/{slug}", GetHead, static async (
            string slug,
            [FromQuery] string? locale,
            SnapshotProvider snapshots,
            IHandler<Projects.GetProject.Request, Projects.GetProject.Response> handler,
            HttpContext context,
            CancellationToken cancellationToken) =>
            await Read(snapshots, locale, context, view =>
                handler.HandleAsync(new Projects.GetProject.Request(view, slug), cancellationToken)))
            .AllowAnonymous()
            .WithName("GetProject")
            .Produces<Projects.GetProject.Response>()
            .Produces(StatusCodes.Status304NotModified)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        _ = routes.MapGet("/diagnostics", static async (
            [FromQuery] string? severity,
            [FromQuery] string? project,
            IHandler<Diagnostics.Request, Diagnostics.Response> handler,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            Result<Diagnostics.Response> result =
                await handler.HandleAsync(new Diagnostics.Request(severity, project), cancellationToken);

            return result.ToHttpResult();
        })
            .AllowAnonymous()
            .WithName("GetDiagnostics")
            .Produces<Diagnostics.Response>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        _ = routes.MapPost("/refresh", static async (
            IHandler<Refresh.Request, Refresh.Response> handler,
            CancellationToken cancellationToken) =>
            (await handler.HandleAsync(new Refresh.Request(), cancellationToken)).ToHttpResult())
            .WithName("TriggerRefresh")
            .Produces<Refresh.Response>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return routes;
    }

    private static async Task<IResult> Read<TResponse>(
        SnapshotProvider snapshots,
        string? locale,
        HttpContext context,
        Func<SnapshotView, Task<Result<TResponse>>> handle)
    {
        string resource = context.Request.Path.Value ?? string.Empty;
        Result<SnapshotView> gate = SnapshotGate.Open(snapshots, locale, resource);

        if (gate.IsFailure)
        {
            return Result.Failure(gate.Error).ToHttpResult();
        }

        SnapshotView view = gate.Value;
        StringValues ifNoneMatch = context.Request.Headers.IfNoneMatch;

        // A tag this specific proves the caller was served this resource, so no projection is needed.
        if (Matches(ifNoneMatch, view.ETag))
        {
            Validate(context, view);
            return Results.StatusCode(StatusCodes.Status304NotModified);
        }

        Result<TResponse> result = await handle(view);

        if (result.IsFailure)
        {
            return result.ToHttpResult();
        }

        Validate(context, view);

        // RFC 9110: '*' asks whether any representation exists, so it cannot answer for one that does not.
        return HasWildcard(ifNoneMatch)
            ? Results.StatusCode(StatusCodes.Status304NotModified)
            : result.ToHttpResult();
    }

    private static void Validate(HttpContext context, SnapshotView view)
    {
        ApiOptions options = context.RequestServices.GetRequiredService<IOptionsMonitor<ApiOptions>>().CurrentValue;

        context.Response.Headers.ETag = view.ETag;
        context.Response.Headers.LastModified = view.Snapshot.BuiltAt.ToString("R");
        context.Response.Headers.CacheControl = $"public, max-age={(int)options.CacheMaxAge.TotalSeconds}";
    }

    private static bool Matches(StringValues ifNoneMatch, string etag) =>
        Tags(ifNoneMatch).Any(candidate => string.Equals(Strong(candidate), etag, StringComparison.Ordinal));

    private static bool HasWildcard(StringValues ifNoneMatch) =>
        Tags(ifNoneMatch).Any(candidate => candidate == "*");

    private static IEnumerable<string> Tags(StringValues ifNoneMatch) =>
        ifNoneMatch
            .Where(header => header is not null)
            .SelectMany(header => header!.Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    /// <summary>RFC 9110 compares <c>If-None-Match</c> weakly, so a returned tag may carry the prefix.</summary>
    private static string Strong(string tag) =>
        tag.StartsWith("W/", StringComparison.Ordinal) ? tag[2..] : tag;
}
