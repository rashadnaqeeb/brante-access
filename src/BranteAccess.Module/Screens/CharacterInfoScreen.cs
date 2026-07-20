using System.Collections.Generic;
using BranteAccess.Module.Game;
using BranteAccess.Module.Speech;
using BranteAccess.Module.UI;
using BranteAccess.Module.UI.Graph;
using CharacterTile = _Scripts.AMVCC.Views.Windows.Family.StatusRelationGetSet;

namespace BranteAccess.Module.Screens
{
    /// <summary>
    /// A character's info panel as browsable rows - what the game fills on the Family and
    /// Relations windows' right page for the selected character: the description paragraph,
    /// estate (the panel shows the bare estate word; the game has no header term for it),
    /// relation, and status with the detail the game renders behind its status help icon.
    /// Opened by Enter on a character tile; Escape returns to the tile list. The screen name
    /// is the owning window's tile label so the announcement matches the row just activated.
    /// </summary>
    public sealed class CharacterInfoScreen : Screen
    {
        private readonly CharacterTile _member;
        private readonly string _key;
        private readonly System.Func<string> _screenName;

        public CharacterInfoScreen(CharacterTile member, string key,
            System.Func<string> screenName)
        {
            _member = member;
            _key = key;
            _screenName = screenName;
        }

        public override string Key => _key;
        public override int Layer => 11;

        // Lifetime is parent-managed (a child screen is never polled; the owning window
        // screen's pop disposes it).
        public override bool IsActive() => true;

        public override Message ScreenName => Message.MaybeRaw(_screenName());

        public override void Build(GraphBuilder b)
        {
            var co = _member.CharacterObject;
            b.PushContext("", role: null, positions: true);
            if (!string.IsNullOrEmpty(Readouts.CharacterDescriptionText(co)))
                b.AddLabel(ControlId.Structural("character:info:description"),
                    () => Readouts.CharacterDescriptionText(co));
            b.AddLabel(ControlId.Structural("character:info:estate"),
                () => Readouts.CharacterEstate(co));
            b.AddLabel(ControlId.Structural("character:info:relation"),
                () => Readouts.CharacterRelationPair(co));
            b.AddLabel(ControlId.Structural("character:info:status"),
                () => Readouts.CharacterStatusPair(co));
            b.PopContext();
        }

        // No HelpText override: the rows carry the panel's full content already, and the base
        // null keeps Space honest ("no tooltip") instead of reciting the owning window's
        // what-this-is blurb inside a single character's panel.

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, null,
                _ => ParentScreen.RemoveChild(this));
        }
    }
}
