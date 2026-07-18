using System.Collections.Generic;
using ParameterButtonChanger = _Scripts.AMVCC.Views.ParameterButtonChanger;
using SceneConsequenceGenerator = _Scripts.AMVCC.Views.Windows.SceneConsequenceGenerator;
using Objective = _Scripts.AMVCC.Views.Windows.Destiny.Objective;
using ObjectiveEnum = _Scripts.AMVCC.Views.Windows.ObjectiveEnum;
using Parameter = _Scripts.AMVCC.Models.Static.Parameter;
using ParametersList = _Scripts.AMVCC.Models.Static.ParametersList;
using GameLoc = I2.Loc.LocalizationManager;

namespace BranteAccess.Module.Game
{
    /// <summary>
    /// On-demand detail readouts (Space/F1) composed from the same serialized model data the
    /// game's hover tooltips render - never from the tooltip views themselves (house rule: read
    /// detail from the model, not a tooltip that lags focus). Section headers and the "now"
    /// word are the game's own strings; only glue templates are mod-authored.
    /// </summary>
    public static class Readouts
    {
        /// <summary>The choice's condition and consequence detail - what the game shows in its
        /// ConditionTooltip and BigConsequenceTooltip on hover. Null when the choice has
        /// neither (the caller speaks the no-details word).</summary>
        public static string ChoiceDetails(ParameterButtonChanger c)
        {
            var sections = new List<string>();
            var conditions = ConditionRows(c);
            if (conditions != null) sections.Add(conditions);
            var consequences = ConsequenceRows(c);
            if (consequences != null) sections.Add(consequences);
            return sections.Count == 0 ? null : string.Join(". ", sections.ToArray());
        }

        // Every condition with its requirement, live current value, and met state, headed by
        // the game's own Conditions Met / Conditions Not Met string. NeedCheckPossible is the
        // game's own gate for showing the condition tooltip at all.
        private static string ConditionRows(ParameterButtonChanger c)
        {
            if (!c.NeedCheckPossible) return null;
            var pm = _Scripts.Managers.ParametersManager.Instance;
            var names = _Scripts.AMVCC.Models.Static.KeyChapterParametersController.Initiate;
            var rows = new List<string>();

            for (int i = 0; i < c.Condition.Count && i < c.Checks.Count; i++)
            {
                var cond = c.Condition[i];
                string row;
                if (cond.ParamName.ParameterName == ParametersList.Heir)
                    row = Loc.T("choice.req.param", new
                    {
                        name = GameLoc.GetTranslation(cond.ParamName.ParameterName.ToString()),
                        op = OpWord(cond.Operations.ToString()),
                        value = pm.GetHeir(cond.SecondValue),
                    });
                else
                    row = WithNow(Loc.T("choice.req.param", new
                    {
                        name = GameLoc.GetTranslation(cond.ParamName.ParameterName.ToString()),
                        op = OpWord(cond.Operations.ToString()),
                        value = cond.SecondValue,
                    }), pm.GetParameterValue(cond.ParamName));
                rows.Add(WithMet(row, c.Checks[i]));
            }

            if (c.ConditionByRelations != null)
                for (int i = 0; i < c.ConditionByRelations.Length && i < c.ChecksByRelation.Count; i++)
                {
                    var cond = c.ConditionByRelations[i];
                    var row = WithNow(Loc.T("choice.req.relation", new
                    {
                        name = names.GetCharacterTrueName(cond.Character.Name),
                        op = OpWord(cond.Operation.ToString()),
                        value = cond.Value,
                    }), pm.GetCharacterRelation(cond.Character,
                        _Scripts.Helpers.CharacterParametersSerializeHelper.Initiate.Characters));
                    rows.Add(WithMet(row, c.ChecksByRelation[i]));
                }

            if (c.ConditionsByStatus != null)
                for (int i = 0; i < c.ConditionsByStatus.Length && i < c.ChecksByStatus.Count; i++)
                {
                    var cond = c.ConditionsByStatus[i];
                    var row = Loc.T("choice.req.status", new
                    {
                        name = names.GetCharacterTrueName(cond.Character.Name),
                        status = GameLoc.GetTranslation(
                            pm.GetCharacterStatusKey(cond.Character, cond.Status) ?? ""),
                    });
                    if (cond.Not) row = Loc.T("choice.req.not", new { req = row });
                    rows.Add(WithMet(row, c.ChecksByStatus[i]));
                }

            if (c.ConditionByObjectives != null)
                for (int i = 0; i < c.ConditionByObjectives.Length && i < c.ChecksByObjective.Count; i++)
                {
                    var cond = c.ConditionByObjectives[i];
                    var row = ObjectiveTitle(cond.Objective);
                    if (cond.Not) row = Loc.T("choice.req.not", new { req = row });
                    rows.Add(WithMet(row, c.ChecksByObjective[i]));
                }

            if (c.DifficultConditions) rows.Add(OrRows(c.OR_Condition, pm, names));

            if (rows.Count == 0) return null;
            return Loc.T("tooltip.section", new
            {
                title = GameLoc.GetTranslation(
                    c.GetButtonInteractbleStatus() ? "ConditionIsMet" : "ConditionNotMet"),
                rows = string.Join(", ", rows.ToArray()),
            });
        }

        // The OR-tree (any one branch satisfies it), each branch with its live met state.
        private static string OrRows(_Scripts.AMVCC.Views.DifficultCondition or,
            _Scripts.Managers.ParametersManager pm,
            _Scripts.AMVCC.Models.Static.KeyChapterParametersController names)
        {
            var rows = new List<string>();
            foreach (var item in or.ByParameter)
                rows.Add(WithMet(WithNow(Loc.T("choice.req.param", new
                {
                    name = GameLoc.GetTranslation(item.Value.ParamName.ParameterName.ToString()),
                    op = OpWord(item.Value.Operations.ToString()),
                    value = item.Value.SecondValue,
                }), pm.GetParameterValue(item.Value.ParamName)), item.GetCompareValue()));
            foreach (var item in or.ByRelation)
                rows.Add(WithMet(WithNow(Loc.T("choice.req.relation", new
                {
                    name = names.GetCharacterTrueName(item.Value.Character.Name),
                    op = OpWord(item.Value.Operations.ToString()),
                    value = item.Value.Value,
                }), pm.GetCharacterRelation(item.Value.Character,
                    _Scripts.Helpers.CharacterParametersSerializeHelper.Initiate.Characters)),
                    item.GetCompareValue()));
            foreach (var item in or.ByStatus)
            {
                var row = Loc.T("choice.req.status", new
                {
                    name = names.GetCharacterTrueName(item.Value.Character.Name),
                    status = GameLoc.GetTranslation(
                        pm.GetCharacterStatusKey(item.Value.Character, item.Value.Status) ?? ""),
                });
                if (item.Value.Not) row = Loc.T("choice.req.not", new { req = row });
                rows.Add(WithMet(row, item.GetCompareValue()));
            }
            foreach (var item in or.ByObjective)
            {
                var row = ObjectiveTitle(item.Value.Objective);
                if (item.Value.Not) row = Loc.T("choice.req.not", new { req = row });
                rows.Add(WithMet(row, item.GetCompareValue()));
            }
            return Loc.T("choice.cond.any", new { rows = string.Join(", ", rows.ToArray()) });
        }

        // Every stat effect with its live current value, headed by the game's own Consequences
        // string. The gates are the game's: it hides consequences in story mode, on
        // NoConsequence choices, and when UseConsequence is off.
        private static string ConsequenceRows(ParameterButtonChanger c)
        {
            if (!c.UseConsequence || c.NoConsequence) return null;
            if (_Scripts.Managers.GameManager.Instance.StoryMode) return null;
            var pm = _Scripts.Managers.ParametersManager.Instance;
            var names = _Scripts.AMVCC.Models.Static.KeyChapterParametersController.Initiate;
            var rows = new List<string>();

            foreach (var p in c.Parameters)
            {
                if (p.Name.ParameterName == ParametersList.Heir)
                    rows.Add(Loc.T("choice.cons.set", new
                    {
                        name = GameLoc.GetTranslation(p.Name.ParameterName.ToString()),
                        value = pm.GetHeir(p.Value),
                    }));
                else
                    rows.Add(WithNow(Loc.T("choice.cons.param", new
                    {
                        name = GameLoc.GetTranslation(p.Name.ParameterName.ToString()),
                        delta = Signed(p.Value),
                    }), pm.GetParameterValue(p.Name)));
            }

            // Objectives ride the consequence prefab's generator (absent on most choices).
            var gen = c.Consequence == null ? null
                : c.Consequence.GetComponentInChildren<SceneConsequenceGenerator>(includeInactive: true);
            if (gen != null)
            {
                if (gen.Objective != null) rows.Add(ObjectiveTitle(gen.Objective));
                else foreach (var obj in gen.Objectives) rows.Add(ObjectiveTitle(obj));
            }

            if (c.Statuses != null)
                foreach (var s in c.Statuses)
                    rows.Add(Loc.T("choice.cons.set", new
                    {
                        name = names.GetCharacterTrueName(s.Character.Name),
                        value = GameLoc.GetTranslation(
                            pm.GetCharacterStatusKey(s.Character, s.CurrentStatus) ?? ""),
                    }));

            if (c.Relations != null)
                foreach (var r in c.Relations)
                    rows.Add(WithNow(Loc.T("choice.cons.relation", new
                    {
                        name = names.GetCharacterTrueName(r.Character.Name),
                        delta = Signed(r.Value),
                    }), pm.GetCharacterRelation(r.Character,
                        _Scripts.Helpers.CharacterParametersSerializeHelper.Initiate.Characters)));

            if (rows.Count == 0) return null;
            return Loc.T("tooltip.section", new
            {
                title = GameLoc.GetTranslation("Consequence"),
                rows = string.Join(", ", rows.ToArray()),
            });
        }

        /// <summary>The parameter's scale readout - what the game's ParameterValueTooltip shows
        /// on hover over a stat row: name, description, every scale segment with its range, the
        /// current one marked with the game's own "now" word.</summary>
        public static string ParameterScales(Parameter p)
        {
            var pm = _Scripts.Managers.ParametersManager.Instance;
            int current = pm.GetParameterValue(p);
            var rows = new List<string>();
            for (int i = 0; i < p.Scales.Length; i++)
            {
                var s = p.Scales[i];
                var text = GameLoc.GetTranslation(
                    p.ParameterName + ".Segment" + (i + 1)) ?? "";
                var row = s.MinValue == s.MaxValue
                    ? Loc.T("param.scale.single", new { value = s.MinValue, text })
                    : Loc.T("param.scale.range", new { min = s.MinValue, max = s.MaxValue, text });
                if (current >= s.MinValue && current <= s.MaxValue)
                    row = Loc.T("param.scale.now", new
                    { now = GameLoc.GetTranslation("Now"), row });
                rows.Add(row);
            }
            var parts = new List<string>
            {
                GameLoc.GetTranslation(p.ParameterName.ToString()),
                // The game text carries its own final period; the join supplies it.
                (GameLoc.GetTranslation(p.ParameterName + ".Description") ?? "").TrimEnd('.'),
                string.Join(", ", rows.ToArray()),
            };
            parts.RemoveAll(string.IsNullOrEmpty);
            return string.Join(". ", parts.ToArray());
        }

        private static string ObjectiveTitle(Objective obj)
            => GameLoc.GetTranslation(obj.Name == ObjectiveEnum.None
                ? obj.name + ".Title"
                : System.Enum.GetName(typeof(ObjectiveEnum), obj.Name) + ".Title");

        private static string WithNow(string row, int current)
            => Loc.T("choice.cond.now", new
            { row, now = GameLoc.GetTranslation("Now"), current });

        private static string WithMet(string row, bool met)
            => Loc.T(met ? "choice.cond.met" : "choice.cond.notmet", new { row });

        private static string Signed(int value) => value > 0 ? "+" + value : value.ToString();

        private static string OpWord(string operation)
            => Loc.T("choice.op." + operation.ToLower());
    }
}
