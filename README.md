# Vending Idle

An idle/clicker prototype where the play surface is a vending machine's stock grid.
C# + MonoGame, no engine.

The cabinet stands in a room, lit from behind, casting a shadow on the floor. Each
compartment shows one drink -- a rectangle standing in for a drink sprite -- with a
stacked deck of silhouettes behind it as a fullness cue and the exact count printed
underneath. Vending drops that drink out of its slot so it tumbles down into the
delivery tray and settles there. The till readout is an LED panel on the machine's
own fascia rather than a HUD, and RESTOCK and SAVE are buttons on its service column
beside the keypad.

Nothing else is on screen by default. Upgrades and the selected slot live in menus
that slide in from the screen edges when you call them, by clicking an edge tab or
touching a compartment, and slide back out when you are done.

You start with one slot in the bottom-left corner. Clicking dispenses a bottle from
the next stocked slot in sequence; when everything is empty it pays out spare change
instead. Stock is real and finite, so it has to be restocked, and "customers" are
auto-clickers that drain that same stock. More customers means faster drain, which is
what makes restock automation something you actually want rather than a convenience.

## Running it

Requires the **.NET 8 SDK**. MonoGame's content pipeline is a local dotnet tool, so
restore it once:

```bash
dotnet tool restore          # installs dotnet-mgcb from .config/dotnet-tools.json
dotnet run --project src/VendingIdle
```

The economy has a headless test suite that needs no window or GPU:

```bash
dotnet run --project tools/VendingIdle.SimTest             # 67 assertions
dotnet run --project tools/VendingIdle.SimTest -- --curve  # progression report
```

### Controls

| Input | Action |
|---|---|
| Click the delivery flap / `Space` | Vend a bottle |
| Click a compartment | Select it, and slide the slot menu in |
| Click a price ticket | Buy that compartment |
| Mouse wheel over the glass | Scroll the machine |
| Click an edge tab / `Q` / `E` | Slide the upgrades / slot menu in or out |
| `Tab` | Send both menus away, or bring them back |
| `R` | Restock everything |
| `S` | Save |

Progress saves to `%LOCALAPPDATA%/VendingIdle/save.json` (`~/.local/share` on
Linux) every 15 seconds and on exit. Time away is simulated on load, capped at 8
hours.

### Command-line flags

| Flag | Purpose |
|---|---|
| `--fresh` | Ignore any existing save |
| `--save <path>` | Use a different save file |
| `--screenshot <path> --frames <n>` | Render n frames, write a PNG, exit |
| `--drawers open\|left\|right` | Slide menus in on launch (default: both closed) |

`--screenshot` exists so the game can be smoke-tested headlessly:

```bash
xvfb-run -a -s "-screen 0 1280x720x24" \
  dotnet run --project src/VendingIdle -- --screenshot out.png --frames 20 --fresh
```

## What is in this prototype

Slot purchasing and endless vertical expansion, click-to-dispense with the
spare-change fallback and falling-drink feedback, double-drop crits, per-slot drink
assignment, finite stock with manual and automated restocking, customers as
auto-clickers, seven global upgrades, six drinks unlocked by lifetime earnings,
save/load, and offline progress.

**Not built yet**, from the original design: packs and duplicate-levelling, machine
themes, and the "Soda Pop" sequencing minigame. The data model leaves room for them
— `DrinkDef` already carries `Rarity` and `EffectId`, and dispensing already walks
slots in sequence order, which is what Soda Pop scoring would hang off.

## Layout

```
src/VendingIdle.Core/     the entire economy. Zero MonoGame references, which is
                          what lets SimTest exercise it headlessly.
  Balance.cs              every tuning constant and cost curve, one file
  Simulation.cs           fixed-step tick, click resolution, offline catch-up
  GameState.cs            the savegame; player actions live here as Try* methods
src/VendingIdle/          MonoGame layer: rendering, immediate-mode UI, input
tools/VendingIdle.SimTest/ headless economy checks and the balance report
```

The UI is immediate-mode: no retained widget tree, every panel is a function of
`GameState`, redrawn each frame. For a prototype whose numbers change constantly
that is far less to keep in sync. Drawers are drawn (and hit-tested) before the
machine, so a click on one can never fall through to the vend tray behind it.

There are no art assets. The room, the cabinet and every drink are drawn from
textures generated at startup (`Render/Primitives.cs`). A compartment's drink is a
plain rectangle sized and positioned exactly where its sprite will go, so dropping
real art in is a matter of blitting a texture into `DrinkDisplay.Front` instead of
calling `DrawDrink`. The content pipeline is wired up and builds the three
SpriteFonts from a bundled DejaVu Sans, so real sprites can be dropped in through
the normal MonoGame workflow whenever you want them.

## Balancing

All the numbers live in `Balance.cs`, `UpgradeDatabase.cs` and `DrinkDatabase.cs`.
After changing any of them, run `-- --curve` to see where a greedy player lands
over 24 hours:

```
    time         earned         cash  slots  cust  drinks    income/s
      5m         $3.63K         $541      6     4       2       $11/s
     30m          $922K      $88.43K     17    20       4    $2.59K/s
      1h        $16.67M       $1.81M     24    29       5   $18.83K/s
      4h         $1.23B     $133.27M     34    43       6  $226.88K/s
     24h         $36.4B        $4.5B     42    55       6  $619.26K/s
```

Two traps this economy already fell into, both now covered by assertions:

- **Stacked multipliers hyperinflate.** With four exponential upgrade tracks
  multiplying each other, the first pass hit $234 *trillion* and the full drink
  roster in thirty minutes. The click-value track is deliberately linear now, and
  the rate upgrades are capped.
- **Per-can cost scaling must be relative to capacity.** Compounding restock price
  per absolute can meant that at 110 capacity the last can of Midnight Brew cost
  76x the first — margins collapsed to zero and progression froze completely.
  `RestockGrowth` is now the empty-to-full price ratio, whatever the capacity.
