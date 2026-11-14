#!/usr/bin/env python3
"""Checks that the Simulation namespaces keep their dependency order (docs/architecture/system-architecture.md).

A namespace may only use the ones listed for it below. Run from the repository root:  python tools/check_layers.py
Exit code 1 and a list of offending files when a rule is broken; used by CI.
"""
import glob
import re
import sys

BASE = 'Assets/_Project/Scripts/Simulation/'

# namespace -> namespaces it may depend on. Lower tiers first; no cycles.
ALLOWED = {
    'Civilization': set(),
    'Climate': set(),
    'Energy': set(),
    'Solar': set(),
    'Planet': {'Civilization', 'Solar'},
    'Resources': {'Planet', 'Solar'},
    'Robots': {'Energy', 'Planet', 'Resources'},
    'Game': {'Civilization', 'Climate', 'Energy', 'Planet', 'Resources', 'Robots', 'Solar'},
}
NAMES = '|'.join(ALLOWED)


def uses(text):
    text = re.sub(r'//.*', '', text)
    found = set(re.findall(r'using KardashevAscent\.Simulation\.(\w+);', text))
    found |= set(re.findall(r'(?<![\w.])(%s)\.[A-Z]\w+' % NAMES, text))
    return found


def main():
    bad = []
    for ns, allowed in ALLOWED.items():
        for path in sorted(glob.glob(BASE + ns + '/*.cs')):
            with open(path, encoding='utf-8') as f:
                used = uses(f.read()) - {ns}
            for other in sorted(used - allowed):
                if other in ALLOWED:
                    bad.append('%s: %s must not use %s' % (path, ns, other))
    for line in bad:
        print(line)
    if bad:
        print('\nlayer check FAILED (%d)' % len(bad))
        return 1
    print('layer check passed')
    return 0


if __name__ == '__main__':
    sys.exit(main())
