# Bangkok video (LINE @droneth): script and checklist

**Due Monday 16 November 2026, 18:00.** After that, late videos are accepted until Tuesday 17 November, 18:00, with a
possible deduction.

Video rules:
- 5–7 minutes long.
- In English.
- **Every team member on camera, with their face clearly visible.**
- Slides on screen.

Slides: use `demo-slides.pdf`, the same 7 slides as the NASA demo. Put in your team name and member names first: edit
`TEAM` and `MEMBERS` in `make_slides.py`, then run `python make_slides.py`.

## Recording plan

- Record each speaker on camera, as a webcam corner or a split screen, over the slides or gameplay.
- Capture gameplay in the Windows build or the Unity Game view with OBS or the Xbox Game Bar (Win + G).
- Speak slowly. About 130 words a minute gives roughly 800 words for 6 minutes.

## Script (about 6 minutes)

Speakers A, B and C are placeholders: share the parts so each member speaks at least once.

### 0:00–0:40 · Speaker A · Slide 1 (title), faces on camera

"Hello! We are team [TEAM NAME] from Bangkok: I'm [A], with [B] and [C]. Our project for the NASA Space Apps
Challenge 2026 is **Kardashev Ascent**, for the challenge *Space Mission Design Game*. It's a game where you run a
robotic mission on a real exoplanet, with real physics and real NASA data."

### 0:40–1:30 · Speaker A · Slide 2 (the problem)

"In school we learn power, mass, time, communications and risk one at a time. In a real mission they are tangled
together: a bigger solar array means more mass, more mass means more time to build, and so on. Students rarely get
to run a whole mission and see those trade-offs. So we built a game where you feel them."

### 1:30–2:40 · Speaker B · Slide 3, then live gameplay

"You choose a world from the NASA Exoplanet Archive, like Proxima Centauri b or TRAPPIST-1 e, or Mars. Before
landing, the orbital brief gives rough values with error bars: temperature, gravity, air pressure. You land, then
place solar panels. Their tilt and facing decide their output, computed from the star's real position through the
year." *(Show: the planet chooser, landing, placing a panel and the panel planner numbers.)*

### 2:40–3:50 · Speaker B · Slide 4 (trade-offs), then gameplay

- **Power:** "Power runs out at night, so you need batteries, or you get a brownout."
- **Robots:** "Robots have real batteries, Perseverance's 2.4 kWh, and can only recharge at a charger. Mountains and
  canyons block their routes until you tunnel or bridge them."
- **Materials:** "Ore runs out. Refining is real chemistry: aluminium needs alumina first, then electrolysis at about
  13.5 kWh per kilogram, so industry competes with everything else for power."

*(Show: the Power window, robots charging, the Manufacturing window, a canyon.)*

### 3:50–4:40 · Speaker C · Slide 5 (NASA data)

- **NASA POWER:** "We used NASA POWER: 25 years of monthly climate for clouds, air temperature, ground reflectance
  and aerosol haze. On Earth-like worlds these change the panel output by place and season."
- **Exoplanet Archive and Mars:** "The NASA Exoplanet Archive gives each planet's orbit, size, mass and star. The NSSDC
  Mars Fact Sheet gives Mars."
- **Hardware:** "The lander's power comes from the MMRTG fact sheet."
- **Checks:** "We checked our solar model against pvlib, and about 950 automated tests check the physics."

### 4:40–5:30 · Speaker C · Slide 6 (learning by doing), then gameplay

"You don't start knowing your planet: you measure it. You find your latitude from a noon shadow, estimate ore grades
from rock cores, and run experiments in a research tree, like a pendulum for gravity. A science calculator, a guide
book and info cards explain every number. And when something fails, the game tells you why."
*(Show: the latitude puzzle, the research tree, the guide book.)*

### 5:30–6:15 · Speaker A (or everyone) · Slide 7 (what's next), faces on camera

"Next, we want team play for up to four players, each with a role: power, robots, science or industry. We also
want mission reports and more energy sources, all the way to star-scale engineering. Thank you! Kardashev Ascent:
from one lander to a civilization, one real trade-off at a time."

## 10–20 photos and videos to send with it

Mix team photos with screenshots and clips:

1. A team photo working together, every face visible.
2. A whiteboard or sketch of the game design.
3. Each member at their laptop, coding or testing.
4. The planet chooser (NASA Exoplanet Archive planets).
5. The landing brief from orbit.
6. Placing solar panels, with the panel planner.
7. The sky view and latitude puzzle.
8. The Power window during a night or brownout.
9. Robots driving and charging.
10. The Manufacturing window and refining.
11. The research tree.
12. A canyon and a mountain, with a bridge or tunnel.
13. The guide book.
14. The unit test run (Unity Test Runner, all green).
15. GitHub: commits, pull requests, CI checks.
16. The NASA POWER and Exoplanet data scripts, or the `docs/nasa-data.md` table.
17. A short gameplay clip of 30–60 seconds.

The screenshots in `docs/submission/images/` can be used directly.

## LINE message (fill in and send to @droneth)

```
Project name: Kardashev Ascent
Team name: [TEAM NAME]
Team members: [Full name 1], [Full name 2], [Full name 3]
Challenge: Space Mission Design Game
Presentation video: [public YouTube/Drive link]
Photos and videos: [public Drive folder link]
NASA project page: [link to your team's project page]
```

## Before sending

- [ ] Video is 5:00–7:00, in English, with every member's face visible and slides shown.
- [ ] The video link opens in a private or incognito window (YouTube "Unlisted" or Drive "Anyone with the link").
- [ ] The photo folder is shared publicly.
- [ ] No one under 18 appears in any image or video (NASA rule).
