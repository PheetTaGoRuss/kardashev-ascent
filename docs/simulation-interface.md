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
| ไฟ | `GenerationW`, `DemandW`, `BatteryStoredWh`, `BatteryCapacityWh`, `IsBrownout`, `KardashevK`, `TypeITargetW` |
| คำสั่ง | `Tick(hours)`, `PreviewPanel(tileId, tilt, azimuth)` → `PanelPreview` (kWh/ปี, capacity factor, EROI, ระยะคืนทุน, เหตุผลถ้าวางไม่ได้), `PlacePanel`, `RemovePanel`, `HasPanel` |
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
| ความต้องการไฟ, แบตเตอรี่ | ค่าชั่วคราว (ต้องการคงที่ 400 kW, แบต 4 MWh) จนกว่า #9 จะแทน |
| ค่า K | จากไฟที่ใช้จริง (ไม่นับไฟที่ทิ้งตอนแบตเต็ม) สูตรเต็มและ UI อยู่ใน #10 |

ข้อจำกัด: ดาวล็อกไทดัลยังทำ `PreviewPanel` ไม่ได้ (ตอบว่าวางไม่ได้พร้อมเหตุผล)
