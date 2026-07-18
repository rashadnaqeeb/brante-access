using WrathAccess.Settings;

namespace WrathAccess.Exploration.Overlays
{
    /// <summary>
    /// Base for the sound-producing systems (sonar, wall tones, fog/object cues). Per-system volume is
    /// a SHARED setting living on the Audio tab (<c>audio.volumes.&lt;key&gt;</c> — one value for all
    /// overlays, per the settings redesign); effective loudness is the global master times it.
    /// Subclasses add their tunables in <see cref="RegisterAudioSettings"/>.
    /// </summary>
    internal abstract class AudioSystem : OverlaySystem
    {
        public override void RegisterSettings(CategorySetting cat) => RegisterAudioSettings(cat);

        protected virtual void RegisterAudioSettings(CategorySetting cat) { }

        /// <summary>This system's shared volume (Audio tab) as a 0..1 fraction.</summary>
        protected float Volume =>
            (ModSettings.GetSetting<IntSetting>("audio.volumes." + Key)?.Get() ?? 100) / 100f;

        /// <summary>Per-system volume scaled by the global master — what the engine should actually use.</summary>
        protected float EffectiveVolume => Volume * OverlayAudio.Master;
    }
}
