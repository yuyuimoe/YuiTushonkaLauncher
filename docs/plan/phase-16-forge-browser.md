# Phase 16 — The Forge browser

## Verified against the live API (`https://sp-mod.com/api/v0`, no auth)

* `sort=` accepts exactly `name, downloads, favourites_count, endorsements_count, featured, created_at, updated_at,
  published_at`, with a leading `-` for descending. Anything else is a `400` that lists the set.
* `filter[...]` accepts `created_between`, `updated_between`, `published_between` as `YYYY-MM-DD,YYYY-MM-DD`
  (any other shape is silently ignored). There is no per-window endorsement count: a windowed "most endorsed" is
  *mods published inside the window, ranked by total endorsements*, and the UI says so.
* A list item carries `thumbnail` (may be `""`), `downloads`, `favourites_count`, `endorsements_count`, `featured`,
  `teaser`, `guid`, `owner`, `detail_url`, `updated_at`, `published_at` and, with `include=versions`, `versions[]`.
  It carries **no** `description`, `license`, `source_code` or `virustotal`.
* `/mod/{id}` adds `description` (HTML) and nothing else that the list lacks.
* `license`, `source code` and `VirusTotal` are only on the HTML page (`/mod/{id}/{slug}`): three `<li>` blocks with
  `h3` labels, the licence as a `choosealicense.com` link with a `title` of the licence name, the source as an `a`,
  and VirusTotal as `virustotal.com/gui/file/<sha256>` links.

## 16.1 Ordering

`ForgeSort` enum + `ForgeSortOption` list in Core: Featured (`featured`), Most endorsed / last 7 days, last 30 days,
all time (`-endorsements_count` + `published_between`), Newest (`-published_at`), Recently updated (`-updated_at`).
A combo in the toolbar; the choice survives page changes and is part of the query cache key.

## 16.2 Versions, filtered to the instance

The browser always knows the active instance's `SptVersion`. A mod is listed when at least one of its versions is
compatible (`VersionRange` over `spt_version_constraint`, already used by the update service); the version combo shows
only the compatible versions, newest first, and the install button is disabled with a reason when none is. The search
also passes `filter[spt_version]=` so the server does the same filtering.

## 16.3 Thumbnails

`IThumbnailCache`: the `thumbnail` URL is downloaded once into `<data>/cache/thumbs/<sha256-of-url>.<ext>`, capped at
512 KiB and 200 files (oldest evicted), served to the UI as a local path; a placeholder letter tile when the URL is
empty or the download fails. No image is decoded off the UI thread; the cache hands out paths only.

## 16.4 The detail sidebar

Order, as asked:

1. **Title** with a button that opens `detail_url` in the browser (`IUrlOpener`).
2. **Details**: GUID, Forge id, licence, downloads, endorsements, updated.
3. **Short description** (`teaser`).
4. **Long description** (`description`, HTML reduced to plain text lines; images and links stripped).
5. **Source-code links**, one per link.
6. **VirusTotal links**, one per hash, with the scan ratio when the page shows one.

3 and 4 come from the list/detail API; 5 and 6 come from a best-effort page scrape (`ForgeModPageReader`) cached per
mod for a day. When the page cannot be read the section says "unavailable" — nothing else fails.

## 16.5 Tabs, selection and history

Three tabs: **Details**, **Dependencies**, **Dependents**.

* Dependencies come from `/mods/dependencies?mods=<id>:<version>&spt_version=<version>` (needs both parameters, verified).
* Dependents come from the same call over every installed mod, intersected with the selected mod — no new endpoint.
* Double-clicking a row in either tab selects that mod, which is a navigation: a `ForgeHistory` with back/forward and
  two buttons in the sidebar header, plus `Alt+Left`/`Alt+Right`. Selecting inside a tab keeps the tab.
* Each row shows name, version, and whether it is installed in the active instance.

## Acceptance

* The nine sort options produce nine different orderings against the live API (checked by a Live trait test).
* A mod with no compatible version cannot be installed and says why.
* Thumbnails appear for mods that have one, placeholders for those that do not, and the cache does not grow past its cap.
* The sidebar shows the six sections in order; dependencies and dependents are navigable with history.
* Release build warning-free, CSharpier clean, tests green.

## What was built

* `ForgeSort` / `ForgeSorts` in Core: seven orderings, each mapped to the sort expression and date window the API
  accepts. `ForgeModQuery` grew `PublishedFrom`/`PublishedTo`, serialised as `filter[published_between]=YYYY-MM-DD,YYYY-MM-DD`.
* `ForgeVersionCompatibility`: a version fits when the API's resolved `SupportedSptVersions` says so, otherwise when the
  constraint is satisfied. The browser offers only fitting versions and explains the absence of any.
* `ThumbnailCache`: disk cache under `<data>/cache/thumbnails`, 512 KiB and 200 files, least recently used evicted,
  one download per URL. **The file host answers 403 to a request without a `Referer` from the site and 200 with one**
  (measured against `files.sp-mod.com`), so the request carries it.
* `ForgePageReader`: licence, source code and VirusTotal links from the three labelled sections of the mod page; a
  forgiving parser, tested against a trimmed copy of a real page.
* `ForgeDescription`: the HTML description reduced to lines a text block can show.
* `NavigationHistory<T>`: back and forward, with the rule that a new selection drops the forward stack.
* The page: sort and category pickers, results with thumbnails and letter tiles, a sidebar with the six sections in the
  order the user asked for, three tabs (details, dependencies, dependents), back/forward, double-click to open, and an
  install button that is off — with the reason — when no version fits the instance.

## Verified against the live catalogue and the live page

* Four orderings produced four different result lists (featured, windowed endorsements, newest, recently updated).
* 47 of the top 50 mods by downloads have a thumbnail; they download and decode (23 KB PNGs measured).
* BigBrain publishes 12 versions, of which **1** is compatible with SPT 4.1.6 — the filter is not cosmetic.
* Its page yields `MIT License`, `https://choosealicense.com/licenses/mit/`, one source-code link and one VirusTotal
  link; the API yields none of those.
* The API rejects `spt_version=0.0.0` with `VALIDATION_FAILED`; with no instance open the sidebar therefore shows no
  dependencies instead of asking the wrong question.

## Still open

* Endorsement windows cannot be exact: the API reports a total per mod, not votes over time. The label says
  "most endorsed · last 7 days" and the tooltip in `forge.sortEndorsedWeek` explains the approximation.
* The sidebar's long description is plain text; links inside it are not clickable.

## Follow-up from the first real run (screenshot)

* **The sidebar scrolls.** Its `ScrollViewer` sat in the `Auto` row of a `Grid RowDefinitions="Auto,*"`, so it grew to
  its content and the window simply cut it off. Measured with a headless window at 1000×620 and a deliberately long
  description: before, extent = viewport = 3108 (nothing scrolls); after, extent 3108 against a viewport of 526.
* **Tabs are lazy.** Selecting a mod fetches the details tab only (versions, description, licence, source, scans).
  Dependencies are fetched when the dependencies tab is first opened for that mod, dependents likewise, and switching
  back and forth costs nothing. Tests count the fake client's calls: one dependency lookup after the tab is opened,
  still one after leaving and returning, none at all when only dependents is opened.
* **Opening the browser searches.** `LoadAsync` loads the categories and, when the list is empty, runs the current
  ordering once — the window opens with results instead of an empty page waiting for a keystroke. `App.OpenForge`
  calls it when the window is shown, so closing and reopening keeps the last list (it only searches when empty).
