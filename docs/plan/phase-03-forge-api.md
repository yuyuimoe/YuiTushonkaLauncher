# Phase 3 — Forge API client

**Branch:** `feature/phase-03-forge-api` · **Depends on:** Phase 2.

**Status:** ☑ done. Delivered: `IForgeClient`/`IForgeDownloader` contracts with domain-shaped models,
`SptForgeClient` over `filter[...]`/`query=`/`include=` (all endpoints above), `ForgeRateLimiter`
(burst + sustained sliding windows, header awareness), `ForgeRetryExecutor` (408/429/5xx/transport,
`Retry-After` as delta or date, jittered backoff, never retries 4xx), `ForgeResponseCache` honouring
`cache-control: max-age`, `ForgeDownloader` (redirects, `Range` resume, streaming SHA-256, 416
handling), `ForgeSptVersionCatalog` upserting `SptReleases`, and `AddLauncherForge()` in the
composition root. 146 tests, Core 97.0 %, Infrastructure 92.8 % coverage.

## Goal

A typed, rate-limited, cached client for `https://sp-mod.com/api/v0` covering every endpoint the app
needs, with DTOs modelled on the live responses.

## Tasks

- [x] `IForgeClient` (Core) + `SptForgeClient` (Infrastructure) (Core contract) + `SptForgeClient` (Infrastructure) over `HttpClient` created
      by `IHttpClientFactory`, `User-Agent: YuiTushonkaLauncher/<version> (+https://sp-mod.com)`.
- [x] DTOs (records, `System.Text.Json` source-gen where practical) for: mod list/detail, mod version,
      addon list, addon version, SPT version, category, updates, dependencies — with `links`/`meta`
      pagination envelope `ApiResponse<T>`.
- [x] Endpoints, exactly as verified:
      `GET /mods`, `GET /mod/{id}/versions`, `GET /mods/updates?mods=&spt_version=`,
      `GET /mods/dependencies?mods=&spt_version=`, `GET /addons`, `GET /addon/{id}/versions`,
      `GET /addons/dependencies`, `GET /mod-categories`, `GET /spt/versions`, `GET /ping`.
- [x] `mods=` building helper (chunked, 40 per request): `<id|guid>:<version>` joined by commas — validated by a test that
      mirrors the server's error message.
- [x] `ForgeRateLimiter`: `System.Threading.RateLimiting` sliding window seeded from
      `x-ratelimit-limit`, honouring `x-ratelimit-remaining` and `429`/`Retry-After`.
- [x] Response cache (in-memory with `max-age` TTL; SQLite catalog caching lives in the `SptReleases` table): in-memory (per session) + SQLite (survives restart) keyed by request URI,
      respecting `cache-control: max-age` (900 s) and a manual force-refresh path.
- [x] Retry with jittered exponential backoff for `408/429/5xx`; no retry on 4xx validation errors.
- [x] `ForgeSptVersionCatalog`: sync `/spt/versions` into the `SptVersions` table (used by the matrix in
      [D12](../plan/04-decisions.md#d12-spt-and-tarkov-compatibility-matrix)).
- [x] `IForgeClient.CheckUpdatesAsync` (the four buckets): given installed `(guid, version)` pairs + instance SPT version, call
      `/mods/updates` and map the four buckets (`updates`, `blocked_updates`, `up_to_date`,
      `incompatible_with_spt`) into domain results.
- [ ] `DependencyResolver` (moved to Phase 6 — it needs the local `IModMetadata` reader to combine both sources): Forge `/mods/dependencies` + local `IModMetadata` constraints, producing an
      install plan (topological order, conflicts, optional deps).
- [x] Download client: follows `307` to third-party hosts, tolerates missing `Content-Length`,
      supports `Range` resume, computes SHA-256 while streaming into the cache.

## Acceptance criteria

- WireMock-backed tests (recorded fixtures from the real API) cover: pagination, `guids=` filter,
  the `mods=`/`spt_version=` pair for updates and dependencies, a 404, a 400 validation error, a 429
  and a redirect chain download.
- A recorded integration test (opt-in, `[Trait("Live", "Forge")]`) fetches `/ping`, `/mods?per_page=1`
  and `/spt/versions` against the real API.
- Rate limiter test proves no more than the permitted number of requests leave in a window, and that
  a `429` with `Retry-After` delays the next call.

## Notes

- **Discovery correction:** `?guids=`, `?ids=`, `?search=` and `?category_id=` are silently ignored by
  the live API. Everything filters through `filter[...]` (batches are comma-separated) and free text
  goes through `query=`. `?filter[guids]=` returns a 400 listing the valid filters. Details in
  [03-verified-facts](../plan/03-verified-facts.md#filtering-is-filter-and-the-obvious-names-are-silently-ignored).
- `content_length` from the API is advisory: the redirect target may disagree. Trust the bytes.
- The API exposes no content hash — the local SHA-256 is the only integrity anchor.
- Do not invent endpoints: `/mods/{id}/versions` 404s; it is `/mod/{id}/versions`.
- The rate limiter defaults sit under the advertised 300/min on purpose; retries consume permits too,
  because the limiter wraps every attempt.
