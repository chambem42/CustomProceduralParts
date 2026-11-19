using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KSP.UI;
using KspTest;
using UnityEngine;
using static CustomProceduralParts.Showcase.B2Showcase;
using static CustomProceduralParts.Showcase.SpineBasicsShowcase;

namespace CustomProceduralParts.Showcase
{
    /// <summary>
    /// A showcase video of RP-1 tooling for a Spine tank (RP-1's ModuleToolingProcSpine; RO's procedural tank,
    /// RO-RFTank-Separate), not a check: the PAW's two tooling lines (the widest and the narrowest bounding
    /// cylinder at the part's length, each tooled or not); tooling it from the PAW and RP-1's purchase dialog; a reshape
    /// inside those sizes that stays tooled; a wider one that needs (cheaper) tooling again. RP-1 is reached by
    /// reflection, so this assembly loads without it. Frames as the other showcases'. Needs a career (RP-1's unlock
    /// credit). Run it (the RP-1 instance):
    /// KSPTEST_EXPLICIT=Showcase_Rp1Tooling dotnet test KspTests/CustomProceduralParts.RP1Tests.Run --filter FullyQualifiedName~Showcase_Rp1Tooling
    /// </summary>
    public static class Rp1ToolingShowcase
    {
        private const string Max = "toolingMax", Min = "toolingMin";   // ModuleToolingProcSpine's PAW lines for a Spine

        [UITest(Kind = "Showcase", Explicit = true, NewGame = "career", Isolated = true, Requires = "RP-1", StartsAt = StartState.Vab, Craft = "SpineTank", TimeoutSec = 3600)]
        public static IEnumerator Showcase_Rp1Tooling(TestContext ctx)
        {
            yield return Wait.Frames(30);
            Part part = EditorLogic.fetch.ship.parts.First(p => p.FindModuleImplementing<ProceduralShapeSpine>() != null);
            ProceduralShapeSpine s = part.FindModuleImplementing<ProceduralShapeSpine>();
            PartModule tool = part.Modules.Cast<PartModule>().FirstOrDefault(m => m.moduleName == "ModuleToolingProcSpine");
            Assert.NotNull(tool, "the tank has RP-1's proc tank tooling");
            var rp1 = new Rp1(tool);
            rp1.Fund();
            // RP-1's Integration Info window, beside the parts list: its Tooling Cost line is RP-1's own word on whether
            // everything is tooled (gone once it is)
            rp1.ShowIntegrationInfo(new Vector2(310f, Screen.height - 330f));
            ctx.Record("mode", HighLogic.CurrentGame.Mode.ToString());
            ctx.Record("tooling type", rp1.ToolingType);
            ctx.Record("untooled", $"{rp1.Info()} costs {rp1.Cost():N0}, tooled {rp1.Tooled()}");
            Assert.False(rp1.Tooled(), "the tank starts untooled (a game of its own)");

            part.SetHighlightType(Part.HighlightType.Disabled);
            part.SetHighlight(false, false);
            SpineSettings.Set(SpineHandleVis.None, false, true);   // outlines on, no handles: the PAW does the editing here
            Textures.HoldNow();
            Pointer(false);
            yield return Wait.Frames(20);

            string framesDir = Environment.GetEnvironmentVariable("CPP_SHOWCASE_DIR") is string sd && sd.Length > 0
                ? Path.Combine(sd, "rp1-" + DateTime.Now.ToString("yyyyMMdd-HHmmss")) : Path.Combine(ctx.ResultsDir, "frames");
            ctx.Record("framesDir", framesDir);
            var rec = FrameRecorder.Begin(framesDir);
            try
            {
                Vector3 home = part.transform.position;
                float d = 15f;
                Cam(home, 120, 15, d, 0f);

                yield return FrameRecorder.Card("RP-1 Tooling", 0f, 1f, 18, f => Cam(home, 120 + 4 * f, 15, d, 0f));
                rec.Caption("", "");
                yield return FrameRecorder.Card("RP-1 Tooling", 1f, 1f, 60);
                yield return FrameRecorder.Card("RP-1 Tooling", 1f, 0f, 18);

                rec.Caption("A Spine tank in an RP-1 career", "RP-1 charges tooling for each new tank size");
                yield return Frames(90, f => Cam(home, 124 + 30 * f, 15, d, 0f));

                // ---- the two cylinders, in the PAW --------------------------------------------------------------------
                rec.HideUi = false;
                yield return Paw.Open(part);
                yield return Frames(4, null);
                MovePaw(part, new Vector2(Screen.width - 440f, 40f));
                Collapse(part);
                Pointer(false);
                yield return CamMove(home, 160, 15, d, -0.08f, 25);
                FrameRecorder.CursorPos = new Vector2(0.5f * Screen.width, 0.6f * Screen.height);
                rec.Caption("Tooled as two cylinders", "Its widest and narrowest section at the tank's length; RP-1's Integration Info shows the cost");
                yield return OnLine(part, Max, 50);
                yield return OnLine(part, Min, 50);

                rec.Caption("Tool it from the part menu", "One purchase covers both cylinders");
                yield return BuyTooling(part, tool, rp1);
                Assert.True(rp1.Tooled(), "tooled after the purchase");
                rec.Caption("Tooled", "Both cylinders read tooled; RP-1's button says so and its Integration Info has no tooling cost left");
                yield return OnLine(part, Max, 30);
                yield return OnLine(part, Min, 30);
                yield return OnButton(part, tool, 40);

                // ---- inside the tooled sizes ---------------------------------------------------------------------------
                rec.Caption("Reshape it inside those sizes", "It stays tooled: any section, slope or height that fits both cylinders");
                yield return PawFloat(part, s, "selectedNode", s.SelIndex, 1f);
                yield return Frames(10, null);
                yield return PawChoose(part, "nodeProfileOpt", "Mk3", () => SetField(s, "nodeProfileOpt", "Mk3"));
                yield return Frames(15, null);
                yield return PawFloat(part, s, "nodeHeight", s.nodes[1].sizeV, 2.5f);
                yield return Frames(10, null);
                yield return PawChoose(part, "nodeSlopeOpt", "Concave", () => SetField(s, "nodeSlopeOpt", "Concave"));
                yield return Frames(15, null);
                ctx.Record("reshaped", $"{rp1.Info()}, tooled {rp1.Tooled()}");
                Assert.True(rp1.Tooled(), "still tooled inside the tooled sizes");
                yield return OnLine(part, Max, 30);
                yield return OnLine(part, Min, 30);

                // ---- wider: tool again ---------------------------------------------------------------------------------
                rec.Caption("Wider than it was tooled for", "The widest needs new tooling (RP-1 shows a tooling cost again); the narrowest is paid for");
                yield return PawFloat(part, s, "nodeWidth", s.nodes[1].sizeH, 3.5f);
                yield return Frames(10, null);
                yield return OnLine(part, Max, 45);
                yield return OnLine(part, Min, 35);
                float rest = rp1.Cost();
                ctx.Record("wider", $"{rp1.Info()}, costs {rest:N0}, tooled {rp1.Tooled()}");
                Assert.False(rp1.Tooled(), "untooled once wider than the tooling");
                rec.Caption("Tool the new size", "Only the new cylinder is charged");
                yield return BuyTooling(part, tool, rp1);
                Assert.True(rp1.Tooled(), "tooled again");
                yield return OnLine(part, Max, 30);
                yield return OnButton(part, tool, 30);

                // ---- outro ---------------------------------------------------------------------------------------------
                FrameRecorder.CursorVisible = false;
                Paw.Close(part);
                rec.HideUi = true;
                rec.Caption("RP-1 tooling for Spines", "Built into RP-1: no setup, just a Spine part in a career");
                yield return Frames(150, f => Cam(home, 160 + 70 * f, Mathf.Lerp(15, 8, f), d, Mathf.Lerp(-0.08f, 0f, f)));
            }
            finally
            {
                ctx.Record("frames", FrameRecorder.End());
                Pointer(true);
                Textures.Release();
            }
        }

        // The cursor resting on one of the PAW's tooling lines (scrolled into view), n frames.
        private static IEnumerator OnLine(Part part, string field, int n)
        {
            Collapse(part);
            if (PawItemRect(part, field) is Rect)
            {
                // at the line's right end, clear of its text
                yield return MoveCursor(() => PawItemRect(part, field) is Rect now ? new Vector2(now.xMax - 12f, now.center.y) : FrameRecorder.CursorPos, 18);
                yield return Frames(n, null);
            }
            else Debug.LogWarning($"[Rp1ToolingShowcase] no PAW line {field}");
        }

        // RealFuels' two PAW groups (Real Fuels, RF Cryogenics) folded, so the tooling lines below them stay on screen.
        // UIPartActionGroup.Collapse by reflection (Assembly-CSharp isn't publicized here); KSPCommunityFixes keeps a
        // group folded when the PAW rebuilds, and this runs again before each look at the PAW anyway.
        private static readonly string[] Folded = { "Real Fuels", "RF Cryogenics" };

        private static void Collapse(Part part)
        {
            UIPartActionWindow w = Paw.Window(part);
            if (w == null) return;
            const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            foreach (UIPartActionGroup g in w.GetComponentsInChildren<UIPartActionGroup>(true))
            {
                Type t = g.GetType();
                string name = t.GetField("groupDisplayName", F)?.GetValue(g) as string;
                if (name == null || !Folded.Contains(name)) continue;
                if (t.GetField("isContentCollapsed", F)?.GetValue(g) is bool folded && folded) continue;
                t.GetMethod("Collapse", F, null, Type.EmptyTypes, null)?.Invoke(g, null);
            }
        }

        // The cursor resting on RP-1's own tooling button (Tooled, or Tool ...), n frames.
        private static IEnumerator OnButton(Part part, PartModule tool, int n)
        {
            Collapse(part);
            UIPartActionButton b = Paw.FindButtonItem(part, tool.Events["ToolingEvent"].guiName);
            if (b == null) { Debug.LogWarning("[Rp1ToolingShowcase] no tooling button"); yield break; }
            ScrollIntoView(part, b);
            yield return Frames(2, null);
            b = Paw.FindButtonItem(part, tool.Events["ToolingEvent"].guiName);
            if (b == null) yield break;
            yield return MoveCursor(() => Center((RectTransform)b.transform) + new Vector2(60f, 0f), 18);
            yield return Frames(n, null);
        }

        // The PAW's tooling button clicked, RP-1's purchase dialog read, and its Purchase Tooling button clicked.
        private static IEnumerator BuyTooling(Part part, PartModule tool, Rp1 rp1)
        {
            string label = tool.Events["ToolingEvent"].guiName;
            yield return ClickPawButton(part, label, () => tool.Events["ToolingEvent"].Invoke());
            PopupDialog dlg = null;
            for (int t = 0; t < 120 && dlg == null; t++)
            {
                dlg = UnityEngine.Object.FindObjectsOfType<PopupDialog>().FirstOrDefault(p => p.dialogToDisplay?.name == "ConfirmToolingPurchase");
                if (dlg == null) yield return null;
            }
            if (dlg == null)
            {
                Debug.LogWarning("[Rp1ToolingShowcase] no tooling dialog; tooled directly");
                rp1.Purchase();
                yield break;
            }
            yield return Frames(50, null);   // time to read it
            DialogGUIButton buy = Buttons(dlg.dialogToDisplay.Options).FirstOrDefault(b => b.OptionText == "Purchase Tooling");
            var ui = buy?.uiItem != null ? buy.uiItem.GetComponent<UnityEngine.UI.Button>() : null;
            if (ui == null)
            {
                Debug.LogWarning("[Rp1ToolingShowcase] no Purchase Tooling button (can't afford?); tooled directly");
                dlg.Dismiss();
                rp1.Purchase();
                yield break;
            }
            yield return MoveCursor(() => UiCenter((RectTransform)ui.transform), 24);
            FrameRecorder.CursorPressed = true;
            yield return Frames(5, null);
            ui.onClick.Invoke();
            FrameRecorder.CursorPressed = false;
            yield return Frames(10, null);
        }

        private static IEnumerable<DialogGUIButton> Buttons(IEnumerable<DialogGUIBase> items)
        {
            foreach (DialogGUIBase i in items ?? Enumerable.Empty<DialogGUIBase>())
            {
                if (i is DialogGUIButton b) yield return b;
                foreach (DialogGUIButton c in Buttons(i.children)) yield return c;
            }
        }

        // A uGUI element's centre in GUI coordinates (y down), on whatever canvas it's on.
        private static Vector2 UiCenter(RectTransform rt)
        {
            Canvas canvas = rt.GetComponentInParent<Canvas>();
            Camera cam = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            Vector2 p = RectTransformUtility.WorldToScreenPoint(cam, 0.5f * (c[0] + c[2]));
            return new Vector2(p.x, Screen.height - p.y);
        }

        /// <summary>RP-1's tooling for the part, by reflection (RP0.dll isn't referenced).</summary>
        internal sealed class Rp1
        {
            private readonly PartModule _tool;

            public Rp1(PartModule tool) => _tool = tool;

            private object Call(string name) => _tool.GetType().GetMethod(name, Type.EmptyTypes).Invoke(_tool, null);
            public string ToolingType => (string)_tool.GetType().GetProperty("ToolingType").GetValue(_tool);
            public bool Tooled() => (bool)Call("IsUnlocked");
            public float Cost() => (float)Call("GetToolingCost");
            public string Info() => (string)Call("GetToolingParameterInfo");
            public void Purchase() => Call("PurchaseTooling");

            // RP-1's Integration Info window open (KCT_GUI.GUIStates.ShowEditorGUI) with its top left at a GUI point
            // (KCT_GUI.EditorWindowPosition, a static Rect)
            public void ShowIntegrationInfo(Vector2 topLeft)
            {
                const System.Reflection.BindingFlags S = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static;
                Type gui = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("RP0.KCT_GUI", false)).FirstOrDefault(t => t != null);
                object states = gui?.GetField("GUIStates", S)?.GetValue(null);
                System.Reflection.FieldInfo show = states?.GetType().GetField("ShowEditorGUI"), pos = gui?.GetField("EditorWindowPosition", S);
                if (show == null || pos == null) { Debug.LogWarning("[Rp1ToolingShowcase] no KCT_GUI Integration Info window"); return; }
                show.SetValue(states, true);
                var r = (Rect)pos.GetValue(null);
                pos.SetValue(null, new Rect(topLeft.x, topLeft.y, r.width, r.height));
            }

            // enough funds that the dialog offers the purchase
            public void Fund()
            {
                if (Funding.Instance != null && Funding.Instance.Funds < 1e7) Funding.Instance.AddFunds(1e7 - Funding.Instance.Funds, TransactionReasons.Cheating);
            }
        }
    }
}
