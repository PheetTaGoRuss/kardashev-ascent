using System.Collections.Generic;
using KardashevAscent.Simulation.Game;
using UnityEngine;

namespace KardashevAscent.Presentation.Planet
{
    /// <summary>
    /// A detailed model for every building (director 2026-11-20: "make detailed model for every building"), made from
    /// primitives so it needs no art files. Each model fits one slot (about 0.9 wide, up to 0.6 tall, facing +Z) and is built
    /// of coloured parts: the same model is stood on each slot a building covers, and a copy is drawn by the build palette.
    /// </summary>
    public static class BuildingModels
    {
        // the palette: concrete, steel, paint, glass and lights
        static readonly Color Concrete = new Color(0.62f, 0.64f, 0.66f);
        static readonly Color DarkConcrete = new Color(0.36f, 0.38f, 0.41f);
        static readonly Color Steel = new Color(0.70f, 0.73f, 0.78f);
        static readonly Color DarkSteel = new Color(0.25f, 0.27f, 0.31f);
        static readonly Color White = new Color(0.93f, 0.94f, 0.96f);
        static readonly Color Copper = new Color(0.82f, 0.48f, 0.27f);
        static readonly Color Rust = new Color(0.62f, 0.32f, 0.20f);
        static readonly Color Warning = new Color(0.98f, 0.78f, 0.15f);
        static readonly Color Glass = new Color(0.40f, 0.72f, 0.90f);
        static readonly Color Green = new Color(0.30f, 0.80f, 0.46f);
        static readonly Color Teal = new Color(0.16f, 0.70f, 0.64f);
        static readonly Color Violet = new Color(0.62f, 0.46f, 0.88f);
        static readonly Color Blue = new Color(0.30f, 0.52f, 0.88f);
        static readonly Color Orange = new Color(0.95f, 0.50f, 0.22f);
        static readonly Color Glow = new Color(1.00f, 0.62f, 0.20f);
        static readonly Color Black = new Color(0.08f, 0.09f, 0.11f);

        /// <summary>Make the model of <paramref name="type"/> under <paramref name="root"/>, every part coloured through <paramref name="parts"/>.</summary>
        public static void Build(GameObject root, BuildingType type, Material lit, List<(Renderer Renderer, Color Colour)> parts)
        {
            var b = new Builder(root, lit, parts);
            switch (type)
            {
                case BuildingType.Battery: Battery(b); break;
                case BuildingType.Workshop: Workshop(b); break;
                case BuildingType.ChargingStation: Charger(b); break;
                case BuildingType.Relay: Relay(b); break;
                case BuildingType.ResearchStation: Lab(b); break;
                case BuildingType.AluminaPlant: Alumina(b); break;
                case BuildingType.Electrolyser: Electrolyser(b); break;
                case BuildingType.TidalTurbine: Turbine(b); break;
                case BuildingType.WaveConverter: Waves(b); break;
                case BuildingType.TidalBarrage: Barrage(b); break;
                case BuildingType.PowerLine: Pole(b); break;
                case BuildingType.HighVoltageLine: Tower(b); break;
                case BuildingType.SolarPanel: Panel(b, false); break;
                case BuildingType.FloatingSolar: Panel(b, true); break;
                default: b.Cube("Block", Concrete, 0, 0.1f, 0, 0.7f, 0.2f, 0.7f); break;
            }
        }

        // ---- the builder: positions are in model units (the slot is about 0.9 wide), y up from the ground ----

        sealed class Builder
        {
            readonly GameObject root;
            readonly Material lit;
            readonly List<(Renderer, Color)> parts;

            public Builder(GameObject root, Material lit, List<(Renderer Renderer, Color Colour)> parts)
            {
                this.root = root; this.lit = lit; this.parts = parts;
            }

            public GameObject Part(PrimitiveType shape, string name, Color colour, float x, float y, float z, float sx, float sy, float sz, Vector3? euler = null)
            {
                GameObject g = GameObject.CreatePrimitive(shape);
                g.name = name;
                Object.Destroy(g.GetComponent<Collider>());
                g.transform.SetParent(root.transform, false);
                g.transform.localPosition = new Vector3(x, y, z);
                g.transform.localScale = new Vector3(sx, sy, sz);
                if (euler.HasValue) g.transform.localRotation = Quaternion.Euler(euler.Value);
                var r = g.GetComponent<MeshRenderer>();
                r.sharedMaterial = lit;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                parts?.Add((r, colour));
                return g;
            }

            /// <summary>A box centred at (x, y, z) with these sizes.</summary>
            public GameObject Cube(string name, Color c, float x, float y, float z, float sx, float sy, float sz, Vector3? euler = null) =>
                Part(PrimitiveType.Cube, name, c, x, y, z, sx, sy, sz, euler);

            /// <summary>An upright cylinder of this diameter and height whose middle is at (x, y, z).</summary>
            public GameObject Cyl(string name, Color c, float x, float y, float z, float diameter, float height, Vector3? euler = null) =>
                Part(PrimitiveType.Cylinder, name, c, x, y, z, diameter, height * 0.5f, diameter, euler);

            public GameObject Ball(string name, Color c, float x, float y, float z, float diameter) =>
                Part(PrimitiveType.Sphere, name, c, x, y, z, diameter, diameter, diameter);

            public GameObject Pill(string name, Color c, float x, float y, float z, float diameter, float length, Vector3? euler = null) =>
                Part(PrimitiveType.Capsule, name, c, x, y, z, diameter, length * 0.5f, diameter, euler);
        }

        // ---- the buildings ----

        static void Battery(Builder b)
        {
            b.Cube("Pad", DarkConcrete, 0, 0.012f, 0, 0.92f, 0.024f, 0.80f);
            for (int row = 0; row < 2; row++)
                for (int col = 0; col < 3; col++)
                {
                    float x = (col - 1) * 0.29f, z = row == 0 ? -0.17f : 0.17f;
                    b.Cube("Container", Green, x, 0.14f, z, 0.25f, 0.2f, 0.30f);
                    b.Cube("Stripe", White, x, 0.15f, z + (row == 0 ? 0.152f : -0.152f), 0.19f, 0.04f, 0.004f);
                    b.Cube("Roof", DarkSteel, x, 0.248f, z, 0.23f, 0.012f, 0.28f);
                    b.Cube("Vent", Steel, x, 0.262f, z, 0.1f, 0.014f, 0.1f);
                }
            b.Cube("Transformer", Warning, -0.36f, 0.09f, 0f, 0.1f, 0.14f, 0.14f);
            b.Cyl("Cooler", Steel, 0.36f, 0.08f, -0.2f, 0.12f, 0.12f);
            b.Cyl("Cooler", Steel, 0.36f, 0.08f, 0.2f, 0.12f, 0.12f);
            b.Cube("Busbar", Copper, 0f, 0.31f, 0f, 0.7f, 0.014f, 0.02f);
        }

        static void Workshop(Builder b)
        {
            b.Cube("Slab", DarkConcrete, 0, 0.012f, 0, 0.92f, 0.024f, 0.8f);
            b.Cube("Hall", Steel, 0f, 0.15f, -0.06f, 0.7f, 0.26f, 0.5f);
            b.Cube("RoofLeft", DarkSteel, -0.17f, 0.32f, -0.06f, 0.42f, 0.02f, 0.54f, new Vector3(0, 0, 20f));
            b.Cube("RoofRight", DarkSteel, 0.17f, 0.32f, -0.06f, 0.42f, 0.02f, 0.54f, new Vector3(0, 0, -20f));
            b.Cube("Door", Black, 0f, 0.12f, 0.2f, 0.34f, 0.2f, 0.01f);
            b.Cube("DoorFrame", Warning, 0f, 0.235f, 0.205f, 0.38f, 0.02f, 0.012f);
            b.Cube("Gantry", Orange, 0.34f, 0.2f, 0.18f, 0.03f, 0.36f, 0.03f);
            b.Cube("GantryArm", Orange, 0.2f, 0.37f, 0.18f, 0.34f, 0.03f, 0.03f);
            b.Cube("Hook", DarkSteel, 0.08f, 0.31f, 0.18f, 0.015f, 0.1f, 0.015f);
            b.Cyl("Chimney", Concrete, -0.28f, 0.36f, -0.2f, 0.07f, 0.2f);
            b.Cube("Crate", Rust, -0.36f, 0.05f, 0.28f, 0.14f, 0.1f, 0.14f);
            b.Cube("Crate", Copper, -0.22f, 0.04f, 0.32f, 0.1f, 0.08f, 0.1f);
        }

        static void Charger(Builder b)
        {
            b.Cube("Pad", DarkConcrete, 0, 0.012f, 0, 0.92f, 0.024f, 0.8f);
            for (int i = 0; i < 4; i++)
                b.Cyl("Post", Steel, (i % 2 == 0 ? -0.34f : 0.34f), 0.18f, (i < 2 ? -0.28f : 0.28f), 0.04f, 0.34f);
            b.Cube("Canopy", White, 0f, 0.37f, 0f, 0.82f, 0.03f, 0.7f);
            b.Cube("CanopyStripe", Warning, 0f, 0.355f, 0.35f, 0.82f, 0.02f, 0.012f);
            for (int i = -1; i <= 1; i += 2)
            {
                float x = i * 0.16f;
                b.Cube("Pillar", DarkSteel, x, 0.13f, 0f, 0.1f, 0.22f, 0.08f);
                b.Cube("Screen", Glass, x, 0.19f, 0.042f, 0.06f, 0.07f, 0.005f);
                b.Ball("Lamp", Green, x, 0.265f, 0f, 0.05f);
                b.Cube("Cable", Black, x + i * 0.07f, 0.06f, 0.02f, 0.02f, 0.1f, 0.02f);
            }
            b.Cube("Bay", Warning, 0f, 0.026f, 0f, 0.7f, 0.004f, 0.012f);
        }

        static void Relay(Builder b)
        {
            b.Cube("Pad", DarkConcrete, 0, 0.012f, 0, 0.7f, 0.024f, 0.7f);
            b.Cube("Hut", Steel, 0f, 0.08f, 0.18f, 0.3f, 0.12f, 0.2f);
            b.Cube("Door", DarkSteel, 0f, 0.07f, 0.282f, 0.08f, 0.09f, 0.006f);
            b.Cube("MastLeg", DarkSteel, -0.025f, 0.28f, -0.1f, 0.02f, 0.5f, 0.02f);
            b.Cube("MastLeg", DarkSteel, 0.025f, 0.28f, -0.1f, 0.02f, 0.5f, 0.02f);
            for (int i = 0; i < 5; i++)
                b.Cube("Brace", DarkSteel, 0f, 0.1f + i * 0.1f, -0.1f, 0.07f, 0.012f, 0.012f, new Vector3(0, 0, i % 2 == 0 ? 25f : -25f));
            b.Part(PrimitiveType.Sphere, "Dish", White, -0.07f, 0.42f, -0.06f, 0.16f, 0.05f, 0.16f, new Vector3(-90f, 0f, 0f));
            b.Part(PrimitiveType.Sphere, "Dish", White, 0.07f, 0.32f, -0.06f, 0.12f, 0.04f, 0.12f, new Vector3(-90f, 0f, 20f));
            b.Cube("Panel", Steel, 0f, 0.5f, -0.1f, 0.12f, 0.1f, 0.01f);
            b.Ball("Beacon", Orange, 0f, 0.55f, -0.1f, 0.045f);
        }

        static void Lab(Builder b)
        {
            b.Cube("Pad", DarkConcrete, 0, 0.012f, 0, 0.92f, 0.024f, 0.8f);
            b.Cube("Block", White, 0.12f, 0.12f, 0f, 0.5f, 0.2f, 0.5f);
            b.Cube("Windows", Glass, 0.12f, 0.15f, 0.252f, 0.4f, 0.06f, 0.004f);
            b.Ball("Dome", Violet, -0.22f, 0.12f, 0.05f, 0.3f);
            b.Cube("DomeBase", DarkSteel, -0.22f, 0.05f, 0.05f, 0.3f, 0.06f, 0.3f);
            b.Cyl("Mast", Steel, 0.28f, 0.34f, -0.15f, 0.02f, 0.3f);
            b.Part(PrimitiveType.Sphere, "Dish", White, 0.28f, 0.5f, -0.15f, 0.14f, 0.04f, 0.14f, new Vector3(-60f, 0f, 0f));
            b.Cube("Sensor", Warning, 0.0f, 0.245f, -0.1f, 0.1f, 0.03f, 0.1f);
            b.Cyl("Tank", Concrete, 0.34f, 0.07f, 0.28f, 0.14f, 0.14f);
            b.Cube("Beacon", Violet, 0.12f, 0.25f, 0.2f, 0.06f, 0.02f, 0.06f);
        }

        static void Alumina(Builder b)
        {
            b.Cube("Pad", DarkConcrete, 0, 0.012f, 0, 0.92f, 0.024f, 0.8f);
            b.Cyl("Kiln", Rust, -0.2f, 0.18f, 0f, 0.28f, 0.34f);
            b.Cyl("KilnBand", DarkSteel, -0.2f, 0.12f, 0f, 0.3f, 0.03f);
            b.Cyl("KilnBand", DarkSteel, -0.2f, 0.26f, 0f, 0.3f, 0.03f);
            b.Cyl("Stack", Concrete, -0.2f, 0.46f, 0f, 0.09f, 0.28f);
            b.Cube("StackTop", Warning, -0.2f, 0.61f, 0f, 0.1f, 0.02f, 0.1f);
            b.Cyl("TankA", White, 0.2f, 0.15f, -0.18f, 0.24f, 0.28f);
            b.Cyl("TankB", White, 0.2f, 0.15f, 0.18f, 0.24f, 0.28f);
            b.Cube("PipeA", Steel, 0f, 0.24f, 0f, 0.4f, 0.03f, 0.03f);
            b.Cube("PipeB", Steel, 0.2f, 0.3f, 0f, 0.03f, 0.03f, 0.36f);
            b.Cube("Hopper", DarkSteel, -0.2f, 0.06f, 0.3f, 0.2f, 0.1f, 0.14f);
        }

        static void Electrolyser(Builder b)
        {
            b.Cube("Pad", DarkConcrete, 0, 0.012f, 0, 0.92f, 0.024f, 0.8f);
            b.Cube("Shed", DarkSteel, 0f, 0.1f, -0.05f, 0.8f, 0.16f, 0.5f);
            b.Cube("Roof", Steel, 0f, 0.19f, -0.05f, 0.84f, 0.02f, 0.54f);
            for (int i = 0; i < 5; i++)
            {
                float x = (i - 2) * 0.15f;
                b.Cube("Cell", Glow, x, 0.09f, 0.2f, 0.11f, 0.12f, 0.03f);
                b.Cube("CellFrame", Black, x, 0.09f, 0.18f, 0.13f, 0.14f, 0.01f);
            }
            b.Cube("Busbar", Copper, 0f, 0.215f, 0f, 0.8f, 0.03f, 0.04f);
            b.Cube("Rectifier", Warning, 0.36f, 0.1f, 0.3f, 0.14f, 0.18f, 0.14f);
            b.Cyl("Vent", Concrete, -0.3f, 0.27f, -0.15f, 0.07f, 0.16f);
            b.Cyl("Vent", Concrete, 0f, 0.27f, -0.15f, 0.07f, 0.16f);
        }

        static void Turbine(Builder b)
        {
            b.Cyl("Mount", DarkConcrete, 0f, 0.015f, 0f, 0.7f, 0.03f);
            b.Cyl("Tower", Teal, 0f, 0.26f, 0f, 0.08f, 0.46f);
            b.Cyl("Collar", DarkSteel, 0f, 0.07f, 0f, 0.12f, 0.06f);
            b.Pill("Nacelle", Steel, 0f, 0.5f, 0.02f, 0.16f, 0.34f, new Vector3(90f, 0f, 0f));
            b.Cyl("Hub", DarkSteel, 0f, 0.5f, 0.2f, 0.07f, 0.06f, new Vector3(90f, 0f, 0f));
            for (int i = 0; i < 3; i++)
            {
                float a = i * 120f * Mathf.Deg2Rad + 0.35f;
                float cx = Mathf.Sin(a) * 0.17f, cy = Mathf.Cos(a) * 0.17f;
                b.Cube("Blade", Teal, cx, 0.5f + cy, 0.21f, 0.07f, 0.34f, 0.012f, new Vector3(0, 0, -a * Mathf.Rad2Deg));
            }
            b.Cube("Warning", Warning, 0f, 0.03f, 0f, 0.1f, 0.01f, 0.1f);
        }

        static void Waves(Builder b)
        {
            for (int i = -1; i <= 1; i++)
            {
                float z = i * 0.3f;
                b.Pill("Float", Blue, 0f, 0.07f, z, 0.17f, 0.52f, new Vector3(0, 0, 90f));
                b.Cube("Stripe", White, 0f, 0.15f, z, 0.46f, 0.012f, 0.05f);
                if (i < 1) b.Ball("Joint", DarkSteel, 0f, 0.07f, z + 0.15f, 0.09f);
            }
            b.Cube("Spine", DarkSteel, 0f, 0.035f, 0f, 0.04f, 0.02f, 0.86f);
            b.Cyl("Anchor", DarkConcrete, 0f, 0.02f, -0.45f, 0.14f, 0.04f);
            b.Cube("Chain", Steel, 0f, 0.04f, -0.37f, 0.015f, 0.015f, 0.12f);
            b.Cube("Beacon", Orange, 0f, 0.21f, 0.3f, 0.04f, 0.04f, 0.04f);
        }

        static void Barrage(Builder b)
        {
            b.Cube("Wall", Concrete, 0f, 0.14f, 0f, 0.96f, 0.28f, 0.32f);
            b.Cube("Deck", DarkConcrete, 0f, 0.29f, 0f, 0.96f, 0.02f, 0.36f);
            for (int i = -1; i <= 1; i++)
            {
                float x = i * 0.3f;
                b.Cyl("Housing", Steel, x, 0.12f, 0.17f, 0.2f, 0.16f, new Vector3(90f, 0f, 0f));
                b.Cube("Gate", DarkSteel, x, 0.22f, -0.17f, 0.2f, 0.18f, 0.02f);
                b.Cube("GateTower", Warning, x, 0.36f, 0f, 0.1f, 0.14f, 0.1f);
            }
            b.Cube("Spillway", Glass, 0f, 0.03f, 0.3f, 0.9f, 0.01f, 0.18f);
            b.Cube("Rail", Steel, 0f, 0.32f, 0.17f, 0.96f, 0.02f, 0.012f);
        }

        static void Pole(Builder b)
        {
            b.Cyl("Foot", DarkConcrete, 0f, 0.015f, 0f, 0.22f, 0.03f);
            b.Cyl("Pole", Warning, 0f, 0.3f, 0f, 0.05f, 0.56f);
            b.Cube("Crossarm", DarkSteel, 0f, 0.52f, 0f, 0.46f, 0.025f, 0.025f);
            for (int i = -1; i <= 1; i += 2)
            {
                b.Cyl("Insulator", White, i * 0.19f, 0.565f, 0f, 0.03f, 0.07f);
                b.Cyl("Insulator", White, i * 0.08f, 0.565f, 0f, 0.03f, 0.07f);
            }
            b.Cube("Transformer", Steel, 0.09f, 0.4f, 0f, 0.1f, 0.14f, 0.1f);
            b.Cube("SwitchBox", DarkSteel, -0.09f, 0.22f, 0.04f, 0.09f, 0.12f, 0.05f);
            b.Cube("Wire", Black, 0f, 0.47f, 0f, 0.8f, 0.006f, 0.006f);
        }

        static void Tower(Builder b)
        {
            b.Cube("Foot", DarkConcrete, 0, 0.015f, 0, 0.5f, 0.03f, 0.5f);
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    b.Cube("Leg", Orange, sx * 0.1f, 0.3f, sz * 0.1f, 0.025f, 0.56f, 0.025f, new Vector3(sz * -5f, 0, sx * 5f));
            for (int i = 0; i < 4; i++)
                b.Cube("Brace", DarkSteel, 0f, 0.1f + i * 0.13f, 0.1f - i * 0.012f, 0.19f - i * 0.02f, 0.012f, 0.012f, new Vector3(0, 0, i % 2 == 0 ? 20f : -20f));
            b.Cube("ArmLow", Orange, 0f, 0.46f, 0f, 0.7f, 0.03f, 0.03f);
            b.Cube("ArmHigh", Orange, 0f, 0.56f, 0f, 0.46f, 0.03f, 0.03f);
            for (int i = -1; i <= 1; i++)
            {
                b.Cyl("String", White, i * 0.3f, 0.41f, 0f, 0.03f, 0.1f);
                if (i != 0) b.Cyl("String", White, i * 0.19f, 0.51f, 0f, 0.03f, 0.1f);
            }
            b.Cube("Cap", Warning, 0f, 0.6f, 0f, 0.06f, 0.04f, 0.06f);
        }

        /// <summary>The same table the field draws (<see cref="SolarPanelVisual"/>): a post under a framed slab, tilted.</summary>
        static void Panel(Builder b, bool floating)
        {
            b.Cube("Post", DarkSteel, 0f, 0.225f, 0f, 0.07f, 0.45f, 0.07f);
            var tilt = new Vector3(-30f, 0f, 0f);
            b.Cube("Frame", Steel, 0f, 0.45f, 0f, 1f, 0.04f, 0.66f, tilt);
            b.Cube("Cells", Blue, 0f, 0.45f + 0.0217f + 0.02f * 0.866f, -0.0125f - 0.01f, 0.92f, 0.02f, 0.58f, tilt);
        }
    }
}
