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

Every bottle sold also drips **crate tokens** into the supply crate on the floor
beside the cabinet. When it can afford a crate it glows; click it and a drink floats
out of the lid, shuffling rapidly through the possibilities and slowing as it climbs
-- then locks onto the roll and bobs there until you click it to claim (the crate is
dead until you do, and an unclaimed roll survives save/quit). Crates are the only
source of **effect drinks**: lower value than the purchase drink they compete with,
but each carries an aura (active while loaded *and stocked*) or an on-dispense proc.
Duplicates raise the effect's level. Purchase drinks stay pure value -- effects push
on every lever except the value curve, deliberately.

### Crates cost a flat price, and the collection is the long game

A crate costs **250 tokens. Always.** No escalating price, the way a pack works in
most games that sell them. Pacing lives in two places instead, and neither is cost.

**A supply quota is the soft cap.** Tokens are earned through a budget that
regenerates at a fixed crates-per-day rate -- 25/day to start, rising to **100/day**
at maximum Supply Contract. Income past the quota earns nothing.

That last part was a finding, not a decision. It began as a 15% trickle so grinding
past the cap was worth *less* rather than worth nothing. Measured, a twelve-slot
machine shaken flat out opened **4,826 crates in a simulated day** against a ceiling
of 100 -- because a trickle proportional to sales scales with the machine exactly
like the income it is meant to be bounding. A share of a growing number cannot bound
that number. The softness comes from the reserve instead: the quota banks while you
are away (up to 1.5 days) and can be spent in a burst, so the cap is a daily average
rather than an hourly wall. There is a test asserting a day of perfect play lands on
99--100 crates.

**Rarity is the real gate**, and there are no mercy mechanics anywhere. No pity
counter, no bad-luck protection, no history: every crate is the same independent
roll as the first. Weights are per *drink*, so adding another Legendary makes each
Legendary rarer.

| Tier | Per-drink chance | Packs to first copy | Level cap |
|---|---|---|---|
| Common ×4 | 17.2% | ~6 | 10 |
| Uncommon ×3 | 6.9% | ~15 | 9 |
| Rare ×4 | 2.1% | ~48 | 7 |
| Epic ×4 | 0.52% | ~194 | 5 |
| Legendary ×3 | 0.069% | ~1,450 | 4 |
| Mythic ×2 | 0.015% | ~6,840 | 3 |

Simulated over 40 independent collections: **18 of 20 drinks in ~2,000 packs** (about
20 days at the cap), and **all 20 in ~7,500** (about 75 days). That number is the
design, so it is measured in the test suite rather than assumed.

Levels cost progressively more copies -- L copies to reach level L, so 55 for a maxed
common -- and rarer tiers cap lower, because a Mythic held to a common's curve would
sit at level 1 for the life of the save. A pull already at its ceiling refunds 35% of
the crate price, so the long tail is never entirely dead pulls.

One consequence worth stating: cascades pay only **40% of a drink's value in cash**
on each hop. Chains multiply bottles and bottles are money, so an unshaded hop makes
chains a value multiplier by the back door -- the exact failure that undid both
earlier balance passes. Hops pay full tokens and consume full stock; what they pay
less of is cash. A cascade is worth building for the collection, not for the till.

### Chains are the pillar

A dispense can jolt the next stocked coil into vending too, and that one can jolt the
next -- a **cascade**. Its length is the thing you build toward: chains are where the
roster stops being a value ladder and starts being a set of pieces that combine.

A cascade is bounded on two axes. It gets a **hop ceiling** (the Longer Coils upgrade,
plus Relay Rum while it is stocked), and each hop **decays** the chance of the one
after it. Decay matters more than it looks: with a flat re-roll the expected length is
a hyperbola in the chance, so it stays flat and then spikes without warning as
upgrades push the number up. Decaying keeps the tail finite and the curve legible.

Termination does not rest on the probabilities at all. Every hop must land on a slot
the cascade has not already visited, so a cascade ends even at 100% chance -- the
guarantee is structural, not statistical.

The combo pieces are deliberately weak read on their own card, because the payoff is
meant to come from what they sit next to:

| Drink | On its own | What it is for |
|---|---|---|
| Chain Fizz | a chance to chain | the seed |
| Jumper Juice | starts chains, extends nothing | a cheap seed for a deck that cannot chain |
| Relay Rum | nothing at all | +hops, so every other chain piece fires more often |
| Surge Syrup | nothing at all | hops can crit, which they otherwise never do |
| Echo Elixir | nothing at all | hops stop consuming stock, so cascades sustain |
| Loyalty Lemon | nothing at all | hops pay crate tokens, turning cascades into crates |
| Twin Tap | a second bottle | the anti-combo: it pays out *without* a chain |

Relay Rum is the enabler the rest lean on, so its scaling is the slowest number in the
game -- a hop multiplies every other chain effect at once, which is also why Longer
Coils is the steepest cost curve here.

None of them multiply raw value. The payoff of a combo is *length*, and length pays in
bottles, tokens and crits -- never in a value multiplier stacked on the value curve.

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
| `W` `A` `S` `D` | Move around the cabinet; at the edge, step into an open drawer |
| Hold `Shift` | Target the selected slot's whole row -- the cabinet marks what it would hit |
| `Shift` + a slot action | Load a drink, restock or automate the **whole row**; prices shown are the row total |
| `F9` | Reset the save and start over (keeps the old one as `.bak`) |
| `1`-`9` | Load that purchase drink into the target (pack drinks are excluded: their order is unpredictable) |
| `Enter` | Shake, or submit inside a focused drawer |
| `Esc` | Hand focus back to the cabinet |
| `C` | Open a crate, or claim the drink waiting above it |
| Mouse wheel | Pan the camera up and down the cabinet |
| Click the supply crate | Open a crate (when the gauge is full) |
| Click the floating drink | Claim the crate roll |
| Click an edge tab / `Q` / `E` | Slide the upgrades / slot menu in or out |
| `Tab` | Send both menus away, or bring them back |
| `M` / the speaker, top right | Mute or unmute all sound |
| `R` | Restock everything |
| `Ctrl`+`S` / `F5` | Save |

Holding shift widens every slot action to the row, and the prices follow. That
matters more than it sounds: restock unit cost climbs with how full a slot already
is, and each auto-restocker costs more than the last, so a row total is the sum of
an escalating run rather than one price times three. `Auto-restock row (3)` quotes
$1,118 where three separate purchases at the first price would suggest $1,800. A
button that charges four times what it advertises is worse than having no row mode,
so those totals are asserted in the test suite.

A row restock fills what it can afford and stops, rather than refusing outright --
restocking charges per bottle as it goes, so a row that outruns your balance leaves
you with partial shelves instead of a denial and no change.

`F9` wipes the save and starts fresh, for balance testing. There is no confirmation
prompt -- the point is to be able to re-run an opening without ceremony -- but the
previous save is copied to `save.json.bak` first. "No confirmation" and
"unrecoverable" are fine apart and bad together, and a copy is cheaper than a dialog
nobody wants during a tuning pass.

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

### Feedback ladder

Late game the screen should be busy. That is a design goal, not an accident -- but
a full-screen overlay on *every* proc would mean never seeing the cabinet again, so
density and impact are produced by different mechanisms. Many small things make it
busy; a few rare big ones make it land.

| Tier | Fires on | Treatment |
|---|---|---|
| local | any sale | payout popup, tilted a few degrees |
| accent | crit, Bottomless/Courier proc | tinted popup, camera kick, crit sting |
| near | cascade of 3 | banner above the cabinet, nothing obscured |
| slam | cascade of 4+, Epic pull | full-screen text, darkened surround, bell |
| mega | cascade of 8+, Legendary/Mythic pull | slam plus speed lines and a longer hold |

Banner text is the *drink's* name -- "CRAZY FIZZ" -- so the rarest things in the
collection produce the loudest moments, which is most of what a Mythic is for.

Three rules keep it from collapsing into noise:

**One banner at a time.** A late-game shake can set off several qualifying cascades
in a single frame. Six overlapping slams is not six times the impact, it is a smear,
so a louder banner replaces the current one and an equal or quieter one is dropped.
Nothing queues -- a banner that played out ten seconds after its cascade would be
announcing something the player has already forgotten.

**The overlay is a pair of gradients, not a wash.** Dark at the top and bottom of
the screen, clear through the middle. The cabinet stays visible, which is the
difference between spectacle and simply losing the game behind a black rectangle.

**Everything is budgeted.** Bottle clinks were already capped at three per frame;
chain ticks are capped at six, or a dozen simultaneous cascades drown the rising
pitch that is the entire point of the cue. Popups cap at 96 -- high enough that
density is real, low enough that the screen stays numbers rather than texture.

The chain tick climbs in pitch with the hop index, so a long cascade walks up the
scale and a combo *sounds* like it is building.

### Command-line flags

| Flag | Purpose |
|---|---|
| `--fresh` | Ignore any existing save |
| `--save <path>` | Use a different save file |
| `--screenshot <path> --frames <n>` | Render n frames, write a PNG, exit |
| `--drawers open\|left\|right` | Slide menus in on launch (default: both closed) |
| `--mute` | Start silent for this run (implied by `--screenshot`); does not change the saved setting |
| `--reveal` | Debug: grant tokens and open a crate on launch |
| `--juice near\|slam\|mega` | Debug: force a banner tier on launch, so the big tiers can be looked at on demand |

`--screenshot` exists so the game can be smoke-tested headlessly:

```bash
xvfb-run -a -s "-screen 0 1280x720x24" \
  dotnet run --project src/VendingIdle -- --screenshot out.png --frames 20 --fresh
```

## What is in this prototype

Slot purchasing and endless vertical expansion, click-to-dispense with the
spare-change fallback and falling-drink feedback, double-drop crits, per-slot drink
assignment, finite stock with manual and automated restocking, customers as
auto-clickers, seven global upgrades, six purchase drinks unlocked by lifetime
earnings, save/load, and offline progress.

Plus the crate system: six effect drinks across three rarities, found only in supply
crates paid for with tokens from bottles sold. Three auras (crit chance, customer
speed, restock discount -- live only while the slot has stock, so aura slots stay
inside the restock tension) and three procs (chain a second dispense, keep the
bottle, drop a free bottle into a dry slot). Duplicate copies raise the effect level
to a cap; the global caps still apply after auras, so no loadout escapes them.

**Not built yet**, from the original design: machine themes and the "Soda Pop"
sequencing minigame. Dispensing already walks slots in sequence order, which is what
Soda Pop scoring would hang off.

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

Under the cues sits a looping music bed, well below them in the mix so it never
competes with feedback the player needs to hear.

The speaker in the top-right corner (or `M`) silences everything, and the choice
is remembered in the save — a mute that forgets itself every launch is worse than
no mute at all. Muting *pauses* the music rather than stopping it, so unmuting
picks the loop up where it left off instead of restarting the same bars every
time the button is tapped.

Audio never takes the game down. A box with no audio device throws from the
subsystem, so any failure disables the rest of the session instead of
propagating — silence beats a crash on a cosmetic. That failure is tracked
separately from the player's mute (`Available` vs `Muted`): unmuting must never
resurrect a device that was never there. When audio is unavailable the button
greys out rather than vanishing, because a corner that quietly loses its control
is worse than one that plainly cannot work. `--mute` starts a single run silent
without touching the saved setting, and `--screenshot` implies it.

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

All the numbers live in `Balance.cs`, `UpgradeDatabase.cs`, `DrinkDatabase.cs` and
`EffectDatabase.cs`.
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
- **Crate prices must not outrun token income.** The first crate-price curve
  (growth 1.25 per crate against a roughly linear token drip) stalled completely
  by late game — a million banked tokens against a five-million crate. Now 300
  base with 1.12 growth: first crate in ~2-3 minutes, still opening at hour 24.
  Effect drinks accelerate the early game (~5x at 30 min) but converge by 24 h
  (~1.04x) — utility, not a second value axis — and the 30-minute hyperinflation
  assertion runs with the greedy player opening crates and loading effects.
