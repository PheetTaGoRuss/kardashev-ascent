using System;
using System.Collections.Generic;
using System.Linq;

namespace KardashevAscent.Simulation.Knowledge
{
    /// <summary>What changed in the colony's known state when a record was added (K5 turns these into reports).</summary>
    public enum KnowledgeEventKind { FirstMeasured, RangeNarrowed, Discrepancy, Stale }

    public readonly struct KnowledgeEvent
    {
        public readonly KnowledgeEventKind Kind;
        public readonly Subject Subject;
        public readonly Quantity Quantity;
        /// <summary>The value shown before (NaN when none) and its half-range, and the new record.</summary>
        public readonly double OldValue, OldUncertainty;
        public readonly Observation New;
        public KnowledgeEvent(KnowledgeEventKind kind, Subject subject, Quantity quantity, double oldValue, double oldUncertainty, Observation now)
        { Kind = kind; Subject = subject; Quantity = quantity; OldValue = oldValue; OldUncertainty = oldUncertainty; New = now; }
    }

    /// <summary>
    /// The colony's knowledge store (K1, decided 2026-11-25; information-architecture.md sections 4 and 6, I2 and I3): the history of immutable records per (subject, quantity), and the
    /// one the display shows. Conflicting records are all kept; the shown one is the most recent of the highest confidence among the known ones. A record a robot holds and has not
    /// synchronised has no known-at time and is never shown. History is kept daily for a year, then weekly (game rule). Pure bookkeeping, deterministic.
    /// </summary>
    public sealed class KnowledgeBase
    {
        public const double DayHours = 24.0, WeekHours = 24.0 * 7.0, YearHours = 24.0 * 365.0;
        /// <summary>Thin a series once it has this many records.</summary>
        const int CompactAt = 96;

        readonly Dictionary<(Subject, Quantity), List<Observation>> series = new Dictionary<(Subject, Quantity), List<Observation>>();
        readonly List<KnowledgeEvent> events = new List<KnowledgeEvent>();

        public int Count { get; private set; }

        /// <summary>Events since the last <see cref="TakeEvents"/> (first measured, range narrowed, discrepancy).</summary>
        public IReadOnlyList<KnowledgeEvent> PendingEvents => events;
        public List<KnowledgeEvent> TakeEvents() { var l = new List<KnowledgeEvent>(events); events.Clear(); return l; }

        /// <summary>Add a record. A known record may raise an event; an unsynced one raises none until it is synchronised.</summary>
        public void Add(Observation o, double now)
        {
            var key = (o.Subject, o.Quantity);
            if (!series.TryGetValue(key, out var list)) series[key] = list = new List<Observation>();
            Observation? before = o.IsKnown ? ShownOf(list) : null;
            Insert(list, o);
            Count++;
            if (o.IsKnown) Raise(before, o, list);
            if (list.Count >= CompactAt) Compact(list, now);
        }

        static void Insert(List<Observation> list, Observation o)
        {
            int i = list.Count;
            while (i > 0 && list[i - 1].ObservedAt > o.ObservedAt) i--;
            list.Insert(i, o);
        }

        void Raise(Observation? before, Observation now, List<Observation> list)
        {
            if (Quantities.IsTimeVarying(now.Quantity)) { if (!before.HasValue) events.Add(new KnowledgeEvent(KnowledgeEventKind.FirstMeasured, now.Subject, now.Quantity, double.NaN, 0.0, now)); return; }
            if (!before.HasValue) { events.Add(new KnowledgeEvent(KnowledgeEventKind.FirstMeasured, now.Subject, now.Quantity, double.NaN, 0.0, now)); return; }
            Observation old = before.Value;
            bool estimate(Observation x) => x.Source.Kind == SourceKind.Assumed || x.Source.Kind == SourceKind.Derived || x.Source.Kind == SourceKind.Dataset;
            // a measurement over an estimate is the first time it is measured
            if (!estimate(now) && estimate(old) && Better(now, old)) { events.Add(new KnowledgeEvent(KnowledgeEventKind.FirstMeasured, now.Subject, now.Quantity, old.Value, old.Uncertainty, now)); return; }
            // a discrepancy: a new measured record outside the ranges of the one shown (a science moment)
            if (!estimate(now) && !estimate(old) && Math.Abs(now.Value - old.Value) > now.Uncertainty + old.Uncertainty + 1e-12) { events.Add(new KnowledgeEvent(KnowledgeEventKind.Discrepancy, now.Subject, now.Quantity, old.Value, old.Uncertainty, now)); return; }
            if (Better(now, old) && old.Uncertainty > 0.0 && now.Uncertainty <= 0.5 * old.Uncertainty) events.Add(new KnowledgeEvent(KnowledgeEventKind.RangeNarrowed, now.Subject, now.Quantity, old.Value, old.Uncertainty, now));
        }

        /// <summary>The ordering of the conflict rule: higher confidence first, then the more recent observation.</summary>
        static bool Better(Observation a, Observation b) => a.Confidence != b.Confidence ? a.Confidence > b.Confidence : a.ObservedAt >= b.ObservedAt;

        static Observation? ShownOf(List<Observation> list)
        {
            Observation? best = null;
            foreach (Observation o in list)
                if (o.IsKnown && (!best.HasValue || Better(o, best.Value))) best = o;
            return best;
        }

        /// <summary>The record the display shows for a subject and quantity: the most recent of the highest confidence among the known ones, or null.</summary>
        public Observation? Shown(Subject subject, Quantity quantity) => series.TryGetValue((subject, quantity), out var l) ? ShownOf(l) : null;

        /// <summary>All records of a series, oldest first (known and unsynced).</summary>
        public IReadOnlyList<Observation> History(Subject subject, Quantity quantity) => series.TryGetValue((subject, quantity), out var l) ? l : (IReadOnlyList<Observation>)Array.Empty<Observation>();

        /// <summary>The known records of a series that disagree with the shown one beyond both uncertainties (listed in the Knowledge window).</summary>
        public List<Observation> Conflicts(Subject subject, Quantity quantity)
        {
            var result = new List<Observation>();
            Observation? shown = Shown(subject, quantity);
            if (!shown.HasValue) return result;
            foreach (Observation o in History(subject, quantity))
                if (o.IsKnown && !o.Equals(shown.Value) && Math.Abs(o.Value - shown.Value.Value) > o.Uncertainty + shown.Value.Uncertainty) result.Add(o);
            return result;
        }

        /// <summary>Every (subject, quantity) with at least one record, in a fixed order.</summary>
        public List<(Subject Subject, Quantity Quantity)> Keys()
        {
            var keys = series.Keys.ToList();
            keys.Sort((a, b) => { int c = a.Item1.CompareTo(b.Item1); return c != 0 ? c : a.Item2.CompareTo(b.Item2); });
            return keys;
        }

        public List<Observation> Unsynced(int robotId) => series.Values.SelectMany(l => l).Where(o => !o.IsKnown && o.Source.Kind == SourceKind.Robot && o.Source.Id == robotId).ToList();

        /// <summary>A robot synchronises (or its wreck is recovered with the electronics intact): its unsynced records become known at this time.</summary>
        public int Sync(int robotId, double now)
        {
            int n = 0;
            foreach (var key in series.Keys.ToList())
            {
                List<Observation> list = series[key];
                for (int i = 0; i < list.Count; i++)
                {
                    Observation o = list[i];
                    if (o.IsKnown || o.Source.Kind != SourceKind.Robot || o.Source.Id != robotId) continue;
                    Observation? before = ShownOf(list);
                    list[i] = o.WithKnownAt(now); n++;
                    Raise(before, list[i], list);
                }
            }
            return n;
        }

        /// <summary>A robot is lost with its electronics gone: what it held is dropped (never known).</summary>
        public int DropUnsynced(int robotId)
        {
            int n = 0;
            foreach (var list in series.Values) n += list.RemoveAll(o => !o.IsKnown && o.Source.Kind == SourceKind.Robot && o.Source.Id == robotId);
            Count -= n;
            return n;
        }

        /// <summary>Daily for the last year, weekly before that (game rule): within a bucket the best (highest confidence, then latest) record is kept; unsynced records are never dropped.</summary>
        static void Compact(List<Observation> list, double now)
        {
            var keep = new List<Observation>();
            var buckets = new Dictionary<long, Observation>();
            foreach (Observation o in list)
            {
                if (!o.IsKnown) { keep.Add(o); continue; }
                double age = now - o.ObservedAt;
                long bucket = age <= YearHours ? (long)Math.Floor(o.ObservedAt / DayHours) : -1 - (long)Math.Floor(o.ObservedAt / WeekHours);
                if (!buckets.TryGetValue(bucket, out Observation cur) || Better(o, cur)) buckets[bucket] = o;
            }
            keep.AddRange(buckets.Values);
            keep.Sort((a, b) => a.ObservedAt.CompareTo(b.ObservedAt));
            list.Clear(); list.AddRange(keep);
        }

        /// <summary>Total records held now.</summary>
        public int TotalRecords() => series.Values.Sum(l => l.Count);

        // ---- freshness and stale reports ----

        public static double AgeHours(Observation o, double now) => Math.Max(0.0, now - o.ObservedAt);
        public static bool IsStale(Observation o, double now) => AgeHours(o, now) > Quantities.StaleAfterHours(o.Quantity);

        readonly HashSet<(Subject, Quantity)> staleReported = new HashSet<(Subject, Quantity)>();

        /// <summary>Records that have just gone stale and are worth telling about (once each, until they are measured again).</summary>
        public List<Observation> NewlyStale(double now)
        {
            var result = new List<Observation>();
            foreach (var key in Keys())
            {
                Observation? shown = Shown(key.Subject, key.Quantity);
                if (!shown.HasValue || !Quantities.ReportsStale(key.Quantity)) continue;
                bool stale = IsStale(shown.Value, now);
                if (stale && staleReported.Add(key)) result.Add(shown.Value);
                else if (!stale) staleReported.Remove(key);
            }
            return result;
        }

        // ---- save ----

        public List<Observation> All() => Keys().SelectMany(k => series[k]).ToList();

        public void Restore(IEnumerable<Observation> records)
        {
            series.Clear(); events.Clear(); staleReported.Clear(); Count = 0;
            foreach (Observation o in records)
            {
                var key = (o.Subject, o.Quantity);
                if (!series.TryGetValue(key, out var list)) series[key] = list = new List<Observation>();
                Insert(list, o); Count++;
            }
        }
    }
}
