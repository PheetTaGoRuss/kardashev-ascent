# ข้อมูลดาวจริงจาก NASA Exoplanet Archive

ดึงค่าดาวเคราะห์จริงจาก [NASA Exoplanet Archive](https://exoplanetarchive.ipac.caltech.edu) (ตาราง `pscomppars`)
แล้วแปลงเป็นค่าที่ระบบจำลองของเกมใช้ (`PlanetSolarParams` ใน `Simulation/Solar`)
ใช้ Python มาตรฐานอย่างเดียว ไม่ต้องติดตั้งอะไรเพิ่ม

| ไฟล์ | ทำอะไร |
| --- | --- |
| `planets.json` | รายชื่อดาวที่เกมใช้ และค่าที่ archive ไม่มี (ความเอียงแกน, ความยาววัน, บรรยากาศ) |
| `convert.py` | แปลงหน่วยและคำนวณค่าที่ขาด พร้อมบอกที่มาของทุกค่า |
| `fetch_exoplanets.py` | ถาม archive ผ่าน TAP API, เก็บคำตอบดิบไว้ใน `cache/`, เขียน JSON ให้ Unity |
| `test_convert.py` | unit test ของ `convert.py` |

## รัน

```bash
python tools/exoplanets/fetch_exoplanets.py
```

ได้ `Assets/_Project/Data/Planets/planets.json` และ `tools/exoplanets/cache/pscomppars.csv`
commit ทั้งสองไฟล์ (รวม `.meta` ที่ Unity สร้าง) เพื่อให้ทุกคนได้ข้อมูลชุดเดียวกันโดยไม่ต้องต่อเน็ต
ถ้าแก้แค่ `planets.json` ใช้ `--offline` สร้าง JSON ใหม่จาก cache เดิม

```bash
python -m unittest discover tools/exoplanets
```

## ค่าทุกตัวมีป้ายที่มา

ใน JSON ทุกค่ามีรายการใน `provenance` บอกว่ามาจากไหน เกมควรแสดงป้ายนี้ให้ผู้เล่นเห็น
เพราะ "ค่าไหนวัดได้จริง ค่าไหนนักวิทยาศาสตร์ยังไม่รู้" เป็นสิ่งที่เกมสอนได้

| ป้าย | ความหมาย | ตัวอย่าง |
| --- | --- | --- |
| `nasa` | ค่าจาก archive แปลงหน่วยอย่างเดียว | คาบโคจร, ระยะวงโคจร, ความสว่างดาวฤกษ์ |
| `derived` | คำนวณจากค่า archive ด้วยสูตรฟิสิกส์ | แรงโน้มถ่วง $g = GM/R^2$, ความเร็วหลุดพ้น, $L = 4\pi R^2\sigma T^4$ เมื่อ archive ไม่มี `st_lum` |
| `assumed` | ยังวัดไม่ได้สำหรับดาวนอกระบบ ใช้ค่าเกมจาก `planets.json` | ความเอียงแกน, ความยาววัน, τ ของบรรยากาศ, ความกดอากาศ (`surfacePressureEarth`), เมฆ (`clouds`: `earth` ใช้สถิติเมฆของโลกจาก NASA POWER, `none` ฟ้าใส) |

## หน่วยและข้อควรรู้

- `st_lum` ใน archive คือ $\log_{10}(L/L_\odot)$ และ `pl_insol` เป็นหน่วยเท่าแสงที่โลกได้รับ (คูณ 1361 W/m²)
- JSON ใช้หน่วย SI ส่วนมุมเป็นองศา (ฝั่ง C# แปลงเป็นเรเดียนก่อนสร้าง `PlanetSolarParams`)
- `pscomppars` รวมค่าจากหลายงานวิจัยไว้ในแถวเดียว ค่าในแถวเดียวกันจึงอาจไม่สอดคล้องกัน
  ตัวแปลงจะเตือนถ้า `pl_insol` ต่างจาก $L/4\pi a^2$ เกิน 25%
- `pl_trueobliq` ใน archive คือมุมระหว่างแกนหมุนของ **ดาวฤกษ์** กับวงโคจร ไม่ใช่ความเอียงแกนของดาวเคราะห์
  ความเอียงแกนของดาวเคราะห์นอกระบบยังวัดไม่ได้ จึงเป็นค่า `assumed` เสมอ
- **ดาวล็อกไทดัล** (`tidallyLocked: true`): `dayLengthHours = 0` หมายถึงไม่มีวันสุริยะ (ดาวฤกษ์ค้างบนฟ้า)
  `AnnualYield` ตอนนี้ยังรองรับกรณีนี้ไม่ได้ ต้องใช้ $\cos z = \cos\varphi\cos(\lambda - \lambda_{sub})$ แทน
- **ดาวโคจรรอบดาวคู่** (`cb_flag = 1` เช่น Kepler-16 b): ค่า `st_*` ของ archive เป็นของดาวหลักดวงเดียว
  ตัวแปลงจะเตือน ค่าดาวดวงที่สองต้องเอามาจากงานวิจัยค้นพบ (Doyle et al. 2011)
  ส่วนระบบที่มีหลายดวงแต่ `cb_flag = 0` (เช่น Proxima Cen b ในระบบ α Centauri ที่มี 3 ดวง) ดาวเคราะห์โคจรรอบดวงเดียว
  ดาวที่เหลืออยู่ไกลมากจนแสงแทบไม่มีผล จึงไม่ใช่ดาวคู่ในเกม
- **มีพื้นผิวให้สร้างไหม** (`flags.surface`): จากรัศมี `rocky` ≤ 1.6 R⊕, `uncertain` 1.6–2.0, `gaseous` ≥ 2.0
  (Rogers 2015; Fulton et al. 2017) เช่น Kepler-16 b มีรัศมี 8.45 R⊕ เป็นดาวแก๊สขนาดดาวเสาร์ ไม่มีพื้นให้วางแผง
  ถ้าจะใช้เป็นด่าน ต้องเป็นดวงจันทร์สมมติของมัน หรือดาวหินในจินตนาการที่โคจรแบบเดียวกัน
- `airTemperatureC` ของดาวจริง = อุณหภูมิสมดุล + `greenhouseOffsetK` (ค่าเกม ค่าเริ่มต้น 0; โลกจริงราว 288 − 255 = 33 K)
- ความยาววัน: ถ้าใส่ `rotationPeriodHours` (คาบหมุนรอบตัวเองเทียบดาวไกล) จะคำนวณวันสุริยะให้
  $1/D = 1/P_{rot} - 1/P_{orb}$ (หมุนสวนทางใส่ `retrograde: true` ใช้เครื่องหมาย +) เช่น โลก 23.93 h → 24.00 h
- archive ไม่ได้ประกาศ rate limit แต่ให้ดึงครั้งเดียวแล้วใช้ cache ไม่ต้องดึงตอนเกมรัน

ถ้าชื่อดาวไม่ตรงกับ `pl_name` ใน archive สคริปต์จะแจ้งชื่อที่หาไม่เจอ พร้อม query สำหรับหาชื่อที่ถูก

## A hand-made planet with NASA values (Mars)

A planet without an archive entry can still name a NASA source. `nasaSource` is the source; `nasaFields` lists the output
fields that come from it (e.g. `solar.eccentricity`, `planet.radiusM`). Those are tagged `nasa` in the provenance,
the rest stay `assumed`. Mars uses the NSSDC Mars Fact Sheet this way. Its solar longitude of perihelion
(Ls = 250.87°, Allison & McEwen 2000) is tagged `literature`: the fact sheet's 336.04° is measured from Earth's equinox.
