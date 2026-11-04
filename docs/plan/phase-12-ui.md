# Phase 12 — User interface

**Branch:** `feature/phase-12-ui` · **Depends on:** Phases 4–11 (features exist behind interfaces).

## Goal

All screens, navigation, theming and localization — built once, on top of services that already work.

## Tasks

- [ ] Shell: single main window, left rail (Instances, Mods, Server, Logs, Settings), per-instance
      workspace with tabs, status bar (server state, patch state, active downloads).
- [ ] Screens:
      - instance list (cards: name, SPT/Tarkov version, mod count, patch state, last launch);
      - instance detail — Mods / Addons / Configs / Patch / Logs / Server;
      - Forge browser — search, filters (category, SPT version, Fika compatibility, featured),
        detail pane with version list and dependencies;
      - dialogs — create instance, adopt existing install (migration plan), dependency resolution,
        export/import, wipe confirmation, update prompt;
      - notifications — toasts for new mod versions and finished downloads, plus an inbox.
- [ ] Replace the reflection `ViewLocator` with explicit view registration (prepares trimming,
      [D14](../plan/04-decisions.md#d14-no-native-aot)).
- [ ] MVVM discipline: `CommunityToolkit.Mvvm` source generators, compiled bindings
      (`AvaloniaUseCompiledBindingsByDefault`), no code-behind beyond view plumbing, no service
      resolved from the container inside a control.
- [ ] Async UX: every long operation shows progress (`IProgress<OperationProgress>`), is cancellable,
      and disables the actions that would conflict.
- [ ] Theming: light/dark following the OS, one accent, density suitable for dense mod lists; large
      lists virtualized (`VirtualizingStackPanel`, `ItemsRepeater` where needed) so 1000+ mod entries
      stay responsive.
- [ ] i18n from the first screen ([D11](../plan/04-decisions.md#d11-i18n-and-ui-tests-from-day-one)):
      `Assets/Locales/{en,pt-BR}.json` with a key-based lookup (`ILocalizer`), no literal strings in
      XAML, format strings kept out of code. Schema compatible with SPT's `Locales/*.json` shape.
- [ ] Accessibility basics: keyboard navigation for the main flows, accessible names on icon buttons,
      focus visible, high-contrast-safe colours.
- [ ] Empty/error states designed deliberately (no blank panels, no raw exception text).

## Acceptance criteria

- Navigation covers every feature delivered by Phases 4–11 with no dead ends.
- A UI test (`Avalonia.Headless.XUnit`) drives: create instance → install mod → launch → see log line,
  asserting on view-model state.
- Launching with `--lang pt-BR` shows no English literal in the main flows (spot-checked by a test that
  scans XAML/VM for hard-coded user-facing strings).
- A 1000-entry mod list scrolls without frame drops in the headless/benchmark harness or, at minimum,
  without allocating per-frame collections.

## Notes

- Screen work starts only after the underlying service is usable — otherwise the UI becomes the test
  harness and the design drifts.
- Localized strings include error messages; log messages stay English and untranslated (they are
  diagnostics).
