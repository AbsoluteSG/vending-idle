# Audio credits

Everything here is **Creative Commons Zero (CC0)** — public domain, free for
commercial use, attribution appreciated but not required. Nothing in this
directory carries an obligation, which is what keeps this repo public without
a licence audit.

## Sound effects

The four cues come from Kenney's **Interface Sounds** pack (v1.0).

- Source: <https://kenney.nl/assets/interface-sounds>
- Licence: <http://creativecommons.org/publicdomain/zero/1.0/>

Files are renamed to their role in the game so that swapping one out is a matter
of dropping a new `.ogg` over it, without touching any code:

| In this repo   | Original in the pack   | Plays when                                 |
|----------------|------------------------|--------------------------------------------|
| `shake.ogg`    | `drop_003.ogg`         | The player shakes the machine              |
| `denied.ogg`   | `error_004.ogg`        | The player asks for something unaffordable |
| `purchase.ogg` | `confirmation_001.ogg` | A purchase goes through                    |
| `bottle.ogg`   | *(not recorded)*       | A bottle lands in the tray, pitched per drink |

All four are short (0.10 s – 0.29 s). Anything much longer overlaps itself when
a player shakes repeatedly, which is what makes UI audio grating.

## Music

`bgm.ogg` is **"Swinging Sweet"** by **hernandack**, from the *Short Loops
Background Music Pack* — CC0, and authored to loop seamlessly.

- Source: <https://opengameart.org/content/short-loops-background-music-pack>
- Author: <https://hernandack.itch.io>
- Licence: <http://creativecommons.org/publicdomain/zero/1.0/>

About 25 s, stereo, 44.1 kHz. It is built through `SoundEffectProcessor` rather
than `SongProcessor`, so it decompresses to roughly 4 MB of PCM at build time
and plays through a looping `SoundEffectInstance`. That buys reliable pause and
resume across platforms, which `MediaPlayer` does not consistently give — worth
the memory for a loop this short, and worth revisiting if the track gets longer.
