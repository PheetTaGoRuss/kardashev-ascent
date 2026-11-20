using System;
using KardashevAscent.Simulation.Logistics;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Logistics
{
    /// <summary>The energy of hauling (MO-04) and the light-time figure (MO-01). Sources: docs/physics/physics-verification.md.</summary>
    public class HaulPhysicsTests
    {
        const double Base = 50.0;
        const double Robot = HaulPhysics.DefaultRobotMassKg;

        [Test]
        public void AnEmptyRobot_CostsExactlyTheOldDrivePower()
        {
            // The old model: drive power = base x g / g_earth. With no cargo the new one must be identical.
            foreach (double g in new[] { 3.7, 9.80665, 12.1, 19.6 })
            {
                double old = Base * g / HaulPhysics.StandardGravityMs2;
                Assert.AreEqual(old, HaulPhysics.DrivePowerW(Base, g, Robot, 0.0), 1e-12);
            }
        }

        [Test]
        public void DoubleTheGravity_DoublesTheRollingPower()
        {
            double one = HaulPhysics.DrivePowerW(Base, HaulPhysics.StandardGravityMs2, Robot, 500.0);
            double two = HaulPhysics.DrivePowerW(Base, 2.0 * HaulPhysics.StandardGravityMs2, Robot, 500.0);
            Assert.AreEqual(2.0 * one, two, 1e-9);
        }

        [Test]
        public void ACargoAsHeavyAsTheRobot_DoublesThePower()
        {
            double empty = HaulPhysics.DrivePowerW(Base, HaulPhysics.StandardGravityMs2, Robot, 0.0);
            Assert.AreEqual(2.0 * empty, HaulPhysics.DrivePowerW(Base, HaulPhysics.StandardGravityMs2, Robot, Robot), 1e-9);
            Assert.AreEqual(2.0, HaulPhysics.LoadFactor(Robot, Robot), 1e-12);
        }

        [Test]
        public void NegativeCargo_IsTreatedAsNone()
        {
            Assert.AreEqual(HaulPhysics.DrivePowerW(Base, 9.80665, Robot, 0.0), HaulPhysics.DrivePowerW(Base, 9.80665, Robot, -50.0), 1e-12);
        }

        [Test]
        public void ClimbingCostsPotentialEnergyOverEfficiency_AndDownhillCostsNothing()
        {
            // 1 000 kg total, g = 9.80665, 100 m up, efficiency 0.5: 1000 x 9.80665 x 100 / 0.5 J = 544.8 Wh.
            double up = HaulPhysics.ClimbEnergyWh(500.0, 500.0, 9.80665, 100.0, 0.5);
            Assert.AreEqual(1000.0 * 9.80665 * 100.0 / 0.5 / 3600.0, up, 1e-9);
            Assert.AreEqual(544.8, up, 0.1);
            Assert.AreEqual(0.0, HaulPhysics.ClimbEnergyWh(500.0, 500.0, 9.80665, -100.0, 0.5), 0.0, "no regeneration downhill");
        }

        [Test]
        public void ClimbingIsOffByDefault()
        {
            Assert.IsFalse(HaulPhysics.ClimbEnabled);
        }

        [Test]
        public void ProximaCentauriB_IsAboutFourPointTwoFourLightYears()
        {
            double ly = HaulPhysics.LightYearsFromParsecs(1.30);
            Assert.AreEqual(4.24, ly, 0.01);
            Assert.AreEqual(8.48, 2.0 * ly, 0.02, "a question to Earth and its answer");
        }

        [Test]
        public void ARobotMassOfZero_IsRefused()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => HaulPhysics.DrivePowerW(Base, 9.8, 0.0, 10.0));
        }
    }
}
