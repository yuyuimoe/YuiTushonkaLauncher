# Phase 12 — User interface

**Branch:** `feature/phase-12-ui` · **Depends on:** Phases 4–11 (features exist behind interfaces).

## Goal

All screens, navigation, theming and localization — built once, on top of services that already work.

## Tasks

- [x] Shell: single main window, left rail (Instances, Mods, Server, Logs, Settings), per-instance
      workspace with tabs, status bar (server state, patch state, active downloads).
- [x] Screens (the Forge browser and the dialogs carry the data the phase asks for):
      - instance list (cards: name, SPT/Tarkov version, mod count, patch state, last launch);
      - instance detail — Mods / Addons / Configs / Patch / Logs / Server;
      - Forge browser — search, filters (category, SPT version, Fika compatibility, featured),
        detail pane with version list and dependencies;
      - dialogs — create instance, adopt existing install (migration plan), dependency resolution,
        export/import, wipe confirmation, update prompt;
      - notifications — toasts for new mod versions and finished downloads, plus an inbox.
- [x] Replace the reflection `ViewLocator` with explicit view registration (prepares trimming,
      [D14](../plan/04-decisions.md#d14-no-native-aot)).
- [x] MVVM discipline: `CommunityToolkit.Mvvm` source generators, compiled bindings
      (`AvaloniaUseCompiledBindingsByDefault`), no code-behind beyond view plumbing, no service
      resolved from the container inside a control.
- [x] Async UX: every long operation shows progress (`IProgress<OperationProgress>`), is cancellable,
      and disables the actions that would conflict.
- [x] Theming: light/dark following the OS, one accent, density suitable for dense mod lists; large
      lists virtualized (`VirtualizingStackPanel`, `ItemsRepeater` where needed) so 1000+ mod entries
      stay responsive.
- [x] i18n from the first screen ([D11](../plan/04-decisions.md#d11-i18n-and-ui-tests-from-day-one)):
      `Assets/Locales/{en,pt-BR}.json` with a key-based lookup (`ILocalizer`), no literal strings in
      XAML, format strings kept out of code. Schema compatible with SPT's `Locales/*.json` shape.
- [x] Accessibility basics: keyboard navigation for the main flows, accessible names on icon buttons,
      focus visible, high-contrast-safe colours.
- [x] Empty/error states designed deliberately (no blank panels, no raw exception text).

## Acceptance criteria

- Navigation covers every feature delivered by Phases 4–11 with no dead ends. — asserted by
  `ShellNavigationTests` (every section shows its page, every page builds a real view).
- A UI test (`Avalonia.Headless`) drives: create instance → install mod → launch → see log line,
  asserting on view-model state. — `FlowTests.CreateInstallLaunchAndSeeALogLine`.
- Launching with `--lang pt-BR` shows no English literal in the main flows (spot-checked by a test that
  scans XAML/VM for hard-coded user-facing strings). — `LocalizationTests`: parity, placeholders, a pt-BR
  shell, the resolution table and a scan that fails on any literal `Text`/`Content`/`Title`.
- A 1000-entry mod list scrolls without frame drops in the headless/benchmark harness or, at minimum,
  without allocating per-frame collections. — `ModListScaleTests`: 1000 rows load once, filtering reuses the
  same row objects (reference equality), fifty filters stay under two seconds.

## Notes

- Screen work starts only after the underlying service is usable — otherwise the UI becomes the test
  harness and the design drifts.
- Localized strings include error messages; log messages stay English and untranslated (they are
  diagnostics).

## Delivered, and what this phase does not do

Delivered: `ShellViewModel` with a rail, a launch panel and a status bar; one page per feature
(instances, mods, patch, Forge, server, logs, settings); `IDialogService` with confirm/create/picker
windows and a fake for the tests; `ILocalizer` + `JsonLocalizer` (embedded `Assets/Locales/{en,pt-BR}.json`,
SPT's nested shape, dotted keys, fallback, `pt`/`pt_BR`/`pt-BR` all resolving); explicit view templates in
`App.axaml` (the reflection `ViewLocator` is gone); theme brushes and a light/dark switch following the OS;
`--lang` for the first window; the already-running window localised too. **1137 tests**, Release
warning-free, CSharpier clean.

Not this phase:
- The window controls are exercised headlessly; nobody has looked at the pixels. A visual pass is a manual
  step before 1.0 (open question 26).
- Virtualisation is asserted through row reuse, not frame timing: a benchmark harness needs a real render
  loop (phase 13 owns it).
- Drag and drop, keyboard shortcuts beyond the Fluent defaults, and a tray icon are not here.
- `IOLogService`'s sources are listed in full (five server dailies); the window shows the newest by default,
  which is the phase-12 reading of the phase-11 note.
