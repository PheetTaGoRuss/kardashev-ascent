# Demo video script (draft, #15)

> **ทีม:** ร่างนี้เขียนเป็นภาษาอังกฤษเพราะกรรมการอ่านอังกฤษ ความยาวจริงต้องรอ Project Submission Guide (ออก 13 พ.ย., ดู #14)
> มีสองแบบ: **30 วินาที** (ถ้า guide กำหนดสั้น) และ **2 นาที** อัดจาก build จริง (`Kardashev Ascent ▸ Build Windows`)
> ตัวเลขในวงเล็บเหลี่ยม [ ] คือค่าที่เห็นในเกมตอนทดสอบ ให้เช็กอีกครั้งตอนอัด

Story arc (from #15): **problem → measure → build → real physics → K grows**.

---

## 30-second cut

| Time | Screen | Voice-over / caption |
|---|---|---|
| 0–5 s | Planet chooser, cursor moving over Proxima Centauri b; NASA / calc / assumed tags visible | "These are real exoplanets from the NASA Exoplanet Archive. Every number says whether it's measured, calculated, or assumed." |
| 5–11 s | Land on Proxima b (L → Land). Eyeball planet, red light, star fixed in the sky | "Proxima b is tidally locked: its star never moves. Its red light gives silicon panels only about half their output." |
| 11–19 s | Site view: place panels (P), best angle (B); press 4 for a day per second; battery and power on the HUD | "You place real solar panels. Power, night, batteries and clouds all come from tested physics." |
| 19–26 s | Colony overview (C): 7-day chart, Kardashev section; HUD milestone message | "Spare power builds more panels, and the colony climbs the Kardashev scale: each milestone is ten times the power." |
| 26–30 s | Zoom out to the planet with solar farms; title card | "Kardashev Ascent: how far can real sunlight take a civilization?" |

---

## 2-minute cut

### 1. The question (0:00–0:15)
- **Screen:** Space, the star, slowly zoom to Viridia (Earth-like). Title card.
- **VO:** "How much energy can a civilization get from its star, using only real physics? Kardashev Ascent lets you find out on real exoplanets."

### 2. Real planets, honest data (0:15–0:40)
- **Screen:** Planet chooser. Hover rows to show the provenance notes (e.g. Brightness → NASA, Gravity → calc, Axial tilt → assumed). Show Kepler-16 b "Can't land: no solid surface".
- **VO:** "Planets come from NASA's Exoplanet Archive. Each value is tagged: measured by NASA, calculated with physics, or assumed because nobody can measure it yet, like an exoplanet's axial tilt. Kepler-16 b is a gas giant, so there is nowhere to build."

### 3. Measure before you build (0:40–1:00)
- **Screen:** Land on Viridia. Press M for the light-potential map. Click a tile: tile info with the sun path and monthly energy.
- **VO:** "First, measure. The light map shows where a panel makes the most energy over a year. Each tile shows its sun path and month-by-month output, with Earth's real cloud records from NASA POWER."

### 4. Build with real physics (1:00–1:25)
- **Screen:** Place panels (P), best angle (B). Speed up (4): day/night, battery draining at night, a brownout warning. Show the panel's energy, capacity factor, EROI and payback.
- **VO:** "Every panel's angle, temperature, air mass and the star's colour matter. At night the battery carries the colony; build too little and it browns out. Each panel shows how many years it takes to pay back the energy that made it."

### 5. Another star, another problem (1:25–1:45)
- **Screen:** L → Proxima Centauri b. Rust-red dry eyeball, star fixed in the sky. Panel numbers: silicon about [54 %] of its Sun output, EROI about [92].
- **VO:** "On Proxima b, a cool red dwarf, silicon panels lose almost half their output to the infrared. But the star never sets, so a panel still pays back in months."

### 6. Climbing the scale (1:45–2:00)
- **Screen:** Colony overview: Kardashev section (absolute K, planet K, humanity 2024 at K ≈ 0.73), milestone message "Unlocked: solar farms", violet farms in the site.
- **VO:** "Spare power becomes build parts, and the colony grows: every milestone is ten times the power, unlocking farms, parks and arrays. Humanity today is at K 0.73. How far can you get?"

---

## Recording checklist
- [ ] Build: `Kardashev Ascent ▸ Build Windows`, 1920×1080 windowed
- [ ] Start a fresh game (no save), hints visible for the first shots, then Skip
- [ ] Use the 1 d/s speed for day/night shots; pause (Space) for reading shots
- [ ] F5 a save at a good moment before the farm shots so takes can repeat (F9)
- [ ] Captions on screen as well as voice-over (judges may watch muted)
- [ ] End card: team name, repo link, "Data: NASA Exoplanet Archive, NASA POWER"
