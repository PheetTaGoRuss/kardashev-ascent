# Development workflow

How a change goes from a request to `main`, with shared steps for every contributor. The Git basics for the team (installing, branching, resolving conflicts) are in [`../../CONTRIBUTING.md`](../../CONTRIBUTING.md) (Thai) and are not repeated here.

## 1. The ten steps

Every development task follows these steps. Do not skip one because the task looks small.

| # | Step | What it means here |
| --- | --- | --- |
| 1 | Read the project instructions | `AGENTS.md`, then the documents it lists for the area |
| 2 | Inspect architecture and documentation | The system's row in [`../architecture/system-architecture.md`](../architecture/system-architecture.md), its specification in `docs/design/`, its entries in [`feature-registry.md`](feature-registry.md) |
| 3 | Inspect existing implementations | Search the code for what already does part of the job. Read the tests of the systems you will touch |
| 4 | Identify dependencies and integration points | Which systems give you inputs, which read your outputs, where in the step order you run, what the save needs |
| 5 | Propose a concise plan | A few lines: scope, files, tests, save version, open questions. For anything that changes the game, get the director's agreement first |
| 6 | Implement only the assigned scope | List unrelated problems as follow-ups; do not fix them in passing |
| 7 | Add or update tests | Per [`testing-standards.md`](testing-standards.md) |
| 8 | Verify scientific correctness | Per [`../physics/scientific-standards.md`](../physics/scientific-standards.md): source first, then code |
| 9 | Update documentation | Section 4 below |
| 10 | Summarise | Changed files, tests and their result, limitations, what remains |

## 2. What a developer must not do

- **Duplicate a system.** If something similar exists, extend it or explain in the pull request why not.
- **Change a design decision silently.** A CONFIRMED entry changes only with the director's agreement, recorded in the registry.
- **Introduce unsupported assumptions.** A number without a source is a placeholder marked `TBD`, or a game rule named as one.
- **Rewrite working code without a demonstrated problem.** Preference is not a reason.
- **Bypass the architecture.** No Unity in Simulation, no game rules in Presentation, no state change outside a command.
- **Add a formula from memory.** Find the source, record it, test against it.
- **Commit to `main`.** Everything goes through a pull request.
- **Claim more than was checked.** "Implemented", "tested" and "verified" mean what the registry says they mean. Say what was not checked.

## 3. Branches, commits and pull requests

| Topic | Rule |
| --- | --- |
| Start | Update `main`; read open issues and pull requests; check that no branch already does the work |
| Branch names | `feature/<name>`, `fix/<name>`, `art/<name>`, `docs/<name>` |
| Working copy | The folder open in the Unity editor belongs to whoever is using the editor. A session that is not driving that editor works in its own Git worktree |
| Play mode | Never stop or start the director's Play mode without asking |
| Commits | One purpose per commit; a sentence that says what changed |
| `.meta` files | Commit them with their asset. A new script made outside Unity needs its `.meta` |
| Scenes and prefabs | Changed through `MainSceneSetup` or in the editor; never as text |
| Size | Build a whole feature, then ask once to merge. Do not ask for a merge per small step |
| Stacked branches | Avoid. If a stack is needed, say the merge order in every pull request of the stack |
| Merging | The director merges, or tells the developer to |

A pull request description contains:

1. what it does, in plain words;
2. which registry entries it changes;
3. the test result (how many tests, where they were run);
4. the save version, if it changed, and how old saves load;
5. what was checked in Play mode and what was not;
6. placeholders introduced, and open questions for the director.

## 4. Which document to update

| When you | Update |
| --- | --- |
| Build, finish or start a feature | Its row in [`feature-registry.md`](feature-registry.md) |
| Add or change an interface member | `docs/simulation-interface.md` |
| Add a system or a flow between systems | [`../architecture/system-architecture.md`](../architecture/system-architecture.md) §5.2 and [`../architecture/system-interactions.md`](../architecture/system-interactions.md) §3–4 |
| Add or change a formula | `docs/physics/physics-verification.md` first, then the code |
| Add a data file or a content table | [`../architecture/data-architecture.md`](../architecture/data-architecture.md) §1 |
| Make an engineering choice others must follow | [`technical-decisions.md`](technical-decisions.md) |
| Find a problem you will not fix now | [`known-issues.md`](known-issues.md) |
| Need a decision from the director | An open question in the registry |
| Finish a roadmap milestone | [`roadmap.md`](roadmap.md) |

A decision that another session needs goes into the repository or a GitHub issue. A decision left in a chat is lost.

## 5. Review and escalation

Assign work according to the task and contributors’ availability. Ask the project director to resolve game-design decisions. Escalate changes that conflict with architecture or specifications, and document scientific gaps when a reliable source cannot be found.

## 6. Starting a first task

1. Read `AGENTS.md` and [`../README.md`](../README.md).
2. Run the tests before changing anything, so you know the starting state.
3. Find the task in the registry and the roadmap. Read its specification, or write one from the template in [`../design/README.md`](../design/README.md) if it has none.
4. Write the plan of step 5 and send it to whoever assigned the task.
5. Create a branch (in a worktree if the editor's folder is in use) and follow the ten steps.

Good first tasks, because they need no design decision: bringing `docs/simulation-interface.md` up to date; the energy ledger test; the determinism test; continuous integration for `tools/simtests`.

## 7. Working with the Unity editor

- The project opens only in Unity `6000.6.8f1`.
- The Unity MCP connection, when it is running, lets a connected development session run tests, read the console and enter Play mode. It is configured for the project folder; a session started elsewhere does not have it.
- After changing scripts outside the editor, let Unity recompile and check the console before running tests.
- New scene objects are added to `MainSceneSetup` and created with **Kardashev Ascent ▸ Set Up Scene**.
