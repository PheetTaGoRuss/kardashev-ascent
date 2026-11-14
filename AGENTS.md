# Kardashev Ascent

เกม Unity แนววางแผนสร้างพลังงานบนดาวเคราะห์นอกระบบ ทำสำหรับ NASA Space Apps 2026
ทีมทำงานร่วมกันผ่าน GitHub โดยบันทึกงานและการตัดสินใจไว้ใน issue, pull request และเอกสารของโปรเจกต์

ทิศทางระยะยาวของเกม (จำลองวิศวกรรมดาวเคราะห์ หุ่นยนต์ อุตสาหกรรม การวิจัย ไปจนถึงอารยธรรมระดับ Kardashev) อยู่ใน `docs/game/game-vision.md`
เอกสารสถาปัตยกรรมและมาตรฐานชุดใหม่เขียนเป็นภาษาอังกฤษ สารบัญทั้งหมดอยู่ที่ `docs/README.md`

## ก่อนเริ่มงานใหม่ทุกครั้ง

1. `git pull` ให้ `main` เป็นปัจจุบัน
2. อ่าน issue ที่เปิดอยู่ (`gh issue list`) และไฟล์ใน `docs/` ก่อน เพราะ session อื่นอาจทำหรือตัดสินใจเรื่องนั้นไปแล้ว
3. ถ้างานที่จะทำมี issue อยู่แล้ว ให้ทำตาม issue นั้นและอ้างเลข issue ใน PR

ผลการตัดสินใจที่ session อื่นต้องรู้ให้เขียนลง issue, PR หรือ `docs/` อย่าเก็บไว้ในแชต

## อ่านตามลำดับนี้

| ลำดับ | เอกสาร | บอกอะไร |
| --- | --- | --- |
| 1 | `docs/game/game-vision.md` | เกมคืออะไร เสาหลัก 10 ข้อ และสิ่งที่เกมต้องไม่เป็น |
| 2 | `docs/development/feature-registry.md` | อะไรสร้างแล้ว อะไรอนุมัติแล้ว อะไรยังเป็นคำถามเปิด |
| 3 | `docs/architecture/system-architecture.md` | โครงสร้างโค้ด กฎสถาปัตยกรรม 10 ข้อ และแผนย้ายโครงสร้าง |
| 4 | `docs/development/development-workflow.md` | ขั้นตอนทำงาน 10 ขั้น สิ่งที่ห้ามทำ และเอกสารที่ต้องอัปเดต |
| 5 | เอกสารของระบบที่จะแตะ | spec ใน `docs/design/`, `docs/architecture/system-interactions.md`, `docs/simulation-interface.md` |

อ่านเพิ่มตามงาน:

- แตะสูตรหรือตัวเลขทางวิทยาศาสตร์: `docs/physics/scientific-standards.md` และ `docs/physics/physics-verification.md`
- เพิ่มเนื้อหาเกม (วัสดุ สิ่งก่อสร้าง สูตรผลิต หุ่นยนต์) หรือแตะเซฟ: `docs/architecture/data-architecture.md`
- เพิ่มข้อมูลที่ผู้เล่นเห็น: `docs/architecture/information-architecture.md`
- เขียนเทสต์: `docs/development/testing-standards.md`
- เลือกงานถัดไป: `docs/development/roadmap.md` และ `docs/development/known-issues.md`

## โครงสร้าง

- `Assets/_Project/Scripts/Simulation/`: C# ล้วน ไม่มี `MonoBehaviour` และไม่อ้าง UnityEngine (`noEngineReferences`) ใช้ `double`
- `Assets/_Project/Scripts/Presentation/`: ภาพ กล้อง UI อ้าง Simulation ได้ แต่ห้ามกลับทาง
- `Assets/_Project/Scripts/Editor/`: สร้างฉาก (`MainSceneSetup`) นำเข้าดาว และ build
- `Assets/_Project/Tests/EditMode/`: unit test ของ Simulation
- `Assets/_Project/Data/`: ข้อมูลเกมที่สร้างจากเครื่องมือ (ตอนนี้มีเฉพาะดาว)
- `prototypes/solar/`: ต้นแบบ Python ของโมเดลแสง ตรวจกับ pvlib
- `tools/`: สคริปต์ของทีม เช่น ตั้งค่า Git ดึงข้อมูล NASA และ `tools/simtests` สำหรับรันเทสต์นอก Unity
- `docs/physics/physics-verification.md`: ผลตรวจฟิสิกส์ของเอกสารดีไซน์ รายการแก้โค้ดเรียงตามความสำคัญ และค่า unit test

Presentation คุยกับ Simulation ผ่าน `IPlanetSimulation` ทางเดียว ระบบย่อยใน Simulation ไม่อ้างถึงกันเอง ตัวประสาน (`PlanetSimulation`) ส่งค่าระหว่างระบบให้

## กฎ

- Unity `6000.6.8f1` เท่านั้น
- ไม่ push ตรงเข้า `main` ทุกอย่างผ่าน PR ตาม `CONTRIBUTING.md`
- สร้างวัตถุในฉากด้วยโค้ดหรือ editor script ไม่แก้ไฟล์ `.unity` / `.prefab` เป็นข้อความ
- สูตรฟิสิกส์ทุกตัวต้องมี unit test และตรงกับ `docs/physics/physics-verification.md`
  ถ้าแก้สูตรใน `prototypes/solar/solar.py` ต้องแก้ C# ให้ตรงกันแล้วรันเทสต์ทั้งสองฝั่ง
- สถานะที่เปลี่ยนได้ทุกอย่างต้องเซฟได้: เพิ่มใน `SimulationState` ขึ้นเลข version และมีเทสต์โหลดเซฟเก่า
- enum ที่อยู่ในเซฟ (`Material`, `BuildingType`, `TaskKind`, `RobotKind` ฯลฯ) เพิ่มสมาชิกได้ที่ท้ายเท่านั้น ห้ามสลับลำดับหรือลบ
- ตัวเลขที่ยังไม่มีแหล่งอ้างอิงให้ติด `TBD` ในคอมเมนต์ ส่วนกติกาที่ไม่ใช่ฟิสิกส์ให้เขียนว่าเป็น game rule
- โฟลเดอร์ที่เปิดอยู่ใน Unity เป็นของคนที่กำลังใช้ editor session อื่นให้ทำงานใน git worktree ของตัวเอง และห้ามหยุดหรือเริ่ม Play mode ของคนอื่นโดยไม่ถาม

## ขั้นตอนทำงานทุกงาน

1. อ่านไฟล์นี้และเอกสารตามลำดับข้างบน
2. ดูสถาปัตยกรรมและ spec ของระบบที่เกี่ยวข้อง
3. ดูโค้ดและเทสต์ที่มีอยู่ก่อนเขียนของใหม่
4. หาว่าระบบไหนส่งค่ามาให้ ระบบไหนอ่านค่าที่เราเขียน และต้องเซฟอะไร
5. เสนอแผนสั้น ๆ ถ้างานเปลี่ยนตัวเกม ต้องให้ผู้กำกับเกมเห็นชอบก่อน
6. ทำเฉพาะขอบเขตที่ได้รับ ปัญหาอื่นที่เจอให้จดเป็นงานต่อ
7. เพิ่มหรือแก้เทสต์
8. ตรวจความถูกต้องทางวิทยาศาสตร์ (หาแหล่งอ้างอิงก่อน แล้วค่อยเขียนโค้ด)
9. อัปเดตเอกสาร โดยเฉพาะ `feature-registry.md`
10. สรุปไฟล์ที่แก้ ผลเทสต์ ข้อจำกัด และงานที่เหลือ

รายละเอียดอยู่ใน `docs/development/development-workflow.md`

## ห้ามทำ

- สร้างระบบซ้ำกับของที่มีอยู่
- เปลี่ยนการตัดสินใจด้านดีไซน์ที่อนุมัติแล้ว (CONFIRMED) โดยไม่บอก
- ใส่ข้อสมมติหรือสูตรที่ไม่มีแหล่งอ้างอิง
- เขียนระบบที่ใช้งานได้อยู่แล้วใหม่โดยไม่มีปัญหาที่แสดงให้เห็นได้
- ข้ามสถาปัตยกรรม เช่น ใส่ Unity ใน Simulation หรือใส่กติกาเกมใน Presentation
- commit ตรงเข้า `main`
- บอกว่างานเสร็จโดยยังไม่ได้ตรวจ ให้บอกตรง ๆ ว่าอะไรยังไม่ได้ตรวจ

## สถานะที่ใช้

- สถานะดีไซน์: **CONFIRMED** (ผู้กำกับเกมอนุมัติ), **PROPOSED** (ข้อเสนอ ยังไม่อนุมัติ), **REJECTED** (ตัดออก), **FUTURE** (ทิศทางที่รับไว้ ยังไม่ออกแบบ)
- สถานะการสร้าง: Not started, Designed, In progress, Implemented, Tested, Verified
- ผู้กำกับเกมเป็นคนตัดสินทิศทางของเกม ผู้ช่วยพัฒนาเพิ่มได้แค่ PROPOSED และคำถามเปิด ห้ามเปลี่ยนเป็น CONFIRMED เอง

## รันเทสต์

- ใน Unity (ผลที่ใช้ตัดสิน): Window → General → Test Runner → EditMode → Run All
- นอก Unity (ตรวจเร็ว): `dotnet test tools/simtests/SimTests.csproj`
- เครื่องมือ Python: `python -m unittest discover tools/exoplanets`
