using System.Collections.Generic;
using BranteAccess.Module.UI;
using ParameterButtonChanger = _Scripts.AMVCC.Views.ParameterButtonChanger;
using SceneConsequenceGenerator = _Scripts.AMVCC.Views.Windows.SceneConsequenceGenerator;
using Objective = _Scripts.AMVCC.Views.Windows.Destiny.Objective;
using ObjectiveCondition = _Scripts.AMVCC.Views.Windows.Destiny.ObjectiveCondition;
using ObjectiveInitializer = _Scripts.AMVCC.Views.Windows.Destiny.ObjectiveInitializer;
using ObjectiveEnum = _Scripts.AMVCC.Views.Windows.ObjectiveEnum;
using KeyParams = _Scripts.AMVCC.Models.Static.KeyChapterParametersController;
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

        /// <summary>A destiny objective's detail: its already-localized description plus the
        /// condition rows, composed the way the game's own earn-popup renders them (operands
        /// through I2 with the el substitution, or-flagged rows as one any-of group). The
        /// hover panel in the Destiny window shows the raw serialized keys instead - the
        /// popup path is the authored one, so speech follows it.</summary>
        public static string ObjectiveDetails(ObjectiveInitializer oi)
        {
            var rows = new List<string>();
            var orRows = new List<string>();
            foreach (var c in oi.Objective.Condition)
                (c.OrCondition ? orRows : rows).Add(ObjectiveConditionRow(c));
            if (orRows.Count > 0)
                rows.Add(Loc.T("choice.cond.any",
                    new { rows = string.Join(", ", orRows.ToArray()) }));
            // The game text carries its own final period; the join supplies it.
            var parts = new List<string> { (oi.ObjectiveDescription ?? "").TrimEnd('.') };
            if (rows.Count > 0) parts.Add(string.Join(", ", rows.ToArray()));
            parts.RemoveAll(string.IsNullOrEmpty);
            return string.Join(". ", parts.ToArray());
        }

        // One serialized condition, rendered like the game's earn-popup: a translate-flagged
        // row is a key-parameter pair ("Post: Judge", "≠" when negated); a bare or
        // "!="-with-empty-value row is an event that must (not) have happened; anything else
        // is a parameter comparison with the symbol spoken as the op word.
        private static string ObjectiveConditionRow(ObjectiveCondition c)
        {
            var name = ElText(GameLoc.GetTranslation(c.Operand1));
            if (string.IsNullOrEmpty(name)) name = c.Operand1;
            if (c.NeedOperandTranslate)
            {
                var value = ElText(GameLoc.GetTranslation(c.Operand2));
                if (string.IsNullOrEmpty(value)) value = c.Operand2;
                var row = Loc.T("choice.req.status", new { name, status = value });
                return c.Operator == "=" ? row : Loc.T("choice.req.not", new { req = row });
            }
            if (string.IsNullOrEmpty(c.Operator)) return name;
            if (c.Operator == "!=" && string.IsNullOrEmpty(c.Operand2))
                return Loc.T("choice.req.not", new { req = name });
            return Loc.T("choice.req.param",
                new { name, op = SymbolOpWord(c.Operator), value = c.Operand2 });
        }

        // Objective conditions serialize the operator as a bare symbol; readers voice those
        // unreliably, so they map onto the same op words the choice requirements use.
        private static string SymbolOpWord(string symbol)
        {
            switch (symbol)
            {
                case "≥": return Loc.T("choice.op.moreequal");
                case "≤": return Loc.T("choice.op.lessequal");
                case ">": return Loc.T("choice.op.more");
                case "<": return Loc.T("choice.op.less");
                case "=": return Loc.T("choice.op.equal");
                case "!=": return Loc.T("choice.op.not");
                default: return symbol;
            }
        }

        /// <summary>The estate word for a character, as the game's info panels translate it.</summary>
        public static string CharacterEstate(_Scripts.AMVCC.Models.Static.Character co)
            => GameLoc.GetTranslation(System.Enum.GetName(
                typeof(_Scripts.AMVCC.Models.Static.Estates),
                _Scripts.Helpers.CharacterParametersSerializeHelper.Initiate.GetCharacterEstate(co)));

        /// <summary>The relation readout the game's info panels render: the Relations label with
        /// the signed value and the game's relation word ("Relations -1 (Indifference)").</summary>
        public static string CharacterRelationPair(_Scripts.AMVCC.Models.Static.Character co)
        {
            var pm = _Scripts.Managers.ParametersManager.Instance;
            var rel = pm.GetCharacterRelation(co,
                _Scripts.Helpers.CharacterParametersSerializeHelper.Initiate.Characters);
            return Loc.T("hud.pair", new
            {
                label = GameLoc.GetTranslation("HUD.Relation"),
                value = (rel > 0 ? "+" : "") + rel + " (" + pm.CheckParameterValue(rel) + ")",
            });
        }

        /// <summary>The character's status word when one is set - null for CharacterStatus.Good,
        /// where the game shows a bare dash and hides its status help icon.</summary>
        public static string CharacterStatusWord(_Scripts.AMVCC.Models.Static.Character co)
        {
            var pm = _Scripts.Managers.ParametersManager.Instance;
            var status = pm.GetCharacterStatus(co,
                _Scripts.Helpers.CharacterParametersSerializeHelper.Initiate.Characters);
            return status == CharacterStatus.Good ? null
                : GameLoc.GetTranslation(pm.GetCharacterStatusKey(co, status));
        }

        /// <summary>The character's description paragraph plus the status title and detail the
        /// game renders behind its help-icon tooltip when a status is set.</summary>
        public static string CharacterDetail(_Scripts.AMVCC.Models.Static.Character co)
        {
            var pm = _Scripts.Managers.ParametersManager.Instance;
            var chars = _Scripts.Helpers.CharacterParametersSerializeHelper.Initiate.Characters;
            var text = GameLoc.GetTranslation(pm.GetCharacterDescription(co, chars));
            var status = pm.GetCharacterStatus(co, chars);
            if (status != CharacterStatus.Good)
            {
                var key = pm.GetCharacterStatusKey(co, status);
                text += "\n" + Loc.T("tooltip.section", new
                {
                    title = GameLoc.GetTranslation(key),
                    rows = GameLoc.GetTranslation(key + ".Description"),
                });
            }
            return text;
        }

        /// <summary>A year-case's detail: the case description, its conditions with live met
        /// state (the same per-check data the game grays the button by), and the stat effects
        /// the case commits for the year - what the game's hover tooltip and detail popup show
        /// around the selection window.</summary>
        public static string CaseDetails(_Scripts.AMVCC.Views.Windows.CaseForYearEnabler e)
        {
            var c = e.CurrentCase;
            var pm = _Scripts.Managers.ParametersManager.Instance;
            var names = _Scripts.AMVCC.Models.Static.KeyChapterParametersController.Initiate;
            var sections = new List<string>
            {
                (ElText(GameLoc.GetTranslation(c.Description)) ?? "").TrimEnd('.'),
            };

            var conds = new List<string>();
            if (c.NeedCheckPossible && c.ConditionByParam != null)
                for (int i = 0; i < c.ConditionByParam.Count && i < e.Checks.Count; i++)
                {
                    var cond = c.ConditionByParam[i];
                    conds.Add(WithMet(WithNow(Loc.T("choice.req.param", new
                    {
                        name = GameLoc.GetTranslation(cond.ParamName.ParameterName.ToString()),
                        op = OpWord(cond.Operations.ToString()),
                        value = cond.SecondValue,
                    }), pm.GetParameterValue(cond.ParamName)), e.Checks[i]));
                }
            if (c.ByRelation != null)
                for (int i = 0; i < c.ByRelation.Count && i < e.ChecksByRelations.Count; i++)
                {
                    var cond = c.ByRelation[i];
                    conds.Add(WithMet(WithNow(Loc.T("choice.req.relation", new
                    {
                        name = names.GetCharacterTrueName(cond.Character.Name),
                        op = OpWord(cond.Operations.ToString()),
                        value = cond.Value,
                    }), pm.GetCharacterRelation(cond.Character,
                        _Scripts.Helpers.CharacterParametersSerializeHelper.Initiate.Characters)),
                        e.ChecksByRelations[i]));
                }
            if (c.ByStatus != null)
                for (int i = 0; i < c.ByStatus.Count && i < e.ChecksByStatus.Count; i++)
                {
                    var cond = c.ByStatus[i];
                    var row = Loc.T("choice.req.status", new
                    {
                        name = names.GetCharacterTrueName(cond.Character.Name),
                        status = GameLoc.GetTranslation(
                            pm.GetCharacterStatusKey(cond.Character, cond.Status) ?? ""),
                    });
                    if (cond.Not) row = Loc.T("choice.req.not", new { req = row });
                    conds.Add(WithMet(row, e.ChecksByStatus[i]));
                }
            if (c.ConditionByObjectives != null)
                for (int i = 0; i < c.ConditionByObjectives.Count && i < e.ChecksByObjective.Count; i++)
                {
                    var cond = c.ConditionByObjectives[i];
                    var row = ObjectiveTitle(cond.Objective);
                    if (cond.Not) row = Loc.T("choice.req.not", new { req = row });
                    conds.Add(WithMet(row, e.ChecksByObjective[i]));
                }
            if (conds.Count > 0)
                sections.Add(Loc.T("tooltip.section", new
                {
                    title = GameLoc.GetTranslation(UiWidgets.Interactable(e.gameObject)
                        ? "ConditionIsMet" : "ConditionNotMet"),
                    rows = string.Join(", ", conds.ToArray()),
                }));

            var cons = new List<string>();
            if (c.ParameterValue != null)
                foreach (var p in c.ParameterValue)
                {
                    if (p.Name.ParameterName == ParametersList.Heir)
                        cons.Add(Loc.T("choice.cons.set", new
                        {
                            name = GameLoc.GetTranslation(p.Name.ParameterName.ToString()),
                            value = pm.GetHeir(p.Value),
                        }));
                    else
                        cons.Add(WithNow(Loc.T("choice.cons.param", new
                        {
                            name = GameLoc.GetTranslation(p.Name.ParameterName.ToString()),
                            delta = Signed(p.Value),
                        }), pm.GetParameterValue(p.Name)));
                }
            if (c.RelationValue != null)
                foreach (var r in c.RelationValue)
                    cons.Add(WithNow(Loc.T("choice.cons.relation", new
                    {
                        name = names.GetCharacterTrueName(r.Character.Name),
                        delta = Signed(r.Value),
                    }), pm.GetCharacterRelation(r.Character,
                        _Scripts.Helpers.CharacterParametersSerializeHelper.Initiate.Characters)));
            if (c.StatusValue != null)
                foreach (var s in c.StatusValue)
                    cons.Add(Loc.T("choice.cons.set", new
                    {
                        name = names.GetCharacterTrueName(s.Character.Name),
                        value = GameLoc.GetTranslation(
                            pm.GetCharacterStatusKey(s.Character, s.CurrentStatus) ?? ""),
                    }));
            if (cons.Count > 0)
                sections.Add(Loc.T("tooltip.section", new
                {
                    title = GameLoc.GetTranslation("Consequence"),
                    rows = string.Join(", ", cons.ToArray()),
                }));

            sections.RemoveAll(string.IsNullOrEmpty);
            return string.Join(". ", sections.ToArray());
        }

        /// <summary>Why a year-case is grayed out: its failed checks only, composed from the
        /// same serialized conditions CaseDetails reads. The plain unavailable word when no
        /// simple check failed.</summary>
        public static string CaseUnavailableReason(_Scripts.AMVCC.Views.Windows.CaseForYearEnabler e)
        {
            var c = e.CurrentCase;
            var pm = _Scripts.Managers.ParametersManager.Instance;
            var names = _Scripts.AMVCC.Models.Static.KeyChapterParametersController.Initiate;
            var reqs = new List<string>();

            if (c.NeedCheckPossible && c.ConditionByParam != null)
                for (int i = 0; i < c.ConditionByParam.Count && i < e.Checks.Count; i++)
                    if (!e.Checks[i])
                        reqs.Add(Loc.T("choice.req.param", new
                        {
                            name = GameLoc.GetTranslation(
                                c.ConditionByParam[i].ParamName.ParameterName.ToString()),
                            op = OpWord(c.ConditionByParam[i].Operations.ToString()),
                            value = c.ConditionByParam[i].SecondValue,
                        }));
            if (c.ByRelation != null)
                for (int i = 0; i < c.ByRelation.Count && i < e.ChecksByRelations.Count; i++)
                    if (!e.ChecksByRelations[i])
                        reqs.Add(Loc.T("choice.req.relation", new
                        {
                            name = names.GetCharacterTrueName(c.ByRelation[i].Character.Name),
                            op = OpWord(c.ByRelation[i].Operations.ToString()),
                            value = pm.CheckParameterValue(c.ByRelation[i].Value),
                        }));
            if (c.ByStatus != null)
                for (int i = 0; i < c.ByStatus.Count && i < e.ChecksByStatus.Count; i++)
                    if (!e.ChecksByStatus[i])
                    {
                        var cond = c.ByStatus[i];
                        var req = Loc.T("choice.req.status", new
                        {
                            name = names.GetCharacterTrueName(cond.Character.Name),
                            status = GameLoc.GetTranslation(
                                pm.GetCharacterStatusKey(cond.Character, cond.Status) ?? ""),
                        });
                        reqs.Add(cond.Not ? Loc.T("choice.req.not", new { req }) : req);
                    }
            if (c.ConditionByObjectives != null)
                for (int i = 0; i < c.ConditionByObjectives.Count && i < e.ChecksByObjective.Count; i++)
                    if (!e.ChecksByObjective[i])
                    {
                        var cond = c.ConditionByObjectives[i];
                        var req = ObjectiveTitle(cond.Objective);
                        reqs.Add(cond.Not ? Loc.T("choice.req.not", new { req }) : req);
                    }

            if (reqs.Count == 0) return Loc.T("state.unavailable");
            return Loc.T("choice.unavailable", new { req = string.Join(", ", reqs.ToArray()) });
        }

        /// <summary>One trigger-popup condition row per serialized type, composed from the same
        /// model the popup's row generators render. The popup marks a negated condition only by
        /// strikethrough styling - these rows speak the not-word instead.</summary>
        public static string TriggerParamRow(_Scripts.Helpers.TriggerConditionByParameter c)
            => Loc.T("choice.req.param", new
            {
                name = GameLoc.GetTranslation(c.ParametersList.ToString()),
                op = OpWord(c.Operations.ToString()),
                value = c.SecondValue,
            });

        public static string TriggerRelationRow(_Scripts.Helpers.TriggerConditionByRelation c)
        {
            var pm = _Scripts.Managers.ParametersManager.Instance;
            var who = pm.GetCharacterByName(c.Character);
            return Loc.T("choice.req.relation", new
            {
                name = KeyParams.Initiate.GetCharacterTrueName(who.Name),
                op = OpWord(c.Operations.ToString()),
                value = c.Value + " (" + pm.CheckParameterValue(c.Value) + ")",
            });
        }

        public static string TriggerStatusRow(_Scripts.Helpers.TriggerConditionByStatus c)
        {
            var pm = _Scripts.Managers.ParametersManager.Instance;
            var who = pm.GetCharacterByName(c.Character);
            var row = Loc.T("choice.req.status", new
            {
                name = KeyParams.Initiate.GetCharacterTrueName(who.Name),
                status = GameLoc.GetTranslation(
                    pm.GetCharacterStatusKey(c.Character, c.Status) ?? ""),
            });
            return c.Not ? Loc.T("choice.req.not", new { req = row }) : row;
        }

        public static string TriggerObjectiveRow(_Scripts.Helpers.TriggerConditionByObjective c)
        {
            var row = ObjectiveTitle(KeyParams.Initiate.GetObjectiveByName(c.Objective));
            return c.Not ? Loc.T("choice.req.not", new { req = row }) : row;
        }

        /// <summary>One spoken line from a label that wraps over multiple lines on screen.</summary>
        public static string Collapse(string text)
            => string.Join(" ", text.Split(
                new[] { ' ', '\n', '\r', '\t' }, System.StringSplitOptions.RemoveEmptyEntries));

        /// <summary>The game's own el-placeholder substitution, as its popup inlines it: the
        /// sword-noble particle when earned, else removed.</summary>
        public static string ElText(string text)
        {
            if (text == null) return null;
            return KeyParams.Initiate.GetNobleSwordStatus()
                ? text.Replace("<el>", GameLoc.GetTranslation("SwordNobleEl"))
                : text.Replace("<el>", string.Empty).Replace("  ", " ");
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
