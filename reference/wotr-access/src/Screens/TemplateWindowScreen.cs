using System.Collections.Generic;
using Owlcat.Runtime.UI.Tooltips; // TooltipBaseTemplate
using WrathAccess.UI;
using WrathAccess.UI.Graph;
using WrathAccess.UI.Tooltips; // TooltipFlowBuilder

namespace WrathAccess.Screens
{
    /// <summary>
    /// Shared base for the game's persistent template windows on <c>CommonVM.TooltipContextVM</c> — the Info
    /// window (item Details / glossary, <see cref="InfoWindowScreen"/>) and the unit Inspect window
    /// (<see cref="InspectScreen"/>). Each is a real modal we must read and close: a subclass points at the
    /// live window object (<see cref="Window"/>, null = closed), supplies its templates, and says how to
    /// close it; the base renders the templates' rows as graph nodes (<see cref="TooltipFlowBuilder.Emit"/>,
    /// so headings + glossary drill-in work) and maps Back to the close. Node keys carry the window object,
    /// so a window swap (e.g. glossary→info) re-keys and re-homes emergently. Layer 30, Exclusive.
    /// </summary>
    public abstract class TemplateWindowScreen : Screen
    {
        protected TemplateWindowScreen() { Wrap = true; }

        public override int Layer => 30;
        public override bool Exclusive => true;

        /// <summary>The live window VM (null = closed) — also the re-key-on-swap identity.</summary>
        protected abstract object Window { get; }
        /// <summary>The templates this window currently shows (usually one).</summary>
        protected abstract IEnumerable<TooltipBaseTemplate> Templates();
        /// <summary>Run the window's own close (dispose + refocus); the reactive then nulls and we pop.</summary>
        protected abstract void CloseWindow();

        public override bool IsActive() => Window != null;

        public override IEnumerable<ElementAction> GetActions()
        {
            // Back/Escape runs the window's own close callback; the reactive nulls, IsActive goes false, we pop.
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"),
                _ => { if (Window != null) CloseWindow(); });
        }


        public override void Build(GraphBuilder b)
        {
            var w = Window;
            if (w == null) return;
            string k = "tplwin:" + w.GetHashCode() + ":"; // window swap (glossary→info) re-keys + re-homes

            int rows = 0, t = 0;
            var templates = Templates();
            if (templates != null)
                foreach (var tpl in templates)
                {
                    if (tpl == null) continue;
                    rows += TooltipFlowBuilder.Emit(b, k + "t" + t + ":", tpl, includeEmptyNotice: false);
                    t++;
                }
            if (rows == 0)
                b.AddItem(ControlId.Structural(k + "empty"), GraphNodes.Text(() => Loc.T("tooltip.empty")));
        }
    }
}
