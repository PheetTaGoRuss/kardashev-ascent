using System;
using System.Collections.Generic;
using System.Linq;
using KardashevAscent.Simulation.Operations;
using KardashevAscent.Simulation.Resources;
using KardashevAscent.Simulation.Robots;

namespace KardashevAscent.Simulation.Game
{
    /// <summary>
    /// What a winning rule does (mission operations 5.1). Robot effects are overrides on top of the group's settings, set
    /// while the rule holds and removed when it stops; a direct order from the player always beats a rule (master design 4
    /// rule 5), so a rule never overwrites an override or a destination the player set. One-shot responses (stock targets,
    /// hauls, plant commands, notes) happen when the rule starts to hold.
    /// </summary>
    public sealed partial class PlanetSimulation
    {
        /// <summary>The overrides rules set, so they can be taken away again and are not mistaken for the player's: (robot, kind) to priority.</summary>
        readonly Dictionary<(int Robot, TaskKind Kind), TaskPriority> ruleOverrides = new Dictionary<(int, TaskKind), TaskPriority>();
        readonly Dictionary<int, OperatingProgram> ruleProgram = new Dictionary<int, OperatingProgram>();
        readonly HashSet<(int Tile, int Slot)> pausedPlants = new HashSet<(int, int)>();

        public bool IsPlantPaused(int tileId, int slot) => pausedPlants.Contains((tileId, A(tileId, slot)));

        void ApplyRobotEffects(Robot r, Rule winner, bool started)
        {
            var desired = new Dictionary<TaskKind, TaskPriority>();
            OperatingProgram? program = null;
            bool safe = false;
            if (winner != null)
            {
                RuleResponse resp = winner.Response;
                switch (resp.Kind)
                {
                    case ResponseKind.SetTaskPriority: desired[resp.Task] = resp.Priority; break;
                    case ResponseKind.SetProgram: program = resp.Program; break;
                    case ResponseKind.SafeMode:
                    case ResponseKind.HoldPosition:
                        foreach (TaskKind k in (TaskKind[])Enum.GetValues(typeof(TaskKind))) desired[k] = TaskPriority.Disabled;
                        safe = resp.Kind == ResponseKind.SafeMode;
                        break;
                }
            }
            bool playerWon = false;
            foreach (TaskKind k in (TaskKind[])Enum.GetValues(typeof(TaskKind)))
            {
                bool ours = ruleOverrides.TryGetValue((r.Id, k), out TaskPriority ourValue);
                TaskPriority? current = r.PriorityOverride(k);
                if (desired.TryGetValue(k, out TaskPriority want))
                {
                    if (current.HasValue && !(ours && current.Value == ourValue)) { playerWon = true; continue; }   // the player's own order
                    if (current != want) Workforce.SetRobotPriorityOverride(r.Id, k, want);
                    ruleOverrides[(r.Id, k)] = want;
                }
                else if (ours)
                {
                    if (current.HasValue && current.Value == ourValue) Workforce.SetRobotPriorityOverride(r.Id, k, null);
                    ruleOverrides.Remove((r.Id, k));
                }
            }
            bool hadProgram = ruleProgram.TryGetValue(r.Id, out OperatingProgram ourProgram);
            if (program.HasValue)
            {
                if (r.ProgramOverride.HasValue && !(hadProgram && r.ProgramOverride.Value == ourProgram)) playerWon = true;
                else
                {
                    if (r.ProgramOverride != program) Workforce.SetRobotProgramOverride(r.Id, program);
                    ruleProgram[r.Id] = program.Value;
                }
            }
            else if (hadProgram)
            {
                if (r.ProgramOverride.HasValue && r.ProgramOverride.Value == ourProgram) Workforce.SetRobotProgramOverride(r.Id, null);
                ruleProgram.Remove(r.Id);
            }
            if (started && playerWon)
                Log($"Robot {r.Id}", "Player order wins", winner.Id, "rule", r.Name, $"{winner.Name} did not override the player's own setting for {r.Name}.", r.Id, winner.Id, r.SyncedRulebookVersion);
            if (started && safe && !r.ManualTarget.HasValue)
                GoToNearestCharger(r);
            if (started && winner != null)
                ApplyOneShot(winner.Response, null);
        }

        /// <summary>Safe mode: drive to the nearest charger and wait there.</summary>
        void GoToNearestCharger(Robot r)
        {
            int best = -1;
            double bestHours = double.PositiveInfinity;
            foreach (RobotFacility f in RobotFacilities())
            {
                double h = Routes == null ? double.PositiveInfinity : Routes.HoursBetween(r.Tile, f.Tile);
                if (h < bestHours - 1e-12 || (Math.Abs(h - bestHours) <= 1e-12 && f.Tile < best)) { best = f.Tile; bestHours = h; }
            }
            if (best >= 0 && best != r.Tile) Workforce.SendTo(r.Id, best);
        }

        /// <summary>The responses that happen once, when the rule starts to hold.</summary>
        void ApplyOneShot(RuleResponse resp, (int Tile, int Slot)? plant)
        {
            switch (resp.Kind)
            {
                case ResponseKind.SetStockTarget:
                    SetStockTarget(resp.Tile, resp.Material, resp.Kg);
                    break;
                case ResponseKind.RequestHaul:
                    if (LogisticsEnabled && resp.Tile != resp.ToTile && resp.Kg > 0.0 && KgAt(resp.Tile, resp.Material) > 0.0)
                        haulBook.Add(resp.Tile, resp.ToTile, resp.Material, Math.Min(resp.Kg, KgAt(resp.Tile, resp.Material)));
                    break;
                case ResponseKind.PausePlant:
                    if (plant.HasValue) pausedPlants.Add(plant.Value);
                    break;
                case ResponseKind.ResumePlant:
                    if (plant.HasValue) { pausedPlants.Remove(plant.Value); StartBatches(); }
                    break;
                case ResponseKind.SetByproductPolicy:
                    if (plant.HasValue) SetByproductPolicy(plant.Value.Tile, plant.Value.Slot, resp.Policy);
                    break;
                case ResponseKind.Notify:
                    Log("Colony", "Notice", 0, "", "Rulebook", string.IsNullOrEmpty(resp.Text) ? "A rule asks you to look." : resp.Text);
                    break;
            }
        }

        void CaptureRules(SimulationState state)
        {
            state.RulebookVersions.AddRange(rulebook.History);
            state.PlantRulebookVersion = plantRulebookVersion;
            foreach (var kv in robotWinner) state.RuleStates.Add(new RuleStateRecord { RuleId = kv.Value, IsPlant = false, Subject = kv.Key, Active = true, Started = ruleStarted.TryGetValue((kv.Value, false, kv.Key), out double a) ? a : 0.0 });
            foreach (var kv in plantWinner) state.RuleStates.Add(new RuleStateRecord { RuleId = kv.Value, IsPlant = true, Subject = kv.Key, Active = true, Started = ruleStarted.TryGetValue((kv.Value, true, kv.Key), out double a) ? a : 0.0 });
            foreach (var kv in ruleStarted)
                if (!(kv.Key.Plant ? plantWinner.TryGetValue(kv.Key.Subject, out int pw) && pw == kv.Key.Rule : robotWinner.TryGetValue(kv.Key.Subject, out int rw) && rw == kv.Key.Rule))
                    state.RuleStates.Add(new RuleStateRecord { RuleId = kv.Key.Rule, IsPlant = kv.Key.Plant, Subject = kv.Key.Subject, Active = false, Started = kv.Value });
            foreach (var kv in ruleOverrides) state.RuleOverrides.Add(new RuleOverrideRecord { RobotId = kv.Key.Robot, Kind = kv.Key.Kind, Priority = kv.Value });
            foreach (var p in pausedPlants) state.PausedPlants.Add(new PausedPlantRecord { Tile = p.Tile, Slot = p.Slot });
            state.OperationsLog.AddRange(operations.Records);
            state.NextOperationId = operations.NextId;
        }

        void RestoreRules(SimulationState state)
        {
            rulebook.Restore(state.Version >= 41 ? state.RulebookVersions : null);
            plantRulebookVersion = state.Version >= 41 ? state.PlantRulebookVersion : 0;
            operations.Restore(state.Version >= 41 ? state.OperationsLog : null, state.NextOperationId);
            robotWinner.Clear(); plantWinner.Clear(); ruleStarted.Clear(); ruleOverrides.Clear(); ruleProgram.Clear(); pausedPlants.Clear();
            plantBlockedSince.Clear(); cutOffRobots.Clear();
            if (state.Version >= 41)
            {
                if (state.RuleStates != null)
                    foreach (RuleStateRecord r in state.RuleStates)
                    {
                        ruleStarted[(r.RuleId, r.IsPlant, r.Subject)] = r.Started;
                        if (r.Active) (r.IsPlant ? plantWinner : robotWinner)[r.Subject] = r.RuleId;
                    }
                if (state.RuleOverrides != null)
                    foreach (RuleOverrideRecord r in state.RuleOverrides) ruleOverrides[(r.RobotId, r.Kind)] = r.Priority;
                if (state.PausedPlants != null)
                    foreach (PausedPlantRecord p in state.PausedPlants)
                        if (refineryModules.ContainsKey((p.Tile, p.Slot))) pausedPlants.Add((p.Tile, p.Slot));
            }
            else
                InstallStarterRulebook();   // an older save gets the starter rulebook on load (MO-14)
        }
    }
}
