# Phase 17 — Installed mods and addons

## 17.1 One place for mods

The Forge browser grows a tab **Installed** and the standalone Mods page goes away as a destination. The manager
window's rail becomes Instances-independent: Mods and Addons are reached from buttons that open the browser window
("Install mods", "Install addons"), all in the same window, each tab a view of the same browser.

* **Installed** tab: the list from the Prism reference — Enable, Image, Name, Version, Last modified, Provider — with
  the right-hand action pane (Download mods, Check for updates, Change version, Add file, Remove, Enable/Disable,
  View homepage, Export list, View folder, View configs).
* The tab reads `IInstanceService.GetAsync` (mods + files) and `IForgeClient` for provider and thumbnail; enable and
  disable move files between `BepInEx/plugins` and the launcher's disabled store through the existing install/uninstall
  services, never by editing a list.
* Search box and column sorting client-side over the loaded rows (no paging).

## 17.2 Addons

New page in the manager window, **separate from mods**:

* Lists `InstalledMod` rows that have a `ParentModGuid` (D10), grouped by parent mod.
* "Install addons" opens the Forge browser in **addon mode**: the query is `/mods?filter[...]` over mods that are
  addons? The API has no addon filter, so addon mode = a mod picked first (the parent), then its addons are listed —
  and a mod that has no addons says so. The chosen addon is installed with `ParentModGuid` set.
* Requirement colouring: the parent's presence is checked against the active instance; the requirement line is green
  with the installed version, or red with "requires X, not installed" and the install button disabled.

## Acceptance

* The installed list matches Prism's columns and actions, and toggling a mod actually moves its files.
* Addons are listed only under their parent, the requirement colour reflects reality, and an addon cannot be installed
  without its parent.
* Release build warning-free, CSharpier clean, tests green.

## What was built

* **Browsing moved out of the rail.** The rail is now Mods, Addons, Server, Logs, Settings — everything that belongs to
  the open instance. The catalogue is a window (`ForgeWindow`), opened by "Browse & install mods" on the mods page and
  by "Install addons" on the addons page.
* **Addons page**: the instance's `InstalledMod` rows that carry a `ParentModGuid`, grouped by parent and showing the
  parent's name and version. Its own empty state, because "no addons" is the normal state of a fresh instance.
* **Addon mode in the browser**: choose a parent from the results ("Add-ons for this mod"), which lists that parent's
  addons with their requirement in green or red, and an install button that is off unless the parent is installed and
  the addon's `mod_version_constraint` admits the parent's version.
* Installing an addon goes through `IAddonService`, which owns the parent check and records the files under the parent.
* Tests: seven, covering grouping, the empty state, both window entry points, a missing parent, an installed parent and
  the refusal that must not call the service.

## Verified against the live API

* `GET /addons` lists them (`total 140`) and **`filter[mod_id]` works** — an earlier local filter was wrong twice over:
  the server supports it, and filtering a 25-item page locally silently hid most addons.
* `include=versions` makes each addon carry its versions (`mod_version_constraint` included), so listing addons costs
  one request, not one per addon. Measured: parent mod 1619 has one addon, `Mip's Preset 0.1.0`, which needs mod `4.2.2`.
* The addons endpoint's valid filters are `id, name, slug, teaser, mod_id, contains_ads, is_detached,
  created_between, updated_between, published_between`.

## Still open

* An addon's own versions are not filtered by SPT: an addon constrains its *mod's* version, not the SPT release, so the
  parent's installed version is the only thing checked.
* Uninstalling an addon from the page is not wired to a button yet; the page lists, it does not remove.
