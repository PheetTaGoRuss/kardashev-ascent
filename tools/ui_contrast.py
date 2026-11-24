"""WCAG contrast of the UI palette (UI information design, stage 1): body text 4.5:1, large text 3:1.
Colours are read from Assets/_Project/Scripts/Presentation/UI/UiPalette.cs. Run: python tools/ui_contrast.py"""
import re, sys

def lin(c):
    return c / 12.92 if c <= 0.03928 else ((c + 0.055) / 1.055) ** 2.4

def lum(rgb):
    r, g, b = (lin(x) for x in rgb)
    return 0.2126 * r + 0.7152 * g + 0.0722 * b

def blend(fg, bg, a):
    return tuple(f * a + b * (1 - a) for f, b in zip(fg, bg))

def ratio(a, b):
    la, lb = lum(a), lum(b)
    hi, lo = max(la, lb), min(la, lb)
    return (hi + 0.05) / (lo + 0.05)

src = open('Assets/_Project/Scripts/Presentation/UI/UiPalette.cs', encoding='utf-8').read()
cols = {}
for name, args in re.findall(r'static readonly Color (\w+) = new Color\(([^)]*)\)', src):
    v = [float(x.strip().rstrip('f')) for x in args.split(',')]
    cols[name] = (v[0], v[1], v[2], v[3] if len(v) > 3 else 1.0)
panel = blend(cols['PanelSolid'][:3], (0.0, 0.0, 0.0), cols['PanelSolid'][3])   # on a dark scene behind
rows = []
for name in ('Text', 'Muted', 'Accent', 'Cool', 'Good', 'Warning', 'Critical'):
    rows.append((name, ratio(cols[name][:3], panel)))
rows.append(('OnAccent on Accent (buttons)', ratio(cols['OnAccent'][:3], cols['Accent'][:3])))
fail = False
for name, r in rows:
    need = 4.5
    ok = r >= need
    fail |= not ok
    print(f'{name:32s} {r:5.2f}:1  {"ok" if ok else "BELOW 4.5"}')
sys.exit(1 if fail else 0)
