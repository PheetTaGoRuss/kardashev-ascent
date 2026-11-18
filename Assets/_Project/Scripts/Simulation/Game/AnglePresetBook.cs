using System;
using System.Collections.Generic;

namespace KardashevAscent.Simulation.Game
{
    /// <summary>
    /// The player's saved panel angle layouts (names, notes, where and when they were saved) and the counter that gives
    /// them ids. Pure bookkeeping: the simulation decides which angles and which place go into a layout and what applying
    /// one does (see <see cref="PlanetSimulation"/>). Extracted from the orchestrator (architecture step M1).
    /// </summary>
    public sealed class AnglePresetBook
    {
        readonly List<AnglePreset> presets = new List<AnglePreset>();
        int nextId = 1;

        public IReadOnlyList<AnglePreset> All => presets;

        public AnglePreset Find(int id) => presets.Find(p => p.Id == id);

        /// <summary>Keep a new layout. Names and notes are trimmed and capped; angles are degrees (tilt 0-90, facing 0-359).</summary>
        public AnglePreset Add(string name, string note, int tileId, int slotAnchor, double latitudeDeg, double longitudeDeg,
                               double tiltRad, double azimuthRad, double savedHours)
        {
            double facing = azimuthRad * 180.0 / Math.PI;
            facing = ((facing % 360.0) + 360.0) % 360.0;
            var preset = new AnglePreset
            {
                Id = nextId++,
                Name = AnglePreset.CleanName(name, $"Slot {slotAnchor + 1}"),
                Note = AnglePreset.CleanNote(note),
                TileId = tileId,
                Slot = slotAnchor,
                LatitudeDeg = latitudeDeg,
                LongitudeDeg = longitudeDeg,
                TiltDeg = Math.Max(0.0, Math.Min(90.0, tiltRad * 180.0 / Math.PI)),
                FacingDeg = facing,
                SavedHours = savedHours,
            };
            presets.Add(preset);
            return preset;
        }

        public bool Update(int id, string name, string note)
        {
            AnglePreset p = Find(id);
            if (p == null) return false;
            p.Name = AnglePreset.CleanName(name, p.Name);
            p.Note = AnglePreset.CleanNote(note);
            return true;
        }

        public bool Delete(int id) => presets.RemoveAll(p => p.Id == id) > 0;

        /// <summary>Copies for a save, and the id the next layout will get.</summary>
        public void Capture(List<AnglePreset> into, out int next)
        {
            foreach (AnglePreset p in presets)
                into.Add(new AnglePreset
                {
                    Id = p.Id, Name = p.Name, Note = p.Note, TileId = p.TileId, Slot = p.Slot,
                    LatitudeDeg = p.LatitudeDeg, LongitudeDeg = p.LongitudeDeg, TiltDeg = p.TiltDeg,
                    FacingDeg = p.FacingDeg, SavedHours = p.SavedHours,
                });
            next = nextId;
        }

        /// <summary>Replace everything with a saved state (null: a save from before layouts existed). Ids keep counting up.</summary>
        public void Restore(IEnumerable<AnglePreset> saved, int next)
        {
            presets.Clear();
            nextId = Math.Max(next, 1);
            if (saved == null) return;
            foreach (AnglePreset p in saved)
            {
                if (p == null) continue;
                presets.Add(p);
                nextId = Math.Max(nextId, p.Id + 1);
            }
        }
    }
}
