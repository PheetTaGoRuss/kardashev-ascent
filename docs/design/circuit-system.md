# Circuit system (player-built DC circuits)

Status: PROPOSED. Part of the electrical redesign (`docs/design/electrical-system.md`, workflow W-34 to W-39). Pure simulation code is built and tested; the editor, the saved library and the first host are not.

## What exists (Simulation/Circuits)

- **Parts (tier 1):** battery (finite Wh, internal resistance), DC supply (current limit), switch, push button, lamp, resistor, fuse, relay (coil and separate contacts), diode, junction. Each is a real electrical model. Saved as integers, append only.
- **Blueprint** (`CircuitBlueprint`): parts with a grid position and rotation, wires, named ports, grid size, a version that rises on every edit. A wire's **physical length is a number the player sets**; where the part sits on the grid changes no resistance (tested).
- **Instance** (`CircuitInstance`): a deep copy of a blueprint installed in one host, with its own state (switches, relay contacts, blown fuses, battery charge) and fault history. Editing the blueprint later does not reach it (tested).
- **Solver** (`CircuitSolver`): steady-state DC by nodal analysis on the same dense Cholesky core as the grid flow (`DenseSpd`). Every pin is a node. Switches, relay contacts, diodes and fuses are settled by repeating the solve until nothing changes; a circuit that never settles is reported (chatter), not hidden.
- **Diagnostics:** no source, open circuit, short circuit, reversed polarity (a blocking diode), overcurrent (supply, lamp, resistor), blown fuse, undersized wire, excessive voltage drop, excessive wire loss, empty battery, disconnected terminal, invalid connection.
- **Three kinds of check, kept apart:** electrical (the solver's diagnostics), host fit (`CircuitCompatibility.Check`: part count, board size, required ports), and function (does it do the host's job; waits for the host definition).
- **Not built:** capacitors, inductors, switching in time (the `IDynamicCircuitStep` seam exists, nothing implements it); AC; signal wires and sensors (a signal wire between tier-1 parts is refused as invalid); motor, heater, meters, circuit breaker; the 2D editor; saving blueprints and instances in `SimulationState`.

## Game rules still to balance (all TBD)

| Rule | Value in code | Why |
|---|---|---|
| Wire current rating | 8 A per mm2 of copper or aluminium | small wire in free air; a placeholder until the thermal model |
| Excessive loss warning | wires lose more than 10 % of source power | readability |
| Excessive drop warning | a wire drops more than 10 % of source voltage | readability |
| Short circuit | loop resistance under 0.05 ohm above the source's own | readability |
| Closed switch, fuse | 1 milliohm | ideal enough |
| Diode on-resistance | 10 milliohm | ideal enough |
| Part values (battery Wh, lamp rating) | set by the player in the blueprint | no catalogue yet |

Physics: Ohm, KCL, KVL, R = rho L / A (copper and aluminium constants as in `physics-verification.md`), P = V I, I2R heat.

## Gate G4: the first host (director decision needed)

A circuit does nothing until a host reads it through a defined interface; the circuit never writes other systems directly. Candidates that exist today:

1. **Charging station controller**: the circuit decides when the station charges a robot (a switch or relay wired to a "charge enable" port). Output: charge enable on or off. Uses the existing charging logic; the smallest first step.
2. **Relay building**: the circuit becomes the relay's power-up logic (what powers a relay, and when it relays). Output: relay on or off.
3. **Robot power board**: a circuit in a robot that cuts a load under low battery. Needs per-robot installation, more work.

The example in the brief, a temperature sensor starting a cooling motor, needs a building thermal model that does not exist (only the planet climate `WasteHeat` does), so it is out of reach for now.

Recommendation: option 1, with ports `power` (the supply side) and `enable` (the control contact).
