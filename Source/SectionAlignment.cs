using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CustomProceduralParts
{
    // Best-fit point pairing for a Spine whose sections are polygons with any number of vertices. The mesh is one
    // grid (each node's ring is shared by the segments above and below it, and a segment lerps ring point k to ring
    // point k), so each node gets ONE sampling of its outline, chosen so ring point k sits on matching features all
    // along the spine:
    //   1. each pair of neighbouring outlines is matched by a monotone map around their perimeters (dynamic time
    //      warping on dense arc-length samples, scored on position and edge direction, over the best few starting
    //      rotations); two outlines with the same vertex count also try vertex i -> vertex i+o, kept unless the warp
    //      is far better (a third the cost), so a family of sections drawn alike morphs vertex to vertex;
    //   2. the maps chain into "tracks" running through every node, parametrized by their mean perimeter fraction;
    //   3. every node's vertices are placed on that parameter and clustered: a cluster is a feature shared across
    //      nodes, one ring point. Sharp corners come first, then the rest by how much they turn; what doesn't fit
    //      in the ring's n points is the vertex that bends the outline least. The remaining ring points spread
    //      evenly over the gaps;
    //   4. each node samples its own outline at those parameters, snapping onto its own vertex where it has one in
    //      that cluster, so every kept vertex is exact.
    // Everything is in the unit box (the morph's space: a ring is the lerp of two unit outlines, then scaled).
    internal static class SectionAlignment
    {
        private const int Dense = 128;          // perimeter samples per outline for the matching
        private const float DirWeight = 0.1f;   // edge-direction term, against squared distance in the unit box
        private const float HardTurnDeg = 20f;  // a vertex turning this much is a corner, kept before any other
        private const float SoftTurnDeg = 0.5f; // below this a vertex is on a straight run and needn't be a ring point
        private const float SameCountSlack = 3f;      // vertex-to-vertex kept unless the warp costs a third as much

        // One node's outline: a closed CCW polygon in the unit box, and which of its vertices are real ones (a
        // custom's vertices; a built-in's samples are only its corners).
        public struct Shape
        {
            public Vector2[] poly;
            public bool samplesOnly;   // a built-in sampled densely: only its sharp corners count as vertices
        }

        public static Vector2[][] Align(IList<Shape> shapes, int n)
        {
            int k = shapes.Count;
            var outlines = new Outline[k];
            for (int i = 0; i < k; i++) outlines[i] = new Outline(shapes[i], i == 0);

            // 1. neighbour maps: maps[i] takes a fraction of outline i to the matching (unwrapped) fraction of i+1
            var maps = new float[Math.Max(0, k - 1)][];
            for (int i = 0; i + 1 < k; i++) maps[i] = Match(outlines[i], outlines[i + 1]);

            // 2. tracks: Dense of them, starting evenly around outline 0
            int T = Dense;
            var f = new float[k][];
            for (int i = 0; i < k; i++) f[i] = new float[T + 1];
            for (int m = 0; m <= T; m++)
            {
                f[0][m] = (float)m / T;
                for (int i = 0; i + 1 < k; i++) f[i + 1][m] = Eval(maps[i], f[i][m]);
            }
            var u = new float[T + 1];
            for (int m = 0; m <= T; m++)
            {
                float s = 0f;
                for (int i = 0; i < k; i++) s += f[i][m];
                u[m] = s / k;
            }

            // 3. every vertex on the track parameter, clustered
            var cands = new List<Cand>();
            for (int i = 0; i < k; i++)
            {
                Outline o = outlines[i];
                for (int v = 0; v < o.poly.Length; v++)
                {
                    float turn = o.turn[v];
                    if (turn < SoftTurnDeg) continue;
                    if (o.samplesOnly && turn < HardTurnDeg) continue;
                    float m = TrackAt(f[i], o.vFrac[v]);
                    cands.Add(new Cand { u = Frac(Lerp(u, m)), node = i, frac = o.vFrac[v], turn = turn, hard = turn >= HardTurnDeg });
                }
            }
            List<Cluster> clusters = ClusterCands(cands, 0.35f / n);
            List<Cluster> kept = clusters.OrderByDescending(c => c.hard ? 1e6f + c.weight : c.weight).Take(n).OrderBy(c => c.u).ToList();

            // targets on the track parameter: the kept clusters, and the rest spread over the gaps between them
            var targets = new List<Target>();
            foreach (Cluster c in kept) targets.Add(new Target { u = c.u, cluster = c });
            int fills = n - kept.Count;
            float u0 = Frac(u[0]);
            if (kept.Count == 0)
            {
                for (int j = 0; j < fills; j++) targets.Add(new Target { u = Frac(u0 + (float)j / n) });
            }
            else
            {
                int C = kept.Count;
                var gap = new float[C];
                for (int c = 0; c < C; c++) gap[c] = Frac(kept[(c + 1) % C].u - kept[c].u + 1f);
                if (C == 1) gap[0] = 1f;
                int[] share = Apportion(gap, fills);
                for (int c = 0; c < C; c++)
                    for (int j = 1; j <= share[c]; j++)
                        targets.Add(new Target { u = Frac(kept[c].u + gap[c] * j / (share[c] + 1)) });
            }
            // ring point 0 at outline 0's start (its rightmost point), the rest in order around
            targets.Sort((a, b) => Frac(a.u - u0 + 1e-6f).CompareTo(Frac(b.u - u0 + 1e-6f)));

            // 4. each node's ring: its outline at the targets' fractions, on its own vertex where it has one
            var rings = new Vector2[k][];
            for (int i = 0; i < k; i++)
            {
                Outline o = outlines[i];
                var fr = new float[n];
                float prev = float.NegativeInfinity;
                for (int j = 0; j < n; j++)
                {
                    float uj = Frac(targets[j].u - targets[0].u) + targets[0].u;   // unwrapped from the first target up
                    float m = TrackAtU(u, uj);
                    float x = Lerp(f[i], m);
                    if (targets[j].cluster != null && targets[j].cluster.frac.TryGetValue(i, out float own))
                        x = own + Mathf.Round(x - own);         // the same turn of the perimeter as the track
                    if (x <= prev) x = prev + 1e-4f;
                    fr[j] = prev = x;
                }
                var ring = new Vector2[n];
                for (int j = 0; j < n; j++) ring[j] = o.At(fr[j]);
                rings[i] = ring;
            }
            return rings;
        }

        // ---------------------------------------------------------------------------------------------------------

        private sealed class Outline
        {
            public readonly Vector2[] poly;
            public readonly bool samplesOnly;
            public readonly float[] cum;      // perimeter fraction at each vertex, cum[V] = 1
            public readonly float[] vFrac;    // = cum[v]
            public readonly float[] turn;     // degrees turned at each vertex
            public readonly Vector2[] pts, dirs;   // Dense samples by arc length and the edge direction there

            public Outline(Shape s, bool startRightmost)
            {
                Vector2[] p = Dedupe(s.poly);
                if (startRightmost)
                {
                    int start = 0;
                    for (int i = 1; i < p.Length; i++)
                    {
                        Vector2 a = p[i], b = p[start];
                        if (a.x > b.x + 1e-5f || (a.x >= b.x - 1e-5f && Mathf.Abs(a.y) < Mathf.Abs(b.y) - 1e-5f)) start = i;
                    }
                    p = p.Skip(start).Concat(p.Take(start)).ToArray();
                }
                poly = p;
                samplesOnly = s.samplesOnly;
                int V = p.Length;
                cum = new float[V + 1];
                for (int i = 0; i < V; i++) cum[i + 1] = cum[i] + (p[(i + 1) % V] - p[i]).magnitude;
                float per = Mathf.Max(cum[V], 1e-6f);
                for (int i = 0; i <= V; i++) cum[i] /= per;
                vFrac = cum.Take(V).ToArray();
                turn = new float[V];
                for (int i = 0; i < V; i++)
                {
                    Vector2 a = p[i] - p[(i + V - 1) % V], b = p[(i + 1) % V] - p[i];
                    turn[i] = (a.sqrMagnitude < 1e-12f || b.sqrMagnitude < 1e-12f) ? 0f : Vector2.Angle(a, b);
                }
                pts = new Vector2[Dense];
                dirs = new Vector2[Dense];
                for (int m = 0; m < Dense; m++)
                {
                    float x = (float)m / Dense;
                    pts[m] = At(x);
                    dirs[m] = DirAt(x);
                }
            }

            private int Edge(float x)
            {
                int V = poly.Length;
                int lo = 0, hi = V - 1;
                while (lo < hi)
                {
                    int mid = (lo + hi + 1) / 2;
                    if (cum[mid] <= x) lo = mid; else hi = mid - 1;
                }
                return lo;
            }

            public Vector2 At(float x)
            {
                x = Frac(x);
                int e = Edge(x), V = poly.Length;
                float len = cum[e + 1] - cum[e];
                float t = len > 1e-9f ? (x - cum[e]) / len : 0f;
                return Vector2.Lerp(poly[e], poly[(e + 1) % V], Mathf.Clamp01(t));
            }

            private Vector2 DirAt(float x)
            {
                int e = Edge(Frac(x)), V = poly.Length;
                Vector2 d = poly[(e + 1) % V] - poly[e];
                return d.sqrMagnitude > 1e-12f ? d.normalized : Vector2.right;
            }
        }

        private static Vector2[] Dedupe(Vector2[] p)
        {
            var o = new List<Vector2>(p.Length);
            foreach (Vector2 v in p)
                if (o.Count == 0 || (v - o[o.Count - 1]).sqrMagnitude > 1e-10f) o.Add(v);
            while (o.Count > 3 && (o[0] - o[o.Count - 1]).sqrMagnitude <= 1e-10f) o.RemoveAt(o.Count - 1);
            return o.ToArray();
        }

        // The map from outline a's fractions to b's: map[m] = b's unwrapped fraction matching a's m / Dense, m = 0..Dense
        // (map[Dense] = map[0] + 1). Strictly increasing.
        private static float[] Match(Outline a, Outline b)
        {
            int M = Dense;
            float Cost(int i, int j) =>
                (a.pts[i % M] - b.pts[j % M]).sqrMagnitude + DirWeight * (1f - Vector2.Dot(a.dirs[i % M], b.dirs[j % M]));

            // starting rotations: the few best rigid ones
            var rigid = new float[M];
            for (int o = 0; o < M; o++)
            {
                float s = 0f;
                for (int i = 0; i < M; i++) s += Cost(i, i + o);
                rigid[o] = s;
            }
            var starts = Enumerable.Range(0, M)
                .Where(o => rigid[o] <= rigid[(o + M - 1) % M] && rigid[o] <= rigid[(o + 1) % M])
                .OrderBy(o => rigid[o]).Take(3).ToList();

            float best = float.PositiveInfinity;
            float[] bestMap = null;
            int band = M / 3;
            foreach (int o in starts)
            {
                // D[i, j]: least cost pairing a[0..i] with b[o..o+j], (0,0) and (M,M) paired
                var D = new float[M + 1, M + 1];
                var step = new byte[M + 1, M + 1];
                for (int i = 0; i <= M; i++)
                    for (int j = 0; j <= M; j++)
                    {
                        D[i, j] = float.PositiveInfinity;
                        if (Math.Abs(i - j) > band) continue;
                        float c = Cost(i, o + j);
                        if (i == 0 && j == 0) { D[i, j] = c; continue; }
                        float dg = (i > 0 && j > 0) ? D[i - 1, j - 1] : float.PositiveInfinity;
                        float up = i > 0 ? D[i - 1, j] : float.PositiveInfinity;
                        float lf = j > 0 ? D[i, j - 1] : float.PositiveInfinity;
                        if (dg <= up && dg <= lf) { D[i, j] = c + dg; step[i, j] = 0; }
                        else if (up <= lf) { D[i, j] = c + up; step[i, j] = 1; }
                        else { D[i, j] = c + lf; step[i, j] = 2; }
                    }
                if (D[M, M] >= best) continue;
                best = D[M, M];
                // a's index i -> the mean of the b indices it pairs with
                var sum = new float[M + 1];
                var cnt = new int[M + 1];
                for (int i = M, j = M; ;)
                {
                    sum[i] += j; cnt[i]++;
                    if (i == 0 && j == 0) break;
                    byte s = step[i, j];
                    if (s == 0) { i--; j--; } else if (s == 1) i--; else j--;
                }
                var map = new float[M + 1];
                for (int i = 0; i <= M; i++) map[i] = (o + sum[i] / cnt[i]) / M;
                bestMap = map;
            }

            // same vertex count: vertex i -> vertex i + r for the best r, unless the warp is clearly better
            if (a.poly.Length == b.poly.Length && !a.samplesOnly && !b.samplesOnly)
            {
                int V = a.poly.Length;
                float bestV = float.PositiveInfinity;
                float[] vMap = null;
                for (int r = 0; r < V; r++)
                {
                    var map = new float[M + 1];
                    float b0 = b.vFrac[r];
                    for (int m = 0; m <= M; m++)
                    {
                        float x = (float)m / M;
                        int e = Math.Min(V - 1, Array.BinarySearch(a.cum, x) is int bs && bs >= 0 ? bs : ~bs - 1);
                        if (e < 0) e = 0;
                        float t = (x - a.cum[e]) / Mathf.Max(a.cum[e + 1] - a.cum[e], 1e-9f);
                        // b's vertex e + r to e + r + 1, unwrapped from b0
                        float s0 = B(b, e + r) - b0, s1 = B(b, e + r + 1) - b0;
                        map[m] = b0 + Mathf.Lerp(s0, s1, t);
                    }
                    float c = 0f;
                    for (int m = 0; m < M; m++)
                    {
                        float y = map[m];
                        c += (a.pts[m] - b.At(y)).sqrMagnitude + DirWeight * (1f - Vector2.Dot(a.dirs[m], DirOf(b, y)));
                    }
                    if (c < bestV) { bestV = c; vMap = map; }
                }
                if (vMap != null && bestV <= best * SameCountSlack) return vMap;   // already strictly increasing
            }

            // the warp strictly increasing (a long pairing of many to one would stack ring points): a little of the
            // straight map. Not the vertex map above: it would pull paired vertices off each other.
            float o0 = bestMap[0];
            for (int i = 0; i <= M; i++) bestMap[i] = 0.98f * bestMap[i] + 0.02f * (o0 + (float)i / M);
            return bestMap;
        }

        // b's vertex fraction for an index past V, unwrapped
        private static float B(Outline b, int idx)
        {
            int V = b.poly.Length;
            return b.vFrac[idx % V] + idx / V;
        }

        private static Vector2 DirOf(Outline b, float x)
        {
            Vector2 d = b.At(x + 1e-4f) - b.At(x - 1e-4f);
            return d.sqrMagnitude > 1e-14f ? d.normalized : Vector2.right;
        }

        // the map at an unwrapped fraction x
        private static float Eval(float[] map, float x)
        {
            float w = Mathf.Floor(x);
            float r = (x - w) * (map.Length - 1);
            int i = Mathf.Min((int)r, map.Length - 2);
            return Mathf.Lerp(map[i], map[i + 1], r - i) + w;
        }

        // the (real) track index where a node's tracks f reach fraction x, modulo whole turns
        private static float TrackAt(float[] f, float x)
        {
            int T = f.Length - 1;
            x = Frac(x - f[0]) + f[0];
            int lo = 0, hi = T;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (f[mid] <= x) lo = mid; else hi = mid;
            }
            float span = f[hi] - f[lo];
            return lo + (span > 1e-9f ? (x - f[lo]) / span : 0.5f);
        }

        // the (real) track index at track parameter v (unwrapped, from u[0])
        private static float TrackAtU(float[] u, float v)
        {
            int T = u.Length - 1;
            float w = Mathf.Floor((v - u[0]) / (u[T] - u[0]));
            v -= w * (u[T] - u[0]);
            int lo = 0, hi = T;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (u[mid] <= v) lo = mid; else hi = mid;
            }
            float span = u[hi] - u[lo];
            return w * T + lo + (span > 1e-9f ? (v - u[lo]) / span : 0.5f);
        }

        // linear interpolation into a periodic table (table[T] = table[0] + 1) at a real index, any number of turns
        private static float Lerp(float[] table, float m)
        {
            int T = table.Length - 1;
            float w = Mathf.Floor(m / T);
            float r = m - w * T;
            int i = Mathf.Min((int)r, T - 1);
            return Mathf.Lerp(table[i], table[i + 1], r - i) + w * (table[T] - table[0]);
        }

        private static float Frac(float x) => x - Mathf.Floor(x);

        private struct Cand { public float u, frac, turn; public int node; public bool hard; }

        private sealed class Cluster
        {
            public float u, weight;
            public bool hard;
            public readonly Dictionary<int, float> frac = new Dictionary<int, float>();   // node -> its vertex's fraction
            private readonly Dictionary<int, float> _turn = new Dictionary<int, float>();
            private float _su, _sw;

            public void Add(Cand c, float shift)
            {
                float w = Mathf.Max(c.turn, 1e-3f);
                _su += (c.u + shift) * w;
                _sw += w;
                u = Frac(_su / _sw);
                weight += c.turn;
                hard |= c.hard;
                if (!_turn.TryGetValue(c.node, out float t) || c.turn > t) { _turn[c.node] = c.turn; frac[c.node] = c.frac; }
            }
        }

        // vertices within tol of each other on the track parameter (around the seam too) are one cluster
        private static List<Cluster> ClusterCands(List<Cand> cands, float tol)
        {
            var sorted = cands.OrderBy(c => c.u).ToList();
            var groups = new List<List<Cand>>();
            foreach (Cand c in sorted)
            {
                if (groups.Count > 0 && c.u - groups[groups.Count - 1].Last().u <= tol) groups[groups.Count - 1].Add(c);
                else groups.Add(new List<Cand> { c });
            }
            if (groups.Count > 1 && groups[0][0].u + 1f - groups[groups.Count - 1].Last().u <= tol)
            {
                groups[0].InsertRange(0, groups[groups.Count - 1]);
                groups.RemoveAt(groups.Count - 1);
            }
            var clusters = new List<Cluster>();
            foreach (List<Cand> g in groups)
            {
                var cl = new Cluster();
                float first = g[0].u;
                foreach (Cand c in g) cl.Add(c, c.u < first - 0.5f ? 1f : 0f);
                clusters.Add(cl);
            }
            return clusters;
        }

        // n split over the gaps in proportion to their size (largest remainder)
        private static int[] Apportion(float[] gaps, int n)
        {
            float total = gaps.Sum();
            var share = new int[gaps.Length];
            if (n <= 0 || total <= 0f) return share;
            var rem = new float[gaps.Length];
            int given = 0;
            for (int i = 0; i < gaps.Length; i++)
            {
                float exact = n * gaps[i] / total;
                share[i] = (int)exact;
                rem[i] = exact - share[i];
                given += share[i];
            }
            foreach (int i in Enumerable.Range(0, gaps.Length).OrderByDescending(i => rem[i]).Take(n - given)) share[i]++;
            return share;
        }

        private struct Target { public float u; public Cluster cluster; }
    }
}
