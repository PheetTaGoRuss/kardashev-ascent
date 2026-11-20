using System;
using System.Collections.Generic;
using System.Linq;
using KardashevAscent.Simulation.Operations;
using KardashevAscent.Simulation.Resources;
using KardashevAscent.Simulation.Robots;
using KardashevAscent.Simulation.Solar;

namespace KardashevAscent.Simulation.Game
{
    /// <summary>
    /// Flight rules (mission operations 5, MO-10 to MO-14). Step 7a of the sub-step order: after field work advances and before
    /// work is settled, every robot and plant reads its known state and follows the rulebook version the colony network last
    /// synced to it. A robot cut off keeps running its own version on its own state; it syncs when it is back (U2).
    /// With no rulebook published nothing here does anything, so a colony without rules steps exactly as before.
    /// </summary>
    public sealed partial class PlanetSimulation
    {
        readonly Rulebook rulebook = new Rulebook();
        readonly OperationsLog operations = new OperationsLog();
        int plantRulebookVersion;
        readonly HashSet<int> cutOffRobots = new HashSet<int>();

        /// <summary>The rule that holds for each subject now (robot id, or tile * 128 + slot for a plant), for the robots and the plants.</summary>
        readonly Dictionary<int, int> robotWinner = new Dictionary<int, int>();
        readonly Dictionary<int, int> plantWinner = new Dictionary<int, int>();
        /// <summary>When each rule last started holding for a subject (cooldown): (rule id, isPlant, subject) to game hours.</summary>
        readonly Dictionary<(int Rule, bool Plant, int Subject), double> ruleStarted = new Dictionary<(int, bool, int), double>();

        public int RulebookVersion => rulebook.CurrentNumber;

        public IReadOnlyList<RulebookVersion> RulebookHistory => rulebook.History;

        public IReadOnlyList<OperationRecord> OperationsLog => operations.Records;

        public int RobotRulebookVersion(int robotId) => Workforce.GetRobot(robotId)?.SyncedRulebookVersion ?? 0;

        public int PlantRulebookVersion => plantRulebookVersion;

        /// <summary>How many robots run each rulebook version (the uplink panel: "v7 on 12 of 15 robots").</summary>
        public IReadOnlyDictionary<int, int> RulebookUplink()
        {
            var d = new SortedDictionary<int, int>();
            foreach (Robot r in Workforce.Robots) d[r.SyncedRulebookVersion] = (d.TryGetValue(r.SyncedRulebookVersion, out int n) ? n : 0) + 1;
            return d;
        }

        /// <summary>Publish the rules as the next version (copied). Null on success, else why not: a rule with no rationale is refused.</summary>
        public string PublishRulebook(IReadOnlyList<Rule> rules, string note)
        {
            string problem = rulebook.Publish(rules, note, TimeHours);
            if (problem != null) return problem;
            Log("Colony", "Rulebook published", rulebook.CurrentNumber, "version", "Operations", $"Rulebook v{rulebook.CurrentNumber}: {rulebook.Current.Rules.Count} rule(s). {note}");
            return null;
        }

        /// <summary>A new mission ships with the starter rulebook (version 1). Does nothing if a rulebook exists.</summary>
        public bool InstallStarterRulebook()
        {
            if (rulebook.CurrentNumber != 0) return false;
            return PublishRulebook(StarterRulebook.Rules(), StarterRulebook.Note) == null;
        }

        OperationRecord Log(string subject, string quantity, double value, string unit, string source, string text, int robotId = -1, int ruleId = 0, int version = 0)
        {
            return operations.Add(new OperationRecord
            {
                Subject = subject, Quantity = quantity, Value = value, Unit = unit, Source = source, Text = text,
                ObservedAtHours = TimeHours, KnownAtHours = robotId >= 0 && !(Workforce.GetRobot(robotId)?.Connected ?? true) ? -1.0 : TimeHours,
                RobotId = robotId, RuleId = ruleId, RulebookVersion = version,
            });
        }

        // ---- step 7a ----

        void EvaluateRules()
        {
            if (rulebook.CurrentNumber == 0)
                return;   // no rules: nothing changes
            SyncRulebooks();
            foreach (Robot r in Workforce.Robots.ToArray())
                EvaluateRobot(r);
            EvaluatePlants();
        }

        void SyncRulebooks()
        {
            int current = rulebook.CurrentNumber;
            foreach (Robot r in Workforce.Robots)
            {
                bool canSync = Workforce.CanSync(r);
                if (canSync && r.SyncedRulebookVersion != current)
                {
                    int was = r.SyncedRulebookVersion;
                    r.SyncedRulebookVersion = current;
                    Log($"Robot {r.Id}", "Rulebook synced", current, "version", r.Name,
                        was == 0 ? $"{r.Name} received rulebook v{current}." : $"{r.Name} synced v{was} to v{current}.", r.Id, 0, current);
                }
                if (canSync)
                {
                    if (cutOffRobots.Remove(r.Id)) operations.MarkKnown(r.Id, TimeHours);   // what it saw while cut off is known now
                }
                else
                    cutOffRobots.Add(r.Id);
            }
            // Plants are on the grid; they sync while it is up and keep their last version through an outage.
            if (!Grid.IsBrownout && plantRulebookVersion != current)
            {
                Log("Plants", "Rulebook synced", current, "version", "Operations", plantRulebookVersion == 0 ? $"Plants received rulebook v{current}." : $"Plants synced v{plantRulebookVersion} to v{current}.", -1, 0, current);
                plantRulebookVersion = current;
            }
        }

        KnownState KnownFor(Robot r)
        {
            bool net = Workforce.CanSync(r);
            var s = new KnownState
            {
                RobotBatteryPct = r.Battery.StateOfCharge * 100.0,
                RobotConditionPct = r.Condition * 100.0,
                IsNight = SunAt(r.Tile).Up <= 0.0,
                HoursCutOff = r.Connected ? 0.0 : r.HoursOffline,
                StellarFlare = IsFlaringStar,
            };
            ColonyKnowledge(s, net);
            return s;
        }

        /// <summary>The colony's values a subject can read: only while it is on the network. Cut off, they are unknown (U4).</summary>
        void ColonyKnowledge(KnownState s, bool net)
        {
            if (!net) return;
            s.GridBatteryPct = Grid.Battery.CapacityWh > 0.0 ? 100.0 * Grid.Battery.StoredWh / Grid.Battery.CapacityWh : 0.0;
            s.Brownout = Grid.IsBrownout;
            s.DepotKg = (tile, m) => tile >= 0 && tile < TileCount ? KgAt(tile, m) : (double?)null;
            if (Parameters.IsMeasured(SolarParameter.AirTemperature))
                s.SurfaceTemperatureC = AirTemperatureAt(BaseTileId, CurrentMeanAnomaly());
        }

        bool Applies(Rule rule, Robot r) =>
            rule.Scope == RuleScopeKind.AllRobots
            || (rule.Scope == RuleScopeKind.Group && rule.ScopeId == r.GroupId)
            || (rule.Scope == RuleScopeKind.RobotKind && rule.ScopeId == (int)r.Kind);

        bool CooledDown(Rule rule, bool plant, int subject) =>
            !ruleStarted.TryGetValue((rule.Id, plant, subject), out double at) || TimeHours - at >= rule.CooldownHours - 1e-9;

        void EvaluateRobot(Robot r)
        {
            RulebookVersion version = rulebook.Get(r.SyncedRulebookVersion);
            Rule winner = null;
            KnownState state = null;
            if (version != null && !r.IsShutDown)
            {
                state = KnownFor(r);
                robotWinner.TryGetValue(r.Id, out int heldId);
                winner = Rulebook.Winner(version, rule => Applies(rule, r), state, rule => rule.Id == heldId, rule => CooledDown(rule, false, r.Id));
            }
            robotWinner.TryGetValue(r.Id, out int was);
            int now = winner?.Id ?? 0;
            bool started = now != 0 && now != was;
            if (started)
            {
                ruleStarted[(now, false, r.Id)] = TimeHours;
                LogFiring($"Robot {r.Id}", r.Name, winner, version.Number, state, r.Id);
            }
            if (now == 0) robotWinner.Remove(r.Id); else robotWinner[r.Id] = now;
            ApplyRobotEffects(r, winner, started);
        }

        void EvaluatePlants()
        {
            RulebookVersion version = rulebook.Get(plantRulebookVersion);
            foreach (var key in new List<(int Tile, int Slot)>(refineryModules.Keys))
            {
                if (Build.IsQueued(key.Tile, key.Slot)) continue;
                int id = BufferId(key.Tile, key.Slot);
                PlantReport report = PlantReportAt(key.Tile, key.Slot);
                // how long it has been blocked
                bool blocked = report.Blocker != IndustrialBlocker.None;
                if (blocked) { if (!plantBlockedSince.ContainsKey(key)) plantBlockedSince[key] = TimeHours; }
                else plantBlockedSince.Remove(key);
                Rule winner = null;
                KnownState state = null;
                if (version != null)
                {
                    state = new KnownState
                    {
                        PlantBlocker = report.Blocker,
                        PlantBlockedHours = blocked ? TimeHours - plantBlockedSince[key] : 0.0,
                        PlantOutputBufferPct = 100.0 * report.OutputBufferKg / (PlantOutputBulkKg + PlantOutputHazardousKg + PlantOutputGasKg),
                        IsNight = SunAt(key.Tile).Up <= 0.0,
                    };
                    ColonyKnowledge(state, !Grid.IsBrownout);
                    plantWinner.TryGetValue(id, out int heldId);
                    winner = Rulebook.Winner(version,
                        rule => rule.Scope == RuleScopeKind.Plant && (rule.PlantTile < 0 || (rule.PlantTile == key.Tile && rule.PlantSlot == key.Slot)),
                        state, rule => rule.Id == heldId, rule => CooledDown(rule, true, id));
                }
                plantWinner.TryGetValue(id, out int was);
                int now = winner?.Id ?? 0;
                if (now != 0 && now != was)
                {
                    ruleStarted[(now, true, id)] = TimeHours;
                    LogFiring($"Plant {key.Tile}.{key.Slot}", "Operations", winner, version.Number, state, -1);
                    ApplyOneShot(winner.Response, key);
                }
                if (now == 0) plantWinner.Remove(id); else plantWinner[id] = now;
            }
        }

        /// <summary>Every rule firing is logged with the rule, its rationale, the version and the known values that tripped it (U3).</summary>
        void LogFiring(string subject, string source, Rule rule, int version, KnownState state, int robotId)
        {
            var known = new List<string>();
            foreach (RuleCondition c in rule.Conditions)
                known.Add(DescribeKnown(c, state));
            Log(subject, "Rule fired", rule.Id, "rule", source,
                $"{rule.Name}: {rule.Rationale} [rulebook v{version}; known: {string.Join(", ", known)}]", robotId, rule.Id, version);
        }

        static string DescribeKnown(RuleCondition c, KnownState s)
        {
            switch (c.Kind)
            {
                case ConditionKind.RobotBatteryBelow:
                case ConditionKind.RobotBatteryAbove: return $"battery {s.RobotBatteryPct:0} %";
                case ConditionKind.RobotConditionBelow: return $"condition {s.RobotConditionPct:0} %";
                case ConditionKind.GridBatteryBelow: return $"grid battery {s.GridBatteryPct:0} %";
                case ConditionKind.BrownoutNow: return $"brownout {s.Brownout}";
                case ConditionKind.IsNight: case ConditionKind.IsDay: return s.IsNight == true ? "night" : "day";
                case ConditionKind.CutOffForHours: return $"cut off {s.HoursCutOff:0.#} h";
                case ConditionKind.DepotStockBelow:
                case ConditionKind.DepotStockAbove: return $"depot {c.Tile} {Resources.Materials.Name(c.Material).ToLowerInvariant()} {s.DepotKg?.Invoke(c.Tile, c.Material):0} kg";
                case ConditionKind.PlantBlockedBy: return $"plant {s.PlantBlocker} for {s.PlantBlockedHours:0.#} h";
                case ConditionKind.PlantOutputBufferAbove: return $"output buffer {s.PlantOutputBufferPct:0} %";
                case ConditionKind.SurfaceTemperatureOutside: return $"surface {s.SurfaceTemperatureC:0} C";
                case ConditionKind.StellarFlareActive: return "flare";
                default: return c.Kind.ToString();
            }
        }

        readonly Dictionary<(int Tile, int Slot), double> plantBlockedSince = new Dictionary<(int, int), double>();
    }
}
