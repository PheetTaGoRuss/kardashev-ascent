# ทำงานร่วมกันในโปรเจกต์นี้

คู่มือนี้สำหรับทุกคนในทีม ทำตามขั้นที่ 1–3 ครั้งเดียวตอนเริ่ม แล้วใช้ขั้นที่ 4 ทุกวัน

## 1. ติดตั้ง

- **Git** และ **Git LFS** (หรือ [GitHub Desktop](https://desktop.github.com) ซึ่งมี LFS ในตัว)
- **Unity `6000.6.8f1`** ผ่าน Unity Hub ต้องเป็นเวอร์ชันนี้เท่านั้น ดูได้ที่ `ProjectSettings/ProjectVersion.txt`

## 2. ดึงโปรเจกต์ลงเครื่อง

```bash
git clone https://github.com/PheetTaGoRuss/kardashev-ascent.git
```

แล้วเปิด Unity Hub → **Add → Add project from disk** → เลือกโฟลเดอร์ที่ clone มา

## 3. ตั้งค่า Git สำหรับ Unity (ครั้งเดียวต่อเครื่อง)

สคริปต์นี้เปิด Git LFS และตั้ง **Unity Smart Merge** ให้ Git รวมไฟล์ฉาก (`.unity`) และ prefab ได้ถูกต้อง
โดยไม่ต้องตั้งเอง รันในโฟลเดอร์โปรเจกต์:

Windows (PowerShell):

```bash
powershell -ExecutionPolicy Bypass -File tools\setup-git.ps1
```

macOS / Linux / Git Bash:

```bash
bash tools/setup-git.sh
```

ถ้าติดตั้ง Unity ไว้ที่อื่น ให้ส่งที่อยู่ของ `UnityYAMLMerge` ไปด้วย เช่น
`bash tools/setup-git.sh "/path/to/UnityYAMLMerge"`

## 4. ขั้นตอนทำงานทุกวัน

1. อัปเดต `main`: `git switch main` แล้ว `git pull`
2. สร้าง branch ตามงาน: `git switch -c feature/<ชื่องาน>`
3. ทำงานใน Unity บันทึกฉาก (Ctrl+S) แล้ว commit: `git add -A` และ `git commit -m "<ทำอะไร>"`
4. ส่งขึ้น GitHub: `git push -u origin feature/<ชื่องาน>`
5. เปิด Pull Request บน GitHub (หรือ `gh pr create --fill`) ให้เพื่อนตรวจ แล้ว merge

ตั้งชื่อ branch: `feature/...` งานใหม่, `fix/...` แก้บั๊ก, `art/...` งานภาพหรือเสียง

## กฎของทีม

1. `git pull` ก่อนเริ่มงานทุกครั้ง
2. ไม่ push ตรงเข้า `main` ทุกอย่างผ่าน Pull Request
3. ใช้ Unity `6000.6.8f1` เท่านั้น
4. commit ไฟล์ `.meta` คู่กับไฟล์จริงเสมอ และย้าย ลบ หรือเปลี่ยนชื่อไฟล์ใน Unity เท่านั้น
5. แต่ละฉากมีเจ้าของหนึ่งคน บอกในทีมก่อนแก้ฉากของคนอื่น ของที่ใช้ซ้ำให้ทำเป็น prefab

## เมื่อ merge แล้ว conflict

- **ไฟล์ `.cs`:** เปิดไฟล์ หาช่วง `<<<<<<<` ถึง `>>>>>>>` เลือกโค้ดที่ถูก แล้ว `git add` ไฟล์นั้น
- **ไฟล์ฉากหรือ prefab:** Smart Merge รวมให้เองเมื่อแก้คนละค่า ถ้าสองคนแก้ค่าเดียวกัน
  Git จะแจ้ง conflict และพิมพ์ชื่อค่าที่ชน (เช่น `Transform.m_LocalPosition`) ไฟล์จะใช้ค่าจาก branch ที่ merge เข้ามา
  ให้เปิดฉากใน Unity ตั้งค่าที่ถูก บันทึก แล้ว `git add` ไฟล์นั้น
  หรือเลือกทั้งไฟล์ด้วย `git checkout --ours <ไฟล์>` (ของเรา) หรือ `git checkout --theirs <ไฟล์>` (ของที่ merge เข้ามา)
- เสร็จแล้ว `git commit` เพื่อจบการ merge ถ้าไม่แน่ใจ `git merge --abort` ยกเลิกแล้วถามทีมก่อน

## สิ่งที่ไม่ขึ้น repo

`Library/`, `Temp/`, `Logs/`, `UserSettings/` และไฟล์ project ของ IDE ถูกกันไว้ใน `.gitignore` แล้ว
ไฟล์ภาพ เสียง โมเดล 3 มิติ และฟอนต์ เก็บผ่าน Git LFS ตาม `.gitattributes` (LFS มีโควตา ดูที่ Settings → Billing)
