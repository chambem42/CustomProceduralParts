using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KspTest;
using ProceduralParts;
using UnityEngine;
using static CustomProceduralParts.Showcase.B2Showcase;

namespace CustomProceduralParts.Showcase
{
    /// <summary>
    /// The demo's opening, not a check: a "Custom Procedural Parts" title, then a brief look at three crafts built with
    /// Spines, each once around: the Cessna 172 floatplane (its fuselage and floats; showcase/ShowcaseCessna.craft is FAR's
    /// water-test craft without its test engine), the NACA 11-A flying-boat hull and the B-2 (its center body). Frames as
    /// the other showcases' (intro-*/NNNNN.jpg under CPP_SHOWCASE_DIR); the basics take follows it in the video.
    /// Run it: dotnet test KspTests/CustomProceduralParts.Showcase.Run --filter FullyQualifiedName~Showcase_Intro
    /// </summary>
    public static class IntroShowcase
    {
        [UITest(Kind = "Showcase", Explicit = true, Mods = "B9-PWings-Fork;KerbalFoundriesContinued;TexturesUnlimited",
                StartsAt = StartState.Sph, Craft = "ShowcaseCessna", TimeoutSec = 3600)]
        public static IEnumerator Showcase_Intro(TestContext ctx)
        {
            yield return Wait.Frames(30);
            SpineSettings.Set(SpineHandleVis.None, false, true);   // no handles; outlines for the part whose PAW is open
            Imgui.EnsurePatched();
            Textures.HoldNow();
            Pointer(false);
            B9PanelOff();
            Bounds all = Ready(false);
            ctx.Record("cessna", all.ToString());
            yield return Wait.Frames(20);

            string framesDir = Environment.GetEnvironmentVariable("CPP_SHOWCASE_DIR") is string sd && sd.Length > 0
                ? Path.Combine(sd, "intro-" + DateTime.Now.ToString("yyyyMMdd-HHmmss")) : Path.Combine(ctx.ResultsDir, "frames");
            ctx.Record("framesDir", framesDir);
            var rec = FrameRecorder.Begin(framesDir);
            try
            {
                float size = all.size.magnitude;
                // ---- the title, over the first craft ------------------------------------------------------------------
                Orbit(all.center, 200, 18, 1.15f * size, 30);
                yield return FrameRecorder.Card("Custom Procedural Parts", 1f, 1f, 75);
                rec.Caption("Cessna 172 floatplane", "Its fuselage and floats are Spine parts, here with their cross-sections");
                yield return FrameRecorder.Card("Custom Procedural Parts", 1f, 0f, 20, f => Orbit(all.center, 200 + 6 * f, 18, 1.15f * size, 30));
                yield return Tour(30, 50, 20, f => Orbit(all.center, 206 + 70 * f, 18 - 6 * f, 1.15f * size, 30));

                // ---- the NACA 11-A hull ------------------------------------------------------------------------------
                yield return Next(ctx, "ShowcaseNaca11A", false, b => all = b);
                ctx.Record("naca", all.ToString());
                size = all.size.magnitude;
                rec.Caption("NACA 11-A flying-boat hull", "One Spine part, its cross-sections from NACA's published hull offsets");
                // long and thin (2 m beam, 11 m): farther out and higher up than the planes
                Orbit(all.center, 215, 28, 1.6f * size, 30);
                yield return FrameRecorder.Card("", 1f, 0f, 12);
                yield return Tour(30, 110, 0, f => Orbit(all.center, 215 + 60 * f, 28 - 12 * Mathf.Sin(Mathf.PI * f), 1.6f * size, 30));

                // ---- the B-2 -------------------------------------------------------------------------------------------
                yield return Next(ctx, "B2Showcase", true, b => all = b);
                ctx.Record("b2", all.ToString());
                size = all.size.magnitude;
                rec.Caption("B-2", "Its center body is one Spine part of custom cross-sections");
                // nearer, and round only so far: farther out the orbit runs into the hangar's frame
                Orbit(all.center, 200, 26, 1.05f * size, 30);
                yield return FrameRecorder.Card("", 1f, 0f, 12);
                yield return Tour(30, 110, 0, f => Orbit(all.center, 200 + 40 * f, 26 - 4 * f, 1.05f * size, 30));
                rec.Caption("", "");
                yield return FrameRecorder.Card("", 0f, 1f, 15, f => Orbit(all.center, 240 + 2 * f, 22, 1.05f * size, 30));
            }
            finally
            {
                ctx.Record("frames", FrameRecorder.End());
                Pointer(true);
                Textures.Release();
            }
        }

        // The craft's Spine parts (Spine their shape) big enough to show their cross-sections (a strut's outlines are
        // specks), the biggest first.
        private static List<Part> SpineParts()
        {
            var spines = EditorLogic.fetch.ship.parts.Where(p => p.FindModuleImplementing<ProceduralPart>()?.CurrentShape is ProceduralShapeSpine)
                .Select(p => (p, size: Bounds(new[] { p }).size.magnitude)).OrderByDescending(x => x.size).ToList();
            float biggest = spines.Count > 0 ? spines[0].size : 0f;
            Log("Spine part sizes: " + string.Join(", ", spines.Select(x => $"{x.p.partInfo.name} {x.size:F1}")));
            return spines.Where(x => x.size >= 0.6f * biggest).Select(x => x.p).ToList();
        }

        // The camera on cam(0..1) for lead frames, then each Spine part's PAW open in turn for `each` frames (the PAW
        // hidden with the rest of the UI; an open PAW is what draws a Spine's cross-section outlines), then tail frames
        // on the last; its PAW closed after. The PAWs are opened in the frame callback so the camera never stalls.
        private static IEnumerator Tour(int lead, int each, int tail, Action<float> cam)
        {
            List<Part> spines = SpineParts();
            Log($"touring {spines.Count} Spine parts: {string.Join(", ", spines.Select(p => p.partInfo.name))}");
            int n = lead + spines.Count * each + tail, frame = 0;
            Part shown = null;
            yield return Frames(n, f =>
            {
                cam(f);
                int i = frame < lead ? -1 : Mathf.Min(spines.Count - 1, (frame - lead) / Mathf.Max(1, each));
                frame++;
                Part want = i >= 0 && spines.Count > 0 ? spines[i] : null;
                if (want == shown) return;
                if (shown != null) Paw.Close(shown);
                if (want != null) UIPartActionController.Instance.SpawnPartActionWindow(want);
                shown = want;
            });
            if (shown != null) Paw.Close(shown);
        }

        private static void Log(string s) => Debug.Log("[IntroShowcase] " + s);

        // A fade to dark, the next craft loaded under it (no frames saved while it loads), framed.
        private static IEnumerator Next(TestContext ctx, string craft, bool paint, Action<Bounds> framed)
        {
            yield return FrameRecorder.Card("", 0f, 1f, 12);
            FrameRecorder.Paused = true;
            EditorLogic.LoadShipFromFile(ctx.CraftPath(craft, "SPH"));
            yield return Wait.Frames(60);
            framed(Ready(paint));
            yield return Wait.Frames(20);
            FrameRecorder.Paused = false;
        }

        // The craft in the editor, no part highlighted (and painted like the B-2 take, if asked); its bounds.
        private static Bounds Ready(bool paint)
        {
            List<Part> parts = EditorLogic.fetch.ship.parts.ToList();
            foreach (Part p in parts)
            {
                p.SetHighlightType(Part.HighlightType.Disabled);
                p.SetHighlight(false, false);
                if (paint) PaintPart(p);
            }
            B9PanelOff();
            return Bounds(parts);
        }
    }
}
