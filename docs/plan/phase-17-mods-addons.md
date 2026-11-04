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
