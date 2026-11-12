# Data architecture

Where game content and configuration live, how they are named and labelled, and how they move toward data files.

Section 1 describes `main` as audited on 2026-11-12. Sections 2–5 are standards for new work. Section 6 is a proposed migration.

## 1. Where content lives today

| Content | Where | Form | Data-driven |
| --- | --- | --- | --- |
| Planets | `tools/exoplanets/planets.json` (hand-edited input) → `Assets/_Project/Data/Planets/planets.json` (generated) → `PlanetDNA` assets | JSON read into `PlanetRecord` | Yes |
| Cloud climatology | `Simulation/Climate/CloudClimateTable.cs` | Generated C# table | Generated |
| Spectral match table | `Simulation/Solar/SpectralMatchTable.cs` | Generated C# table | Generated |
| pvlib reference values | `Tests/EditMode/Solar/PvlibReference.cs` | Generated C# table | Generated |
| Physical constants | `Solar/SolarConstants.cs`, `Civilization/Kardashev.cs` | C# constants | No, and should not be |
| Materials | `Resources/Material.cs` | Enum plus `switch` for names and origins | No |
| Deposit rules | `Resources/DepositMap.cs` | Constants and code | No |
| Building types | `BuildingType` enum; costs in `Construction.cs` and `RobotTech`; footprints in `BuildingFootprints.cs` | Spread over four files | No |
| Robot kinds, attributes, compatibility | `Robots/RobotCatalog.cs` | One static catalogue | Centralised, in code |
| Robot energy and wear numbers | `Robots/RobotSpecs.cs` | Constants | No |
| Recipes, modules, unlocks | `Robots/Manufacturing.cs` (`RobotTech`) | Methods returning recipes | No |
| Panel tiers | `Game/PanelTiers.cs` | Static array | Centralised, in code |
| Lander start values | `PowerGrid.Lander()`, `BuildCosts.LanderStockWh` | Constants | No |
| Simulation settings | `StubPlanetSimulation` properties, constants such as `MaxStepHours` | Properties and constants | No |
| Technology, research discoveries, energy sources other than solar, environment parameters per tile | — | Do not exist | — |

Generated files carry a header that says so. Never edit them by hand; change the tool and run it again.

## 2. The loading pattern

The Simulation assembly cannot use `JsonUtility` or read files. The planet pipeline shows the pattern every data file follows:

1. **Record type in Simulation.** A plain `[Serializable]` class whose field names match the file (`PlanetRecord`).
2. **File under `Assets/_Project/Data/<Domain>/`.** Human-readable JSON.
3. **Loading outside Simulation.** An editor importer or Presentation parses the file and hands the record to the simulation's constructor.
4. **Conversion in Simulation.** The record converts itself to the units the models use (`ToSolarParams()` turns degrees into radians).
5. **Validation test.** A test loads the real file and checks every entry.

## 3. Definitions and state

| | Definition | State |
| --- | --- | --- |
| What | What a thing is: a material's density, a recipe, a robot kind's attributes | What the colony has: stock, robots, buildings, progress |
| Changes during play | Never | Yes |
| Lives in | Catalogue or data file | `SimulationState` |
| Identified by | A stable id | The id of its definition plus its own fields |

A save stores state and refers to definitions. It never copies a definition.

## 4. Naming, units and identity

| Topic | Standard |
| --- | --- |
| Ids | Lowercase words joined by hyphens, as the planets already use (`proxima-cen-b`). Stable for ever once saved games contain them |
| Units in field names | The unit is the suffix: `luminosityW`, `radiusM`, `massKg`, `yearLengthHours`, `obliquityDeg`, `airTemperatureC`, `teffK`, `insolationWm2`. A field without a unit suffix is dimensionless or a count |
| Units in code | SI, except time in hours and energy in Wh. Angles in radians inside Simulation, degrees in files and on screen |
| Temperatures | Kelvin for stars and equilibrium temperatures, Celsius for air and equipment; the suffix says which |
| Ratios | 0–1 in files and code; the interface converts to per cent |
| Missing values | State them. Use a provenance entry of `assumed` with a note, never a silent zero |

### Enums in saved games

`JsonUtility` writes enums as integers, and the save contains `Material`, `BuildingType`, `TaskKind`, `RobotKind`, `RobotModule`, `JobKind`, `OperatingProgram`, `GroupState` and others.

- **Append only.** Add new members at the end. Never reorder, insert or delete.
- A removed concept keeps its member, marked obsolete.
- New saved references to content should use string ids (section 6, step D4).

## 5. Provenance of values

Every number that describes the world or sets a balance belongs to one class. The class is written where the value is defined.

| Class | Meaning | How it is marked today |
| --- | --- | --- |
| Measured | Taken from a named dataset, units converted only | `nasa` in planet provenance |
| Derived | Computed from measured values with a stated formula | `derived` |
| Assumed | Not measurable yet; a chosen plausible value | `assumed`, with a note |
| Sourced | A constant from a cited publication or mission document | A comment naming the source, and an entry in `docs/physics/` |
| Game rule | Chosen for play, not physics | A comment saying "game rule" |
| Placeholder | Makes the mechanic run; no balance claim | A comment containing `TBD` |

Rules:

- A `TBD` value may ship. It may not be presented to the player as a scientific fact.
- Replacing a placeholder with a sourced value or a game rule is a change worth its own line in the pull request.
- The scientific classification of content (established, theoretical, speculative, fictional) is a separate axis; see [`../physics/scientific-standards.md`](../physics/scientific-standards.md).

## 6. Moving content into data (proposed)

Not everything should be a file. Move a table when at least one of these is true: it has more than about ten entries; people who do not write C# need to edit it; the same content is defined in more than one place; saved games need to refer to its entries by id.

| Step | Change | Unblocks |
| --- | --- | --- |
| D1 | **Material definitions.** One catalogue with id, name, phase, density, storage class and origin text. The uncommitted industrial-production work already starts a `MaterialCatalog`; align with it instead of making a second | Industry, storage, logistics, water |
| D2 | **Building definitions.** One table: id, footprint, cost, staffing, charging capability, unlock | Every new building; removes the per-type bookkeeping in the orchestrator |
| D3 | **Recipe definitions.** Inputs, outputs, byproducts, electricity, heat, work hours, required facility. Validated for mass balance | Industry and chemical processing |
| D4 | **String ids in saves** for materials, buildings and recipes, with a migration from enum integers | Safe growth of every content list |
| D5 | **One unlock table** replacing the three found by the audit | Technology and research |
| D6 | **Robot definitions.** Merge `RobotSpecs` into the `RobotCatalog` shape, then move to a file | Workforce stage F |
| D7 | **Energy source and environment definitions** | Written with those systems, not before |

Each step keeps the game's numbers identical and is covered by a test that compares the new table with the old constants before the constants are deleted.

Keep in code: physical constants, formulas, and any table that a tool generates.

## 7. Data validation

Every data file has a test that loads the shipped file and checks:

- ids are unique and well formed;
- every reference to another definition resolves;
- every physical value is finite and inside a stated plausible range;
- every value has a provenance class;
- recipes balance in mass within a stated tolerance, with byproducts counted.

`PlanetRecordTests.cs` and `tools/exoplanets/test_convert.py` are the existing examples.
