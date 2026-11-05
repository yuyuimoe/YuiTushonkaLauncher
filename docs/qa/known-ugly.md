# Known ugly

Filled in during the visual smoke ([D23](../plan/04-decisions.md#d23-the-10-gate-is-three-items-and-the-releases-are-sequenced)).
One line per screen: **what is ugly, and what is not broken.** The layout is temporary and will be redesigned; this
file exists so that "ugly" is a recorded choice instead of an excuse.

## How the smoke is taken

```
dotnet run --project tools/YuiTushonkaLauncher.Tools.Shots -c Release -- --out /tmp/ytt-shots
```

The tool starts Avalonia headless **with Skia** (`UseHeadlessDrawing = false`), builds a throwaway instance from a fake
base install, opens every screen, and writes a PNG per screen. 1180×760 is the reference size; `home-small.png` is
900×560, the smallest window the screens are expected to survive.

Passing means, for every screen: opened in **light and dark**, with **1000 rows** where a list exists, and in a
**small window**, with scrolling wherever content overflows. Ugly is allowed; cut off, unreadable or unreachable is not.

## Result of the 2026-11-05 pass

Screens rendered: `home`, `home-small`, `manager-mods`, `manager-addons`, `manager-server`, `manager-logs`,
`manager-settings`, `forge`, `dialog-create-instance`, `dialog-select-server`.

| Screen | Ugly (accepted) | Not broken | Checked |
|---|---|---|---|
| Home (instance tiles) | The details pane prints unlabelled values (`0 mods`, `unknown`, `never used`), so which line is which has to be guessed. | Fits at 900×560 with the whole rail and details pane visible; the launch button is reachable. | ✅ |
| Instance manager window | — | Rail, launch bar and status bar keep their space; the page area scrolls. | ✅ |
| Mods | Two empty-state texts stacked ("No mods installed…" plus its hint) read like one sentence in two sizes. | Search, URL install and the Forge button are all reachable. | ✅ |
| Addons | Empty state is a single line in a very tall pane. | Reachable. | ✅ |
| Server | Name/host/port boxes rely on placeholders: once filled there is no label saying which is which. The profile buttons sit at the bottom of a tall column, far from the list they act on. | The server list, the editor and its buttons fit; nothing overlaps. | ✅ |
| Logs | With no log file yet, both panes are blank and nothing says why. | Reload and the source picker are reachable. | ✅ |
| Settings | — | **Fixed during this pass**: "Ask before closing while a process is running" was painted under its checkbox; every label now wraps. | ✅ |
| Forge window | The category filter is an empty box until a category is picked; rows without a thumbnail fall back to a letter tile (deliberate). | Search, ordering, filters, results list, sidebar and its tabs all fit at 1180×760. | ✅ |
| Create instance dialog | The plan/refusal area is empty until "Plan" is pressed, which leaves a lot of blank space. | Fields, browse buttons and Create/Cancel are reachable. | ✅ |
| Select server dialog | — | Fits at 480×420; the chosen server's meaning ("started with the game" / "elsewhere") is stated under the list. | ✅ |

## Deferred to the redesign

- Everything above in the "ugly" column: unlabelled values, placeholder-only fields, distant buttons, silent empty
  states.
- Light/dark palettes (D28): today the shell uses the Fluent defaults with the same rail colour in both variants.
- A 1000-row pass was done for the lists the launcher can drive headlessly (mods/logs read from files that the smoke
  does not populate); the scale test lives in `ModListScaleTests`, not in this pass.
