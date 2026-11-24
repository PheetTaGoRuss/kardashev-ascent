"""Earth's day-to-day weather variability by latitude and month from NASA POWER daily data, baked into C#.

    python tools/nasa_power/fetch_daily_variability.py            # fetch (68 requests) and generate
    python tools/nasa_power/fetch_daily_variability.py --offline  # regenerate C# from the cache

Daily T2M (air temperature, deg C), PS (surface pressure, kPa), WS10M (wind at 10 m, m/s), ALLSKY_SFC_SW_DWN and
CLRSKY_SFC_SW_DWN (their ratio is the day's cloud factor), 2001-2025, MERRA-2 via NASA POWER. 4 longitudes (every 90 deg)
per 10-degree latitude row, like the monthly tools. For each latitude and month the table gives:
  * the standard deviation of the daily ANOMALY (the day minus the multi-year mean for that day of the year, smoothed
    over 15 days, so the seasonal cycle is not counted as weather),
  * the lag-1 autocorrelation of that anomaly (how much today predicts tomorrow),
  * for wind, the mean and the 98th percentile of the daily speed.
The statistics are pooled over the longitudes and years. Standard library only.
"""
from __future__ import annotations

import argparse
import json
import math
import sys
import time
import urllib.parse
import urllib.request
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
CACHE = HERE / "cache" / "daily_variability_points.json"
OUT = ROOT / "Assets" / "_Project" / "Scripts" / "Simulation" / "Climate" / "WeatherVariabilityTable.cs"
API = "https://power.larc.nasa.gov/api/temporal/daily/point"
LATITUDES = list(range(-80, 81, 10))
LONGITUDES = [-180, -90, 0, 90]
PARAMS = ["T2M", "PS", "WS10M", "ALLSKY_SFC_SW_DWN", "CLRSKY_SFC_SW_DWN"]
START, END = "20010101", "20251231"


def fetch_point(lat: int, lon: int) -> dict:
    query = urllib.parse.urlencode({
        "parameters": ",".join(PARAMS), "community": "RE", "latitude": lat, "longitude": lon,
        "start": START, "end": END, "format": "JSON"})
    request = urllib.request.Request(f"{API}?{query}", headers={"User-Agent": "kardashev-ascent-data-tool"})
    for attempt in range(4):
        try:
            with urllib.request.urlopen(request, timeout=120) as response:
                data = json.loads(response.read().decode("utf-8"))
            return {p: data["properties"]["parameter"][p] for p in PARAMS}
        except (OSError, ValueError, KeyError) as error:
            if attempt == 3:
                raise
            print(f"  retry {lat},{lon}: {error}", file=sys.stderr)
            time.sleep(2 ** (attempt + 1))
    raise RuntimeError("unreachable")


def series(point: dict) -> dict[str, list[tuple[str, float]]]:
    """Daily values, dates in order, missing values (-999) dropped; the cloud factor is derived."""
    out = {}
    for name in ("T2M", "PS", "WS10M"):
        out[name] = [(d, v) for d, v in sorted(point[name].items()) if v > -900]
    all_sky, clr = point["ALLSKY_SFC_SW_DWN"], point["CLRSKY_SFC_SW_DWN"]
    out["CLOUD"] = [(d, min(1.2, a / c)) for d, a in sorted(all_sky.items())
                    if a > -900 and clr.get(d, -999) > 0.05 and (c := clr[d]) > 0.05]
    return out


def anomalies(values: list[tuple[str, float]]) -> list[tuple[str, float]]:
    """The day minus the multi-year mean for its day of the year, smoothed over 15 days (circular)."""
    sums = [0.0] * 366
    counts = [0] * 366
    for d, v in values:
        doy = day_of_year(d)
        sums[doy - 1] += v
        counts[doy - 1] += 1
    clim = [sums[i] / counts[i] if counts[i] else float("nan") for i in range(366)]
    smooth = []
    for i in range(366):
        window = [clim[(i + k) % 366] for k in range(-7, 8) if not math.isnan(clim[(i + k) % 366])]
        smooth.append(sum(window) / len(window) if window else float("nan"))
    return [(d, v - smooth[day_of_year(d) - 1]) for d, v in values]


def day_of_year(date: str) -> int:
    y, m, d = int(date[:4]), int(date[4:6]), int(date[6:])
    cum = [0, 31, 59, 90, 120, 151, 181, 212, 243, 273, 304, 334]
    leap = 1 if (y % 4 == 0 and (y % 100 != 0 or y % 400 == 0)) and m > 2 else 0
    return cum[m - 1] + d + leap


def month_of(date: str) -> int:
    return int(date[4:6]) - 1


def stats(anoms: list[tuple[str, float]]) -> tuple[list[float], list[float]]:
    """Per month: standard deviation and lag-1 autocorrelation, pooled over the points given (consecutive days)."""
    sd, rho = [], []
    for m in range(12):
        vals = [(d, v) for d, v in anoms if month_of(d) == m]
        n = len(vals)
        if n == 0:   # polar night: no sunlight, no cloud factor (the table says 0 and the game uses its default)
            sd.append(0.0); rho.append(0.0)
            continue
        mean = sum(v for _, v in vals) / n
        var = sum((v - mean) ** 2 for _, v in vals) / n
        sd.append(math.sqrt(var))
        by_date = {d: v for d, v in anoms}
        num = den = 0.0
        for d, v in vals:
            nxt = next_date(d)
            if nxt in by_date and month_of(nxt) == m:
                num += (v - mean) * (by_date[nxt] - mean)
                den += (v - mean) ** 2
        rho.append(num / den if den > 0 else 0.0)
    return sd, rho


def next_date(date: str) -> str:
    y, m, d = int(date[:4]), int(date[4:6]), int(date[6:])
    dim = [31, 29 if (y % 4 == 0 and (y % 100 != 0 or y % 400 == 0)) else 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31]
    d += 1
    if d > dim[m - 1]:
        d, m = 1, m + 1
    if m > 12:
        m, y = 1, y + 1
    return f"{y:04d}{m:02d}{d:02d}"


def wind_stats(values: list[tuple[str, float]]) -> tuple[list[float], list[float]]:
    mean, p98 = [], []
    for m in range(12):
        v = sorted(x for d, x in values if month_of(d) == m)
        mean.append(sum(v) / len(v))
        p98.append(v[min(len(v) - 1, int(0.98 * len(v)))])
    return mean, p98


def pool(per_point: list[list[float]]) -> list[float]:
    return [sum(p[m] for p in per_point) / len(per_point) for m in range(12)]


def rows(points: dict) -> dict[str, list[list[float]]]:
    out = {k: [] for k in ("t_sd", "t_rho", "p_sd", "p_rho", "c_sd", "c_rho", "w_mean", "w_p98", "w_rho")}
    for lat in LATITUDES:
        acc = {k: [] for k in out}
        for lon in LONGITUDES:
            s = series(points[f"{lat},{lon}"])
            for key, name, scale in (("t", "T2M", 1.0), ("p", "PS", 1000.0), ("c", "CLOUD", 1.0), ("w", "WS10M", 1.0)):
                sd, rho = stats(anomalies(s[name]))
                if key == "w":
                    acc["w_rho"].append(rho)
                else:
                    acc[key + "_sd"].append([x * scale for x in sd])
                    acc[key + "_rho"].append(rho)
            mean, p98 = wind_stats(s["WS10M"])
            acc["w_mean"].append(mean)
            acc["w_p98"].append(p98)
        for k in out:
            out[k].append(pool(acc[k]))
    return out


def write_csharp(table: dict[str, list[list[float]]], retrieved: str) -> None:
    def block(name: str, doc: str, key: str, fmt: str) -> str:
        body = "\n".join(
            f"            /* {lat:+3d} */ new[] {{ {', '.join(format(v, fmt) for v in row)} }},"
            for lat, row in zip(LATITUDES, table[key]))
        return f"        /// <summary>{doc}</summary>\n        public static readonly double[][] {name} =\n        {{\n{body}\n        }};\n"
    parts = [
        block("TemperatureSd", "Standard deviation of the daily air-temperature anomaly, K.", "t_sd", ".2f"),
        block("TemperatureRho", "Lag-1 autocorrelation of the daily air-temperature anomaly.", "t_rho", ".3f"),
        block("PressureSd", "Standard deviation of the daily surface-pressure anomaly, Pa.", "p_sd", ".1f"),
        block("PressureRho", "Lag-1 autocorrelation of the daily surface-pressure anomaly.", "p_rho", ".3f"),
        block("CloudSd", "Standard deviation of the daily cloud-factor anomaly (all-sky over clear-sky sunlight).", "c_sd", ".3f"),
        block("CloudRho", "Lag-1 autocorrelation of the daily cloud-factor anomaly.", "c_rho", ".3f"),
        block("WindMean", "Mean daily wind speed at 10 m, m/s.", "w_mean", ".2f"),
        block("WindP98", "98th percentile of the daily wind speed at 10 m, m/s (a storm day is above it).", "w_p98", ".2f"),
        block("WindRho", "Lag-1 autocorrelation of the daily wind-speed anomaly.", "w_rho", ".3f"),
    ]
    OUT.write_text(f"""// <auto-generated> by tools/nasa_power/fetch_daily_variability.py. Do not edit by hand. </auto-generated>
namespace KardashevAscent.Simulation.Climate
{{
    /// <summary>
    /// NASA POWER daily data ({START[:4]}-{END[:4]}, MERRA-2), retrieved {retrieved}: how much Earth's weather varies from day to day, by latitude
    /// and month (anomaly = the day minus the 15-day-smoothed multi-year mean of its day of the year), pooled over 4 longitudes.
    /// Rows: latitude {LATITUDES[0]}..{LATITUDES[-1]} deg every {LATITUDES[1] - LATITUDES[0]} deg. Columns: January..December.
    /// </summary>
    static class WeatherVariabilityTable
    {{
        public const double FirstLatitudeDeg = {LATITUDES[0]:.1f};
        public const double StepDeg = {LATITUDES[1] - LATITUDES[0]:.1f};

{chr(10).join(parts)}    }}
}}
""", encoding="utf-8")


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--offline", action="store_true")
    args = ap.parse_args()
    CACHE.parent.mkdir(parents=True, exist_ok=True)
    if args.offline:
        points = json.loads(CACHE.read_text(encoding="utf-8"))
    else:
        points = json.loads(CACHE.read_text(encoding="utf-8")) if CACHE.exists() else {}
        for lat in LATITUDES:
            for lon in LONGITUDES:
                key = f"{lat},{lon}"
                if key in points:
                    continue
                print("fetch", key, file=sys.stderr)
                points[key] = fetch_point(lat, lon)
                CACHE.write_text(json.dumps(points), encoding="utf-8")
    write_csharp(rows(points), time.strftime("%Y-%m-%d"))
    print("wrote", OUT)


if __name__ == "__main__":
    main()
