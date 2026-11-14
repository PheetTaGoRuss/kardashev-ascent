using System;
using System.Collections.Generic;

namespace KardashevAscent.Simulation.Game
{
    public sealed partial class PlanetSimulation
    {
        readonly List<AnglePreset> anglePresets = new List<AnglePreset>();
        int nextAnglePresetId = 1;

        public IReadOnlyList<AnglePreset> AnglePresets => anglePresets;

        public AnglePreset SaveAnglePreset(int tileId, int slot, string name, string note)
        {
            if (!TryGetPanelAngles(tileId, slot, out double tilt, out double azimuth))
                return null;
            if (TryGetAdjustOrder(tileId, slot, out double orderedTilt, out double orderedAzimuth))
            {
                tilt = orderedTilt;       // a layout saves the angles the player chose, even if a robot has not turned the panel yet
                azimuth = orderedAzimuth;
            }
            return SaveAnglePresetAt(tileId, slot, name, note, tilt, azimuth);
        }

        public AnglePreset SaveAnglePresetAt(int tileId, int slot, string name, string note, double tilt, double azimuth)
        {
            if (tileId < 0 || tileId >= TileCount || slot < 0 || slot >= SlotCount(tileId) || GetTile(tileId).IsWater)
                return null;
            if (double.IsNaN(tilt) || double.IsNaN(azimuth))
                return null;
            int anchor = A(tileId, slot);
            double facing = azimuth * 180.0 / Math.PI;
            facing = ((facing % 360.0) + 360.0) % 360.0;
            var preset = new AnglePreset
            {
                Id = nextAnglePresetId++,
                Name = AnglePreset.CleanName(name, $"Slot {anchor + 1}"),
                Note = AnglePreset.CleanNote(note),
                TileId = tileId,
                Slot = anchor,
                LatitudeDeg = Sphere.Latitude(tileId) * 180.0 / Math.PI,
                LongitudeDeg = Sphere.Longitude(tileId) * 180.0 / Math.PI,
                TiltDeg = Math.Max(0.0, Math.Min(90.0, tilt * 180.0 / Math.PI)),
                FacingDeg = facing,
                SavedHours = TimeHours,
            };
            anglePresets.Add(preset);
            return preset;
        }

        public bool UpdateAnglePreset(int id, string name, string note)
        {
            AnglePreset p = anglePresets.Find(x => x.Id == id);
            if (p == null) return false;
            p.Name = AnglePreset.CleanName(name, p.Name);
            p.Note = AnglePreset.CleanNote(note);
            return true;
        }

        public bool DeleteAnglePreset(int id) => anglePresets.RemoveAll(x => x.Id == id) > 0;

        public bool ApplyAnglePreset(int id, int tileId, int slot)
        {
            AnglePreset p = anglePresets.Find(x => x.Id == id);
            return p != null && OrderPanelAngles(tileId, slot, p.TiltDeg * Math.PI / 180.0, p.FacingDeg * Math.PI / 180.0);
        }
    }
}
