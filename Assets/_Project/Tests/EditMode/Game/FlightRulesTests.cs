using System;
using System.Collections.Generic;
using System.Linq;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Operations;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Resources;
using KardashevAscent.Simulation.Robots;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Game
{
    /// <summary>Flight rules in the simulation (MO-10 to MO-14): step 7a, versions and uplink, overrides, the log.</summary>
    public class FlightRulesTests
    {
        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));
        static readonly PlanetSolarParams Earthlike = new PlanetSolarParams(
            SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0,
            282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 25.0);

        static PlanetSimulation Make() => new PlanetSimulation(Sphere, Surface, Earthlike);

        /// <summary>A colony whose network stays up (panels, a battery yard and a charger, built at once) so connection depends on distance only.</summary>
        static PlanetSimulation MakePowered()
        {
            var sim = Make();
            sim.Debug.FreeBuilding = true;
            int t = sim.BaseTileId;
            for (int slot = 0; slot < 10; slot++) Assert.IsTrue(sim.PlacePanel(t, slot, 0.35, Math.PI));
            Assert.IsTrue(sim.PlaceBattery(t, 12));
            Assert.IsTrue(sim.PlaceChargingStation(t, 20));
            sim.Debug.FreeBuilding = false;
            sim.Debug.FullBattery = true;
            return sim;
        }

        static Rule Safe(double below, double cooldown = 0.0) => new Rule
        {
            Name = "Safe below " + below, Rationale = "A test rule: low batteries go home.", CooldownHours = cooldown,
            Conditions = new List<RuleCondition> { new RuleCondition { Kind = ConditionKind.RobotBatteryBelow, Value = below } },
            Response = new RuleResponse { Kind = ResponseKind.SafeMode },
        };

        static void Drain(Robot r, double toFraction)
        {
            for (int i = 0; i < 400 && r.Battery.StateOfCharge > toFraction; i++) r.Battery.Draw(300.0, 0.25);
        }

        static void Fill(Robot r) => r.Battery.Charge(1_000_000.0, 48.0);

        static int Fired(PlanetSimulation sim, int robotId = -1) => sim.OperationsLog.Count(x => x.Quantity == "Rule fired" && (robotId < 0 || x.RobotId == robotId));

        [Test]
        public void AColonyWithNoRules_StepsExactlyAsBefore()
        {
            string Run(bool publishEmpty)
            {
                var sim = Make();
                if (publishEmpty) Assert.IsNull(sim.PublishRulebook(new Rule[0], "empty"));
                int tile = sim.BaseTileId;
                for (int slot = 0; slot < 4; slot++) sim.PlacePanel(tile, slot, 0.35, Math.PI);
                sim.PlaceBattery(tile, 8);
                sim.OrderSurvey(tile);
                sim.OrderMining(tile, Material.Rock);
                for (int i = 0; i < 240; i++) sim.Tick(1.0);
                return $"{sim.GenerationW:R}|{sim.Materials[Material.Rock]:R}|{sim.Materials[Material.Iron]:R}|" +
                       string.Join(";", sim.Workforce.Robots.Select(r => $"{r.Tile}/{r.Battery.StateOfCharge:R}/{r.Condition:R}/{r.Task}"));
            }
            Assert.AreEqual(Run(false), Run(true), "an empty rulebook changes nothing");
        }

        [Test]
        public void NoRulebookAtAll_LogsNothing()
        {
            var sim = Make();
            for (int i = 0; i < 48; i++) sim.Tick(1.0);
            Assert.AreEqual(0, sim.RulebookVersion);
            Assert.IsEmpty(sim.OperationsLog);
        }

        [Test]
        public void ANewMissionShipsStarterV1_AndTheRobotsReceiveIt()
        {
            var sim = Make();
            Assert.IsTrue(sim.InstallStarterRulebook());
            Assert.IsFalse(sim.InstallStarterRulebook(), "only once");
            sim.Tick(0.25);
            Assert.AreEqual(1, sim.RulebookVersion);
            Assert.AreEqual(3, sim.RulebookHistory[0].Rules.Count);
            Assert.IsTrue(sim.Workforce.Robots.All(r => sim.RobotRulebookVersion(r.Id) == 1));
            Assert.AreEqual(1, sim.PlantRulebookVersion);
            Assert.AreEqual(sim.Workforce.Robots.Count, sim.RulebookUplink()[1]);
            Assert.IsTrue(sim.OperationsLog.Any(x => x.Quantity == "Rulebook synced"));
        }

        [Test]
        public void ALowBattery_TriggersTheRule_WithItsRationaleAndKnownValuesInTheLog_AndItEndsWhenTheCauseDoes()
        {
            var sim = Make();
            Assert.IsNull(sim.PublishRulebook(new[] { Safe(40.0) }, "v1"));
            sim.Tick(0.25);
            Robot r = sim.Workforce.Robots[0];
            Assert.IsNull(r.PriorityOverride(TaskKind.Build), "a charged robot is left alone");
            Drain(r, 0.3);
            sim.Tick(0.25);
            Assert.AreEqual(TaskPriority.Disabled, r.PriorityOverride(TaskKind.Build), "safe mode takes no work");
            OperationRecord fired = sim.OperationsLog.Last(x => x.Quantity == "Rule fired");
            StringAssert.Contains("low batteries go home", fired.Text, "the rationale is in the log");
            StringAssert.Contains("battery", fired.Text, "so are the known values that tripped it");
            Assert.AreEqual(1, fired.RulebookVersion);
            Fill(r);
            sim.Tick(0.25);
            Assert.IsNull(r.PriorityOverride(TaskKind.Build), "the rule let go: the override is removed");
        }

        [Test]
        public void ThePlayersOwnOrder_BeatsARule()
        {
            var sim = Make();
            Robot r = sim.Workforce.Robots[0];
            Assert.IsTrue(sim.Workforce.SetRobotPriorityOverride(r.Id, TaskKind.Build, TaskPriority.High), "the player's order");
            var rule = Safe(101.0);   // holds always
            Assert.IsNull(sim.PublishRulebook(new[] { rule }, "v1"));
            sim.Tick(0.25);
            Assert.AreEqual(TaskPriority.High, r.PriorityOverride(TaskKind.Build), "the rule did not overwrite the player's override");
            Assert.AreEqual(TaskPriority.Disabled, r.PriorityOverride(TaskKind.Mine), "it still applied where the player had said nothing");
            Assert.IsTrue(sim.OperationsLog.Any(x => x.Quantity == "Player order wins"), "and the log says so");
        }

        [Test]
        public void TheCooldown_StopsARuleFlapping_OnAnOscillatingInput()
        {
            var sim = Make();
            Assert.IsNull(sim.PublishRulebook(new[] { Safe(50.0, cooldown: 12.0) }, "v1"));
            Robot r = sim.Workforce.Robots[0];
            for (int i = 0; i < 48; i++)
            {
                if (i % 2 == 0) Drain(r, 0.3); else Fill(r);
                sim.Tick(1.0);
            }
            Assert.LessOrEqual(Fired(sim, r.Id), 5, "48 hours of flapping, one start per 12 hours at most (plus the first)");
            Assert.GreaterOrEqual(Fired(sim, r.Id), 2);
        }

        [Test]
        public void ARobotCutOff_KeepsItsVersion_AndSyncsToTheNewOneWhenItIsBack()
        {
            var sim = MakePowered();
            Assert.IsNull(sim.PublishRulebook(new Rule[0] , "v1 empty"));
            Robot r = sim.Workforce.Robots[0];
            sim.Tick(0.25);
            Assert.AreEqual(1, sim.RobotRulebookVersion(r.Id));
            // a tile beyond the network's reach
            int far = Enumerable.Range(0, sim.TileCount).Where(t => !sim.GetTile(t).IsWater && !sim.Network.IsConnected(t))
                                 .OrderBy(t => sim.Routes.HoursBetween(sim.BaseTileId, t)).First(t => !double.IsInfinity(sim.Routes.HoursBetween(sim.BaseTileId, t)));
            Assert.IsTrue(sim.Workforce.SendTo(r.Id, far));
            for (int i = 0; i < 800 && r.Connected; i++) sim.Tick(0.25);
            Assert.IsFalse(r.Connected, "the robot drove out of the network's reach");
            Assert.IsNull(sim.PublishRulebook(new[] { Safe(10.0) }, "v2"));
            for (int i = 0; i < 8; i++) sim.Tick(0.25);
            Assert.AreEqual(1, sim.RobotRulebookVersion(r.Id), "cut off: still on v1 after the player published v2");
            Assert.AreEqual(2, sim.RulebookVersion);
            Assert.IsTrue(sim.Workforce.SendTo(r.Id, sim.BaseTileId), "the player calls it home");
            for (int i = 0; i < 2000 && sim.RobotRulebookVersion(r.Id) != 2; i++) sim.Tick(0.25);
            Assert.AreEqual(2, sim.RobotRulebookVersion(r.Id), "back in contact it runs v2");
            Assert.IsTrue(sim.OperationsLog.Any(x => x.Quantity == "Rulebook synced" && x.Text.Contains("v1 to v2") && x.RobotId == r.Id), "the sync is logged");
        }

        [Test]
        public void WhatACutOffRobotObserved_IsKnownOnlyWhenItSyncs()
        {
            var sim = MakePowered();
            var hold = new Rule
            {
                Name = "Hold when cut off", Rationale = "A robot out of contact for an hour waits where it is.",
                Conditions = new List<RuleCondition> { new RuleCondition { Kind = ConditionKind.CutOffForHours, Value = 1.0 } },
                Response = new RuleResponse { Kind = ResponseKind.HoldPosition },
            };
            Assert.IsNull(sim.PublishRulebook(new[] { hold }, "v1"));
            Robot r = sim.Workforce.Robots[0];
            sim.Tick(0.25);
            int far = Enumerable.Range(0, sim.TileCount).Where(t => !sim.GetTile(t).IsWater && !sim.Network.IsConnected(t) && !double.IsInfinity(sim.Routes.HoursBetween(sim.BaseTileId, t)))
                                 .OrderBy(t => sim.Routes.HoursBetween(sim.BaseTileId, t)).First();
            Assert.IsTrue(sim.Workforce.SendTo(r.Id, far));
            for (int i = 0; i < 800 && r.Connected; i++) sim.Tick(0.25);
            Assert.IsFalse(r.Connected);
            for (int i = 0; i < 12 && !sim.OperationsLog.Any(x => x.Quantity == "Rule fired" && x.RobotId == r.Id); i++) sim.Tick(0.25);
            OperationRecord seen = sim.OperationsLog.First(x => x.Quantity == "Rule fired" && x.RobotId == r.Id);
            Assert.IsFalse(seen.IsKnown, "observed by a cut-off robot: the colony does not know it yet");
            Assert.AreEqual(TaskPriority.Disabled, r.PriorityOverride(TaskKind.Build), "it follows its own rulebook on its own state");
            Assert.IsNull(sim.PublishRulebook(new Rule[0], "v2: nothing"));
            Assert.IsTrue(sim.Workforce.SendTo(r.Id, sim.BaseTileId));
            for (int i = 0; i < 2000 && !seen.IsKnown; i++) sim.Tick(0.25);
            Assert.IsTrue(seen.IsKnown, "once it syncs, what it saw is known");
            Assert.Greater(seen.KnownAtHours, seen.ObservedAtHours);
        }

        [Test]
        public void APlantRule_FreesAPlantAFullStoreStopped_ByVenting()
        {
            // the plant scenario of the flowsheet tests: a cell that stores its gas with no tank stops when its buffer is full
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike) { ConstructionTimeEnabled = false };
            sim.Workforce.Reset(sim.BaseTileId, 1);
            foreach (var (m, kg) in sim.BuildBill(BuildingType.Workshop).Items()) sim.Materials.Add(m, kg);
            sim.PlaceWorkshop(sim.BaseTileId, 5);
            for (int i = 0; i < 400 && sim.WorkshopCount == 0; i++) sim.Tick(0.25);
            sim.RefineryPowerForTests = true;
            sim.Materials.Clear();
            int t = sim.BaseTileId;
            int cell = Enumerable.Range(0, sim.SlotCount(t)).First(s2 => sim.GetBuilding(t, s2) == BuildingType.None && sim.RefineryBlocker(BuildingType.Electrolyser, t, s2) == null);
            foreach (var (m, kg) in sim.BuildBill(BuildingType.Electrolyser).Items()) sim.Materials.Add(m, kg);
            sim.PlaceRefinery(BuildingType.Electrolyser, t, cell);
            for (int i = 0; i < 400 && sim.IsUnderConstruction(t, cell); i++) sim.Tick(0.25);
            sim.Materials.Clear();
            sim.LogisticsEnabled = true;
            sim.SetByproductPolicy(t, cell, ByproductPolicy.Store);
            var rule = new Rule
            {
                Name = "Vent rather than stop", Rationale = "A stuck plant wastes power; the gas is vented and logged until a tank is built.",
                Scope = RuleScopeKind.Plant, PlantTile = -1, PlantSlot = -1,
                Conditions = new List<RuleCondition> { new RuleCondition { Kind = ConditionKind.PlantBlockedBy, Blocker = IndustrialBlocker.OutputStorageUnavailable, Value = 0.0 } },
                Response = new RuleResponse { Kind = ResponseKind.SetByproductPolicy, Policy = ByproductPolicy.Vent },
            };
            Assert.IsNull(sim.PublishRulebook(new[] { rule }, "v1"));
            sim.Materials.Add(Material.Salt, 5000.0);
            for (int i = 0; i < 20; i++) sim.OrderRefine(RefineProcess.SodiumFromSalt);
            for (int i = 0; i < 2200 && sim.ByproductPolicyOf(t, cell) == ByproductPolicy.Store; i++) sim.Tick(1.0);
            Assert.AreEqual(ByproductPolicy.Vent, sim.ByproductPolicyOf(t, cell), "the rule switched the plant to venting");
            Assert.IsTrue(sim.OperationsLog.Any(x => x.Quantity == "Rule fired" && x.Subject.StartsWith("Plant")), "and the log says which rule did it");
            for (int i = 0; i < 150; i++) sim.Tick(1.0);
            Assert.Greater(sim.VentedKg(Material.Chlorine), 0.0, "the plant runs again");
        }

        [Test]
        public void TheOneShotResponses_SetATarget_PausePlants_AndNotify()
        {
            var sim = Make();
            int tile = Sphere.Neighbors[sim.BaseTileId].First(n => !sim.GetTile(n).IsWater);
            sim.LogisticsEnabled = true;
            var rule = new Rule
            {
                Name = "Keep iron at the outpost", Rationale = "The outpost builds from iron, so it keeps a reserve.",
                Conditions = new List<RuleCondition> { new RuleCondition { Kind = ConditionKind.DepotStockBelow, Tile = tile, Material = Material.Iron, Value = 100.0 } },
                Response = new RuleResponse { Kind = ResponseKind.SetStockTarget, Tile = tile, Material = Material.Iron, Kg = 150.0 },
            };
            Assert.IsNull(sim.PublishRulebook(new[] { rule }, "v1"));
            Assert.AreEqual(0.0, sim.StockTarget(tile, Material.Iron), 1e-12);
            sim.Tick(0.25);
            Assert.AreEqual(150.0, sim.StockTarget(tile, Material.Iron), 1e-12, "the rule set the stock target");
            Assert.IsTrue(sim.OperationsLog.Any(x => x.Quantity == "Rule fired" && x.Text.Contains("reserve")));
        }

        [Test]
        public void AnUnmeasuredTemperature_NeverFiresARule_ItDoesOnceMeasured()
        {
            var sim = Make();
            var rule = new Rule
            {
                Name = "Too hot or cold", Rationale = "Cells and electronics have limits.",
                Conditions = new List<RuleCondition> { new RuleCondition { Kind = ConditionKind.SurfaceTemperatureOutside, Value = 200.0, Value2 = 300.0 } },
                Response = new RuleResponse { Kind = ResponseKind.Notify, Text = "Surface temperature is outside the safe band." },
            };
            Assert.IsNull(sim.PublishRulebook(new[] { rule }, "v1"));
            for (int i = 0; i < 10; i++) sim.Tick(1.0);
            Assert.AreEqual(0, Fired(sim), "not measured: unknown, never fires");
            sim.Parameters.MarkMeasured(SolarParameter.AirTemperature);
            for (int i = 0; i < 10; i++) sim.Tick(1.0);
            Assert.GreaterOrEqual(Fired(sim), 1, "measured, and outside the band (200 to 300 C): it fires");
            Assert.IsTrue(sim.OperationsLog.Any(x => x.Quantity == "Notice"));
        }

        [Test]
        public void SaveAndLoad_KeepsTheVersionHistory_TheRobotsVersions_AndTheLog()
        {
            var sim = Make();
            sim.PublishRulebook(new[] { Safe(25.0) }, "first");
            sim.Tick(1.0);
            sim.PublishRulebook(new[] { Safe(30.0), Safe(10.0) }, "second");
            sim.Tick(1.0);
            SimulationState state = sim.CaptureState();
            Assert.AreEqual(SimulationState.CurrentVersion, state.Version);
            var other = new PlanetSimulation(Sphere, Surface, Earthlike);
            Assert.IsTrue(other.RestoreState(state));
            Assert.AreEqual(2, other.RulebookVersion);
            Assert.AreEqual(2, other.RulebookHistory.Count);
            Assert.AreEqual(1, other.RulebookHistory[0].Rules.Count);
            Assert.AreEqual(2, other.RulebookHistory[1].Rules.Count);
            Assert.AreEqual("first", other.RulebookHistory[0].Note);
            Assert.AreEqual(sim.Workforce.Robots.Select(r => sim.RobotRulebookVersion(r.Id)), other.Workforce.Robots.Select(r => other.RobotRulebookVersion(r.Id)));
            Assert.AreEqual(sim.OperationsLog.Count, other.OperationsLog.Count);
            Assert.AreEqual(sim.PlantRulebookVersion, other.PlantRulebookVersion);
        }

        [Test]
        public void AnOldSave_GetsTheStarterRulebookOnLoad()
        {
            var sim = Make();
            SimulationState state = sim.CaptureState();
            state.Version = 40;
            var other = new PlanetSimulation(Sphere, Surface, Earthlike);
            Assert.IsTrue(other.RestoreState(state));
            Assert.AreEqual(1, other.RulebookVersion);
            Assert.AreEqual(StarterRulebook.Note, other.RulebookHistory[0].Note);
        }

        [Test]
        public void Determinism_TwoRunsGiveTheSameLog()
        {
            string Run()
            {
                var sim = Make();
                sim.InstallStarterRulebook();
                sim.PublishRulebook(new[] { Safe(40.0) }, "v2");
                for (int i = 0; i < 100; i++)
                {
                    if (i % 17 == 0) Drain(sim.Workforce.Robots[0], 0.3);
                    if (i % 29 == 0) Fill(sim.Workforce.Robots[0]);
                    sim.Tick(1.0);
                }
                return string.Join("|", sim.OperationsLog.Select(x => $"{x.Id}:{x.Quantity}:{x.ObservedAtHours:R}:{x.Text}"));
            }
            Assert.AreEqual(Run(), Run());
        }
    }
}
