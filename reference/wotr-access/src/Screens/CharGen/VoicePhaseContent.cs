using System.Collections.Generic;
using HarmonyLib;
using Kingmaker.Blueprints; // Gender
using Kingmaker.UI.MVVM._VM.CharGen.Phases.Voice;
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// Voice phase. IMPORTANT: the gender filter is VIEW state, not on the VM — it lives on the live
    /// <c>CharGenVoiceSelectorPCView.Gender</c> (a <c>ReactiveProperty&lt;Gender&gt;</c>), initialized to
    /// the character's gender; the game's Male/Female filter buttons write it, and the view's
    /// <c>IsVisible</c> shows same-gender voices PLUS the empty "None" voice (cross-gender voices are
    /// hidden). So we reflect that live property — a "Voice gender" dropdown reads and writes it, driving
    /// the game's real filter (both displays stay in sync) — and mirror IsVisible for our list. No shadow
    /// field; when the view isn't up yet we fall back to the character's gender. Selecting a voice chooses
    /// it and plays a sample (off the resulting Barks); re-selecting the current one replays it. Built
    /// lazily by the game — renders once it materializes.
    /// </summary>
    public sealed class VoicePhaseContent : CharGenPhaseContent<CharGenVoicePhaseVM>
    {
        // CharGenView.s_Instance → SelectedDetailView → (voice) VoiceSelectorPc → Gender reactive.
        private static readonly System.Type CharGenViewType =
            AccessTools.TypeByName("Kingmaker.UI.MVVM._PCView.CharGen.CharGenView");
        private static readonly System.Reflection.FieldInfo InstanceField =
            CharGenViewType != null ? AccessTools.Field(CharGenViewType, "s_Instance") : null;
        private static readonly System.Reflection.FieldInfo SelectedDetailField =
            CharGenViewType != null ? AccessTools.Field(CharGenViewType, "SelectedDetailView") : null;
        private static readonly System.Type VoiceDetailViewType =
            AccessTools.TypeByName("Kingmaker.UI.MVVM._PCView.CharGen.Phases.Voice.CharGenVoicePhaseDetailedPCView");
        private static readonly System.Reflection.FieldInfo VoiceSelectorField =
            VoiceDetailViewType != null ? AccessTools.Field(VoiceDetailViewType, "VoiceSelectorPc") : null;
        private static readonly System.Type VoiceSelectorViewType =
            AccessTools.TypeByName("Kingmaker.UI.MVVM._PCView.CharGen.Phases.Voice.CharGenVoiceSelectorPCView");
        private static readonly System.Reflection.FieldInfo GenderField =
            VoiceSelectorViewType != null ? AccessTools.Field(VoiceSelectorViewType, "Gender") : null;

        public VoicePhaseContent(CharGenVoicePhaseVM phase) : base(phase) { }

        public override void Build(GraphBuilder b, string k)
        {
            var filter = FilterGender(); // null when the selector view isn't present yet

            // The gender filter (the game's Male/Female buttons), reflecting the live view. Only shown
            // once we can reach the property — otherwise there's nothing to drive.
            if (filter.HasValue)
                b.AddItem(ControlId.Structural(k + "genderfilter"),
                    ModSettingNodes.ChoiceDropdown(Loc.T("chargen.voice_gender"),
                        new List<string> { Loc.T("gender.male"), Loc.T("gender.female") },
                        () => filter.Value == Gender.Female ? 1 : 0,
                        i => SetFilterGender(i == 1 ? Gender.Female : Gender.Male)));

            var voices = Voices();
            if (voices.Count == 0) return; // lazy — renders once the selector materializes

            // Mirror CharGenVoiceSelectorPCView.IsVisible: same-gender voices + the empty "None"
            // (cross-gender voices are hidden). Fall back to the character's gender when the view isn't up.
            var g = filter ?? Phase.CharacterGender;
            var visible = new List<CharGenVoiceItemVM>();
            foreach (var v in voices)
                if (v != null && (v.Gender == g || v.IsEmptyVoice)) visible.Add(v);
            visible.Sort((a, bb) => a.IsEmptyVoice == bb.IsEmptyVoice ? 0 : a.IsEmptyVoice ? 1 : -1);

            b.BeginStop("voices").PushContext(Loc.T("chargen.voices"), "list");
            int i = 0;
            foreach (var v in visible)
            {
                var voice = v; // capture for the live closure
                // Activate mirrors the game's item view: re-activating the current voice replays its
                // sample (PlayPreview); picking a new one selects it (the sample plays off the Barks).
                b.AddItem(ControlId.Referenced(voice, k + "voice:" + i),
                    GraphNodes.SelectionItem(voice, () => voice.DisplayName, onActivate: () =>
                    {
                        // The game plays the voice SAMPLE (off Barks) on top of the UI click, so keep the
                        // default click (don't suppress it — that was a port slip).
                        if (voice.IsSelected.Value) voice.Barks?.PlayPreview();
                        else voice.SetSelectedFromView(true);
                    }));
                i++;
            }
            b.PopContext();
        }

        // The live Gender reactive property object, or null when the voice selector view isn't present.
        private static object GenderReactive()
        {
            var cgv = InstanceField?.GetValue(null);
            if (cgv == null) return null;
            var view = SelectedDetailField?.GetValue(cgv);
            if (view == null || VoiceDetailViewType == null || !VoiceDetailViewType.IsInstanceOfType(view)) return null;
            var selector = VoiceSelectorField?.GetValue(view);
            return selector != null ? GenderField?.GetValue(selector) : null;
        }

        private static Gender? FilterGender()
        {
            var rp = GenderReactive();
            var val = rp?.GetType().GetProperty("Value")?.GetValue(rp);
            return val is Gender g ? g : (Gender?)null;
        }

        private static void SetFilterGender(Gender gender)
        {
            var rp = GenderReactive();
            rp?.GetType().GetProperty("Value")?.SetValue(rp, gender); // drives the game's filter too
        }

        private List<CharGenVoiceItemVM> Voices()
        {
            var result = new List<CharGenVoiceItemVM>();
            var entities = Phase.VoiceSelector?.EntitiesCollection;
            if (entities != null)
                foreach (var v in entities) if (v != null) result.Add(v);
            return result;
        }
    }
}
