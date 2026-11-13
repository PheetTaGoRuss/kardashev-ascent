# Testing standards

What gets tested, how, and what "done" means for a change. Sections 1–2 describe what exists on `main` as audited on 2026-11-12; the rest are the standards for new work.

## 1. What exists

| Kind | Where | State |
| --- | --- | --- |
| Simulation unit and integration tests | `Assets/_Project/Tests/EditMode` (40 files, NUnit, EditMode) | 753 test cases pass on `main` when run outside Unity |
| Scientific reference tests | `Solar/SolarModelTests.cs` against values generated from pvlib (`PvlibReference.cs`); tolerance table in `docs/physics/physics-verification.md` | In place for the solar model only |
| Save migration tests | Inside the feature's test file (for example `VersionOneSave_LoadsEverythingFinished_WithTheLanderParts`, `Save_Version6_LoadsWithOneGeneralRobot_AndItsSitesKeepGoing`) | Six found; not every one of the 16 versions has its own |
| Data validation | `Planet/PlanetRecordTests.cs`; `tools/exoplanets/test_convert.py` (Python `unittest`) | Planets only |
| Presentation tests | — | None |
| Whole-simulation determinism | `Game/ConservationAndDeterminismTests.cs` | Same inputs give the same colony; chunked ticking gives the same time |
| Performance | — | None (planned as workforce stage H) |
| Conservation balances | same file | Energy balances on every step (found and fixed a 2 kW accounting gap); mass checked only for non-negative stock |
| Continuous integration | `.github/workflows/tests.yml` | Runs the simulation tests, `tools/check_layers.py` and the Python tests on pull requests (written, not yet seen to run on GitHub) |

## 2. Running the tests

**In Unity (authoritative):** Window → General → Test Runner → EditMode → Run All. An AI developer with the Unity MCP connection runs the same tests through it.

**Inside Unity from a script:** the editor driver (`Assets/_Project/Scripts/Editor/DevDriver.cs`) runs every EditMode test when sent `tests`, and writes `TESTS passed=… failed=…` to `Temp/dev-driver.log` (803 pass on 2026-11-13).

**Outside Unity (fast check, added with this document):**

```bash
dotnet test tools/simtests/SimTests.csproj
```

This compiles the Simulation and test sources with the .NET SDK and NUnit. It works because Simulation has no engine references. It uses a different runtime and NUnit version from Unity, so a pass here is a strong signal and not the final word: run the Unity Test Runner before merging anything that touches Simulation.

**Python tools:**

```bash
python -m unittest discover tools/exoplanets
```

## 3. Kinds of test and when each is required

| Kind | Checks | Required when |
| --- | --- | --- |
| Unit | One class or formula in isolation | Always, for new Simulation code |
| Integration | Two or more systems through the orchestrator (`PlanetSimulationTests`, `RobotWorkTests`) | A change affects how systems interact or the step order |
| Simulation scenario | A colony run for days of game time, asserting outcomes (survives the night, reaches a milestone) | A change affects balance or progression |
| Scientific verification | A model against numbers from outside the implementation, with a stated tolerance | Any new or changed formula |
| Regression | The exact case of a fixed bug | Every bug fix |
| Save migration | A state of the previous version loads into the new one with stated defaults; current state round-trips | Every save version bump |
| Data validation | The shipped data file: ids, references, ranges, provenance, mass balance | Every data file and every edit to one |
| Determinism | Same seed and commands give identical state; visuals do not change results | Changes to scheduling, randomness or step order |
| Performance | A stated colony size steps within a stated time budget | Changes to per-robot or per-tile loops, once stage H sets the budget |
| Conservation | Energy and mass ledgers balance over a run | New producers or consumers of energy or matter |

## 4. Writing tests

- **Location:** `Tests/EditMode/<Namespace>/<Subject>Tests.cs`, mirroring the Simulation folder. Commit the `.meta` file.
- **Names:** `Subject_Condition_Result` or a plain sentence, as in the existing files (`Staffing_IsBalanced_EveryFacilityGetsSomeoneFirst`).
- **One reason to fail** per test.
- **Tolerances** are written with their reason: the model's accuracy, the reference's precision, or floating-point noise. Never loosen a tolerance to make a test pass without saying why in the pull request.
- **Reference values** state where they come from in a comment: library and version, publication, or hand calculation.
- **Placeholders:** a test of a mechanic that runs on `TBD` numbers asserts the rule (more depth ages faster), not the number, so that balancing does not break it. Where a test must use the number, read it from the constant.
- **No Unity types** in Simulation tests. No reliance on wall-clock time, file system or network.
- **Internal access** is available to tests through `InternalsVisibleTo`; use it to set up state, not to assert on private details.

## 5. Scientific verification

For each formula the test file or `physics-verification.md` records:

| Item | Example |
| --- | --- |
| The formula and its source | Kasten–Young 1989 air mass |
| Units of every input and output | zenith in degrees, result dimensionless |
| Range of validity | zenith 0–90°, guarded above |
| Reference values and their origin | pvlib 0.16.1 `get_relative_airmass` |
| Tolerance, and the difference actually found | stated in the test; the comparison found at most 2 × 10⁻¹⁴ |
| Edge cases | sun at and below the horizon |

Generated reference files are regenerated by their tool, never edited.

## 6. Presentation

There are no automated Presentation tests and none are required yet. Until there are:

- Keep logic out of views so that it can be tested in Simulation.
- Check a Presentation change in Play mode and say in the pull request exactly what was looked at and what was not.
- A view that fails to find its simulation or its UI document logs one clear error instead of throwing every frame.

## 7. Definition of done

A change is done when:

1. the whole EditMode suite passes in Unity, with the count stated in the pull request;
2. new behaviour has tests of the kinds section 3 requires;
3. new formulas are sourced and verified (section 5);
4. a save version bump has its migration test;
5. Presentation changes were checked in Play mode, with what was checked written down;
6. documentation is updated (see [`development-workflow.md`](development-workflow.md));
7. anything not verified is stated plainly.

"Implemented", "tested" and "verified" are three different claims; the feature registry defines them.

## 8. Gaps to close (proposed order)

1. Run the out-of-Unity test project in continuous integration on every pull request.
2. Energy and mass ledger tests.
3. A determinism test for a scripted colony.
4. A performance budget and test (workforce stage H).
5. Data validation for each new data file as content moves out of code.
6. A small set of PlayMode smoke tests: the scene builds, the simulation ticks, a save round-trips.
