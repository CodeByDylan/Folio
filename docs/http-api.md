# HTTP API

The contract Folio offers a portfolio frontend. Everything here is served from the in-memory
snapshot described in [architecture.md](architecture.md); no request reaches GitHub.

All routes are under `/v1`.

| Method | Route | Auth | Cache |
| --- | --- | --- | --- |
| `GET` | `/v1/site` | anonymous | `public, max-age=60` |
| `GET` | `/v1/pages/{slug}` | anonymous | `public, max-age=60` |
| `GET` | `/v1/projects` | anonymous | `public, max-age=60` |
| `GET` | `/v1/projects/{slug}` | anonymous | `public, max-age=60` |
| `GET` | `/v1/diagnostics` | anonymous | `no-store` |
| `POST` | `/v1/refresh` | API key | — |

`/alive` and `/ready` sit outside `/v1`; they are operational, not part of the contract, and the
caller-facing rate limit does not apply to them.

## Why the surface is split this way

`/projects` returns **summaries** and `/projects/{slug}` returns the **full project**. An index page
needs slug, name, tagline, tags, hero, `featured`, status, dates and derived GitHub metadata — and no
section bodies. Bodies are the bulk of the payload, and serving them to the index would make the
most-visited page the most expensive one. These are two response types on purpose; do not collapse
them.

`/site` and `/pages/{slug}` split along the same seam. `/site` is what every route needs — locale,
links, interface strings, and the page list that becomes the navigation. `/pages/{slug}` is what one
route needs: the sections that page renders, with their bodies. A frontend loads `/site` once per
navigation and one page payload per route, so visiting a project never downloads the Q&A.

The page list carries `slug`, `home`, `nav` and `navLabel`, and deliberately **not** the section ids
each page holds. Stating section identity in both endpoints would mean triaging every future section
field into "declaration" or "data" forever. `/site` answers what pages exist; `/pages/{slug}` answers
what one contains.

**Folio names no routes.** A page carries a slug and a `home` flag, not a URL. Which path a slug
becomes, and that `home` is served at `/`, is the frontend's decision — the same reason sibling
section links become anchors rather than routes.

`/diagnostics` is separate and unlocalized because it has a different audience — you, and CI — on a
different cadence. It is also the one endpoint that answers **without a snapshot**: when the very
first rebuild fails, every content endpoint returns `503` and this is the only place that says why.
Alongside the diagnostics it reports `lastRefresh`, the outcome of the most recent rebuild attempt,
which may be newer than the content being served. Read the two together: `succeeded` beside a recent
`builtAt` means the site is current; `abandoned-transient` means content is frozen at `builtAt`.

A bulk `GET /v1/portfolio` composing the three content endpoints is a sensible later addition for a
static site generator. It is additive; it is not the primary shape.

## Locale

Locale is an explicit `?locale=` query parameter. It is **not** negotiated from `Accept-Language`.

The frontend serves locale as a path prefix, so by the time it calls Folio the locale is a known fact
rather than a preference — and an API that states facts should not be performing content negotiation.
The practical consequences agree: `Vary: Accept-Language` wrecks CDN hit rates, an explicit parameter
gives clean per-locale cache keys, and `GET /v1/site?locale=nl` is debuggable by pasting it into a
browser.

| Request | Result |
| --- | --- |
| Omitted | Served as `site.default_locale`. |
| Declared (`nl`) | Served as `nl`. |
| Resolves by subtag truncation (`nl-BE` → declared `nl`) | Served as `nl`; the response echoes both `requestedLocale` and `locale`. |
| Resolves to nothing declared (`pt-BR`) | **`400`.** |

The last row is deliberate. Falling back would produce a response labelled `pt-BR` in which every
value carries `fallback: true` — a page claiming to be Portuguese and being entirely English. `400`
states the true fact, which is that the site does not publish Portuguese, and lets the frontend route
a 404.

The fallback chain is unaffected: it governs *values within a servable locale*, not whether a locale
is servable at all. Conflating the two is what produces the all-English "Portuguese" page.

Every response echoes both the requested and the resolved locale at the top level, so a truncation hit
is visible without reading per-value provenance.

## Provenance is inline; diagnostics are not

Two different things, kept apart:

- **Provenance** — per-value, always inline. What the frontend *renders* with, and how it decides
  whether to show a "not available in Dutch" notice.
- **Diagnostics** — the build report. What you and CI *audit* with. Available only at
  `/v1/diagnostics`.

Once provenance is inline, no diagnostic is needed to render anything. Everything left in that array
— a mistyped tag ID, a stripped `<div>`, an orphaned locale key — is something you fix in a
repository, and no visitor should download it. A development-only banner on the site fetches
`/v1/diagnostics` in development, which makes it opt-in by construction.

The consequence to accept: a project dropped by an `error` is simply absent from `/v1/projects`, with
no in-band explanation. Its absence is the fact; the reason is one fetch away.

**Content responses have no envelope.** `GET /v1/projects/{slug}` returns a project, not
`{ data, diagnostics }`. Consumers never unwrap.

## Provenance shape

Localized values are **flat**, with a sparse sidecar keyed by RFC 6901 JSON Pointer:

```json
{
  "slug": "folio",
  "name": "Folio",
  "tagline": "Portfolio's samengesteld uit de repos die ze beschrijven",
  "sections": [
    { "id": "overview",     "title": "Overzicht",    "body": "…", "source": "folio" },
    { "id": "architecture", "title": "Architecture", "body": "…", "source": "folio" }
  ],
  "provenance": {
    "/sections/1/title": { "locale": "en", "fallback": true },
    "/sections/1/body":  { "locale": "en", "fallback": true }
  }
}
```

A value absent from `provenance` came from the requested locale. On the default locale the map is
empty; on a well-translated locale it is a few entries. It is proportional to the problem, not to the
content.

Wrapping every localized value as `{ value, locale, fallback }` would tax every read with `.value`
forever, and would make a fully translated page pay the full wrapping cost to carry `fallback: false`
— the least interesting fact in the response. Reads are constant; provenance checks are rare.

Pointers also give `/v1/diagnostics` a join key, so a diagnostic can name the exact field it concerns.

## Section types

A site section on `/pages/{slug}` carries a `type`, and `type` is the discriminator: it says which
component renders the section, and which fields are meaningful. `prose` is the only type today, and
is what a section declaring no `type` becomes.

A `type` Folio does not know is **dropped at build time** with `schema.unknown_value`, so an authored
typo never reaches the wire. The case a client must still handle is the other direction — a Folio
release serving a type a deployed client predates. **Validate the union leniently.** A client that
rejects an unknown `type` loses the whole page, navigation included, rather than one section it could
have skipped.

## Section bodies

A section body is **rewritten markdown**, never HTML and never an AST. Rewriting is applied as
surgical edits to the authored source, so anything not listed below survives byte for byte — tables,
fenced blocks, footnotes and mermaid included. Markdig's roundtrip renderer is not used; it does not
support the GFM extensions and mangles tables.

| In the source | On the wire |
| --- | --- |
| The leading H1 | Removed, and returned as the section's `title` |
| A relative or root-absolute image path | An absolute `raw.githubusercontent.com` URL at the pinned SHA |
| A relative `.md` link matching a declared section | `#<section-id>` |
| An absolute URL under `site.url` | The site-relative path, with the prefix stripped |
| Raw HTML | Removed, with a `markdown.html_stripped` warning |
| Everything else | Untouched |

**Sibling section links become anchors, not routes.** An anchor is the only target the API can name
honestly: a route would require knowing the frontend's URL patterns, and the format design refuses to
carry those. It also reuses what `id` already is — the section's fragment.

The cost, stated plainly: this is correct when a project's sections render on one page, and wrong when
each section is its own page. In that layout the frontend rewrites `#overview` into its own route,
which it can do because it is the only party that knows what that route is.

Note the asymmetry with cross-project links, which authors write as absolute site URLs: there the API
only has to *recognize* a route, never invent one.

A root-relative link is itself the "internal" signal: there is no way to mark a link in markdown, and
a frontend router already treats a relative href as client-side navigation. That is what the path
prefix in `site.url` is stripped for.

## Caching

A response is a deterministic pure function of **(snapshot, endpoint, requested locale, resolved
locale)**, so that tuple is itself a valid strong validator. No body hashing. The endpoint must be in
the tag: without it one validator answers for every resource, and a client holding it gets `304` for a
project that does not exist. Both locales must be in it: `?locale=nl-BE` and `?locale=nl` can resolve
to the same content while echoing different `requestedLocale` values, so one tag cannot answer for both.

```
ETag: "a3f1c9e2:/v1/projects/folio:nl-BE:nl"
Last-Modified: Fri, 07 Aug 2026 09:14:22 GMT
Cache-Control: public, max-age=60
```

The snapshot id is a content hash taken once at build, and **it incorporates the application's own
version**. Identical inputs with new resolver code can produce different output, and an id derived
only from GitHub SHAs would serve a stale `304` to every client across a deploy that changed the
markdown rewriter.

`If-None-Match` is handled **before** any projection work, so revalidation costs a lookup and a string
compare.

**Build metadata never appears in the response body.** If `builtAt` were in the body, a refresh that
found nothing changed would still produce a different payload, the ETag would have to change, and
every client would re-download byte-identical content every 15 minutes. It goes in `Last-Modified`.

`Vary: Accept-Language` is never sent. Locale is a query parameter.

## Errors

Every non-2xx response is `ProblemDetails` (RFC 9457). There is no bespoke error envelope, and no
slice writes a status code — `Loom.Results` categories map through `ToHttpResult()` at one point.

| Category | Status | Cause |
| --- | --- | --- |
| `Invalid` | 400 | Locale resolves to nothing declared; malformed slug. |
| `Unauthorized` | 401 | Missing or wrong API key on `/v1/refresh`. |
| `NotFound` | 404 | No project with that slug in the current snapshot. |
| `Unavailable` | 503 | No snapshot has been built yet. |

Validation failures populate the `errors` extension rather than inventing a parallel shape.

## JSON conventions

- **camelCase**, serialized through a source-generated `JsonSerializerContext`.
- **Enums are strings, never integers** — `status`, `role`, `severity`, link `type`, relation `type`,
  tag `kind`. An integer enum breaks silently when a member is inserted.
- **Empty collections are always present as `[]`**, never `null` and never omitted. `media` is an
  ordered array carrying its `role`, not an object keyed by role, so ordering is guaranteed by the type.
- **Absent optional scalars are omitted**, not null.
- **`started` and `ended` are strings exactly as authored** (`"2026"` or `"2026-03"`). They are
  deliberately partial dates, and the string's length carries the precision a frontend needs to choose
  between "2026" and "March 2026".
- **Instants are ISO 8601 UTC.**
- **Relations return `{ type, target, label, generated }`** — a slug and a localized label, never a
  nested project. Nesting makes `companion` relations cyclic and forces an invented depth limit.
  `generated` says the edge was inverted from one declared on the target, so a frontend can present
  declared and inferred edges differently; the API does not decide that.
- **`metadata.releases` is newest first**, capped at the most recent 20, and excludes drafts. Each
  carries `prerelease` so the frontend decides whether to show one; the API does not filter on it.

OpenAPI is generated by the built-in `AddOpenApi()`; the document and the Scalar UI are mapped in
development only, so neither is served in production.

The document is also committed, at `docs/openapi.json`, because a consumer generating a client cannot
run this service to obtain it. `scripts/openapi.sh` regenerates it and CI fails when the committed copy
disagrees, so a change to a wire type cannot land without the contract moving with it. `servers` is
stripped: it would otherwise record whichever port the generating run used.

Two things the generator cannot read off a wire type are supplied by a schema transformer. A property
whose value is omitted when absent is not `required`, and a string property carrying an enum lists its
members — from the enum the property declares, so the list cannot drift from the one the mapper writes.
Every response type is named `Response` within its slice, so schema ids are qualified by their
operation; the document's schema namespace is flat and would otherwise collapse all five onto one.

## Refresh

```
POST /v1/refresh
X-Folio-Key: <key>
→ 200 OK
  { "snapshotId": "a3f1c9e2", "builtAt": "…", "projects": 3, "diagnostics": 7 }
```

**Synchronous, and `200` rather than `202`.** The rebuild is awaited, so by the time the response is
written the work is done — `202` would claim otherwise, and could not carry the failure statuses this
endpoint genuinely returns. The point of triggering by hand is "I just pushed, show me now", so the
caller wants the outcome: a GitHub Action gets a non-zero exit for free.

A trigger arriving mid-rebuild **joins** the running one and receives its result. A failure returns the
mapped category — `503` for a transient fault, `400` for a central config that cannot be read at all.

The summary body is a report on the operation, not portfolio content, which is why `builtAt` appears
here and never in a content response.

Anonymous reads, API key on refresh. The data is public by construction — every byte of it is readable
on github.com — so a token in front of the reads would protect nothing and cost the frontend a
secret-management story. The refresh caller set is one person and one GitHub Action, for which an
identity provider is disproportionate.

The key is compared with `CryptographicOperations.FixedTimeEquals`, sourced from user-secrets locally
and an environment variable when deployed.

Routes map into a group carrying `RequireAuthorization()`, with the four reads marked
`AllowAnonymous()` explicitly. That looks backwards when four of five endpoints are open, and it is
still correct: forgetting `AllowAnonymous` gives an obvious 401 in development, while forgetting to
protect an admin endpoint is silent and permanent.

Rate limiting (fixed window, per IP) and CORS are configured in `Program.cs`, never per-slice. The IP is
the socket peer, which behind a reverse proxy or CDN is the proxy, collapsing every client into one
partition; set `Api:TrustForwardedHeaders` when a trusted proxy fronts the service, and the limiter
then partitions on `X-Forwarded-For`. It is off by default, because trusting that header from a direct
client would let anyone forge their address and evade the limit. CORS origins come from configuration
and **not** from `site.url` — configuration arriving from a repository someone else could edit must not
decide security policy.
