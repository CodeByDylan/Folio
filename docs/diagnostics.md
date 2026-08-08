# Diagnostics

A diagnostic is a structured record of something the resolver found. Diagnostics are **data**, served
at `GET /v1/diagnostics` — never log lines, and never mixed into content responses.

## Shape

```json
{
  "code": "locale.key_missing",
  "severity": "info",
  "project": "folio",
  "file": ".folio/locales/nl.toml",
  "position": { "line": 12, "column": 1 },
  "pointer": "/tagline",
  "message": "Missing key 'project.tagline'; fell back to 'en'"
}
```

| Field | Required | Notes |
| --- | --- | --- |
| `code` | yes | Stable, machine-readable. The catalogue below is the complete set. |
| `severity` | yes | `error` \| `warning` \| `info` |
| `project` | no | Slug. Absent for central and portfolio-wide diagnostics. |
| `file` | no | Repo-relative path. Absent where no single file is responsible. |
| `position` | no | Line and column, 1-based. Present wherever a parser can supply one. |
| `pointer` | no | RFC 6901 pointer into the response the diagnostic concerns. |
| `message` | yes | Human-readable, and **free to be reworded**. Never discriminate on it. |

**`code` is the contract.** A CI check that fails on "any unknown tag" matches `tags.unknown`, not a
prose string. Codes are lowercase, dot-separated, `area.condition`, and once published they are not
renamed.

`position` is why Tomlyn lives in `Folio.Domain` rather than behind a neutral document tree; discarding
source positions at a layer boundary would waste it. `pointer` is the join key between a diagnostic and
the field it concerns, reusing the same addressing as the provenance sidecar.

## Severities

| Severity | Meaning |
| --- | --- |
| `error` | Something was dropped. A project, a file, or the whole refresh. |
| `warning` | Something was ignored or substituted, and you probably want to know. |
| `info` | Fallbacks and coverage. Expected, but recorded. |

## Failure isolation

A **content fault** — deterministic, caused by what is in a repository — drops one project and reports
why. Every other project resolves.

A **transient fault** — a 5xx, a timeout, an exhausted rate-limit budget — abandons the whole refresh.
The previous snapshot keeps serving. A network blip must never remove a project from the live site with
a diagnostic blaming the repository.

Central-config errors are fatal to **that refresh**, not to the service.

## Ordering

Central diagnostics first, then per-project in `projects.toml` array order, then emission order within
a project. Deterministic, so the report diffs cleanly between builds.

## Filtering

`/v1/diagnostics` supports `?severity=` and `?project=`, and reports aggregate counts for every
severity at the top level — including zero, so a CI check reading `counts.error` never finds a missing
key. A CI check wants "are there any errors?", not the full report; a development banner
wants a count, not a client-side reduction.

It also reports `lastRefresh`, the outcome of the most recent rebuild attempt, which may be newer than
the snapshot being served. That is the pair worth reading together: a `succeeded` outcome beside a
recent `builtAt` means the site is current, while an `abandoned-transient` outcome means the content
is frozen at `builtAt` and the reason is in this report.

**This endpoint answers even when no snapshot exists.** When the very first rebuild fails there is
nothing to serve and every content endpoint returns 503, so this is the only place that says why.

There is no pagination. A build report is useful whole.

`?severity=` accepts `info`, `warning` or `error`; anything else is `Invalid` → 400 rather than an
empty list, so a typo in a CI check fails loudly. `?project=` must be a well-formed slug, with the
same 400 for anything else; it matches a slug — a project that could not be fetched is stamped with
the slug derived from its directory, since it has no `project.toml` to declare one.

## Catalogue

### `central.*` — the central config, fatal to the refresh

| Code | Severity | Behaviour |
| --- | --- | --- |
| `central.missing` | error | The central repository or a config file it must hold is absent; refresh abandoned |
| `central.unparseable` | error | Refresh abandoned |
| `central.default_locale_undeclared` | error | Refresh abandoned |
| `central.duplicate_slug` | error | The later project is dropped; the first keeps the identity |

### `schema.*` — versioning

| Code | Severity | Behaviour |
| --- | --- | --- |
| `schema.version_missing` | warning | Assumed `1` |
| `schema.version_unsupported_high` | error | File refused; project dropped |
| `schema.version_unsupported_low` | error | Below the N−1 window; project dropped |
| `schema.unknown_key` | warning | An unrecognized key, table or table array; ignored |
| `schema.unknown_value` | warning | Unknown enum value dropped |
| `schema.invalid_value` | warning | A value malformed for its key; dropped |
| `schema.version_lagging` | info | One aggregate diagnostic naming every project on an older version |

### `project.*` — per-project resolution

| Code | Severity | Behaviour |
| --- | --- | --- |
| `project.unparseable` | error | Project dropped |
| `project.not_found` | error | Repo or `path` returned 404; project dropped |
| `project.slug_invalid` | error | An authored slug is malformed, or none could be derived; project dropped |
| `project.tree_truncated` | error | The repo tree exceeded GitHub's listing limit; project dropped rather than resolved from a partial file set |
| `project.no_sections` | info | Legitimate — metadata, hero, links and tags make a good entry |
| `project.readme_used` | info | Once per project; the section carries `source: "readme"` |
| `project.readme_ignored` | warning | `use_readme` set while sections exist |

### `locale.*` — localization

| Code | Severity | Behaviour |
| --- | --- | --- |
| `locale.key_missing` | info | Falls back |
| `locale.key_orphaned` | warning | Ignored |
| `locale.truncated` | info | Resolved by subtag truncation (`nl-BE` → `nl`), for strings and section files alike |
| `locale.content_dir_undeclared` | error | Content under `content/` that is not inside a declared locale directory; the project is dropped, or in the central repo the content is ignored |
| `locale.file_undeclared` | warning | A `locales/` file naming no declared locale in canonical form; the file is ignored |
| `locale.unparseable` | error | A `locales/` file that is not valid TOML; the file is ignored and its keys fall back |
| `locale.empty` | warning | A declared non-default locale with zero content anywhere. One per locale, not per string |

### `section.*` — authored prose

| Code | Severity | Behaviour |
| --- | --- | --- |
| `section.missing_all_locales` | warning | No file in any locale; the section is dropped everywhere |
| `section.missing_chain` | warning | A file exists somewhere, but not in one locale's fallback chain; dropped from that locale |
| `section.missing_locale` | info | Falls back |
| `section.empty` | warning | Empty section with a fallback title — not silently dropped |
| `section.body_h1` | warning | A second H1 after the title; left as-is |

### `markdown.*` — content rewriting

| Code | Severity | Behaviour |
| --- | --- | --- |
| `markdown.unparseable` | warning | A section's markdown could not be parsed; the section is dropped from that locale |
| `markdown.html_stripped` | warning | Raw HTML removed at parse |
| `markdown.link_unresolved` | warning | A relative link that escapes the repo or matches no section; left unrewritten |
| `markdown.fragment_dropped` | warning | A link to a sibling section carried a fragment; the section anchor replaced it |
| `markdown.host_near_match` | info | A link whose host differs from `site.url` only by `www.`; treated as external |

### `tags.*` and `relations.*` — vocabularies

| Code | Severity | Behaviour |
| --- | --- | --- |
| `tags.unknown` | warning | Tag ID not in the central vocabulary; dropped |
| `relations.target_unknown` | warning | Relation target not in the portfolio; dropped |

### `media.*`

| Code | Severity | Behaviour |
| --- | --- | --- |
| `media.not_found` | warning | Not present at the pinned SHA; media omitted |
| `media.dimensions_unreadable` | warning | The header could not be read or recognized; URL returned without dimensions |
| `media.dimensions_external` | info | Externally hosted, so not probed; URL returned without dimensions |

### `portfolio.*` and `refresh.*`

| Code | Severity | Behaviour |
| --- | --- | --- |
| `portfolio.empty` | warning | Zero projects. Almost certainly misconfiguration, but day one genuinely is zero |
| `refresh.abandoned` | error | A transient fault; previous snapshot still serving |
| `refresh.rate_limit_insufficient` | error | Not enough budget to complete; abandoned before starting |

## Every code must be reachable

`DiagnosticCoverageTests` holds a readonly table of code → scenario, each scenario a real portfolio
resolved end to end. Three assertions run over it: every scenario produces its code, the table covers
the catalogue, and no scenario names a code that has left the catalogue. It is self-enforcing in both
directions — add a code and the build fails until a scenario produces it; remove one and the build
fails until its scenario goes too.

The table is readonly data rather than a collector, so nothing is shared between parallel tests.

Two groups are accounted for rather than covered, each in a named list with a reason:

| List | Codes | Why |
| --- | --- | --- |
| `CoveredElsewhere` | `project.not_found`, `project.tree_truncated` | Produced while fetching. Covered in `Folio.Ingestion.Tests`. |
| `CoveredElsewhere` | `refresh.abandoned`, `refresh.rate_limit_insufficient` | Produced by the API when a rebuild is abandoned. Covered in `Folio.Api.Tests`. |
| `AwaitingSchemaV2` | `schema.version_lagging` | Implemented, but nothing can lag while only version 1 exists. |

Adding a code means adding a scenario that produces it, or a line in one of those lists saying why it
cannot have one.
