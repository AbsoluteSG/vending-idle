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

The cabinet is three slots wide and ships four rows tall, which is exactly what
fits on screen. It does not stay that size. Every row you buy makes the machine
physically taller, growing upward from a floor line that never moves, until it runs
off the top of the screen and keeps going. Nothing scrolls inside the glass — the
compartments sit at their true height and the **camera** pans up and down the
cabinet on the mouse wheel. A bottle from the top row really does fall the whole
way into the tray.

That growth is why the cabinet is built in three parts. The **base** is bolted to
the floor and holds everything you need constantly: the till readout, RESTOCK and
SAVE, and the delivery tray. The **body** — glass and service column — is the part
that stretches, with cooling vents filling whatever height the controls do not. The
**crown** carries the branding and ends up far overhead, which is the point: you
scroll up to look at what you have built.

You start with one slot in the bottom-left corner. Shaking the machine knocks one
bottle out of *every* stocked slot at once — the whole cabinet rocks, so every loaded
coil gives something up — and when everything is empty it pays out spare change
instead. The per-slot yield is a single number (`Balance.ShakeBottlesPerSlot`, read
through `GameState.ShakeBottlesPerSlot`), so an effect that knocks out more than one
has a seam to hang off. Customers are unaffected: they buy one drink at a time from
the next stocked slot in sequence, which is what keeps a shake feeling like something
you did rather than a faster tick.

Stock is real and finite, so it has to be restocked, and "customers" are auto-clickers
that drain that same stock. More customers means faster drain, which is what makes
restock automation something you actually want rather than a convenience.

## Running it

Requires the **.NET 8 SDK**. MonoGame's content pipeline is a local dotnet tool, so
restore it once:

```bash
dotnet tool restore          # installs dotnet-mgcb from .config/dotnet-tools.json
dotnet run --project src/VendingIdle
```

Runs on Windows, macOS and Linux off the same checkout — MonoGame's DesktopGL
backend, no per-platform project.

### macOS

Apple Silicon is supported natively, Rosetta not required. SDL2, OpenAL and the
freetype the content pipeline needs for SpriteFonts all ship as universal binaries
with an arm64 slice.

```bash
brew install --cask dotnet-sdk      # or download the arm64 .NET 8 SDK installer
git clone https://github.com/AbsoluteSG/AbsoluteSG.git
cd AbsoluteSG/vending-idle
dotnet tool restore
dotnet run --project src/VendingIdle
```

Nothing needs to come from Homebrew beyond the SDK; the native libraries arrive
through NuGet.

The economy has a headless test suite that needs no window or GPU:

```bash
dotnet run --project tools/VendingIdle.SimTest             # 104 assertions
dotnet run --project tools/VendingIdle.SimTest -- --curve  # progression report
```

### Controls

| Input | Action |
|---|---|
| Click the delivery flap / `Space` | Shake: one bottle out of every stocked slot |
| Click a compartment | Select it, and slide the slot menu in |
| Click a price ticket | Buy that compartment |
| Mouse wheel | Pan the camera up and down the cabinet |
| Click an edge tab / `Q` / `E` | Slide the upgrades / slot menu in or out |
| `Tab` | Send both menus away, or bring them back |
| `R` | Restock everything |
| `S` | Save |

Progress saves every 15 seconds and on exit, to whichever your platform expects:

| Platform | Save location |
|---|---|
| Windows | `%LOCALAPPDATA%\VendingIdle\save.json` |
| macOS | `~/Library/Application Support/VendingIdle/save.json` |
| Linux | `~/.local/share/VendingIdle/save.json` |

Time away is simulated on load, capped at 8 hours.

Saves carry a version. Version 2 narrowed the grid from four columns to three, which
changes what every stored slot index means — slot indices are row-major, so the grid
width is baked into them. `GameState.Migrate` re-lays an older save by compacting its
unlocked slots into the bottom of the new grid, keeping their drinks, stock and
automation. Positions are not meaningful in themselves, only the count, the contents,
and the rule that a slot needs one below it.

### Command-line flags

| Flag | Purpose |
|---|---|
| `--fresh` | Ignore any existing save |
| `--save <path>` | Use a different save file |
| `--screenshot <path> --frames <n>` | Render n frames, write a PNG, exit |
| `--drawers open\|left\|right` | Slide menus in on launch (default: both closed) |
| `--mute` | Start silent (implied by `--screenshot`) |

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

Drawing happens in two spaces, and `Ui.Begin(Space)` switches between them. **World**
content — the floor, the cabinet, falling bottles — is laid out around the fixed
floor line and rides the camera. **Screen** content — drawers, tooltips, the wall and
the hint text — ignores the pan and stays where it is. Input follows the same split:
`Ui.Mouse` reports whichever space the open batch is drawing in, so a hit test always
reads the coordinates the drawing used. The shake is layered on top of both and is
deliberately excluded from input, so a rattle can never jog a click off its target.

One trap worth knowing if you touch this: the scissor rectangle is *not* covered by
the sprite transform. An explicit clip is in the current space and gets offset by
hand; the default clip is the whole viewport and must not be, or panning up a tall
cabinet shrinks the drawable area to a band wherever the camera happens to point.

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

## Sound

Four cues, all in `Sfx.cs`: a shake, a refused purchase, a completed one, and a
bottle landing in the tray.
They are built through the content pipeline from `Content/Audio/*.ogg` — the mgcb
tool ships its own macOS ffmpeg, so no system-wide install is needed to build them.

Every cue is tied to something the *player* did. Customers buying in the
background stay silent: a sound per idle tick turns into a drone within a minute
of hiring the second customer. The shake's pitch wanders slightly on each play,
because an idle game is hundreds of presses of the same button and the identical
sample every time is what makes a good cue grating.

Audio never takes the game down. A box with no audio device throws from the
subsystem, so any failure mutes the rest of the session instead of propagating —
silence beats a crash on a cosmetic. `--mute` starts silent, and `--screenshot`
implies it.

Every drink has its own voice without its own file. `DrinkDef.SoundPitch` shifts
the one clink sample per drink — light drinks ring high, premium ones land heavy
and low — so the roster filling out is something you hear, not only read. The clink
fires from `Effects` when a bottle actually *settles*, not when the shake throws it,
which is a few hundred milliseconds later and later still if it bounces twice. A
late-game shake empties two dozen slots at once, so clinks are capped at three per
frame and duck as they go, making a big shake one fat clatter rather than a wall.

The files are CC0 from Kenney's Interface Sounds pack and are named for their role
rather than their origin, so swapping one is dropping a new `.ogg` over it with no
code change. See `Content/Audio/ATTRIBUTION.md`.

## Balancing

All the numbers live in `Balance.cs`, `UpgradeDatabase.cs` and `DrinkDatabase.cs`.
After changing any of them, run `-- --curve` to see where a greedy player lands
over 24 hours:

```
    time         earned         cash  slots  cust  drinks    income/s
      5m      $2,700.80      $481.57      3     3       2      $2.50/s
     30m     $14,362.10    $1,455.46      5     6       2     $17.59/s
      1h     $98,500.65    $8,760.35      8    11       3     $91.51/s
      4h        $1.75e7      $1.96e6     17    24       4  $4,120.01/s
     24h        $3.83e9      $4.52e8     27    39       6 $122,538.54/s
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
