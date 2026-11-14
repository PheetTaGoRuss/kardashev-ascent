# เมฆของโลกจริงจาก NASA POWER

ดึง climatology รายเดือนจาก [NASA POWER](https://power.larc.nasa.gov) (ค่าเฉลี่ย 2001–2025) แล้วคำนวณ
**cloud factor = แสงจริง (all-sky) ÷ แสงตอนฟ้าใส (clear-sky)** ตามละติจูดและเดือน
ใช้คูณกับผลของโมเดลแสงฟ้าใส เพื่อให้ดาวคล้ายโลก (Viridia) มีสภาพอากาศเหมือนโลกจริง

```bash
python tools/nasa_power/fetch_cloud_factor.py            # ดึง 136 จุด (ละติจูด −80..80 ทุก 10°, ลองจิจูด 8 จุด)
python tools/nasa_power/fetch_cloud_factor.py --offline  # สร้าง C# ใหม่จาก cache
```

| ไฟล์ | คืออะไร |
| --- | --- |
| `cache/cloud_points.json` | คำตอบดิบของทุกจุด (kWh/m²/วัน ราย เดือน) |
| `Assets/_Project/Scripts/Simulation/Climate/CloudClimateTable.cs` | ตารางที่สร้างอัตโนมัติ ห้ามแก้เอง |
| `Assets/_Project/Scripts/Simulation/Climate/CloudClimate.cs` | `CloudClimate.Factor(latitude, yearFraction)` interpolate ระหว่างแถวและเดือน |

แต่ละแถวเป็นค่าเฉลี่ยของ 8 ลองจิจูด จึงผสมบกและทะเลแบบโลกจริง เดือนที่เป็นกลางคืนขั้วโลก (แสงฟ้าใส < 0.05 kWh/m²/วัน) ตั้งเป็น 1

ผลเทียบที่ใช้ตรวจโมเดลแสงฟ้าใสของเกมกับ NASA POWER อยู่ใน `docs/physics/physics-verification.md` หัวข้อ "ผลเทียบกับ NASA POWER"

## อุณหภูมิอากาศของโลกจริง (T2M)

ดึง **T2M** (อุณหภูมิอากาศสูง 2 เมตร, °C) แบบ climatology รายเดือนชุดเดียวกัน บนตารางจุดเดียวกับเมฆ (136 จุด)
แล้วเฉลี่ย 8 ลองจิจูดต่อแถวละติจูด ใช้กับดาวคล้ายโลก: อุณหภูมิเซลล์แผง แบตเตอรี่หุ่นยนต์ตอนหนาว และค่า "Air heat"

```bash
python tools/nasa_power/fetch_air_temperature.py            # ดึงจาก NASA POWER แล้วสร้าง C#
python tools/nasa_power/fetch_air_temperature.py --offline  # สร้าง C# ใหม่จาก cache
```

| ไฟล์ | คืออะไร |
| --- | --- |
| `cache/air_temperature_points.json` | คำตอบดิบของทุกจุด (°C รายเดือน) |
| `Assets/_Project/Scripts/Simulation/Climate/AirTemperatureTable.cs` | ตารางที่สร้างอัตโนมัติ ห้ามแก้เอง |
| `Assets/_Project/Scripts/Simulation/Climate/CloudClimate.cs` | `EarthAirTemperature.Celsius(latitude, yearFraction)` interpolate แบบเดียวกับเมฆ |

## การสะท้อนแสงของพื้นและหมอกควัน (ALLSKY_SRF_ALB, AOD_55)

ดึงสองค่าในคำขอเดียว บนตารางจุดเดียวกัน แล้วเฉลี่ย 8 ลองจิจูดต่อแถว

```bash
python tools/nasa_power/fetch_ground_and_haze.py            # ดึงจาก NASA POWER แล้วสร้าง C#
python tools/nasa_power/fetch_ground_and_haze.py --offline  # สร้าง C# ใหม่จาก cache
```

- **ALLSKY_SRF_ALB**: ใช้เป็น "การเปลี่ยนตามฤดู" บวกบนค่าการสะท้อนของภูมิประเทศแต่ละแบบ (เฉลี่ยทั้งปี = 0) เช่นหิมะฤดูหนาว
- **AOD_55**: aerosol optical depth ที่ 550 nm แปลงเป็นทั้งสเปกตรัมด้วยความยาวคลื่นหลัก 700 nm (Molineaux et al. 1998)
  และ Ångström α = 1.3 แล้วใช้เป็น "ส่วนต่างจากค่าเฉลี่ยของโลก" บวกบน τ ของดาว
- ไฟล์: `cache/ground_haze_points.json`, `Assets/_Project/Scripts/Simulation/Climate/GroundAndHazeTable.cs` (สร้างอัตโนมัติ),
  `CloudClimate.cs` (`EarthGroundAndHaze`)
