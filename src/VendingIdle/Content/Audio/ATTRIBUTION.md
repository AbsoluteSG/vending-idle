# Audio credits

All three effects come from Kenney's **Interface Sounds** pack (v1.0), released
under **Creative Commons Zero (CC0)** — public domain, free for commercial use,
attribution appreciated but not required.

- Source: <https://kenney.nl/assets/interface-sounds>
- Licence: <http://creativecommons.org/publicdomain/zero/1.0/>

Files are renamed to their role in the game so that swapping one out is a matter
of dropping a new `.ogg` over it, without touching any code:

| In this repo   | Original in the pack   | Plays when                                |
|----------------|------------------------|-------------------------------------------|
| `shake.ogg`    | `drop_003.ogg`         | The player shakes the machine             |
| `denied.ogg`   | `error_004.ogg`        | The player asks for something unaffordable|
| `purchase.ogg` | `confirmation_001.ogg` | A purchase goes through                   |

All three are short (0.10 s – 0.29 s). Anything much longer overlaps itself when
a player shakes repeatedly, which is what makes UI audio grating.
