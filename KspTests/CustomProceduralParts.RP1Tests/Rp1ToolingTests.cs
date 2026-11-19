using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using KspTest;
using ProceduralParts;
using RP0;

namespace CustomProceduralParts.RP1Tests
{
    /// <summary>
    /// RP-1's tooling of Spine parts (ModuleToolingPTank, from the Spine's MinBoundingDiameter) in a game with RP-1
    /// from CKAN, its RP0.dll one with Spine tooling (see the Run project). SpineTest is one proceduralStructural part (RP-1
    /// tools it as Stringers) with a Spine whose section varies: 1.5 m round, 2.2 x 1.2, 1.8 x 1.4, 0.6 m round.
    /// </summary>
    public static class Rp1ToolingTests
    {
        // The tests share one craft: each puts back what it changes.
        private static Part Body() => EditorLogic.fetch.ship.parts.First(p => p.FindModuleImplementing<ProceduralShapeSpine>() != null);

        private static List<SpineNode> Snapshot(ProceduralShapeSpine s) => s.nodes.Select(n => n.Clone()).ToList();

        // ModuleToolingPTank's two PAW lines for a Spine and their refresh: by name, so the tests build against an RP0.dll
        // from before RP-1 tooled Spines (the RP-1 they run with is the Run project's Rp1Source)
        private static bool ToolsSpines => typeof(ModuleToolingPTank).GetMethod("UpdatePawLines") != null;
        private static string Max(ModuleToolingPTank t) => t.Fields["toolingMax"]?.GetValue<string>(t) ?? "";
        private static string Min(ModuleToolingPTank t) => t.Fields["toolingMin"]?.GetValue<string>(t) ?? "";
        private static void Refresh(ModuleToolingPTank t) => t.GetType().GetMethod("UpdatePawLines")?.Invoke(t, null);

        private static void Restore(ProceduralShapeSpine s, List<SpineNode> saved)
        {
            s.nodes.Clear();
            s.nodes.AddRange(saved);
            s.UpdateShape(true);
        }

        // Picks the part's shape as the PAW's shape selector does.
        private static IEnumerator SetShape(Part part, string shape)
        {
            var pp = part.FindModuleImplementing<ProceduralPart>();
            BaseField f = pp.Fields["shapeName"];
            object old = f.GetValue(pp);
            f.SetValue(shape, pp);
            f.uiControlEditor.onFieldChanged?.Invoke(f, old);
            yield return Wait.Frames(5);
            Assert.True((string)f.GetValue(pp) == shape, $"the part is a {shape}");
        }

        [UITest(Kind = "Check", Requires = "RP-1", StartsAt = StartState.Vab, Craft = "SpineTest", TimeoutSec = 1800)]
        public static IEnumerator Rp1_SpinePartsGetSpineTooling(TestContext ctx)
        {
            yield return Wait.Frames(10);
            // this RP-1 tools a Spine itself
            Assert.True(ToolsSpines, "RP-1's ModuleToolingPTank tools Spines");
            var spineParts = PartLoader.LoadedPartsList.Select(a => a.partPrefab)
                .Where(p => p != null && p.FindModuleImplementing<ProceduralShapeSpine>() != null).ToList();
            var tooled = spineParts.Where(p => p.FindModuleImplementing<ModuleToolingPTank>() != null).ToList();
            ctx.Record("spineParts", spineParts.Count);
            ctx.Record("tooled", string.Join(" ", tooled.Select(p => $"{p.partInfo.name}:{p.FindModuleImplementing<ModuleToolingPTank>().GetType().Name}")));
            // RP-1 tools these as Spines: structural (Stringers) and a procedural fuel tank
            void Has(string name)
            {
                Part prefab = PartLoader.getPartInfoByName(name)?.partPrefab;
                Assert.NotNull(prefab, $"{name} is loaded");
                Assert.NotNull(prefab.FindModuleImplementing<ProceduralShapeSpine>(), $"{name} can be a Spine");
                // the module saved craft have, not a subclass swapped in for it
                Assert.True(prefab.FindModuleImplementing<ModuleToolingPTank>()?.GetType() == typeof(ModuleToolingPTank), $"{name}'s tooling is ModuleToolingPTank");
            }
            Has("proceduralStructural");
            Has("RO-RFTank-Separate");
            // and RP-1 takes the Spine off procedural avionics (one diameter x length tooling)
            Part avionics = PartLoader.getPartInfoByName("proceduralAvionics")?.partPrefab;
            Assert.NotNull(avionics, "proceduralAvionics is loaded");
            Assert.True(avionics.FindModuleImplementing<ProceduralShapeSpine>() == null, "proceduralAvionics can't be a Spine");
            Assert.True(avionics.FindModuleImplementing<ModuleToolingPTank>()?.GetType() == typeof(ModuleToolingProcAvionics), "proceduralAvionics' tooling is ModuleToolingProcAvionics");
        }

        // RP-1 tools only in career (ToolingManager.toolingEnabled, set from the game mode); the tests run in sandbox.
        private static bool EnableTooling()
        {
            bool was = ToolingManager.Instance.toolingEnabled;
            ToolingManager.Instance.toolingEnabled = true;
            return was;
        }

        [UITest(Kind = "Check", Requires = "RP-1", StartsAt = StartState.Vab, Craft = "SpineTest", TimeoutSec = 1800)]
        public static IEnumerator Rp1_SpineToolsAsTwoBoundingCylinders(TestContext ctx)
        {
            yield return Wait.Frames(10);
            Part part = Body();
            var spine = part.FindModuleImplementing<ProceduralShapeSpine>();
            var tool = part.FindModuleImplementing<ModuleToolingPTank>();
            Assert.NotNull(tool, "the part has ModuleToolingPTank");
            float maxD = spine.MaxDiameter, minD = spine.MinBoundingDiameter, len = spine.length;
            ctx.Record("max", maxD);
            ctx.Record("min", minD);
            ctx.Record("length", len);
            Assert.True(minD < maxD - 0.1f, $"the section varies ({minD:F3} .. {maxD:F3} m)");

            // its one-size stand-in is the widest bounding cylinder
            tool.GetDimensions(out float d, out float l);
            Assert.Near(maxD, d, 1e-4, "tooling diameter = the widest bounding diameter");
            Assert.Near(len, l, 1e-4, "tooling length = the Spine's length");
            string info = tool.GetToolingParameterInfo();
            ctx.Record("info", info);
            Assert.True(info.Contains("+"), $"two cylinders listed ({info})");

            // the hidden Cylinder (what RP-1 alone would read) doesn't count
            var cyl = part.FindModuleImplementing<ProceduralShapeCylinder>();
            float cylD = cyl.diameter, cylL = cyl.length;
            cyl.diameter = 0.3f;
            cyl.length = 0.4f;
            tool.GetDimensions(out d, out l);
            cyl.diameter = cylD;
            cyl.length = cylL;
            Assert.Near(maxD, d, 1e-4, "the hidden Cylinder's size is ignored");

            // untooled; tooling the widest alone leaves it untooled, and costs less to finish
            bool toolingWas = EnableTooling();
            string type = tool.ToolingType;
            ctx.Record("type", type);
            Assert.False(tool.IsUnlocked(), "untooled at first");
            // the PAW's two lines: each cylinder's size at the part's length, and not tooled yet
            Refresh(tool);
            ctx.Record("paw", $"{Max(tool)} | {Min(tool)}");
            Assert.True(Max(tool).StartsWith($"{maxD:F3} x {len:F3} m") && Max(tool).EndsWith("not tooled"), $"the widest line ({Max(tool)})");
            Assert.True(Min(tool).StartsWith($"{minD:F3} x {len:F3} m") && Min(tool).EndsWith("not tooled"), $"the narrowest line ({Min(tool)})");
            float full = tool.GetToolingCost();
            ctx.Record("cost", full);
            Assert.True(full > 0f, $"tooling costs something ({full})");
            // one cylinder's cost as RP-1 works it out (ModuleToolingDiamLen.GetToolingCost(d, l), by name)
            float Cylinder(float dia) => tool.finalToolingCostMultiplier * (float)typeof(ModuleToolingDiamLen)
                .GetMethod("GetToolingCost", BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(float), typeof(float) }, null)
                .Invoke(tool, new object[] { dia, len });
            float minCost = Cylinder(minD);
            Assert.Near(Cylinder(maxD) + minCost, full, 0.001 * full, "both cylinders' cost");
            ToolingDatabase.UnlockTooling(type, maxD, len);
            Assert.False(tool.IsUnlocked(), "the widest cylinder alone doesn't tool it");
            float rest = tool.GetToolingCost();
            ctx.Record("costAfterMax", rest);
            // the tooled cylinder isn't charged again, nor listed
            Assert.Near(minCost, rest, 0.001 * minCost, $"only the narrowest is charged with the widest tooled ({rest} of {full})");
            info = tool.GetToolingParameterInfo();
            ctx.Record("infoAfterMax", info);
            Assert.True(info == $"{minD:F3}m x {len:F3}m", $"only the narrowest listed ({info})");

            // buying it tools both
            tool.PurchaseTooling();
            Assert.True(tool.IsUnlocked(), "tooled after buying");
            Assert.True(ToolingDatabase.GetToolingLevel(type, minD, len) == 2, "the narrowest cylinder is tooled");
            Assert.True(ToolingDatabase.GetToolingLevel(type, maxD, len) == 2, "the widest cylinder is tooled");
            Refresh(tool);
            Assert.True(Max(tool).EndsWith(", tooled") && Min(tool).EndsWith(", tooled"), $"both PAW lines read tooled ({Max(tool)} | {Min(tool)})");
            ToolingManager.Instance.toolingEnabled = toolingWas;
        }

        [UITest(Kind = "Check", Requires = "RP-1", StartsAt = StartState.Vab, Craft = "SpineTest", TimeoutSec = 1800)]
        public static IEnumerator Rp1_ConstantSectionToolsOnce(TestContext ctx)
        {
            yield return Wait.Frames(10);
            Part part = Body();
            var spine = part.FindModuleImplementing<ProceduralShapeSpine>();
            var tool = part.FindModuleImplementing<ModuleToolingPTank>();
            List<SpineNode> saved = Snapshot(spine);
            foreach (SpineNode n in spine.nodes)
            {
                n.sizeH = n.sizeV = 1.7f;
                n.offsetH = n.offsetV = 0f;
                n.section = spine.nodes[0].section;
            }
            spine.UpdateShape(true);
            yield return Wait.Frames(2);
            Assert.Near(spine.MaxDiameter, spine.MinBoundingDiameter, 1e-3, "one bounding size along the part");
            string info = tool.GetToolingParameterInfo();
            ctx.Record("info", info);
            Assert.False(info.Contains("+"), $"one cylinder listed ({info})");
            bool toolingWas = EnableTooling();
            Assert.False(tool.IsUnlocked(), "untooled before buying");
            tool.PurchaseTooling();
            Assert.True(tool.IsUnlocked(), "tooled after buying");
            ToolingManager.Instance.toolingEnabled = toolingWas;
            Restore(spine, saved);
        }

        [UITest(Kind = "Check", Requires = "RP-1", StartsAt = StartState.Vab, Craft = "SpineTest", TimeoutSec = 1800)]
        public static IEnumerator Rp1_OtherShapesToolAsBefore(TestContext ctx)
        {
            yield return Wait.Frames(10);
            Part part = Body();
            var spine = part.FindModuleImplementing<ProceduralShapeSpine>();
            var tool = part.FindModuleImplementing<ModuleToolingPTank>();
            List<SpineNode> saved = Snapshot(spine);
            float spineLength = spine.length;
            yield return SetShape(part, "Cylinder");
            var cyl = part.FindModuleImplementing<ProceduralShapeCylinder>();
            tool.GetDimensions(out float d, out float l);
            ctx.Record("cylinder", $"{cyl.diameter} x {cyl.length}");
            Assert.Near(cyl.diameter, d, 1e-4, "a Cylinder tools by its diameter");
            Assert.Near(cyl.length, l, 1e-4, "and its length");
            Assert.False(tool.GetToolingParameterInfo().Contains("+"), "one cylinder");
            yield return SetShape(part, spine.displayName);
            spine.length = spineLength;
            Restore(spine, saved);
        }

        /// <summary>
        /// In a career, RP-1's own views agree with the Spine's tooling: the editor's tooling cost (the Integration Info
        /// window's "Tooling Cost") and its untooled-parts list hold the part until it's tooled and drop it then, and its
        /// PAW button reads Tooled (and back to Tool ... once a reshape outgrows the tooling). RP-1's 4% margin holds for a
        /// Spine's two diameters and its length alike: within it a tooled Spine stays tooled, past it it doesn't.
        /// SpineBody: 2.4 m round, 3.0 m round, 1.6 m round, 6 m long.
        /// </summary>
        [UITest(Kind = "Check", NewGame = "career", Requires = "RP-1", StartsAt = StartState.Vab, Craft = "SpineBody", TimeoutSec = 1800)]
        public static IEnumerator Rp1_ToolingShowsInRp1AndKeepsItsMargin(TestContext ctx)
        {
            yield return Wait.Frames(10);
            Part part = Body();
            var spine = part.FindModuleImplementing<ProceduralShapeSpine>();
            var tool = part.FindModuleImplementing<ModuleToolingPTank>();
            Assert.True(ToolingManager.Instance.toolingEnabled, "a career has tooling on");
            List<SpineNode> saved = Snapshot(spine);
            float len0 = spine.length;
            float total = 0f;
            bool listed = false;
            string button = "";

            // RP-1 recomputes its editor numbers on its own Update after a change: what it shows now
            IEnumerator Rp1View()
            {
                SpaceCenterManagement.Instance.IsEditorRecalcuationRequired = true;
                for (int f = 0; f < 600 && SpaceCenterManagement.Instance.IsEditorRecalcuationRequired; f++)
                    yield return null;
                yield return Wait.Frames(2);
                ToolingGUI.GetUntooledPartsAndCost(out List<ModuleTooling> untooled, out total);
                listed = untooled.Contains(tool);
                Refresh(tool);
                button = tool.Events["ToolingEvent"].guiName;
            }

            yield return Rp1View();
            ctx.Record("untooled", $"listed {listed}, cost {total:F0}, Integration Info {SpaceCenterManagement.EditorToolingCosts:F0}, button '{button}'");
            Assert.True(listed, "RP-1 lists the part as untooled");
            Assert.Near(tool.GetToolingCost(), total, 0.01 * total, "at the Spine's tooling cost");
            Assert.True(SpaceCenterManagement.EditorToolingCosts > 0, "Integration Info shows a tooling cost");
            Assert.True(button != "Tooled", $"RP-1's button offers tooling ('{button}')");

            tool.PurchaseTooling();   // what the dialog's Purchase Tooling does, bar the unlock credit
            yield return Rp1View();
            ctx.Record("tooled", $"listed {listed}, cost {total:F0}, Integration Info {SpaceCenterManagement.EditorToolingCosts:F0}, button '{button}'");
            Assert.False(listed, "RP-1 no longer lists it as untooled");
            Assert.True(total == 0f && SpaceCenterManagement.EditorToolingCosts == 0, "Integration Info shows no tooling cost");
            Assert.True(button == "Tooled", $"RP-1's button reads Tooled ('{button}')");

            // the margin, one size at a time: the widest node (1), the narrowest (2), the length
            var cases = new (string what, int node, float lenScale, float scale, bool tooled)[]
            {
                ("widest +3%", 1, 1f, 1.03f, true), ("widest +5%", 1, 1f, 1.05f, false),
                ("widest -3%", 1, 1f, 0.97f, true), ("widest -5%", 1, 1f, 0.95f, false),
                ("narrowest +3%", 2, 1f, 1.03f, true), ("narrowest -5%", 2, 1f, 0.95f, false),
                ("length +3%", -1, 1.03f, 1f, true), ("length -5%", -1, 0.95f, 1f, false),
            };
            var fails = new List<string>();
            foreach (var c in cases)
            {
                Restore(spine, saved.Select(n => n.Clone()).ToList());
                spine.length = len0 * c.lenScale;
                if (c.node >= 0)
                {
                    spine.nodes[c.node].sizeH *= c.scale;
                    spine.nodes[c.node].sizeV *= c.scale;
                }
                spine.UpdateShape(true);
                yield return Rp1View();
                bool ok = tool.IsUnlocked() == c.tooled && listed == !c.tooled && (total == 0f) == c.tooled && (button == "Tooled") == c.tooled
                          && Max(tool).EndsWith(c.tooled || c.node == 2 ? ", tooled" : "not tooled") == true;
                string line = $"{c.what}: {tool.GetToolingParameterInfo()}, tooled {tool.IsUnlocked()}, RP-1 listed {listed} cost {total:F0}, button '{button}' | {Max(tool)} | {Min(tool)}";
                ctx.Record(c.what, line);
                if (!ok) fails.Add(line);
            }
            spine.length = len0;
            Restore(spine, saved);
            Assert.True(fails.Count == 0, "within 4% still tooled, past it not, in RP-1's views too: " + string.Join(" || ", fails));
        }
    }
}
