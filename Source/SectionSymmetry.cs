using System.Collections.Generic;
using UnityEngine;

namespace CustomProceduralParts
{
    // Mirror editing for the section dialog. With left-right mirroring the left half is the master, with
    // top-bottom the top half, with both the top-left quadrant; the rest of the polygon is its mirror image.
    // The master is kept as a chain of points from one mirror line to the other, in the polygon's CCW order:
    //   left-right: top (on x = 0) .. down the left .. bottom (on x = 0)
    //   top-bottom: right (on y = 0) .. over the top .. left (on y = 0)
    //   both:       top (on x = 0) .. left (on y = 0)
    // Full() builds the polygon from it, with each point's master index and mirror flags.
    public static class SectionSymmetry
    {
        public const int FlipX = 1, FlipY = 2;
        private const float Eps = 1e-5f;

        public static Vector2 Flip(Vector2 p, int flags) =>
            new Vector2((flags & FlipX) != 0 ? -p.x : p.x, (flags & FlipY) != 0 ? -p.y : p.y);

        // Copies of the chain in the full polygon: 2 with one mirror, 4 with both.
        public static int Copies(bool lr, bool tb) => (lr ? 2 : 1) * (tb ? 2 : 1);

        // Most chain points (ends included) whose full polygon fits `cap` vertices: 2n or 4n points for n+1.
        public static int MaxChain(bool lr, bool tb, int cap) => cap / Copies(lr, tb) + 1;

        // Fewest chain points (ends included) for a polygon of 3 or more vertices.
        public static int MinChain(bool lr, bool tb) => lr && tb ? 2 : 3;

        public static List<Vector2> Full(IList<Vector2> c, bool lr, bool tb, List<int> master, List<int> flags)
        {
            var o = new List<Vector2>();
            master?.Clear();
            flags?.Clear();
            int n = c.Count - 1;
            void Add(int m, int f)
            {
                o.Add(Flip(c[m], f));
                master?.Add(m);
                flags?.Add(f);
            }
            for (int k = 0; k <= n; k++) Add(k, 0);
            if (lr && tb)
            {
                for (int j = n - 1; j >= 0; j--) Add(j, FlipY);            // bottom-left, to the bottom middle
                for (int j = 1; j <= n; j++) Add(j, FlipX | FlipY);       // bottom-right, to the right middle
                for (int j = n - 1; j >= 1; j--) Add(j, FlipX);            // top-right, back to the top middle
            }
            else
            {
                int f = lr ? FlipX : FlipY;
                for (int j = n - 1; j >= 1; j--) Add(j, f);
            }
            return o;
        }

        // Keeps chain point m of n+1 in the master region, and an end on its mirror line.
        public static Vector2 Constrain(Vector2 p, int m, int n, bool lr, bool tb)
        {
            if (lr) p.x = Mathf.Min(p.x, 0f);
            if (tb) p.y = Mathf.Max(p.y, 0f);
            bool first = m == 0, last = m == n;
            if (lr && tb)
            {
                if (first) p.x = 0f;
                if (last) p.y = 0f;
            }
            else if (first || last)
            {
                if (lr) p.x = 0f;
                else p.y = 0f;
            }
            return p;
        }

        // The master chain of polygon `poly` (the part in the master region, cut at the mirror lines), at
        // most maxPts points, or null if the polygon doesn't reach across the mirror line(s) there.
        public static List<Vector2> Chain(IList<Vector2> poly, bool lr, bool tb, int maxPts)
        {
            var p = new List<Vector2>(poly);
            if (SectionGeometry.SignedArea(p) < 0f) p.Reverse();
            // the master region first; failing that a mirror image of the polygon that reaches into it
            foreach (int f in new[] { 0, FlipX, FlipY, FlipX | FlipY })
            {
                if ((f & FlipX) != 0 && !lr || (f & FlipY) != 0 && !tb) continue;
                var q = new List<Vector2>(p.Count);
                foreach (Vector2 v in p) q.Add(Flip(v, f));
                if (f == FlipX || f == FlipY) q.Reverse();   // one mirror turns it clockwise
                List<Vector2> c = ChainOf(q, lr, tb);
                if (c != null) return Simplify(c, Mathf.Max(maxPts, MinChain(lr, tb)));
            }
            return null;
        }

        private static List<Vector2> ChainOf(List<Vector2> p, bool lr, bool tb)
        {
            if (lr) p = Clip(p, v => -v.x, (a, b) => Cut(a, b, a.x / (a.x - b.x)), true);
            if (tb) p = Clip(p, v => v.y, (a, b) => Cut(a, b, a.y / (a.y - b.y)), false);
            // drop repeats (a vertex on a mirror line comes out twice)
            var q = new List<Vector2>();
            foreach (Vector2 v in p)
                if (q.Count == 0 || (v - q[q.Count - 1]).sqrMagnitude > Eps * Eps) q.Add(v);
            while (q.Count > 1 && (q[0] - q[q.Count - 1]).sqrMagnitude <= Eps * Eps) q.RemoveAt(q.Count - 1);
            int n = q.Count;
            if (n < 3) return null;
            // edges along a mirror line are the cut; the longest run of the rest is the chain
            bool OnX(Vector2 v) => lr && Mathf.Abs(v.x) <= Eps;
            bool OnY(Vector2 v) => tb && Mathf.Abs(v.y) <= Eps;
            bool CutEdge(int i)
            {
                Vector2 a = q[i], b = q[(i + 1) % n];
                return OnX(a) && OnX(b) || OnY(a) && OnY(b);
            }
            int start = -1;
            for (int i = 0; i < n; i++) if (CutEdge(i)) { start = i; break; }
            if (start < 0) return null;   // the polygon stays clear of the mirror line
            List<Vector2> best = null;
            float bestLen = 0f;
            List<Vector2> run = null;
            float len = 0f;
            for (int k = 1; k <= n; k++)
            {
                int i = (start + k) % n;
                if (CutEdge(i))
                {
                    if (run != null && len > bestLen) { best = run; bestLen = len; }
                    run = null;
                    continue;
                }
                if (run == null) { run = new List<Vector2> { q[i] }; len = 0f; }
                Vector2 b = q[(i + 1) % n];
                len += (b - run[run.Count - 1]).magnitude;
                run.Add(b);
            }
            if (run != null && len > bestLen) best = run;
            if (best == null || best.Count < 2) return null;
            // its ends on the right mirror lines (see the class comment)
            Vector2 s = best[0], e = best[best.Count - 1];
            bool ok = lr && tb ? OnX(s) && OnY(e) : lr ? OnX(s) && OnX(e) : OnY(s) && OnY(e);
            if (!ok) return null;
            int last = best.Count - 1;
            for (int i = 0; i <= last; i++) best[i] = Constrain(best[i], i, last, lr, tb);
            return best;
        }

        private static Vector2 Cut(Vector2 a, Vector2 b, float t) => a + t * (b - a);

        // Sutherland-Hodgman against one half-plane (inside: side(v) >= 0), the cut point put exactly on the line.
        private static List<Vector2> Clip(List<Vector2> p, System.Func<Vector2, float> side,
                                          System.Func<Vector2, Vector2, Vector2> cut, bool onX)
        {
            var o = new List<Vector2>();
            for (int i = 0; i < p.Count; i++)
            {
                Vector2 a = p[i], b = p[(i + 1) % p.Count];
                bool ina = side(a) >= 0f, inb = side(b) >= 0f;
                if (ina) o.Add(a);
                if (ina != inb)
                {
                    Vector2 c = cut(a, b);
                    if (onX) c.x = 0f; else c.y = 0f;
                    o.Add(c);
                }
            }
            return o;
        }

        // Drops the interior points that change the outline least (smallest triangle with their neighbours)
        // until at most maxPts remain; the ends stay.
        private static List<Vector2> Simplify(List<Vector2> c, int maxPts)
        {
            while (c.Count > maxPts && c.Count > 2)
            {
                int drop = -1;
                float least = float.MaxValue;
                for (int i = 1; i < c.Count - 1; i++)
                {
                    Vector2 a = c[i - 1], b = c[i], d = c[i + 1];
                    float area = Mathf.Abs((b.x - a.x) * (d.y - a.y) - (d.x - a.x) * (b.y - a.y));
                    if (area < least) { least = area; drop = i; }
                }
                if (drop < 0) break;
                c.RemoveAt(drop);
            }
            return c;
        }
    }
}
