"""Build the 7-slide NASA demo deck (16:9 PDF) from real gameplay screenshots. Edit TEAM and MEMBERS, then run: python make_slides.py (needs reportlab)."""
import os
from reportlab.lib.pagesizes import landscape
from reportlab.pdfgen import canvas
from reportlab.lib.colors import HexColor
from reportlab.lib.utils import ImageReader

ROOT = os.path.dirname(os.path.abspath(__file__))
IMG = os.path.join(ROOT, "images")
OUT = os.path.join(ROOT, "demo-slides.pdf")

W, H = 1280, 720
BG = HexColor("#101114")
PANEL = HexColor("#1b1c20")
ACCENT = HexColor("#ffc75c")
TEXT = HexColor("#ececee")
MUTED = HexColor("#a6a6ab")

TEAM = "[TEAM NAME]"
MEMBERS = "[Member 1] · [Member 2] · [Member 3]"

c = canvas.Canvas(OUT, pagesize=(W, H))
c.setTitle("Kardashev Ascent - NASA Space Apps 2026 demo")


def page(title, number):
    c.setFillColor(BG); c.rect(0, 0, W, H, stroke=0, fill=1)
    c.setFillColor(ACCENT); c.rect(0, H - 8, W, 8, stroke=0, fill=1)
    if title:
        c.setFillColor(TEXT); c.setFont("Helvetica-Bold", 40); c.drawString(60, H - 80, title)
    c.setFillColor(MUTED); c.setFont("Helvetica", 14)
    c.drawRightString(W - 40, 24, f"Kardashev Ascent · Space Mission Design Game · {number}/7")


def bullets(lines, x, y, size=24, gap=14, width=None):
    for line in lines:
        head = line.startswith("#")
        txt = line[1:] if head else line
        c.setFont("Helvetica-Bold" if head else "Helvetica", size + (2 if head else 0))
        c.setFillColor(ACCENT if head else TEXT)
        prefix = "" if head else "•  "
        # simple word wrap
        words = (prefix + txt).split(" ")
        cur = ""
        maxw = width or 560
        for w in words:
            test = (cur + " " + w).strip()
            if c.stringWidth(test, "Helvetica", size) > maxw:
                c.drawString(x, y, cur); y -= size + 6; cur = "   " + w
            else:
                cur = test
        c.drawString(x, y, cur)
        y -= size + gap
    return y


def image(name, x, y, w):
    img = ImageReader(os.path.join(IMG, name))
    iw, ih = img.getSize()
    h = w * ih / iw
    c.setFillColor(ACCENT); c.rect(x - 3, y - 3, w + 6, h + 6, stroke=0, fill=1)
    c.drawImage(img, x, y, w, h)
    return h


# 1 Title
page(None, 1)
image("01-title.png", 640, 170, 590)
c.setFillColor(TEXT); c.setFont("Helvetica-Bold", 58); c.drawString(60, 520, "Kardashev Ascent")
c.setFillColor(ACCENT); c.setFont("Helvetica", 26)
c.drawString(60, 470, "Run a robotic mission on a real exoplanet,")
c.drawString(60, 438, "with real physics and NASA data.")
c.setFillColor(TEXT); c.setFont("Helvetica-Bold", 22); c.drawString(60, 340, "Team: " + TEAM)
c.setFont("Helvetica", 20); c.drawString(60, 305, MEMBERS)
c.setFillColor(MUTED); c.setFont("Helvetica", 18)
c.drawString(60, 255, "NASA Space Apps Challenge 2026 · Bangkok, Thailand")
c.drawString(60, 225, "Challenge: Space Mission Design Game")
c.setFont("Helvetica-Oblique", 13)
c.drawString(60, 60, "Slides drafted with AI assistance; all images are real gameplay screenshots.")
c.showPage()

# 2 Problem
page("The problem: mission trade-offs stay abstract", 2)
bullets([
    "Power, mass, time, communications and risk are taught one by one.",
    "In a real mission, every choice pulls on all of them at once.",
    "Students rarely get to run a whole mission, from landing site to working industry.",
    "#Our answer",
    "A game where you land on a real world and feel each trade-off through real physics.",
], 60, H - 150, size=24, gap=10, width=1100)
image("07-mountain-tunnel.png", 360, 50, 560)
c.showPage()

# 3 How it plays
page("How it plays", 3)
y = bullets([
    "Pick a real planet (NASA Exoplanet Archive) or Mars.",
    "Read the orbital brief; land on safe, flat ground.",
    "Place solar panels: tilt and facing set their real yield.",
    "Robots build, mine, survey and measure on their own.",
    "Refine metals in real steps; grow on the Kardashev scale.",
], 60, H - 150, size=22, width=540)
image("02-planet-hud.png", 640, 200, 590)
c.showPage()

# 4 Trade-offs
page("Every choice is a real trade-off", 4)
bullets([
    "#Power",
    "Day and night, clouds, haze, battery, brownouts.",
    "#Mass and materials",
    "Finite ore; refining costs real energy (13.5 kWh per kg aluminium).",
    "#Time and robots",
    "Batteries, chargers, routes around mountains and canyons.",
    "#Knowledge",
    "Measure the planet first, or design with safety margins.",
], 60, H - 140, size=20, gap=8, width=540)
image("03-manufacturing.png", 640, 330, 590)
image("06-power.png", 760, 70, 360)
c.showPage()

# 5 NASA data
page("NASA data makes it real", 5)
bullets([
    "#NASA Exoplanet Archive",
    "9 real exoplanets: orbit, radius, mass, star light and metals.",
    "#NASA POWER (2001-2025 climatology)",
    "Clouds, 2 m air temperature, ground albedo, aerosol haze by latitude and month.",
    "#NSSDC Mars Fact Sheet · MMRTG · Perseverance battery",
    "Mars, the lander's RTG power, the robots' 2.4 kWh batteries.",
    "#Checked",
    "Solar model verified against pvlib; about 950 automated tests.",
], 60, H - 140, size=20, gap=8, width=1150)
c.showPage()

# 6 Learning
page("Learning by doing", 6)
bullets([
    "Measure the latitude from a noon shadow; estimate grades from rock cores.",
    "Research tree: air pressure, boiling point, gravity (pendulum), seismic survey.",
    "Built-in science calculator, guide book, 'i' notes and hover cards.",
    "Failure explains itself: brownouts, stranded robots, paused refining.",
], 60, H - 150, size=21, width=540)
image("04-research-tree.png", 640, 180, 590)
c.showPage()

# 7 Future
page("Where it goes next", 7)
bullets([
    "Team play for up to 4: power, robots, science and industry roles.",
    "Mission scenarios, end-of-mission reports, leaderboards.",
    "More energy sources, orbital and star-scale engineering.",
    "#Try it",
    "github.com/PheetTaGoRuss/kardashev-ascent",
], 60, H - 150, size=22, width=540)
image("05-canyon-site.png", 640, 200, 590)
c.showPage()
c.save()
print(OUT)
