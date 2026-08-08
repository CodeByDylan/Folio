# AGENTS.md

Folio is a single-tenant read-only HTTP API. It assembles a portfolio from GitHub repository metadata
and `.folio` files committed inside the showcased repositories, and serves it to a portfolio frontend.

The reasoning behind these rules is in `docs/`. This file is only the rules.

## Layout

```text
src/Folio.Domain/      the .folio format: model, parsers, resolver. Pure, synchronous.
src/Folio.Ingestion/   all I/O: GitHub, the snapshot store, media probing.
src/Folio.Api/         endpoints, slices, refresh scheduling, composition root.

tests/Folio.Domain.Tests/        fixtures and value objects; the bulk of the suite
tests/Folio.Ingestion.Tests/     stubbed at HttpMessageHandler; Redis against a container, skipped without one
tests/Folio.Api.Tests/           through HTTP, against a stubbed content source
tests/Folio.ArchitectureTests/   the boundaries below
```

A background refresher builds the whole portfolio into one immutable graph. Requests read that graph.
There is no database, and no request path reaches GitHub.

A **slice** is one operation, in one file, holding its request, response, handler and route.

## Stack

Versions live in `Directory.Packages.props`. Never put a `Version` on a `PackageReference`.

| Concern | Package |
| --- | --- |
| Results and errors | `CodeByDylan.Loom.Results`, `.Results.AspNetCore` |
| Dispatch | `CodeByDylan.Loom.Handlers`, `.Handlers.Abstractions` |
| TOML | Tomlyn |
| Markdown | Markdig |
| GitHub | Octokit |
| Snapshot store | StackExchange.Redis, or a file |
| Telemetry | `ILogger` + OpenTelemetry |
| Tests | TUnit, NetArchTest, Testcontainers |

Loom is a dependency, not a template. Its own repository conventions do not apply here.

Do not add: a database or ORM; `Loom.Entities`, `Loom.Specifications`, `Loom.Paging`; Aspire; a
mocking library; an imaging library; a mediator or dispatcher.

## Enforced boundaries

`tests/Folio.ArchitectureTests` fails the build on any of these. Add a structural rule, add its test.

1. `Folio.Domain` references only Loom.Results, Markdig, Tomlyn and the BCL.
2. `Folio.Domain` uses no `File`, `Directory`, `FileStream`, `HttpClient` or socket type.
3. `Folio.Api` references neither Octokit nor StackExchange.Redis.
4. No slice namespace depends on another slice namespace.
5. `IConfiguration` is a constructor parameter of no type.

## Domain

- Resolution is one pure synchronous function: `PortfolioResolver.Resolve(central, repos,
  applicationVersion, builtAt, priorDiagnostics)` → `Result<Snapshot>`.
- Failure means the central config was fatally broken. Success carries the snapshot including its
  diagnostics.
- **One snapshot holds every declared locale.** Parse once, then localize per locale. Structural
  diagnostics come from parsing; fallback diagnostics from localizing. Merging them reports every
  unknown key once per locale.
- Media sizes arrive on `RepoInput`, and declared-media existence arrives on `RepoInput.MediaPaths`
  from the tree listing — media may live anywhere in the repository, and Domain cannot probe an image
  or list a tree.
- Derived values are computed in Domain, never while mapping to a wire type. A language's share of the
  repository is `RepoMetadata.LanguageShares`, rounded to one decimal place, because the stored inputs
  hold what GitHub reported and nothing computed from it.
- A relation type's name, its inverse and whether it can be declared come from `RelationVocabulary`.
  Adding a relation is one row there; nothing switches over `RelationType`.
- Media and markdown image URLs are built by `RawContentUrl.For`, never by interpolating the host.
- The directory, diagnostic identity and `.folio` root a repo-and-path location implies come from
  `ProjectLocation`; nothing re-derives them inline.
- An undeclared `content/` directory drops the project it belongs to. The central repo has no project
  to drop, so its directory is reported and never read. A `locales/` file whose name is not a declared
  locale in canonical form is reported and never read, in either repo.
- No ports, no async, no injected fetcher. The caller supplies a complete file set.
- Phase one resolves each project independently. Phase two applies only the rules that need the whole
  set: slug uniqueness, relation inversion, ordering. Nothing else reaches across projects.
- Collaborators in Domain are concrete sealed types, not interfaces. An interface belongs where a
  second implementation exists or a boundary must be stubbed: `ISnapshotStore`, `IGitHubContentSource`
  and `IMediaProbe` in Ingestion, `ISnapshotProvider` and `IRefreshReporter` in the API. Domain has none.
- Everything is immutable: `sealed record`, `init` or constructor-set. There are no entities and no
  identity equality. A project's identity is its slug.
- A slug is lowercase letters, digits and hyphens, not starting or ending with one. An authored slug
  is refused if invalid; a derived one is normalized. Never repair an authored identity — repository
  names carry capitals and dots, config does not.
- Diagnostics go through a scoped `DiagnosticSink`, never a returned tuple. Scope before use:
  `sink.ForProject(slug).ForFile(path)`.
- Derived collections are sorted deterministically and this is not configurable: languages by bytes
  descending then alphabetical, topics alphabetical, releases by publish date descending then by tag
  name. Ordering is carried by an ordered type, never by a dictionary's insertion order. Every sort needs a tie-break, or two identical builds can produce different bytes.
- Releases exclude drafts, which are unpublished and have no publish date. Prereleases are kept and
  flagged; whether to show one is the frontend's call.
- Authored values win over derived ones. The one exception: GitHub reporting a repository archived
  forces `status = "archived"`.
- A config parser declares its whole shape — root keys, tables and table arrays — and reports the rest
  under `schema.unknown_key`. Anything the schema does not name is dropped, so a mistyped `[[sectons]]`
  must not cost its content in silence. Locale files are exempt; their keys are open by design, and
  a table header form (`[project]` + `tagline`) loads as the same key as its dotted form.

## Ingestion

- Blobs are keyed by pinned SHA and never re-fetched. A blob is addressed by its own content hash, so
  a hit cannot be stale and the fetch is skipped rather than revalidated.
- Mutable endpoints are revalidated with `If-None-Match`, added by `ConditionalHttpClient` at the
  transport because Octokit cannot send it through its typed clients. A `304` is answered from the
  cached body and reported as `200`. The ETag cache is in memory; never persist it.
- The rate-limit check is never revalidated. A cached budget reports a figure already spent.
- Ingestion composes itself through `AddFolioIngestion`, taking a plain `IngestionSettings`. Octokit
  and Redis types never appear in the host — boundary rule 3.
- The snapshot store persists raw inputs, never the resolved snapshot.
- The store is an optimization, so both its reads and its writes are best-effort: each implementation
  catches its own storage failures and logs them. A caller never enumerates the exception types of an
  implementation it does not know about, but the refresh keeps a broad backstop around the write,
  because the snapshot is already published by then. Cancellation is not a storage failure and always
  propagates.
- Octokit and StackExchange.Redis take no `CancellationToken`. Octokit calls are preceded by
  `cancellationToken.ThrowIfCancellationRequested()`; Redis calls are awaited through
  `WaitAsync(cancellationToken)`, which abandons the wait rather than the command.
- Transient faults abandon the whole refresh; content faults drop one project.
  - Content: `404` on a repo or path, unparseable TOML, unsupported schema version, duplicate slug.
  - Transient: `5xx`, timeout, connection failure, secondary rate limit, exhausted budget.
- A fetch abandons on any fault it did not classify as content. A timeout arrives as an
  `OperationCanceledException`, so it must be told apart from the fetch's own abandon signal, or a blip
  silently publishes a snapshot with projects missing.
- Check the remaining rate-limit budget before starting a refresh.
- Fetch concurrency is bounded by `RefreshOptions.FetchConcurrency`.
- The refresh timeout lives in the handler, so the timer and the endpoint are bounded identically.
- A refresh that fails with nothing yet published resolves the stored inputs and serves those, so a
  boot with GitHub unreachable degrades to stale content rather than 503s.
- Media dimensions are probed only for `raw.githubusercontent.com` URLs. Media URLs come from
  repositories that may not be yours, so probing an arbitrary one is a request-forgery primitive.
- Image headers are parsed by hand from a range request: PNG, JPEG, GIF, WebP, SVG. The first read is
  1 KiB; only a JPEG whose frame header lies beyond it earns a second, longer read.
- Only media the configuration names is measured. Nothing else can carry dimensions.
- A media probe never abandons a refresh. Dimensions are an optimization, and a repository is not at
  fault for a transport failure reaching its images.

## Refresh and the snapshot

- The timer and `POST /v1/refresh` invoke the same handler.
- The snapshot is immutable and swapped with `Interlocked.Exchange`. Never mutated in place.
- Refreshes never overlap. A trigger arriving mid-refresh joins the running one.
- A failed refresh leaves the previous snapshot serving.
- Always rebuild the whole snapshot. Skip fetches for unchanged SHAs; never patch incrementally.
- Before the first snapshot exists, content endpoints return `Unavailable` and `/ready` is unready.
  Never serve an empty portfolio during startup — empty is a valid state and would be indistinguishable.
- No webhooks. `projects.toml` may list repositories that cannot carry one.

## HTTP

- Minimal APIs, no controllers. All routes under `/v1`.
- Routes are registered explicitly in `Infrastructure/FolioEndpoints.cs`, handlers in `Program.cs`.
  No assembly scanning for endpoints, so the route table is readable in one place.
- Rule 4 treats `Folio.Api.Features.<Aggregate>.<Operation>` as a slice. A slice may use its own
  aggregate's `_Shared` types and nothing else under `Features`.
- Locale is `?locale=`, never `Accept-Language`. Never send `Vary: Accept-Language`.
- A locale that resolves to nothing declared is `Invalid` → 400, not a fallback to the default.
- Enum values reach the wire through `Wire.Lower` or `Wire.Hyphenate`, never inline conversion.
- A query value naming an enum member matches the name only. `Enum.TryParse` also accepts `"0"` and
  every other underlying value, which turns a typo into a silent filter.
- Every non-2xx response is `ProblemDetails`. A slice never writes a status code; use `ToHttpResult()`.
- `ETag` is `"{snapshotId}:{resource}:{requested}:{resolved}"`, strong. The resource must be in it, or
  one validator answers for every route and a client holding it gets `304` for a project that does not
  exist. Both locales must be in it, because the response echoes the requested one. Handle
  `If-None-Match` before any projection work, accepting `*` and comma-joined lists.
- Caching headers go on 2xx and 304 only. A 404 must not be publicly cacheable.
- `If-None-Match: *` asks whether any representation exists, so it may answer only once the handler has
  produced one. A specific tag may short-circuit, because holding it proves the caller was served this
  resource. Comparison is weak: a returned tag may come back carrying `W/`.
- `ETag` and `Last-Modified` are CORS-exposed, or the frontend the caching exists for cannot read them.
- The snapshot id incorporates the application version.
- Build metadata goes in `Last-Modified`, never in a response body.
- Provenance is inline, flat, with a sparse JSON-Pointer sidecar. Diagnostics are served only at
  `/v1/diagnostics`. Content responses have no envelope.
- A relative `.md` link to a declared section rewrites to `#<section-id>`. Never to a route: that
  needs the frontend's URL patterns, which the API must not carry. An internal absolute URL rewrites
  to a root-relative path, which is the only way markdown can signal client-side navigation.
- Empty collections are `[]`. Absent optional scalars are omitted. Never `null` for either.
- Enums serialize as strings. `started` and `ended` stay strings exactly as authored.
- Wire types are owned by the slice and mapped from Domain types. Never return a Domain record.
- `ListProjects` returns summaries and `GetProject` returns full projects. Do not merge the shapes.
- Route groups carry `RequireAuthorization()`. Reads opt out with `AllowAnonymous()`.
- Rate limiting and CORS are configured in `Program.cs`, never per-slice. CORS origins come from
  configuration, never from `site.url`.

## Errors

- Every failure is a `Loom.Results.Error` with one of the six categories. Declare domain errors as
  types deriving from `Error`. Never invent a second error type or a seventh category.
- A `Result` failure means the request or refresh produced nothing. A `Diagnostic` means content was
  wrong and resolution carried on. A per-project fault is always a diagnostic.
- Never throw for an expected failure.
- Never ignore a returned `Result`. Write `_ = …` when the outcome genuinely does not matter.
- Never serialize a `Result`.
- Request shape is checked at the top of the handler, before any lookup, returning `Invalid`. There is
  no validation library: the whole request surface is two query strings and one path segment, which a
  package, a decorator and a reflective scan would not check better than an `if`.
- The decorator chain is declared once in `Program.cs`, logging outermost.

## Slices

- One file per operation: `Features/<Aggregate>/<Operation>.cs`.
- Namespace `Folio.Api.Features.<Aggregate>.<Operation>`. Boundary test 4 watches this root.
- `Features/<Aggregate>/_Shared.cs` is the only cross-slice sharing, within one aggregate.
- Over ~250 lines means the operation is doing too much. Split the operation.
- Endpoints adapt input and dispatch. They validate nothing and decide nothing.
- Request and response types are private to their slice. Two slices wanting the same shape get two
  types.

## Configuration

- Typed options only, one class per concern with a `const string SectionName`.
- `.ValidateDataAnnotations().ValidateOnStart()` on every one.
- `IConfiguration` is read only in the composition root. Do not re-bind a section by hand: resolve the
  validated `IOptions<T>`, or `ValidateOnStart` guards a value nothing uses.
- Secrets: user-secrets locally, environment variables when deployed. Never in `appsettings*.json`.

## Observability

- Injected `ILogger<T>`. `[LoggerMessage]` source-generated methods, not interpolated strings.
- Never log individual diagnostics. They are already data at `/v1/diagnostics`.
- Instrument `folio.snapshot.age`, refresh duration and outcome, GitHub calls and remaining budget,
  and diagnostic counts by severity.

## Testing

- Domain tests are driven by committed fixture directories laid out as a real `.folio`.
- Golden files compare the resolved shape and live in the fixture directory. A missing one fails;
  never write one from the test, or it approves itself. Diagnostics are asserted explicitly by `code`,
  never through a golden file.
- Every diagnostic code must be produced by at least one test. `DiagnosticCoverageTests` holds a
  readonly table of code → scenario and asserts the table covers the catalogue, so adding a code fails
  the build until a scenario produces it. A code the resolver cannot emit goes in `CoveredElsewhere`
  or `AwaitingSchemaV2`, naming the assembly that covers it, never left unlisted.
- `Folio.Ingestion.Tests` stub at `HttpMessageHandler`. Real Octokit, real deserialization, no network.
- `Folio.Api.Tests` go through HTTP with `IGitHubContentSource` stubbed, not with a seeded snapshot.
- No mocking library.
- Iterate a hashed collection only through an explicit order. Hash order is not stable across processes.
- TUnit, awaited assertions. `IsEquivalentTo` ignores order unless passed `CollectionOrdering.Matching`;
  pass it whenever the test name promises an order. Test names read as sentences. Fixtures are `internal`, and `sealed` unless
  `static`; there are no test base classes. No mutable static state.

## Comments

- Describe what the code is or constrains. Never narrate reasoning, alternatives, or history.
- Doc comments on public members: one sentence.
- Inline comments only for a non-obvious constraint, one line. Default to none.
- Never address a reader. Comments are documentation, not conversation.

## Verify

```bash
dotnet format
dotnet build
dotnet test
dotnet format --verify-no-changes
```

Run the fixing `dotnet format`, not verify-only. `.editorconfig` is the authority on style — change it
there, never by adding a style rule to this file.

## Unsettled

`UNDECIDED` in a comment or this file means stop and ask. Do not resolve one yourself.

Nothing is currently unsettled.
