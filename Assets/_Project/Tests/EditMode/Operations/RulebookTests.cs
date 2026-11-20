using System.Collections.Generic;
using System.Linq;
using KardashevAscent.Simulation.Operations;
using KardashevAscent.Simulation.Resources;
using KardashevAscent.Simulation.Robots;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Operations
{
    /// <summary>The rulebook (MO-10, MO-11): typed conditions on known state, rationales, versions, first match wins, unknown never fires.</summary>
    public class RulebookTests
    {
        static Rule Night(double below = 25.0, string rationale = "Cold cuts usable capacity.") => new Rule
        {
            Id = 1, Name = "Night safing", Rationale = rationale,
            Conditions = new List<RuleCondition>
            {
                new RuleCondition { Kind = ConditionKind.RobotBatteryBelow, Value = below },
                new RuleCondition { Kind = ConditionKind.IsNight },
            },
            Response = new RuleResponse { Kind = ResponseKind.SafeMode },
        };

        static KnownState State(double battery, bool night) => new KnownState { RobotBatteryPct = battery, IsNight = night };

        [Test]
        public void AllConditionsMustHold_AndUnknownNeverFires()
        {
            Rule r = Night();
            Assert.IsTrue(RuleEvaluator.Holds(r, State(20.0, true)));
            Assert.IsFalse(RuleEvaluator.Holds(r, State(30.0, true)), "battery fine");
            Assert.IsFalse(RuleEvaluator.Holds(r, State(20.0, false)), "daytime");
            var unknown = new KnownState { RobotBatteryPct = 20.0 };   // does not know whether it is night
            Assert.AreEqual(Truth.Unknown, RuleEvaluator.Evaluate(r.Conditions[1], unknown));
            Assert.IsFalse(RuleEvaluator.Holds(r, unknown), "a rule never fires on a guess");
        }

        [Test]
        public void ARuleOnAnUnmeasuredTemperature_NeverFires_UntilItIsMeasured()
        {
            var c = new RuleCondition { Kind = ConditionKind.SurfaceTemperatureOutside, Value = -40.0, Value2 = 60.0 };
            Assert.AreEqual(Truth.Unknown, RuleEvaluator.Evaluate(c, new KnownState { SurfaceTemperatureC = null }));
            Assert.AreEqual(Truth.True, RuleEvaluator.Evaluate(c, new KnownState { SurfaceTemperatureC = -55.0 }));
            Assert.AreEqual(Truth.False, RuleEvaluator.Evaluate(c, new KnownState { SurfaceTemperatureC = 20.0 }));
        }

        [Test]
        public void EveryCondition_HasATrueAndAFalseCase_NoneThrows()
        {
            foreach (ConditionKind kind in System.Enum.GetValues(typeof(ConditionKind)))
            {
                var c = new RuleCondition { Kind = kind, Value = 10.0, Value2 = 20.0, Tile = 1, Material = Material.Iron, Blocker = IndustrialBlocker.MissingInput };
                var s = new KnownState
                {
                    RobotBatteryPct = 5.0, RobotConditionPct = 5.0, IsNight = true, HoursCutOff = 50.0, GridBatteryPct = 5.0, Brownout = true,
                    SurfaceTemperatureC = 99.0, StellarFlare = true, PlantBlocker = IndustrialBlocker.MissingInput, PlantBlockedHours = 50.0,
                    PlantOutputBufferPct = 90.0, DepotKg = (t, m) => 5.0,
                };
                Truth a = RuleEvaluator.Evaluate(c, s);
                Assert.AreNotEqual(Truth.Unknown, a, kind + " with full knowledge is known");
                Assert.AreEqual(Truth.Unknown, RuleEvaluator.Evaluate(c, new KnownState()), kind + " with no knowledge is unknown");
            }
        }

        [Test]
        public void ARuleWithoutARationale_IsRefused_AndNothingIsPublished()
        {
            var book = new Rulebook();
            string why = book.Publish(new[] { Night(rationale: " ") }, "bad", 0.0);
            StringAssert.Contains("rationale", why);
            Assert.AreEqual(0, book.CurrentNumber);
        }

        [Test]
        public void TheLimits_OneToThreeConditions_AndScopeMatchesTheKindOfRule()
        {
            Rule none = Night(); none.Conditions.Clear();
            Assert.IsNotNull(none.Problem());
            Rule four = Night();
            for (int i = 0; i < 2; i++) four.Conditions.Add(new RuleCondition { Kind = ConditionKind.IsDay });
            Assert.IsNotNull(four.Problem(), "more than three conditions");
            Rule plantCondition = Night(); plantCondition.Conditions[0] = new RuleCondition { Kind = ConditionKind.PlantOutputBufferAbove, Value = 80.0 };
            Assert.IsNotNull(plantCondition.Problem(), "a plant condition on a robot rule");
            Rule plantResponse = Night(); plantResponse.Response = new RuleResponse { Kind = ResponseKind.PausePlant };
            Assert.IsNotNull(plantResponse.Problem(), "a plant response on a robot rule");
            Assert.IsNull(Night().Problem());
        }

        [Test]
        public void EveryEditMakesANewVersion_AndTheHistoryIsKept()
        {
            var book = new Rulebook();
            Assert.IsNull(book.Publish(new[] { Night(25.0) }, "first", 10.0));
            Assert.IsNull(book.Publish(new[] { Night(30.0) }, "raised", 20.0));
            Assert.AreEqual(2, book.CurrentNumber);
            Assert.AreEqual(25.0, book.Get(1).Rules[0].Conditions[0].Value, "version 1 still reads as it was");
            Assert.AreEqual(30.0, book.Get(2).Rules[0].Conditions[0].Value);
            Assert.AreEqual("raised", book.Get(2).Note);
            Assert.AreEqual(20.0, book.Get(2).EditedAtHours);
            Assert.IsNull(book.Get(3));
        }

        [Test]
        public void APublishedRule_IsACopy_NotTheCallersObject()
        {
            var book = new Rulebook();
            Rule r = Night();
            book.Publish(new[] { r }, "", 0.0);
            r.Conditions[0].Value = 99.0;
            Assert.AreEqual(25.0, book.Current.Rules[0].Conditions[0].Value, "editing the caller's rule does not rewrite history");
        }

        [Test]
        public void TheFirstRuleThatHolds_Wins()
        {
            var first = Night(); first.Id = 1; first.Name = "first";
            var second = Night(); second.Id = 2; second.Name = "second"; second.Response = new RuleResponse { Kind = ResponseKind.HoldPosition };
            var version = new RulebookVersion { Number = 1, Rules = new List<Rule> { first, second } };
            Rule won = Rulebook.Winner(version, r => true, State(10.0, true), r => false, r => true);
            Assert.AreEqual("first", won.Name);
            first.Enabled = false;
            Assert.AreEqual("second", Rulebook.Winner(version, r => true, State(10.0, true), r => false, r => true).Name, "a disabled rule is skipped");
            Assert.IsNull(Rulebook.Winner(version, r => r.Id == 99, State(10.0, true), r => false, r => true), "scope decides who a rule applies to");
        }

        [Test]
        public void ARuleStillCoolingDown_CannotStart_ButOneThatIsAlreadyHoldingKeepsHolding()
        {
            var rule = Night();
            var version = new RulebookVersion { Number = 1, Rules = new List<Rule> { rule } };
            Assert.IsNull(Rulebook.Winner(version, r => true, State(10.0, true), r => false, r => false), "cooling down, not active");
            Assert.IsNotNull(Rulebook.Winner(version, r => true, State(10.0, true), r => true, r => false), "already holding");
        }

        [Test]
        public void TheStarterRulebook_HasThreeRulesWithRationales_AndPublishes()
        {
            List<Rule> rules = StarterRulebook.Rules();
            Assert.AreEqual(3, rules.Count);
            foreach (Rule r in rules)
            {
                Assert.IsNull(r.Problem(), r.Name);
                Assert.IsFalse(string.IsNullOrWhiteSpace(r.Rationale), r.Name);
            }
            Assert.AreEqual(25.0, rules[0].Conditions[0].Value, "night safing below 25 % battery");
            Assert.AreEqual(6.0, rules[1].Conditions[0].Value, "cut off for more than 6 h");
            Assert.AreEqual(12.0, rules[2].Conditions[0].Value, "a plant blocked more than 12 h");
            Assert.IsNull(new Rulebook().Publish(rules, StarterRulebook.Note, 0.0));
        }

        [Test]
        public void TheLog_KeepsOrder_MarksWhatACutOffRobotSawOnceItSyncs_AndIsBounded()
        {
            var log = new OperationsLog();
            log.Add(new OperationRecord { Subject = "Robot 3", RobotId = 3, ObservedAtHours = 5.0 });
            log.Add(new OperationRecord { Subject = "Robot 4", RobotId = 4, ObservedAtHours = 6.0, KnownAtHours = 6.0 });
            Assert.IsFalse(log.Records[0].IsKnown, "observed by a robot that was cut off: not known yet");
            Assert.AreEqual(1, log.MarkKnown(3, 40.0));
            Assert.AreEqual(40.0, log.Records[0].KnownAtHours);
            Assert.AreEqual(6.0, log.Records[1].KnownAtHours, "records that were already known are left");
            for (int i = 0; i < OperationsLog.Capacity + 20; i++) log.Add(new OperationRecord());
            Assert.AreEqual(OperationsLog.Capacity, log.Records.Count);
            Assert.AreEqual(log.Records.Count, log.Records.Select(r => r.Id).Distinct().Count(), "ids never repeat");
        }
    }
}
