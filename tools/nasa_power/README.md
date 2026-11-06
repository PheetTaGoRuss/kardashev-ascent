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
