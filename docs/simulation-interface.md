# ข้อตกลงระหว่าง Simulation กับ Presentation

ชั้นภาพ (Presentation) คุยกับชั้นจำลอง (Simulation) ผ่าน `IPlanetSimulation` เท่านั้น
ส่งคำสั่งลงไป แล้วอ่านสถานะและฟัง event กลับขึ้นมา ชั้นจำลองไม่รู้จัก Unity และไม่อ้าง Presentation (บังคับด้วย `.asmdef`)

```
Presentation (MonoBehaviour)                 Simulation (C# ล้วน)
    │  Tick(hours), PlacePanel, PreviewPanel ...  ▼
    │ ─────────────────────────────────────────▶ IPlanetSimulation
    │ ◀───────────────────────────────────────── สถานะ + event
```

## พิกัด (ใช้ร่วมกันทุกระบบ)

| เรื่อง | ข้อตกลง |
| --- | --- |
| กรอบของดาว | ทรงกลมหนึ่งหน่วย, **+Y = ขั้วเหนือ**, ลองจิจูด 0 ชี้ **+Z**, ลองจิจูดเพิ่มไปทาง **+X (ตะวันออก)** ตรงกับ local space ของ `PlanetView` ใน Unity |
| `tileId` | index ใน `HexSphere` (0 ถึง `TileCount − 1`) ละติจูด/ลองจิจูดอ่านจาก `GetTile(id)` หรือ `HexSphere.Latitude/Longitude` |
| มุม | เรเดียนเสมอ (UI แปลงเป็นองศาเอง) |
| ทิศในพื้นที่ช่อง | (ตะวันออก, เหนือ, ขึ้น) แบบเดียวกับ `Simulation/Solar` แปลงด้วย `PlanetFrame.ToLocal` / `FromLocal` |
| ทิศดาวฤกษ์ | `StarDirection` = เวกเตอร์หนึ่งหน่วยจากศูนย์กลางดาวไปหาดาวฤกษ์ ในกรอบของดาว ช่องที่ลองจิจูด λ เห็นมุมชั่วโมง h = λ − ลองจิจูดใต้ดาวฤกษ์ |
| หน่วย | SI ยกเว้นเวลาเป็นชั่วโมง และพลังงานเป็น Wh |

ฝั่ง Unity ตั้งแสงดาวฤกษ์ได้ด้วย `light.transform.forward = -planet.transform.TransformDirection(StarDirection)`

## สิ่งที่ Presentation เรียกได้

| กลุ่ม | สมาชิก |
| --- | --- |
| ดาวและช่อง | `Sphere`, `TileCount`, `GetTile(id)` → `TileInfo` (ละติจูด, ลองจิจูด, terrain, ความสูง, พื้นที่ m²), `BaseTileId` |
| เวลาและดาวฤกษ์ | `TimeHours`, `DayLengthHours` (0 = ล็อกไทดัล), `YearLengthHours`, `StarDirection`, `IsDayAtBase` |
| ไฟ | `GenerationW` (แผง + RTG), `RtgW`, `DemandW`, `BatteryStoredWh`, `BatteryCapacityWh`, `BatteryPowerW` (+ ชาร์จ, − ดึง), `CurtailedW`, `IsBrownout`, `KardashevK`, `TypeITargetW` |
| คำสั่ง | `Tick(hours)`, `PreviewPanel(tileId, tilt, azimuth)` → `PanelPreview` (kWh/ปี, capacity factor, EROI, ระยะคืนทุน, เหตุผลถ้าวางไม่ได้), `SiteRings`, `PlacePanel(tileId, slot, tilt, azimuth)`, `RemovePanel(tileId, slot)`, `HasPanel(tileId, slot)`, `PanelCount(tileId)` |
| event | `BrownoutChanged(bool)`, `DayNightChanged(bool)` ที่ฐาน, `TargetReached()` |

## Stub สำหรับเริ่มงานภาพ

`StubPlanetSimulation` ใช้ได้เลยตอนนี้:

```csharp
var sim = new StubPlanetSimulation(planetView.Sphere, planetView.Surface, planetParams);
sim.DayNightChanged += isDay => Debug.Log(isDay ? "Sunrise" : "Sunset");
// ทุกเฟรม: sim.Tick(Time.deltaTime * hoursPerSecond);
```

| ส่วน | ใน stub |
| --- | --- |
| ทิศดาวฤกษ์, กลางวัน/กลางคืน, ฤดู | **จริง** จาก `Simulation/Solar` |
| ไฟจากแผงตอนนี้, `PreviewPanel` | **จริง** จาก solar model (แผง 1 จุด = ฟาร์ม 10,000 m² ชั่วคราว) |
| ความต้องการไฟ, แบตเตอรี่ | **จริง** จาก `Simulation/Energy/PowerGrid` (#9) ค่าเริ่มของยานลงจอด: ฐานใช้ 100 kW คงที่, แบต Li-ion 2 MWh เต็ม (ไป-กลับ 90 %, 1C), MMRTG 4 ตัว ≈ 440 W ที่ค่อย ๆ ลดลง แบตอย่างเดียวอยู่ได้ราว 19 ชั่วโมง |
| ค่า K | จากไฟที่ใช้จริง (ไม่นับไฟที่ทิ้งตอนแบตเต็ม) สูตรเต็มและ UI อยู่ใน #10 |

ข้อจำกัด: ดาวล็อกไทดัลยังทำ `PreviewPanel` ไม่ได้ (ตอบว่าวางไม่ได้พร้อมเหตุผล)

## มุมแผงที่ดีที่สุด (#7)

`PanelAngleSearch.Best(sim, tileId)` หามุมเอียง β และมุมทิศ γ ที่ได้พลังงานต่อปีมากที่สุดบนช่องหนึ่ง
ค้นแบบ grid หยาบ (β ทุก 10°, γ ทุก 30°) แล้วละเอียดขึ้นรอบจุดดีที่สุด 3 รอบ รวมประมาณ 133 ครั้งของ `PreviewPanel`
ผู้เล่นได้โบนัส best-angle เมื่อพลังงานของมุมที่ตั้งเองได้ ≥ 98% ของมุมที่ดีที่สุด (`PanelAngleSearch.Rate`)

ข้อตกลง: **`PreviewPanel` ต้องเป็นการอ่านอย่างเดียวและเรียกจาก thread อื่นได้** เพราะ `PanelPlacer` รันการค้นหานี้ใน `Task.Run`
ระบบจำลองตัวจริงที่มาแทน stub ต้องรักษาข้อนี้ไว้

## ช่องสร้างในแต่ละช่อง (site)

คลิกช่องบนดาวแล้วกล้องจะบินลงไปที่ช่องนั้น (`SiteView` + `OrbitCamera.FocusSite`) ในช่องหนึ่งมีช่องย่อย (slot) เรียงเป็น hex grid
`TileSite.Create(sphere, tileId, rings)` ซึ่ง**ปูเต็มช่องพอดี ทุก slot เต็มชิ้น ไม่มีครึ่ง**: จุดกลาง slot วางบน hex lattice ปกติ R วง ให้แถวนอกสุดห่างขอบ √3/4 ของระยะห่าง แต่ละ slot คือพื้นที่ที่ใกล้จุดกลางของตัวเองที่สุด (Voronoi) ตัดด้วยขอบช่อง
ผลคือ slot ด้านในเป็นหกเหลี่ยมสมบูรณ์ แถวขอบเป็นหกเหลี่ยมที่ปลายด้านนอกตัดเรียบชิดขอบ พื้นที่เท่า slot เต็มพอดี มุมทั้งหกเล็กกว่าราว 4 % แล้วยืดให้พอดีช่องจริงทีละสามเหลี่ยม (ช่องบนดาวไม่ได้เป็นหกเหลี่ยมสมบูรณ์)
ช่องหกเหลี่ยมได้ `3R(R+1)+1` slot ค่าเริ่มต้น R = `SiteRings` = 4 → **61 slot** ช่องห้าเหลี่ยม 12 ช่องได้ 50 slot (มุมใหญ่กว่า) ถามด้วย `SlotCount(tileId)`
slot 0 อยู่กลาง เลขเพิ่มทีละวง แผงหนึ่งแผงต่อหนึ่ง slot ทุก slot ในช่องเดียวกันได้แสงเท่ากัน (ใช้ละติจูด/ลองจิจูดของช่อง)
`PanelCount(tileId)` บอกจำนวนแผงในช่อง ระบบอื่นที่จะวางสิ่งก่อสร้าง (แบตเตอรี่ ฯลฯ) ควรใช้ slot ชุดเดียวกันนี้

## กริดไฟ (#9)

`PowerGrid.Step(solarW, hours, years)` ทุก step: กำลังผลิต (แผง + RTG) − ความต้องการ ถ้าเหลือชาร์จแบต (`Battery.Charge`) ส่วนที่ชาร์จไม่ได้เป็น `CurtailedW`
ถ้าขาดดึงจากแบต (`Battery.Discharge`) ถ้ายังไม่พอเป็น brownout แล้วยิง `BrownoutChanged(true)` ครั้งเดียว และ `false` เมื่อไฟกลับมาพอ
ประสิทธิภาพไป-กลับแบ่งเท่ากันทั้งขาเข้าและขาออก (√RTE) RTG ใช้ $P(t) = P_0 e^{-(\ln 2/87.7 + k_{TE}) t}$ โดย $k_{TE}$ ตั้งให้ 110 W เหลือ 72 W ที่ 17 ปี (2.46 %/ปี)
ค่า K ยังนับไฟที่ถึงโหลด + ไฟที่ชาร์จเข้าแบตเหมือนเดิม (ไม่นับไฟทิ้ง) ให้ #10 ตัดสินนิยามสุดท้าย
