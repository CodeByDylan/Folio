using System.Net;
using System.Text;
using Folio.Domain.Diagnostics;
using Folio.Domain.Model;
using Folio.Ingestion.GitHub;
using Folio.Ingestion.Snapshots;
using Loom.Results;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;

namespace Folio.Api.Tests;

/// <summary>Stands in for GitHub at the network edge, and can start failing partway through a test.</summary>
internal sealed class StubContentSource(StoredInputs? inputs, Error? failure = null) : IGitHubContentSource
{
    private Error? _failure = failure;
    private int _fetches;
    private bool _hang;
    private Exception? _throws;

    /// <summary>Gets how many fetches were issued.</summary>
    public int Fetches => Volatile.Read(ref _fetches);

    /// <summary>Makes every later fetch fail.</summary>
    /// <param name="error">The failure to return.</param>
    public void StartFailing(Error error) => Volatile.Write(ref _failure, error);

    /// <summary>Makes every later fetch run until its token is cancelled.</summary>
    public void StartHanging() => Volatile.Write(ref _hang, true);

    /// <summary>Makes every later fetch throw rather than return a failure.</summary>
    /// <param name="fault">The exception to throw.</param>
    public void StartThrowing(Exception fault) => Volatile.Write(ref _throws, fault);

    /// <inheritdoc />
    public async Task<Result<FetchResult>> FetchAsync(StoredInputs? previous, CancellationToken cancellationToken)
    {
        _ = Interlocked.Increment(ref _fetches);

        if (Volatile.Read(ref _hang))
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        if (Volatile.Read(ref _throws) is { } fault)
        {
            throw fault;
        }

        Error? failed = Volatile.Read(ref _failure);

        return failed is not null ? failed : new FetchResult(inputs!, [], Requests: 0, BudgetRemaining: 5000);
    }
}

/// <summary>Keeps stored inputs in memory for a test.</summary>
internal sealed class MemorySnapshotStore : ISnapshotStore
{
    private StoredInputs? _inputs;
    private bool _throwOnWrite;

    /// <summary>Makes every later write throw, as a store breaking its contract would.</summary>
    public void StartThrowingOnWrite() => Volatile.Write(ref _throwOnWrite, true);

    /// <inheritdoc />
    public Task<StoredInputs?> ReadAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Volatile.Read(ref _inputs));

    /// <inheritdoc />
    public Task WriteAsync(StoredInputs inputs, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _throwOnWrite))
        {
            throw new InvalidOperationException("The store is unavailable.");
        }

        Volatile.Write(ref _inputs, inputs);
        return Task.CompletedTask;
    }
}

/// <summary>Boots the real API with GitHub stubbed out.</summary>
internal sealed class FolioApp(
    StoredInputs? inputs,
    Error? failure = null,
    FakeTimeProvider? clock = null,
    bool refreshOnTimer = false) : WebApplicationFactory<Program>
{
    /// <summary>The key the test host accepts for a refresh.</summary>
    public const string RefreshKey = "0123456789abcdef0123456789abcdef";

    /// <summary>Gets the stubbed content source, so a test can make later fetches fail.</summary>
    public StubContentSource Source { get; } = new(inputs, failure);

    /// <summary>Gets the stubbed store, so a test can make later writes throw.</summary>
    public MemorySnapshotStore Store { get; } = new();

    /// <summary>Builds the first snapshot and returns a client.</summary>
    /// <returns>A client with a snapshot already published.</returns>
    public async Task<HttpClient> ReadyAsync()
    {
        HttpClient client = CreateClient();
        _ = await RefreshAsync(client);

        return client;
    }

    /// <summary>Triggers a rebuild with the key the host accepts.</summary>
    /// <param name="client">The client to send with.</param>
    /// <returns>The status code, for a caller that expects a failure.</returns>
    public async Task<HttpStatusCode> RefreshAsync(HttpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        using HttpRequestMessage request = new(HttpMethod.Post, "/v1/refresh");
        request.Headers.Add("X-Folio-Key", RefreshKey);

        using HttpResponseMessage response = await client.SendAsync(request);

        return response.StatusCode;
    }

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        _ = builder.UseSetting("GitHub:Token", "stub-token");
        _ = builder.UseSetting("GitHub:CentralRepository", "dutchy/portfolio");
        _ = builder.UseSetting("Api:RefreshKey", RefreshKey);
        _ = builder.UseSetting("SnapshotStore:FilePath", "unused.json");


        _ = builder.ConfigureServices(services =>
        {
            if (!refreshOnTimer)
            {
                services.RemoveAll<IHostedService>();
            }

            services.RemoveAll<IGitHubContentSource>();
            services.RemoveAll<ISnapshotStore>();

            if (clock is not null)
            {
                services.RemoveAll<TimeProvider>();
                _ = services.AddSingleton<TimeProvider>(clock);
            }

            _ = services.AddSingleton<IGitHubContentSource>(Source);
            _ = services.AddSingleton<ISnapshotStore>(Store);
        });
    }

    /// <summary>Builds the vault's worked example as an input set.</summary>
    /// <returns>The inputs.</returns>
    public static StoredInputs WorkedExample()
    {
        Dictionary<string, string> central = new(StringComparer.Ordinal)
        {
            [".folio/site.toml"] = """
                version = 1

                [site]
                url            = "https://dutchy.dev"
                default_locale = "en"
                locales        = ["en", "nl"]
                owner          = "dutchy"

                [[site.links]]
                type = "github"
                url  = "https://github.com/dutchy"

                [[site.sections]]
                id   = "intro"
                type = "hero"

                [[site.sections]]
                id   = "stack"
                type = "skills"

                [[site.sections]]
                id   = "faq"
                type = "qa"

                [[site.sections]]
                id   = "reach"
                type = "contact"

                [[site.sections]]
                id   = "work"
                type = "projects"

                [[site.sections]]
                id   = "about"
                type = "prose"
                file = "about.md"

                [[site.pages]]
                slug     = "home"
                home     = true
                sections = ["intro", "stack", "faq", "reach", "work", "about"]
                """,
            [".folio/sections/intro.toml"] = """
                version = 1

                [[actions]]
                id  = "work"
                url = "https://dutchy.dev/projects"
                """,
            [".folio/sections/stack.toml"] = """
                version = 1

                [[categories]]
                id = "languages"

                [[categories.skills]]
                id    = "rust"
                level = "expert"
                """,
            [".folio/sections/faq.toml"] = "version = 1\n\n[[entries]]\nid = \"why\"\n",
            [".folio/content/en/faq.md"] = "## why\n\nBecause it should.\n",
            [".folio/content/nl/faq.md"] = "## why\n\nOmdat het moet.\n",
            [".folio/sections/work.toml"] = "version = 1\n\nfeatured = true\nlimit = 3\n",
            [".folio/projects.toml"] = "version = 1\n\n[[projects]]\nrepo = \"folio\"\nfeatured = true\n",
            [".folio/tags.toml"] = "version = 1\n\n[[tags]]\nid = \"rust\"\nkind = \"language\"\n",
            [".folio/locales/en.toml"] =
                "site.title = \"Dutchy\"\nlink.github = \"GitHub\"\ntag.rust = \"Rust\"\npage.home = \"Home\"\nsection.intro.headline = \"I build things\"\nsection.intro.action.work = \"See my work\"\nsection.stack.category.languages = \"Languages\"\nsection.stack.skill.rust = \"Rust\"\nsection.faq.question.why = \"Why?\"\nsection.reach.heading = \"Get in touch\"\nsection.work.heading = \"Featured work\"\n",
            [".folio/locales/nl.toml"] =
                "site.title = \"Dutchy\"\nlink.github = \"GitHub\"\ntag.rust = \"Rust\"\npage.home = \"Start\"\nsection.intro.headline = \"Ik bouw dingen\"\nsection.intro.action.work = \"Bekijk mijn werk\"\nsection.stack.category.languages = \"Talen\"\nsection.stack.skill.rust = \"Rust\"\nsection.faq.question.why = \"Why?\"\n",
            [".folio/content/en/about.md"] = "# About\n\nI build things.\n",
            [".folio/content/nl/about.md"] = "# Over mij\n\nIk bouw dingen.\n",
        };

        Dictionary<string, string> repo = new(StringComparer.Ordinal)
        {
            [".folio/project.toml"] = """
                version = 1

                [project]
                slug    = "folio"
                status  = "active"
                started = "2026-03"
                tags    = ["rust"]

                [[sections]]
                id   = "overview"
                file = "overview.md"
                """,
            [".folio/locales/en.toml"] = "project.name = \"Folio\"\nproject.tagline = \"Assembled portfolios\"\n",
            [".folio/locales/nl.toml"] = "project.name = \"Folio\"\n",
            [".folio/content/en/overview.md"] = "# Overview\n\nFolio reads two sources.\n",
            [".folio/content/nl/overview.md"] = "# Overzicht\n\nFolio leest twee bronnen.\n",
        };

        return new StoredInputs(
            "dutchy/portfolio",
            "central-sha",
            Set(central),
            new Dictionary<string, MediaSize>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal),
            [
                new RepoInput(
                    "dutchy/folio",
                    Path: string.Empty,
                    PinnedSha: "abc123",
                    Files: Set(repo),
                    Metadata: new RepoMetadata(
                        "dutchy", "folio", "Assembled portfolios", null, ["cli", "rust"], "Rust",
                        [new RepoLanguage("Rust", 900), new RepoLanguage("Shell", 100)],
                        12, 3, "MIT", false,
                        new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                        new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
                        [
                            new RepoRelease(
                                "v2.0.0",
                                "Release v2.0.0",
                                new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
                                new Uri("https://github.com/dutchy/folio/releases/tag/v2.0.0"),
                                IsPrerelease: false),
                            new RepoRelease(
                                "v1.0.0",
                                null,
                                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                                new Uri("https://github.com/dutchy/folio/releases/tag/v1.0.0"),
                                IsPrerelease: true),
                        ]),
                    MediaSizes: new Dictionary<string, MediaSize>(StringComparer.Ordinal),
                    MediaPaths: new HashSet<string>(StringComparer.Ordinal)),
            ],
            DateTimeOffset.UnixEpoch);
    }

    private static FileSet Set(Dictionary<string, string> files) =>
        new(files.Select(file => new KeyValuePair<string, ReadOnlyMemory<byte>>(
            file.Key,
            Encoding.UTF8.GetBytes(file.Value))));
}
