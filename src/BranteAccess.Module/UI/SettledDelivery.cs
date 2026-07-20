using UnityEngine;

namespace BranteAccess.Module.UI
{
    /// <summary>
    /// Debounce for panels that build over several frames (prefab placeholder text one frame
    /// before Start localizes, stat rows populating while values animate): a changed content
    /// signature is delivered only once it has held still for the settle window, so
    /// placeholder frames and half-filled states are never spoken. When the content merely
    /// GREW, only the new tail is returned (no repeat of what the player already heard) -
    /// callers keep focus put for growth and re-seat for a swap.
    /// </summary>
    internal sealed class SettledDelivery
    {
        private readonly float _settleSeconds;
        private string _spokenSig;
        private string _pendingSig;
        private float _pendingSince;

        public SettledDelivery(float settleSeconds)
        {
            _settleSeconds = settleSeconds;
        }

        public void Reset()
        {
            _spokenSig = null;
            _pendingSig = null;
        }

        /// <summary>
        /// Feed the current content signature every frame. Returns the settled delivery text
        /// exactly once per change (possibly empty for pure-whitespace growth), null otherwise.
        /// A delivery is marked spoken even if the caller chooses not to speak it.
        /// </summary>
        public string Poll(string sig, out bool grew)
        {
            grew = false;
            if (sig.Length == 0 || sig == _spokenSig)
            {
                _pendingSig = null;
                return null;
            }
            if (sig != _pendingSig)
            {
                _pendingSig = sig;
                _pendingSince = Time.unscaledTime;
                return null;
            }
            if (Time.unscaledTime - _pendingSince < _settleSeconds) return null;
            grew = _spokenSig != null && sig.StartsWith(_spokenSig);
            var delivery = grew
                ? sig.Substring(_spokenSig.Length).TrimStart(',', ' ')
                : sig;
            _spokenSig = sig;
            _pendingSig = null;
            return delivery;
        }
    }
}
