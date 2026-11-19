import io, os, re, sys, glob

os.chdir(sys.argv[1])
rows = []
const = re.compile(r'(?:public|internal)?\s*(?:static\s+)?(?:readonly\s+)?(?:const\s+)?(?:double|int)\s+([A-Z][A-Za-z0-9_]*)\s*=\s*([^;]+);\s*(?://\s*(.*))?')
for path in sorted(glob.glob('Assets/_Project/Scripts/Simulation/**/*.cs', recursive=True)):
    lines = io.open(path, encoding='utf-8').read().split('\n')
    for i, line in enumerate(lines):
        if 'TBD' not in line and not (i > 0 and 'TBD' in lines[i - 1] and line.strip().startswith(('public', 'const'))):
            continue
        # look at this line and the next two for the declaration
        for j in range(i, min(i + 3, len(lines))):
            m = const.search(lines[j])
            if m and ('const' in lines[j] or 'static readonly' in lines[j]):
                doc = lines[i].strip().lstrip('/').replace('<summary>', '').replace('</summary>', '').strip()
                rows.append((path.replace(chr(92), '/').replace('Assets/_Project/Scripts/Simulation/', ''), m.group(1), m.group(2).strip(), doc))
                break

seen = set()
out = []
for r in rows:
    key = (r[0], r[1])
    if key in seen:
        continue
    seen.add(key)
    out.append(r)

text = '''# Balance placeholders

Every number below is a placeholder (`TBD` in the code): it makes the mechanic run and carries no balance claim. The game director sets them, or approves a balancing pass (open question OQ-12). This list is generated from the code by searching for `TBD` next to a constant; numbers inside formulas or tables that are marked `TBD` in prose are not all listed.

How to use it: change the value in the code file shown, keep the `TBD` mark until the number is sourced or approved, and run the tests (they assert rules, not these values, wherever possible).

| File (under `Assets/_Project/Scripts/Simulation/`) | Constant | Value | What it is |
| --- | --- | --- | --- |
'''
for f, name, value, doc in out:
    doc = doc.replace('|', '/').replace('TBD', '').strip(' .(),;')
    text += f'| `{f}` | `{name}` | `{value}` | {doc[:140]} |\n'
text += f'\n{len(out)} constants listed. Generated 2026-11-18 by `tools/balance_sheet.py` (run it again after changing the code).\n'
io.open('docs/development/balance-placeholders.md', 'w', encoding='utf-8', newline='\n').write(text)
print(len(out))
