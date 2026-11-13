# Alpha test guide

What to try in the first playable build, and what is known to be rough. Checked in Play mode on 2026-11-13 (`main` plus PR #64).

## Start

1. Open the project in Unity `6000.6.8f1`, open `Assets/Scenes/Main.unity`, press Play. (If the scene looks empty: **Kardashev Ascent ▸ Set Up Scene**.)
2. Or use **Kardashev Ascent ▸ Build Windows** for a standalone build.

## A first session (about 15 minutes)

| Step | What to do | What you should see |
| --- | --- | --- |
| 1 | Click a land tile near the base, or press `L` to choose another planet | The camera flies to the tile's 61 building slots |
| 2 | Drag **Solar panel** from the bar at the bottom onto a slot (or click a slot and press `P`) | An orange construction site, then a panel once a robot has built it |
| 3 | Place two **Battery yards** (`Tab` switches building) | Battery capacity rises in the HUD |
| 4 | Press `4` to run a day per second | Power at night comes from the battery; no brownout if you built enough |
| 5 | Press `C` | The colony overview: demand, growth, the week's power graph |
| 6 | Reach milestone 1 (planet K 0.1) | New robot kinds and bigger panels unlock |
| 7 | Press `G`, then `U` | Robots and the Workshop: order a robot, refine aluminium (leaves slag) |

Other keys: `V` sky view, `M` light map, `F10` god mode (shows hidden ground), `F5` / `F9` save and load, `H` hide help.

## Known rough edges

- Robot, recipe and balance numbers are placeholders; progress may feel too fast or slow.
- The Robots window is still dense; the site view and tile panel are text-heavy.
- Most of the vision is not built yet: see `docs/development/feature-registry.md`.
- No automated UI tests; the simulation has 795 passing tests (run outside Unity).
- Report anything odd with the game day and what you clicked.
