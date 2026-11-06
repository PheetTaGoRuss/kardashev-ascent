"""Fetch real planets from the NASA Exoplanet Archive and write the game's planet data.

    python tools/exoplanets/fetch_exoplanets.py            # fetch, convert, write JSON
    python tools/exoplanets/fetch_exoplanets.py --offline  # rebuild JSON from the cached CSV

Standard library only. Reads tools/exoplanets/planets.json (which planets, plus game-assumed values),
caches the raw archive answer in tools/exoplanets/cache/pscomppars.csv, and writes
Assets/_Project/Data/Planets/planets.json for Unity.
"""
from __future__ import annotations

import argparse
import csv
import datetime as dt
import io
import json
import sys
import urllib.parse
import urllib.request
from pathlib import Path

from convert import COLUMNS, convert

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
CONFIG = HERE / "planets.json"
CACHE = HERE / "cache" / "pscomppars.csv"
OUTPUT = ROOT / "Assets" / "_Project" / "Data" / "Planets" / "planets.json"
TAP_URL = "https://exoplanetarchive.ipac.caltech.edu/TAP/sync"


def adql_for(names: list[str]) -> str:
    quoted = ",".join("'" + n.replace("'", "''") + "'" for n in names)
    return f"select {','.join(COLUMNS)} from pscomppars where pl_name in ({quoted})"


def fetch_csv(query: str) -> str:
    url = TAP_URL + "?" + urllib.parse.urlencode({"query": query, "format": "csv"})
    request = urllib.request.Request(url, headers={"User-Agent": "kardashev-ascent-data-tool"})
    with urllib.request.urlopen(request, timeout=60) as response:
        return response.read().decode("utf-8")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--offline", action="store_true", help="use the cached CSV instead of the network")
    parser.add_argument("--out", type=Path, default=OUTPUT)
    args = parser.parse_args()

    config = json.loads(CONFIG.read_text(encoding="utf-8"))
    entries = config["planets"]
    names = [e["archiveName"] for e in entries if e.get("archiveName")]

    if args.offline:
        if not CACHE.exists():
            print(f"No cache at {CACHE}; run once without --offline.", file=sys.stderr)
            return 1
        text = CACHE.read_text(encoding="utf-8")
        retrieved = CACHE.with_suffix(".date").read_text(encoding="utf-8").strip() \
            if CACHE.with_suffix(".date").exists() else ""
    else:
        print(f"Querying NASA Exoplanet Archive for {len(names)} planets ...")
        text = fetch_csv(adql_for(names))
        retrieved = dt.date.today().isoformat()
        CACHE.parent.mkdir(parents=True, exist_ok=True)
        CACHE.write_text(text, encoding="utf-8")
        CACHE.with_suffix(".date").write_text(retrieved + "\n", encoding="utf-8")

    rows = {r["pl_name"]: r for r in csv.DictReader(io.StringIO(text))}
    missing = [n for n in names if n not in rows]
    if missing:
        print("Not found in pscomppars (check the exact pl_name on the archive):", file=sys.stderr)
        for n in missing:
            host = n.rsplit(" ", 1)[0]
            print(f"  {n!r}  -> try: select pl_name from pscomppars where hostname like '{host}%'",
                  file=sys.stderr)
        return 1

    planets = []
    for entry in entries:
        name = entry.get("archiveName")
        planet = convert(rows[name] if name else None, entry, retrieved)
        for warning in planet.pop("_warnings"):
            print("warning:", warning)
        planets.append(planet)

    document = {
        "generatedBy": "tools/exoplanets/fetch_exoplanets.py",
        "archive": "NASA Exoplanet Archive (https://exoplanetarchive.ipac.caltech.edu), table pscomppars",
        "retrieved": retrieved,
        "units": "SI unless the field name says otherwise; angles in degrees",
        "planets": planets,
    }
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(document, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"Wrote {len(planets)} planets to {args.out.relative_to(ROOT) if args.out.is_relative_to(ROOT) else args.out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
