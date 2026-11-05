# Kardashev Ascent

เกม Unity แนววางแผนสร้างพลังงานบนดาวเคราะห์นอกระบบ ทำสำหรับ NASA Space Apps 2026
ทีม 3 คนใช้ Claude Code หลาย session พร้อมกัน (ทั้งบนเครื่องและบน cloud) จึงต้องคุยกันผ่าน GitHub

## ก่อนเริ่มงานใหม่ทุกครั้ง

1. `git pull` ให้ `main` เป็นปัจจุบัน
2. อ่าน issue ที่เปิดอยู่ (`gh issue list`) และไฟล์ใน `docs/` ก่อน เพราะ session อื่นอาจทำหรือตัดสินใจเรื่องนั้นไปแล้ว
3. ถ้างานที่จะทำมี issue อยู่แล้ว ให้ทำตาม issue นั้นและอ้างเลข issue ใน PR

ผลการตัดสินใจที่ session อื่นต้องรู้ให้เขียนลง issue, PR หรือ `docs/` อย่าเก็บไว้ในแชต

## โครงสร้าง

- `Assets/_Project/Scripts/Simulation/`: C# ล้วน ไม่มี `MonoBehaviour` และไม่อ้าง UnityEngine (`noEngineReferences`) ใช้ `double`
- `Assets/_Project/Scripts/Presentation/`: ภาพ กล้อง UI อ้าง Simulation ได้ แต่ห้ามกลับทาง
- `Assets/_Project/Tests/EditMode/`: unit test ของ Simulation
- `prototypes/solar/`: ต้นแบบ Python ของโมเดลแสง ตรวจกับ pvlib
- `tools/`: สคริปต์ของทีม เช่น ตั้งค่า Git
- `docs/physics/physics-verification.md`: ผลตรวจฟิสิกส์ของเอกสารดีไซน์ รายการแก้โค้ดเรียงตามความสำคัญ และค่า unit test

## กฎ

- Unity `6000.6.8f1` เท่านั้น
- ไม่ push ตรงเข้า `main` ทุกอย่างผ่าน PR ตาม `CONTRIBUTING.md`
- สร้างวัตถุในฉากด้วยโค้ดหรือ editor script ไม่แก้ไฟล์ `.unity` / `.prefab` เป็นข้อความ
- สูตรฟิสิกส์ทุกตัวต้องมี unit test และตรงกับ `docs/physics/physics-verification.md`
  ถ้าแก้สูตรใน `prototypes/solar/solar.py` ต้องแก้ C# ให้ตรงกันแล้วรันเทสต์ทั้งสองฝั่ง
