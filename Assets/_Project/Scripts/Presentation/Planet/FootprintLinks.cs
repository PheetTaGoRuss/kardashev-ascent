using System.Collections.Generic;
using UnityEngine;

namespace KardashevAscent.Presentation.Planet
{
    /// <summary>
    /// Joins the modules of a building that covers several slots (director 2026-11-15: a 2-slot building must look like
    /// one building across both slots): a flat deck between each module and its nearest earlier neighbour, and for
    /// block buildings a duct along it at container height. Pooled cubes under the planet, like the modules.
    /// </summary>
    public sealed class FootprintLinks
    {
        readonly Transform planet;
        readonly Material deckMaterial, ductMaterial;
        readonly bool withDuct;
        readonly string name;
        readonly List<GameObject> decks = new List<GameObject>();
        readonly List<GameObject> ducts = new List<GameObject>();
        MaterialPropertyBlock block;

        public FootprintLinks(string name, Transform planet, Material deck, Material duct, bool withDuct)
        {
            this.name = name;
            this.planet = planet;
            deckMaterial = deck;
            ductMaterial = duct;
            this.withDuct = withDuct;
        }

        /// <summary>
        /// Link modules at these planet-local positions (each on the ground, <paramref name="ups"/> their local up),
        /// modules <paramref name="size"/> across; neighbours are under 1.6 module sizes apart.
        /// </summary>
        public void Show(IReadOnlyList<Vector3> positions, IReadOnlyList<Vector3> ups, float size, float growth)
        {
            int used = 0;
            float reach = size * 1.6f;
            for (int i = 1; i < positions.Count; i++)
            {
                int nearest = -1;
                float best = float.MaxValue;
                for (int j = 0; j < i; j++)
                {
                    float d = (positions[i] - positions[j]).magnitude;
                    if (d < best) { best = d; nearest = j; }
                }
                if (nearest < 0 || best > reach)
                    continue;
                Vector3 a = positions[nearest], b = positions[i];
                Vector3 up = (ups[nearest] + ups[i]).normalized;
                Vector3 along = b - a;
                Quaternion rotation = Quaternion.LookRotation(along.normalized, up);
                Vector3 middle = (a + b) * 0.5f;

                GameObject deck = Get(decks, used, "Deck", deckMaterial);
                deck.transform.localPosition = middle + up * (size * 0.012f);
                deck.transform.localRotation = rotation;
                deck.transform.localScale = new Vector3(size * 0.55f, size * 0.022f, along.magnitude);
                deck.SetActive(true);

                if (withDuct)
                {
                    GameObject duct = Get(ducts, used, "Duct", ductMaterial);
                    duct.transform.localPosition = middle + up * (size * 0.12f * growth);
                    duct.transform.localRotation = rotation;
                    duct.transform.localScale = new Vector3(size * 0.12f, size * 0.07f * growth, along.magnitude);
                    duct.SetActive(true);
                }
                used++;
            }
            for (int k = used; k < decks.Count; k++) decks[k].SetActive(false);
            for (int k = used; k < ducts.Count; k++) ducts[k].SetActive(false);
        }

        GameObject Get(List<GameObject> pool, int index, string part, Material material)
        {
            while (pool.Count <= index)
            {
                GameObject g = GameObject.CreatePrimitive(PrimitiveType.Cube);
                g.name = $"{name} {part} {pool.Count + 1}";
                g.hideFlags = HideFlags.DontSave;
                Object.Destroy(g.GetComponent<Collider>());
                g.transform.SetParent(planet, false);
                g.GetComponent<MeshRenderer>().sharedMaterial = material;
                if (block != null) g.GetComponent<Renderer>().SetPropertyBlock(block);
                g.SetActive(false);
                pool.Add(g);
            }
            return pool[index];
        }

        public void Hide()
        {
            foreach (GameObject g in decks) g.SetActive(false);
            foreach (GameObject g in ducts) g.SetActive(false);
        }

        public void SetTint(Color color)
        {
            block ??= new MaterialPropertyBlock();
            block.SetColor("_Color", color);
            foreach (GameObject g in decks) g.GetComponent<Renderer>().SetPropertyBlock(block);
            foreach (GameObject g in ducts) g.GetComponent<Renderer>().SetPropertyBlock(block);
        }

        public void Destroy()
        {
            foreach (GameObject g in decks) if (g != null) Object.Destroy(g);
            foreach (GameObject g in ducts) if (g != null) Object.Destroy(g);
            decks.Clear();
            ducts.Clear();
        }
    }
}
