using System;
using System.Collections.Generic;
using System.Numerics;
using KardashevAscent.Simulation.Civilization;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Solar;
using KardashevAscent.Simulation.Energy;
using KardashevAscent.Simulation.Robots;
using KardashevAscent.Simulation.Resources;

namespace KardashevAscent.Simulation.Game
{
    public sealed partial class PlanetSimulation
    {
        public SimulationState CaptureState()
        {
            var state = new SimulationState
            {
                PlanetId = PlanetId,
                Notebook = Notebook.Capture(),
                MilestonesReached = MilestonesReached,
                DayAveragePowerW = DayAveragePowerW,
                DayEnergyWh = dayEnergyWh,
                DayHours = dayHours,
                TileCount = TileCount,
                BaseTileId = BaseTileId,
                TimeHours = TimeHours,
                BatteryStoredWh = Grid.Battery.StoredWh,
                ColonyDemandW = Colony.DemandW,
                ColonyHoursSinceBrownout = Colony.HoursSinceBrownout,
                ColonyAverageGenerationW = Colony.AverageGenerationW,
                ColonyDayEnergyWh = Colony.DayEnergyWh,
                ColonyDayClockHours = Colony.DayClockHours,
                BuildStockWh = Build.StockWh,
                History = History.ToList(),
                HistoryGeneratedWh = History.TotalGeneratedWh,
                HistoryDemandWh = History.TotalDemandWh,
                HistoryServedWh = History.TotalServedWh,
                HistoryBuildWh = History.TotalBuildWh,
                HistoryBrownoutHours = History.BrownoutHours,
                HistoryHours = History.TotalHours,
                Workforce = Workforce.Capture(),
            };
            foreach (int tile in Deposits.Surveyed)
                state.SurveyedTiles.Add(tile);
            state.SurveyedTiles.Sort();
            state.SurveyOrders.AddRange(surveyOrders);
            state.MeasuredParameters = Parameters.Capture();
            state.LatitudePuzzles.AddRange(latitudePuzzles);
            state.ParameterPuzzles.AddRange(parameterPuzzles);
            state.RoughParameters = Parameters.CaptureRough();
            state.SurveyPuzzles.AddRange(surveyPuzzles);
            state.RoughSurveys.AddRange(roughSurveys);
            state.RoughSurveys.Sort();
            foreach (var mo in measureOrders)
                state.MeasureOrders.Add(new MeasureOrderRecord { Parameter = mo.Parameter, Tile = mo.Tile });
            foreach (var mo in miningOrders)
                state.MiningOrders.Add(new MiningOrderRecord { Tile = mo.Tile, Material = mo.Material });
            foreach (Material m in Resources.Materials.All)
                if (Materials[m] > 0.0)
                    state.MaterialsKg.Add(new MaterialRecord { Material = m, Kg = Materials[m] });
            foreach (var kv in fieldWork)
                state.FieldWork.Add(new FieldWorkRecord { Kind = kv.Key.Kind, Tile = kv.Key.Tile, Slot = kv.Key.Slot, Hours = kv.Value });
            foreach (var salvage in salvageOrders)
                state.SalvageOrders.Add(new SalvageOrderRecord { Tile = salvage.Tile, Slot = salvage.Slot });
            foreach (var adjust in adjustOrders)
                state.AdjustOrders.Add(new AdjustOrderRecord { Tile = adjust.Tile, Slot = adjust.Slot, Tilt = adjust.Tilt, Azimuth = adjust.Azimuth });
            foreach (var kv in panels)
                state.Buildings.Add(new BuildingRecord { Tile = kv.Key.Tile, Slot = kv.Key.Slot, Type = floatingPanels.Contains(kv.Key) ? BuildingType.FloatingSolar : BuildingType.SolarPanel, Tilt = kv.Value.Tilt, Azimuth = kv.Value.Azimuth, AreaM2 = kv.Value.Area });
            foreach (var b in batteries)
                state.Buildings.Add(new BuildingRecord { Tile = b.Tile, Slot = b.Slot, Type = BuildingType.Battery });
            foreach (var w in workshops)
                state.Buildings.Add(new BuildingRecord { Tile = w.Tile, Slot = w.Slot, Type = BuildingType.Workshop });
            foreach (var c in stations)
                state.Buildings.Add(new BuildingRecord { Tile = c.Tile, Slot = c.Slot, Type = BuildingType.ChargingStation });
            foreach (var c in relays)
                state.Buildings.Add(new BuildingRecord { Tile = c.Tile, Slot = c.Slot, Type = BuildingType.Relay });
            foreach (ManufacturingJob j in jobs)
                state.Jobs.Add(new JobRecord
                {
                    Kind = j.Kind, RobotKind = j.RobotKind, SodiumBattery = j.SodiumBattery, AluminiumWiring = j.AluminiumWiring,
                    RobotId = j.RobotId, Module = j.Module, Output = j.Output, OutputKg = j.OutputKg, DoneHours = j.DoneHours,
                });
            foreach (AnglePreset preset in anglePresets)
                state.AnglePresets.Add(new AnglePreset
                {
                    Id = preset.Id, Name = preset.Name, Note = preset.Note, TileId = preset.TileId, Slot = preset.Slot,
                    LatitudeDeg = preset.LatitudeDeg, LongitudeDeg = preset.LongitudeDeg, TiltDeg = preset.TiltDeg,
                    FacingDeg = preset.FacingDeg, SavedHours = preset.SavedHours,
                });
            state.NextAnglePresetId = nextAnglePresetId;
            foreach (IndustrialReport report in IndustrialReports)
                state.IndustrialReports.Add(new IndustrialReport
                {
                    TimeHours = report.TimeHours,
                    Facility = report.Facility,
                    Blocker = report.Blocker,
                    Message = report.Message,
                });
            state.Buildings.Sort((a, b) => a.Tile != b.Tile ? a.Tile.CompareTo(b.Tile) : a.Slot.CompareTo(b.Slot));
            foreach (BuildingRecord r in state.Buildings)
                if (footprintMap.TryGetFootprint(r.Tile, r.Slot, out List<int> f))
                    for (int i = 1; i < f.Count; i++) r.Covered.Add(f[i]);
            int order = 0;
            var sites = new Dictionary<(int, int), (double Paid, int Order)>();
            foreach (var site in Build.Sites())
                sites[(site.Tile, site.Slot)] = (site.PaidWh, order++);
            foreach (BuildingRecord r in state.Buildings)
                if (sites.TryGetValue((r.Tile, r.Slot), out var site))
                {
                    r.UnderConstruction = true;
                    r.PaidWh = site.Paid;
                    r.QueueOrder = site.Order;
                }
            return state;
        }

        bool restoringState;

        public bool RestoreState(SimulationState state)
        {
            restoringState = true;
            try { return RestoreStateCore(state); }
            finally { restoringState = false; }
        }

        bool RestoreStateCore(SimulationState state)
        {
            if (state == null || state.TileCount != TileCount || state.BaseTileId != BaseTileId)
                return false;
            // Saves before version 3 have no planet id; they load on any planet with the same tiles.
            if (!string.IsNullOrEmpty(state.PlanetId) && state.PlanetId != PlanetId)
                return false;
            foreach (var key in new List<(int Tile, int Slot)>(panels.Keys))
                RemoveBuilding(key.Tile, key.Slot);
            foreach (var key in new List<(int Tile, int Slot)>(batteries))
                RemoveBuilding(key.Tile, key.Slot);
            foreach (var key in new List<(int Tile, int Slot)>(workshops))
                RemoveBuilding(key.Tile, key.Slot);
            foreach (var key in new List<(int Tile, int Slot)>(stations))
                RemoveBuilding(key.Tile, key.Slot);
            foreach (var key in new List<(int Tile, int Slot)>(relays))
                RemoveBuilding(key.Tile, key.Slot);
            TimeHours = state.TimeHours;
            MilestonesReached = Math.Max(0, Math.Min(10, state.MilestonesReached));
            DayAveragePowerW = Math.Max(0.0, state.DayAveragePowerW);
            dayEnergyWh = Math.Max(0.0, state.DayEnergyWh);
            dayHours = Math.Max(0.0, state.DayHours);
            targetReached = MilestonesReached >= 10;
            Notebook.Restore(state.Notebook);
            UpdateStar();
            // A version-1 save had no parts: every building in it is finished, and the colony keeps the lander's parts.
            Build.Reset(state.Version >= 2 ? state.BuildStockWh : BuildCosts.LanderStockWh);
            var sitesInOrder = new List<BuildingRecord>();
            foreach (var r in state.Buildings)
            {
                if (r.UnderConstruction) sitesInOrder.Add(r);
                else Restore(r, false);
            }
            sitesInOrder.Sort((a, b) => a.QueueOrder.CompareTo(b.QueueOrder));
            foreach (var r in sitesInOrder)
                Restore(r, true);
            Grid.Battery.SetStored(state.BatteryStoredWh);
            History.Restore(state.History, state.HistoryGeneratedWh, state.HistoryDemandWh, state.HistoryServedWh,
                            state.HistoryBuildWh, state.HistoryBrownoutHours, state.HistoryHours);
            Colony.Restore(state.ColonyDemandW, state.ColonyHoursSinceBrownout, state.ColonyAverageGenerationW, state.ColonyDayEnergyWh, state.ColonyDayClockHours);
            Grid.DemandW = Colony.DemandW;
            DemandW = Colony.DemandW;
            IsDayAtBase = SunAt(BaseTileId).Up > 0.0;
            // Version 7 added robots; an older save starts with the one General robot at the base.
            if (state.Version < 7 || !Workforce.Restore(state.Workforce))
                Workforce.Reset(BaseTileId);
            Workforce.Network = Network;
            RefreshNetwork();   // the relays restored above start relaying; power follows the restored grid
            adjustOrders.Clear();
            if (state.AdjustOrders != null)   // version 17
                foreach (AdjustOrderRecord o in state.AdjustOrders)
                    if (panels.ContainsKey((o.Tile, o.Slot)))
                        adjustOrders.Add((o.Tile, o.Slot, o.Tilt, o.Azimuth));
            salvageOrders.Clear();
            if (state.SalvageOrders != null)
                foreach (SalvageOrderRecord o in state.SalvageOrders)
                    if (GetBuilding(o.Tile, o.Slot) != BuildingType.None && !Build.IsQueued(o.Tile, o.Slot) && !salvageOrders.Contains((o.Tile, o.Slot)))
                        salvageOrders.Add((o.Tile, o.Slot));
            // Version 9 added resources; older saves start with nothing surveyed or mined.
            Deposits.SetSurveyed(state.SurveyedTiles);
            Materials.Clear();
            surveyOrders.Clear();
            miningOrders.Clear();
            fieldWork.Clear();
            Parameters.Restore(state.MeasuredParameters);   // version 18
            Parameters.RestoreRough(state.RoughParameters);   // version 20
            surveyPuzzles.Clear();
            roughSurveys.Clear();
            if (state.SurveyPuzzles != null)   // version 21
                foreach (SurveyPuzzle p in state.SurveyPuzzles)
                    if (p != null && p.Tile >= 0 && p.Tile < TileCount && !Deposits.IsSurveyed(p.Tile))
                        surveyPuzzles.Add(p);
            if (state.RoughSurveys != null)
                foreach (int tile in state.RoughSurveys)
                    if (Deposits.IsSurveyed(tile))
                        roughSurveys.Add(tile);
            parameterPuzzles.Clear();
            if (state.ParameterPuzzles != null)
                foreach (ParameterPuzzle p in state.ParameterPuzzles)
                    if (p != null && p.Tile >= 0 && p.Tile < TileCount)
                        parameterPuzzles.Add(p);
            latitudePuzzles.Clear();
            if (state.LatitudePuzzles != null)   // version 19
                foreach (LatitudePuzzle p in state.LatitudePuzzles)
                    if (p != null && p.Tile >= 0 && p.Tile < TileCount)
                        latitudePuzzles.Add(p);
            measureOrders.Clear();
            if (state.MeasureOrders != null)
                foreach (MeasureOrderRecord mo in state.MeasureOrders)
                    if (mo.Tile >= 0 && mo.Tile < TileCount && !Parameters.IsMeasured(mo.Parameter) && !IsMeasureOrdered(mo.Parameter))
                        measureOrders.Add((mo.Parameter, mo.Tile));
            if (state.MaterialsKg != null)
                foreach (MaterialRecord m in state.MaterialsKg)
                    Materials.Set(m.Material, m.Kg);
            if (state.SurveyOrders != null)
                foreach (int tile in state.SurveyOrders)
                    if (tile >= 0 && tile < TileCount && !Deposits.IsSurveyed(tile) && !surveyOrders.Contains(tile))
                        surveyOrders.Add(tile);
            if (state.MiningOrders != null)
                foreach (MiningOrderRecord o in state.MiningOrders)
                    if (o.Tile >= 0 && o.Tile < TileCount && Deposits.IsKnown(o.Tile, o.Material) && Deposits.GradeOf(o.Tile, o.Material) > 0.0
                        && !miningOrders.Contains((o.Tile, o.Material)))
                        miningOrders.Add((o.Tile, o.Material));
            if (state.FieldWork != null)
                foreach (FieldWorkRecord f in state.FieldWork)
                    fieldWork[new WorkTask(f.Kind, f.Tile, f.Slot)] = Math.Max(0.0, f.Hours);
            // Version 10 added the Workshop's jobs; older saves have none.
            jobs.Clear();
            if (state.Jobs != null)
                foreach (JobRecord jr in state.Jobs)
                {
                    var j = new ManufacturingJob
                    {
                        Kind = jr.Kind, RobotKind = jr.RobotKind, SodiumBattery = jr.SodiumBattery, AluminiumWiring = jr.AluminiumWiring,
                        RobotId = jr.RobotId, Module = jr.Module, Output = jr.Output, OutputKg = jr.OutputKg, DoneHours = Math.Max(0.0, jr.DoneHours),
                    };
                    j.Recipe = RecipeOf(j);
                    jobs.Add(j);
                }
            industrialReports.Restore(state.IndustrialReports);
            anglePresets.Clear();
            foreach (AnglePreset p in state.AnglePresets ?? new System.Collections.Generic.List<AnglePreset>())
                if (p != null) anglePresets.Add(p);
            nextAnglePresetId = Math.Max(state.NextAnglePresetId, 1);
            foreach (AnglePreset p in anglePresets) nextAnglePresetId = Math.Max(nextAnglePresetId, p.Id + 1);
            Workforce.GroupCapacity = Math.Max(Workforce.GroupCapacity, RobotTech.GroupCapacity(MilestonesReached));
            Workforce.Movement = CurrentMovement();
            Workforce.Milestones = MilestonesReached;
            Workforce.NowHours = TimeHours;
            Workforce.Assign(OpenTasks(), Needs());
            GenerationW = ComputeGeneration() + RtgW;
            return true;
        }

        void Restore(BuildingRecord r, bool site)
        {
            if (r.Type == BuildingType.SolarPanel || r.Type == BuildingType.FloatingSolar)
            {
                bool floating = r.Type == BuildingType.FloatingSolar;
                if (!CanRestorePanel(r.Tile, r.Slot, r.Tilt, r.Azimuth, floating))
                    return;
                if (floating) floatingPanels.Add((r.Tile, r.Slot));
                // Saves before version 5 have no area: their panels are base panel fields.
                double area = r.AreaM2 > 0.0 ? r.AreaM2 : PanelAreaM2;
                if (site) Build.RestoreSite(r.Tile, r.Slot, PanelCost(area) * (floating ? FloatingCostFactor : 1.0), r.PaidWh);
                AddPanel(r.Tile, r.Slot, r.Tilt, r.Azimuth, area);
            }
            else if (r.Type == BuildingType.ChargingStation)
            {
                if (r.Tile < 0 || r.Tile >= TileCount || r.Slot < 0 || r.Slot >= SlotCount(r.Tile) || GetBuilding(r.Tile, r.Slot) != BuildingType.None)
                    return;
                if (site) Build.RestoreSite(r.Tile, r.Slot, BuildCostWh(BuildingType.ChargingStation), r.PaidWh);
                stations.Add((r.Tile, r.Slot));
            }
            else if (r.Type == BuildingType.Relay)
            {
                if (r.Tile < 0 || r.Tile >= TileCount || r.Slot < 0 || r.Slot >= SlotCount(r.Tile) || GetBuilding(r.Tile, r.Slot) != BuildingType.None)
                    return;
                if (site) Build.RestoreSite(r.Tile, r.Slot, BuildCostWh(BuildingType.Relay), r.PaidWh);
                relays.Add((r.Tile, r.Slot));
            }
            else if (r.Type == BuildingType.Workshop)
            {
                if (r.Tile < 0 || r.Tile >= TileCount || r.Slot < 0 || r.Slot >= SlotCount(r.Tile) || GetBuilding(r.Tile, r.Slot) != BuildingType.None)
                    return;
                if (site) Build.RestoreSite(r.Tile, r.Slot, BuildCostWh(BuildingType.Workshop), r.PaidWh);
                workshops.Add((r.Tile, r.Slot));
            }
            else if (r.Type == BuildingType.Battery)
            {
                if (r.Tile < 0 || r.Tile >= TileCount || r.Slot < 0 || r.Slot >= SlotCount(r.Tile) || GetBuilding(r.Tile, r.Slot) != BuildingType.None)
                    return;
                if (site) Build.RestoreSite(r.Tile, r.Slot, BuildCostWh(BuildingType.Battery), r.PaidWh);
                else Grid.Battery.AddCapacity(BatteryUnitWh);
                AddBattery(r.Tile, r.Slot);
            }
            // Its footprint, as saved (version 16); older saves: one slot each.
            if (GetBuildingAt(r.Tile, r.Slot) != BuildingType.None && r.Covered != null && r.Covered.Count > 0)
            {
                var f = new List<int> { r.Slot };
                foreach (int s in r.Covered)
                    if (s >= 0 && s < SlotCount(r.Tile) && GetBuilding(r.Tile, s) == BuildingType.None) f.Add(s);
                Occupy(r.Tile, f);
            }
        }
    }
}
