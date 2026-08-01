# Vending Idle

An idle/clicker prototype where the play surface is a vending machine's stock grid.
C# + MonoGame, no engine.

You start with one slot in the bottom-left corner. Clicking the machine dispenses a
can from the next stocked slot in sequence; when everything is empty it pays out
spare change instead. Stock is real and finite, so it has to be restocked, and
"customers" are auto-clickers that drain that same stock. More customers means
faster drain, which is what makes restock automation something you actually want
rather than a convenience.

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
| Click the tray / `Space` | Vend a can |
| Click a slot | Select it (the inspector acts on the selected slot) |
| Click a locked slot | Buy it |
| Mouse wheel over the grid | Scroll the machine |
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

`--screenshot` exists so the game can be smoke-tested headlessly:

```bash
xvfb-run -a -s "-screen 0 1280x720x24" \
  dotnet run --project src/VendingIdle -- --screenshot out.png --frames 20 --fresh
```

## What is in this prototype

Slot purchasing and endless vertical expansion, click-to-dispense with the
spare-change fallback, double-drop crits, per-slot drink assignment, finite stock
with manual and automated restocking, customers as auto-clickers, seven global
upgrades, six drinks unlocked by lifetime earnings, save/load, and offline progress.

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
that is far less to keep in sync.

There are no art assets. Every shape is drawn from textures generated at startup
(`Render/Primitives.cs`). The content pipeline is wired up and builds the three
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
