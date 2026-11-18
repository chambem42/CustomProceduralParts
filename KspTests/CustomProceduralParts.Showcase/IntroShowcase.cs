using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KspTest;
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
            SpineSettings.Set(SpineHandleVis.None, false, false);   // no handles, no outlines: the crafts as they fly
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
                rec.Caption("Cessna 172 floatplane", "Its fuselage and floats are Spine parts");
                yield return FrameRecorder.Card("Custom Procedural Parts", 1f, 0f, 20, f => Orbit(all.center, 200 + 6 * f, 18, 1.15f * size, 30));
                yield return Frames(130, f => Orbit(all.center, 206 + 60 * f, 18 - 6 * f, 1.15f * size, 30));

                // ---- the NACA 11-A hull ------------------------------------------------------------------------------
                yield return Next(ctx, "ShowcaseNaca11A", false, b => all = b);
                ctx.Record("naca", all.ToString());
                size = all.size.magnitude;
                rec.Caption("NACA 11-A flying-boat hull", "One Spine part, its sections from NACA's published hull offsets");
                // long and thin (2 m beam, 11 m): farther out and higher up than the planes
                Orbit(all.center, 215, 28, 1.6f * size, 30);
                yield return FrameRecorder.Card("", 1f, 0f, 12);
                yield return Frames(130, f => Orbit(all.center, 215 + 60 * f, 28 - 12 * Mathf.Sin(Mathf.PI * f), 1.6f * size, 30));

                // ---- the B-2 -------------------------------------------------------------------------------------------
                yield return Next(ctx, "B2Showcase", true, b => all = b);
                ctx.Record("b2", all.ToString());
                size = all.size.magnitude;
                rec.Caption("B-2", "Its center body is one Spine part of custom sections");
                // nearer, and round only so far: farther out the orbit runs into the hangar's frame
                Orbit(all.center, 200, 26, 1.05f * size, 30);
                yield return FrameRecorder.Card("", 1f, 0f, 12);
                yield return Frames(140, f => Orbit(all.center, 200 + 40 * f, 26 - 4 * f, 1.05f * size, 30));
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
