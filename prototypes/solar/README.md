# Solar model prototype (Phase 0)

ต้นแบบสูตรระบบวางโซล่าเซลล์ ขั้นที่ 1–9 ในเอกสารดีไซน์ เขียนด้วย Python เพื่อเทียบกับ [pvlib](https://pvlib-python.readthedocs.io)
ก่อนพอร์ตเป็น C# ที่ `Assets/_Project/Scripts/Simulation/Solar`

| ไฟล์ | ทำอะไร |
| --- | --- |
| `solar.py` | สูตรต้นแบบ (ต้นฉบับของโค้ด C#) |
| `compare_pvlib.py` | เทียบกับ pvlib แล้วสร้าง `Assets/_Project/Tests/EditMode/Solar/PvlibReference.cs` ให้ unit test ใน Unity |

## รัน

Windows:

```bash
python -m venv .venv
.venv\Scripts\python -m pip install -r requirements.txt
.venv\Scripts\python compare_pvlib.py
```

macOS / Linux: ใช้ `.venv/bin/python` แทน

ทุกครั้งที่แก้สูตรใน `solar.py` ให้แก้ C# ให้ตรงกัน แล้วรัน `compare_pvlib.py` ใหม่ จากนั้นรัน EditMode tests ใน Unity
(Window → General → Test Runner)

## สิ่งที่เทียบกับ pvlib

| สูตร | เทียบกับ | ต่างกันสูงสุด |
| --- | --- | --- |
| air mass (Kasten–Young) | `atmosphere.get_relative_airmass` | 2e-14 |
| มุมเซนิทและมุมทิศของดวงอาทิตย์ | `solar_zenith_analytical`, `solar_azimuth_analytical` | 2e-13° |
| มุมตกกระทบ | `irradiance.aoi` | 1e-16 |
| แสงบนแผง (ท้องฟ้าสม่ำเสมอ) | `get_total_irradiance(model="isotropic")` | 1e-13 W/m² |
| อุณหภูมิเซลล์ (NOCT) | `temperature.ross` | 0 |
| กำลังไฟแผง | `pvsystem.pvwatts_dc` | 6e-14 W |
| ระยะโลก–ดวงอาทิตย์ | NREL SPA | 7e-5 AU |
| declination | NREL SPA | 0.005° |
| ตำแหน่งดวงอาทิตย์ทั้งสาย (กรุงเทพฯ) | NREL SPA | 0.18° (มาจากสมการเวลาแบบย่อ) |

## ข้อจำกัดตอนนี้

- ยังไม่มีเมฆ: ผลรายปีเป็นแบบฟ้าใสตลอด (กรุงเทพฯ ได้ราว 500 kWh/m²/ปี ที่ประสิทธิภาพ 20%)
- ยังไม่รองรับดาวล็อกไทดัล (วันยาวไม่สิ้นสุด) และดาวฤกษ์คู่
- อุณหภูมิอากาศคงที่ทั้งปี
- ตอนเที่ยงตรง (h = 0) azimuth ของ pvlib แบบ analytical พลิกเป็น 180° แม้ดวงอาทิตย์อยู่ทิศเหนือ จึงตรวจกรณีนี้ด้วยเทสต์ที่คำนวณมือแทน
