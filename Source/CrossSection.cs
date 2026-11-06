using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace CustomProceduralParts
{
    // One Spine cross-section. Built-ins are the parametric profiles (fillet / stretch sliders apply);
    // customs are user polygons in the unit box [-1,1]^2 (x = width, y = height), CCW, every vertex
    // kept as a corner. Either way Outline() returns ProceduralShapeSpine.Sides points that start near
    // +x and run CCW, so any two sections morph point-for-point along a segment.
    public class CrossSection
    {
        public readonly string name;
        public readonly SpineProfile? builtIn;
        public readonly List<Vector2> verts;   // custom only
        private Vector2[] _unit;

        public bool IsBuiltIn => builtIn.HasValue;

        private CrossSection(string name, SpineProfile? builtIn, List<Vector2> verts)
        {
            this.name = name;
            this.builtIn = builtIn;
            this.verts = verts;
        }

        public static CrossSection BuiltIn(SpineProfile p) => new CrossSection(p.ToString(), p, null);
        public static CrossSection Custom(string name, List<Vector2> verts) => new CrossSection(name, null, new List<Vector2>(verts));

        // Sides-point outline scaled to half-extents rH x rV. Fillets / stretch only affect built-ins.
        public Vector2[] Outline(float rH, float rV, float filletTop, float filletBottom, float stretch)
        {
            if (builtIn.HasValue)
                return ProceduralShapeSpine.ProfileOutline(builtIn.Value, rH, rV, filletTop, filletBottom, stretch);
            Vector2[] u = UnitOutline;
            var o = new Vector2[u.Length];
            for (int j = 0; j < u.Length; j++) o[j] = new Vector2(u[j].x * rH, u[j].y * rV);
            return o;
        }

        public Vector2[] UnitOutline => builtIn.HasValue
            ? ProceduralShapeSpine.ProfileOutline(builtIn.Value, 1f, 1f, 0f, 0f, 0f)
            : (_unit ??= SectionGeometry.Resample(verts, ProceduralShapeSpine.Sides));

        // Starting vertices for the editor: a custom's own polygon, or a built-in's default outline minus
        // the filler points along its straight runs (a Rectangle opens as its 4 corners, leaving room to
        // add vertices; curved sections keep their samples).
        public List<Vector2> EditableVerts()
        {
            if (!builtIn.HasValue) return new List<Vector2>(verts);
            var o = new List<Vector2>(UnitOutline);
            for (bool removed = true; removed && o.Count > 3;)
            {
                removed = false;
                for (int i = 0; i < o.Count && o.Count > 3; i++)
                {
                    Vector2 a = o[(i + o.Count - 1) % o.Count] - o[i], b = o[(i + 1) % o.Count] - o[i];
                    if (Mathf.Abs(a.x * b.y - a.y * b.x) <= 1e-4f * a.magnitude * b.magnitude) { o.RemoveAt(i); removed = true; i--; }
                }
            }
            return o;
        }

        // Changes whenever the geometry does (drag-cube / icon cache key).
        public string Key => builtIn.HasValue ? name : name + "#" + SectionGeometry.Format(verts).GetHashCode().ToString("x8");

        public ConfigNode ToNode()
        {
            var n = new ConfigNode(CrossSectionLibrary.NodeName);
            n.AddValue("name", name);
            foreach (Vector2 v in verts) n.AddValue("v", SectionGeometry.Format(v));
            return n;
        }

        // A custom section from config, or null if it's malformed / invalid.
        public static CrossSection FromNode(ConfigNode n)
        {
            string name = n.GetValue("name");
            if (CrossSectionLibrary.ValidateName(name) != null) return null;
            var vs = new List<Vector2>();
            foreach (string s in n.GetValues("v"))
            {
                if (!SectionGeometry.TryParse(s, out Vector2 v)) return null;
                vs.Add(v);
            }
            if (SectionGeometry.Validate(vs) != null) return null;
            return Custom(name.Trim(), SectionGeometry.Normalize(vs));
        }
    }

    // Built-ins plus the user's custom sections (PluginData/CrossSections.cfg, which KSP's GameDatabase
    // skips). Crafts also embed the custom sections they use, imported here on load.
    public static class CrossSectionLibrary
    {
        public const string NodeName = "CROSS_SECTION";
        private const string ModTag = "[CrossSectionLibrary]";

        private static List<CrossSection> _sections;

        // Fired with the section name after a custom section is added, changed or deleted.
        public static event Action<string> Changed;

        private static string FilePath =>
            Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "CustomProceduralParts", "PluginData", "CrossSections.cfg");

        public static IList<CrossSection> All { get { EnsureLoaded(); return _sections; } }

        public static string[] Names => All.Select(s => s.name).ToArray();

        public static CrossSection Get(string name) => All.FirstOrDefault(s => s.name == name);

        // Unknown names (a deleted section, a typo) render as Ellipse; the node keeps its name.
        public static CrossSection Resolve(string name) => Get(name) ?? Get(nameof(SpineProfile.Ellipse));

        public static bool IsBuiltInName(string name) => Enum.GetNames(typeof(SpineProfile)).Contains(name);

        // Null if usable as a custom section name, else why not.
        public static string ValidateName(string name)
        {
            name = name?.Trim();
            if (string.IsNullOrEmpty(name)) return "Name is empty.";
            if (IsBuiltInName(name)) return $"\"{name}\" is a built-in section.";
            if (name.IndexOfAny(new[] { '{', '}', '=', '\n', '\r' }) >= 0 || name.Contains("//")) return "Name can't contain { } = or //.";
            return null;
        }

        private static void EnsureLoaded()
        {
            if (_sections != null) return;
            _sections = new List<CrossSection>();
            foreach (SpineProfile p in Enum.GetValues(typeof(SpineProfile))) _sections.Add(CrossSection.BuiltIn(p));
            try
            {
                if (File.Exists(FilePath))
                {
                    ConfigNode root = ConfigNode.Load(FilePath);
                    foreach (ConfigNode n in root?.GetNodes(NodeName) ?? new ConfigNode[0])
                    {
                        CrossSection cs = CrossSection.FromNode(n);
                        if (cs == null) { Debug.LogWarning($"{ModTag} skipping invalid section \"{n.GetValue("name")}\" in {FilePath}"); continue; }
                        if (Get(cs.name) == null) _sections.Add(cs);
                    }
                }
            }
            catch (Exception e) { Debug.LogError($"{ModTag} loading {FilePath}: {e}"); }
            SortCustoms();
        }

        private static void SortCustoms()
        {
            int nb = Enum.GetValues(typeof(SpineProfile)).Length;
            List<CrossSection> customs = _sections.Skip(nb).OrderBy(s => s.name, StringComparer.OrdinalIgnoreCase).ToList();
            _sections.RemoveRange(nb, _sections.Count - nb);
            _sections.AddRange(customs);
        }

        private static void Persist()
        {
            try
            {
                var root = new ConfigNode();
                foreach (CrossSection cs in _sections.Where(s => !s.IsBuiltIn)) root.AddNode(cs.ToNode());
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                root.Save(FilePath);
            }
            catch (Exception e) { Debug.LogError($"{ModTag} saving {FilePath}: {e}"); }
        }

        // Add or replace a custom section (verts already validated + normalized).
        public static void Save(string name, List<Vector2> verts)
        {
            name = name.Trim();
            EnsureLoaded();
            int i = _sections.FindIndex(s => s.name == name);
            if (i >= 0 && _sections[i].IsBuiltIn) throw new InvalidOperationException("can't overwrite built-in " + name);
            if (i >= 0) _sections[i] = CrossSection.Custom(name, verts);
            else _sections.Add(CrossSection.Custom(name, verts));
            SortCustoms();
            Persist();
            Changed?.Invoke(name);
        }

        public static void Delete(string name)
        {
            EnsureLoaded();
            if (_sections.RemoveAll(s => !s.IsBuiltIn && s.name == name) == 0) return;
            Persist();
            Changed?.Invoke(name);
        }

        // A section embedded in a craft: returns the library name it ended up under. Same name + same
        // shape -> that one; else any custom with the same shape; else the name if free; else "name (N)",
        // so a downloaded craft never overwrites (or is overwritten by) a different local section.
        public static string Import(CrossSection cs)
        {
            EnsureLoaded();
            string shape = SectionGeometry.Format(cs.verts);
            CrossSection same = Get(cs.name);
            if (same != null && !same.IsBuiltIn && SectionGeometry.Format(same.verts) == shape) return same.name;
            CrossSection twin = _sections.FirstOrDefault(s => !s.IsBuiltIn && SectionGeometry.Format(s.verts) == shape);
            if (twin != null) return twin.name;
            string name = cs.name;
            for (int k = 2; Get(name) != null; k++) name = $"{cs.name} ({k})";
            Debug.Log($"{ModTag} importing section \"{cs.name}\" from a craft as \"{name}\"");
            Save(name, cs.verts);
            return name;
        }
    }

    public static class SectionGeometry
    {
        public static string Format(Vector2 v) =>
            v.x.ToString("0.#####", CultureInfo.InvariantCulture) + "," + v.y.ToString("0.#####", CultureInfo.InvariantCulture);

        public static string Format(IEnumerable<Vector2> vs) => string.Join(" ", vs.Select(Format).ToArray());

        public static bool TryParse(string s, out Vector2 v)
        {
            v = default;
            string[] p = s?.Split(',');
            if (p == null || p.Length != 2) return false;
            if (!float.TryParse(p[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float x)) return false;
            if (!float.TryParse(p[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float y)) return false;
            v = new Vector2(x, y);
            return true;
        }

        private static float SignedArea(IList<Vector2> p)
        {
            float a = 0f;
            for (int i = 0; i < p.Count; i++)
            {
                Vector2 c = p[i], n = p[(i + 1) % p.Count];
                a += c.x * n.y - n.x * c.y;
            }
            return 0.5f * a;
        }

        // Null if the polygon can be a section, else why not.
        public static string Validate(IList<Vector2> p)
        {
            int n = p.Count;
            if (n < 3) return "Needs at least 3 vertices.";
            if (n > ProceduralShapeSpine.Sides) return $"At most {ProceduralShapeSpine.Sides} vertices.";
            for (int i = 0; i < n; i++)
                if ((p[(i + 1) % n] - p[i]).sqrMagnitude < 1e-8f) return "Two neighbouring vertices are on top of each other.";
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                {
                    if (j == i + 1 || (i == 0 && j == n - 1)) continue;   // adjacent edges share a vertex
                    if (SegmentsCross(p[i], p[(i + 1) % n], p[j], p[(j + 1) % n])) return "Edges cross each other.";
                }
            Vector2 min = p[0], max = p[0];
            foreach (Vector2 v in p) { min = Vector2.Min(min, v); max = Vector2.Max(max, v); }
            if (max.x - min.x < 1e-3f || max.y - min.y < 1e-3f) return "Section is flat.";
            if (Mathf.Abs(SignedArea(p)) < 1e-4f) return "Section has no area.";
            return null;
        }

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        private static bool SegmentsCross(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            float d1 = Cross(b - a, c - a), d2 = Cross(b - a, d - a), d3 = Cross(d - c, a - c), d4 = Cross(d - c, b - c);
            if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0))) return true;
            const float eps = 1e-7f;   // touching / collinear overlap also counts
            return (Mathf.Abs(d1) < eps && OnSeg(a, b, c)) || (Mathf.Abs(d2) < eps && OnSeg(a, b, d))
                || (Mathf.Abs(d3) < eps && OnSeg(c, d, a)) || (Mathf.Abs(d4) < eps && OnSeg(c, d, b));
        }

        private static bool OnSeg(Vector2 a, Vector2 b, Vector2 p) =>
            p.x >= Mathf.Min(a.x, b.x) - 1e-6f && p.x <= Mathf.Max(a.x, b.x) + 1e-6f &&
            p.y >= Mathf.Min(a.y, b.y) - 1e-6f && p.y <= Mathf.Max(a.y, b.y) + 1e-6f;

        // CCW, and stretched to fill the unit box on both axes: a node's width / height are then the
        // section's true extents (diameters, attach-node sizes, tip handles all assume that).
        public static List<Vector2> Normalize(IList<Vector2> p)
        {
            var o = new List<Vector2>(p);
            if (SignedArea(o) < 0f) o.Reverse();
            Vector2 min = o[0], max = o[0];
            foreach (Vector2 v in o) { min = Vector2.Min(min, v); max = Vector2.Max(max, v); }
            Vector2 c = 0.5f * (min + max), h = 0.5f * (max - min);
            for (int i = 0; i < o.Count; i++)
                o[i] = new Vector2((o[i].x - c.x) / h.x, (o[i].y - c.y) / h.y);
            return o;
        }

        // N points by arc length with every vertex kept, rotated to start at the point nearest +x
        // (where the built-ins start) so morphs line up.
        public static Vector2[] Resample(IList<Vector2> verts, int N)
        {
            Vector2[] r = ProceduralShapeSpine.ResampleWithCorners(verts, Enumerable.Range(0, verts.Count).ToList(), N);
            int start = 0;
            float best = float.MaxValue;
            for (int i = 0; i < r.Length; i++)
            {
                float a = Mathf.Abs(Mathf.Atan2(r[i].y, r[i].x));
                if (a < best - 1e-5f) { best = a; start = i; }
            }
            var o = new Vector2[N];
            for (int i = 0; i < N; i++) o[i] = r[(start + i) % N];
            return o;
        }

        // Ear-clipping triangulation of a simple CCW polygon: (n-2) triangles, each CCW. A degenerate
        // remainder (collapsed ring, all-collinear leftovers) is closed as a fan so the count holds.
        public static int[] Triangulate(IList<Vector2> p)
        {
            int n = p.Count;
            var tris = new List<int>((n - 2) * 3);
            var idx = Enumerable.Range(0, n).ToList();
            if (SignedArea(p) < 0f) idx.Reverse();
            int guard = 0;
            while (idx.Count > 3 && guard++ < n * n)
            {
                bool clipped = false;
                for (int k = 0; k < idx.Count; k++)
                {
                    int ia = idx[(k + idx.Count - 1) % idx.Count], ib = idx[k], ic = idx[(k + 1) % idx.Count];
                    Vector2 a = p[ia], b = p[ib], c = p[ic];
                    if (Cross(b - a, c - b) <= 1e-9f) continue;   // reflex or collinear: not an ear
                    bool inside = false;
                    foreach (int m in idx)
                    {
                        if (m == ia || m == ib || m == ic) continue;
                        if (InTriangle(p[m], a, b, c)) { inside = true; break; }
                    }
                    if (inside) continue;
                    tris.Add(ia); tris.Add(ib); tris.Add(ic);
                    idx.RemoveAt(k);
                    clipped = true;
                    break;
                }
                if (!clipped) break;
            }
            for (int k = 1; k + 1 < idx.Count; k++) { tris.Add(idx[0]); tris.Add(idx[k]); tris.Add(idx[k + 1]); }
            return tris.ToArray();
        }

        private static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c) =>
            Cross(b - a, p - a) >= 0f && Cross(c - b, p - b) >= 0f && Cross(a - c, p - c) >= 0f;
    }
}
