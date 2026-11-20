# Balance placeholders

Every number below is a placeholder (`TBD` in the code): it makes the mechanic run and carries no balance claim. The game director sets them, or approves a balancing pass (open question OQ-12). This list is generated from the code by searching for `TBD` next to a constant; numbers inside formulas or tables that are marked `TBD` in prose are not all listed.

How to use it: change the value in the code file shown, keep the `TBD` mark until the number is sourced or approved, and run the tests (they assert rules, not these values, wherever possible).

| File (under `Assets/_Project/Scripts/Simulation/`) | Constant | Value | What it is |
| --- | --- | --- | --- |
| `Game/BuildTimes.cs` | `BaseHours` | `2.0` | Robot work hours every building takes at least (game rule |
| `Game/BuildTimes.cs` | `HoursPerRootTonne` | `4.0` | Further work hours per square root of a tonne of materials (game rule |
| `Game/PlanetSimulation.Findings.cs` | `ComputingBatteryReserve` | `0.30` | Grid battery share below which computing is cut when there is no spare power (game rule |
| `Game/PlanetSimulation.Hauls.cs` | `BasePayloadKg` | `490.0` | What a robot with a transport capacity of 1 can carry, kg. Candidate from the Apollo rover's payload rating (about 490 kg); game rule |
| `Game/PlanetSimulation.Plants.cs` | `PlantOutputBulkKg` | `2000.0, PlantOutputHazardousKg = 500.0, PlantOutputGasKg = 200.0` | What an output buffer holds of each storage class, kg. Game rules |
| `Game/PlanetSimulation.Research.cs` | `BatteryUnknownTemperatureShare` | `0.8` | Usable share of battery capacity until the surface temperature range is known (game rule, ): |
| `Game/PlanetSimulation.Research.cs` | `MinimumDesignShare` | `0.5` | The solar safety margin never cuts output below this share (game rule |
| `Game/PlanetSimulation.Solar.cs` | `FloatingCoolingGain` | `0.04` | Output of a floating panel is this much higher than a land panel's: the water keeps the cells cooler (game rule; about 10 °C cooler at -0.35 |
| `Game/PlanetSimulation.Wiring.cs` | `LineLossPerHop` | `0.02` | Share of a generator's power lost for every tile of line between it and the lander tile (resistive losses; real lines lose a few percent per |
| `Game/PlanetSimulation.Wiring.cs` | `LineCapacityPlainW` | `20e6` | What one tile of power line (copper cable) can carry, W (about a 33 kV line). Game rule |
| `Game/PlanetSimulation.Wiring.cs` | `LineCapacityHighW` | `1e9` | What one tile of high-voltage line can carry, W (about a 500 kV line). Game rule |
| `Game/PlanetSimulation.Wiring.cs` | `HighVoltageMilestone` | `1` | The milestone at which the high-voltage line opens |
| `Logistics/HaulPhysics.cs` | `MetresPerElevationUnit` | `1000.0` | game rule,  (open question Q5). Climbing is off by default (<see cref="ClimbEnabled"/> |
| `Logistics/StorageSpecs.cs` | `YardCapacityKg` | `100_000.0` | Storage yard: kg of bulk solids on its two slots. Game rule |
| `Logistics/StorageSpecs.cs` | `SealedStoreCapacityKg` | `20_000.0` | Sealed store: kg of liquids and hazardous solids (sodium, lithium). Game rule |
| `Logistics/StorageSpecs.cs` | `TankVolumeM3` | `10.0` | Gas tank volume, m3, and filling pressure, Pa (20 MPa, a standard high-pressure cylinder bank). Game rule |
| `Planet/Experiments.cs` | `TemperatureSpreadC` | `15.0` | ± °C on the planet's mean temperature seen from orbit (game rule |
| `Planet/Experiments.cs` | `GravitySpreadShare` | `0.10` | ± share on surface gravity from mass and radius (game rule |
| `Planet/Experiments.cs` | `PressureSpreadShare` | `0.50` | ± share on surface pressure from orbit (game rule |
| `Planet/Experiments.cs` | `ChargeMinC` | `0.0, ChargeMaxC = 45.0` | A123 ANR26650); industrial-grade electronics are rated −40 to +85 °C. Game rule built on those ranges |
| `Planet/PlanetSurface.cs` | `CanyonTilesPerThousand` | `12.0` | Canyon tiles per thousand tiles of the planet, roughly (game rule |
| `Planet/PlanetSurface.cs` | `CanyonMinLength` | `6, CanyonMaxLength = 14` | Length of one rift, tiles (game rule |
| `Planet/Tides.cs` | `TurbineCp` | `0.35` | Practical turbine power coefficient (game rule; real tidal rotors reach about 0.35 to 0.45 |
| `Planet/Tides.cs` | `CutInMs` | `0.7` | Cut-in current speed, m/s (below it the rotor does not turn |
| `Planet/Tides.cs` | `RotorAreaM2` | `380.0` | Rotor swept area of one free-flow turbine, m2 (a 22 m rotor |
| `Planet/Tides.cs` | `PeakMsPerMetreOfRange` | `1.0` | Peak stream speed per metre of local tidal range, m/s per m (game rule: strong channels run about 1 m/s per metre, e.g. a 4 m range with a 5 |
| `Planet/Tides.cs` | `RatedMs` | `3.0` | Rated stream speed, m/s: above it the rotor sheds power and holds its rating (real rotors rate at about 3 m/s, e.g. a 1.2 MW SeaGen at 2.4 m |
| `Planet/Tides.cs` | `BarrageBasinM2` | `22e6` | Basin behind one barrage, m2 (La Rance's is about 22 km2). A tile is far bigger than a real basin, so this is a fixed game rule |
| `Planet/Tides.cs` | `BarragePlantShare` | `0.3` | Share of one emptying's energy the plant turns into electricity. About 0.3 puts La Rance's 8 m range at about 420 GWh a year (real: about 50 |
| `Planet/Tides.cs` | `BarrageMinRangeM` | `4.0` | The smallest local tidal range a barrage is worth building on, m (La Rance works at about 8 |
| `Planet/Tides.cs` | `BarrageRangeDrop` | `0.25` | How much of the tile's tidal range a built barrage takes away (a basin's tide weakens behind a dam |
| `Planet/Tides.cs` | `BarrageMinLandShare` | `0.33` | The share of land around a tile that makes it a bay (2 of 6 neighbours |
| `Planet/Tides.cs` | `BarrageGenStart` | `0.10, BarrageGenLength = 0.40` | One-way (ebb) generation: it starts this far into the tide cycle after high water and runs for the next share of the cycle (La Rance generat |
| `Planet/Waves.cs` | `DevelopmentShare` | `0.5` | A real sea is not fully developed (limited fetch and time): this share of the height (game rule |
| `Planet/Waves.cs` | `LineWidthM` | `150.0` | Width of one wave-converter line, m (game rule |
| `Planet/Waves.cs` | `CaptureShare` | `0.35` | Share of the wave power a line turns into electricity (real converters take about a third; game rule |
| `Planet/Waves.cs` | `RatedW` | `1.5e6` | One line's electric rating, W (a Pelamis was 0.75 MW; game rule |
| `Planet/Waves.cs` | `CutInHeightM` | `0.8` | Below this height the line does not move enough to run, m (game rule |
| `Planet/Waves.cs` | `SurvivalHeightM` | `8.0` | Above this height the line shuts down to survive (game rule |
| `Resources/BuildCosts.cs` | `OnSiteShare` | `0.01` | Share of an installation's real material mass the colony supplies on site (game rule |
| `Resources/DepositMap.cs` | `CopperShare` | `0.35, LithiumShare = 0.35` | Share of eligible tiles that hold copper or lithium.  (D4 |
| `Resources/DepositMap.cs` | `ArcReachTiles` | `4` | Earth porphyry belts lie 100–300 km inland of the trench, geology_resources.md claim 1 |
| `Resources/DepositMap.cs` | `SulfideCopperShare` | `0.12, SulfideCopperGrade = 0.5` | poorer than arc copper. Share and grade factor |
| `Resources/DepositMap.cs` | `Spread` | `0.5` | Tile-to-tile spread of grades: grade × (1 − Spread … 1 + Spread).  (D4 |
| `Resources/DepositMap.cs` | `TundraIceGrade` | `0.3` | Ground ice under tundra compared with a glacier.  (D4 |
| `Resources/DepositMap.cs` | `AnorthositeShare` | `0.5` | massif anorthosites stand in old mountain belts). Share of highland and mountain tiles |
| `Resources/DepositMap.cs` | `CaveShare` | `0.2` | Share of mountain, canyon and volcanic tiles with a cave (karst or a lava tube). Game rule |
| `Resources/DepositMap.cs` | `DeepReserveFactor` | `3.0` | so its deposits hold this many times as much (director 2026-11-15: "good amount of ore"). Game rule |
| `Resources/DepositMap.cs` | `RareEarthCanyonShare` | `0.4, RareEarthMountainShare = 0.15, RareEarthVolcanicShare = 0.1` | mountains, volcanic rock; a cave doubles the chance (it reaches deeper rock). Shares are game rules |
| `Resources/Passages.cs` | `CaveTunnelShare` | `0.5` | A cave already goes some way in: a tunnel takes this share of the work and steel (game rule |
| `Resources/Refining.cs` | `AluminaRecovery` | `0.85` | Share of the alumina a sinter-and-leach process recovers (game rule, : 80–90 % is reported for lime-soda sintering |
| `Resources/Refining.cs` | `AluminaWhPerKg` | `6_000.0` | 10–12 GJ/t); sintering anorthosite at about 1 300 °C takes more: game rule |
| `Resources/StandIns.cs` | `SodiumPerLithiumKg` | `22.99 / 6.94` | 3.31 kg. Sodium cells run at a slightly lower voltage, so a little less energy per charge (not modelled |
| `Resources/StandIns.cs` | `SlagPerRockKg` | `1.0` | Slag for one kg of rock. Slag aggregate replaces natural aggregate in concrete (ASTM C989, EN 12620); the 1 : 1 ratio is a game rule,  (MO-0 |
| `Resources/SurveyPuzzle.cs` | `Cores` | `5` | Cores drilled per survey |
| `Resources/SurveyPuzzle.cs` | `PreciseShare` | `0.10, RoughShare = 0.25` | Within this share of the answer: precise; within the rough share: rough |
| `Robots/ColonyNetwork.cs` | `RangeTiles` | `3` | Tiles from a powered node (hub or relay) a robot can still reach the network |
| `Robots/ColonyNetwork.cs` | `HubPowerW` | `2_000.0` | The hub's draw, part of colony demand, W |
| `Robots/ColonyNetwork.cs` | `RelayPowerW` | `500.0` | One relay's draw, W |
| `Robots/FacilityStaffing.cs` | `BuildCrewCapacity` | `6` | Robots that can work one construction site together (director 2026-11-15: more workers, sooner |
| `Robots/Manufacturing.cs` | `ProcessorMilestone` | `4, AdvancedProcessorMilestone = 6` | Milestones that unlock the processors, until research exists (game rule |
| `Robots/Manufacturing.cs` | `InsulatedBatteryC` | `0.0` | An insulated battery is kept at least this warm, °C |
| `Robots/Manufacturing.cs` | `RareEarthPerDriveKg` | `2.0` | Rare earths in a faster drive's magnets, kg (game rule |
| `Robots/RobotBattery.cs` | `DepthExponent` | `1.5` | Wöhler exponent: deeper cycles age the battery more than in proportion.  (no fitted value found |
| `Robots/RobotBattery.cs` | `ZeroDischargeDamage` | `0.002` | Extra health lost each time the battery is run to 0 %.  (spec v2 §7 confirms the effect, no source for the size |
| `Robots/RobotBattery.cs` | `MinorRepairAbove` | `0.90` | Minor ageing (health at or above this) can be repaired in maintenance |
| `Robots/RobotCatalog.cs` | `HealthyCondition` | `0.70` | Above this condition a robot works at full rate |
| `Robots/RobotCatalog.cs` | `WornRate` | `0.5` | Rate at condition 0 from the gradual decline alone |
| `Robots/RobotCatalog.cs` | `CriticalCondition` | `0.30, CriticalFactor = 0.8, SevereCondition = 0.10, SevereFactor = 0.6` | Critical thresholds and the extra factor below each |
| `Robots/RobotSpecs.cs` | `IdlePowerW` | `20.0` | Draw while idle or waiting, W.  (D4 |
| `Robots/RobotSpecs.cs` | `BuildPowerW` | `200.0` | Draw while building, W.  (D4 |
| `Robots/RobotSpecs.cs` | `SalvagePowerW` | `200.0` | Draw while taking a building down, W.  (D4 |
| `Robots/RobotSpecs.cs` | `SurveyPowerW` | `100.0` | Draw while surveying (sensors, drilling cores), W.  (D4 |
| `Robots/RobotSpecs.cs` | `MinePowerW` | `300.0` | Draw while mining, W.  (D4 |
| `Robots/RobotSpecs.cs` | `HaulHandlingPowerW` | `60.0` | Power while loading or unloading cargo, W. Game rule,  (driving with cargo is counted by the drive power |
| `Robots/RobotSpecs.cs` | `HaulHandlingHours` | `0.25` | Hours a robot takes to load or unload its cargo at the stop. Game rule |
| `Robots/RobotSpecs.cs` | `SurveyWorkHours` | `2.0` | Hours of work to survey one tile's ground.  (D4 |
| `Robots/RobotSpecs.cs` | `MiningBatchHours` | `1.0` | Hours of work in one mining batch; the robot re-evaluates its work after each.  (D4 |
| `Robots/RobotSpecs.cs` | `MiningKgPerHour` | `150.0` | Material mined per hour of work from a grade-1 deposit, kg.  (D4 |
| `Robots/RobotSpecs.cs` | `SpecialistSpeed` | `2.0` | A specialised robot's work speed at its own job, relative to a General robot.  (D4 |
| `Robots/RobotSpecs.cs` | `WearPerWorkingHour` | `0.001` | Condition lost per hour of work (1 = new).  (D4 |
| `Robots/RobotSpecs.cs` | `FlareWearPerHour` | `0.0005` | Extra condition lost per hour on a planet of a flaring red dwarf (design D13).  (D4 |
| `Robots/RobotSpecs.cs` | `MaintenanceBelowCondition` | `0.30` | Go for maintenance below this condition.  (D4 |
| `Robots/RobotSpecs.cs` | `RepairPerHour` | `0.10` | Condition restored per hour of maintenance.  (D4 |
| `Robots/RobotSpecs.cs` | `RepairIronKg` | `20.0` | Iron for a full repair (spec v2 §8: basic maintenance uses basic materials), kg |
| `Robots/RobotSpecs.cs` | `RepairWithoutIronRate` | `0.5` | Without iron, repairs go this much slower |
| `Robots/RobotSpecs.cs` | `MinorBatteryRepairPerHour` | `0.01` | Battery health restored per hour of maintenance when the ageing is minor |
| `Robots/RouteFinder.cs` | `FlatHopHours` | `4.0` | Hours to cross one flat tile.  (D4 |
| `Robots/RouteFinder.cs` | `BaseDrivePowerW` | `50.0` | Drive power on flat ground under Earth gravity, W.  (D4 |
| `Robots/TaskBook.cs` | `AssistanceAfterHours` | `12.0` | Blocked this long, a task asks for assistance (and the player is told |
| `Robots/Workforce.cs` | `DefaultGroupCapacity` | `4` | public const int DefaultGroupCapacity = 4; //  (D-CAP |
| `Robots/Workforce.cs` | `ComputingWPerLevel` | `300.0` | Colony computing the robots need, W: each AI level above 1 adds a share |
| `Solar/LatitudePuzzle.cs` | `RodM` | `2.0` | Rod height, m |
| `Solar/LatitudePuzzle.cs` | `PreciseDeg` | `0.5` | Within this many degrees the answer is a precise fix |
| `Solar/LatitudePuzzle.cs` | `RoughDeg` | `2.0` | Within this many degrees the answer is a rough fix (the colony keeps it, marked rough |
| `Solar/LightPuzzles.cs` | `PreciseShare` | `0.05, RoughShare = 0.15` | Within this share of the answer: precise; within the rough share: rough (half the range kept |
| `Solar/LightPuzzles.cs` | `AlbedoPreciseShare` | `0.10` | Precise and rough limits: a share of the answer, or absolute (°C, or share of light for dust |
| `Solar/SolarParameters.cs` | `MeasureHours` | `6.0` | Hours a robot needs on site to take one measurement |
| `Solar/SolarParameters.cs` | `RoughRangeLeft` | `0.5` | Share of the range left after a rough measurement |

99 constants listed. Generated 2026-11-20 by `tools/balance_sheet.py` (run it again after changing the code).
