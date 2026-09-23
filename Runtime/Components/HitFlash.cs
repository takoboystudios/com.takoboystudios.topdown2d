using System.Collections.Generic;
using UnityEngine;

namespace TakoBoyStudios.TopDown2D
{
    /// <summary>
    /// Flashes a body into the hurt palette when it takes a hit, and does nothing else.
    ///
    /// The reason this is a material effect rather than an animation is that a damage clip has to
    /// interrupt something. On an enemy mid-charge or mid-volley that reads as a stagger it did not
    /// actually take, and on a boss it would be unusable. Recolouring costs the enemy nothing: it
    /// keeps thinking, keeps moving, and keeps playing whatever clip it was on.
    ///
    /// Every renderer shares one material and gets its own flash value through a
    /// MaterialPropertyBlock, so a screen full of enemies being shot allocates nothing and does not
    /// break batching.
    ///
    /// A plain class, driven by whoever owns it, in keeping with the manual-tick entities.
    /// </summary>
    public sealed class HitFlash
    {
        /// <summary>
        /// Seconds each flash frame holds. 60ms, matching grim@damaged, whose json is five frames at
        /// 60 each. Matching it is the point: a hit enemy and a hit player should strobe together.
        /// </summary>
        public float FrameDuration = 0.06f;

        /// <summary>
        /// How many frames the flash runs for. Four, because Grim's clip is four recoloured frames
        /// and a fifth that is already back to normal.
        /// </summary>
        public int FrameCount = 4;

        static readonly int FlashPaletteId = Shader.PropertyToID("_FlashPalette");
        static readonly int FlashOverlayId = Shader.PropertyToID("_FlashOverlay");
        static readonly int SwapOnId = Shader.PropertyToID("_SwapOn");
        static readonly int SwapFromId = Shader.PropertyToID("_SwapFrom");
        static readonly int SwapToId = Shader.PropertyToID("_SwapTo");
        static readonly int LitOnId = Shader.PropertyToID("_LitOn");
        static readonly int LitFromId = Shader.PropertyToID("_LitFrom");
        static readonly int LitToId = Shader.PropertyToID("_LitTo");

        /// <summary>
        /// A soft white overlay instead of Grim's amber/red hurt palette. A barrel or crate is not
        /// flesh, so the red read as wrong (Tom, 2026-09-01): this blends a light white over the
        /// sprite, keeping its own detail, and fades out fast rather than holding. Set before Bind.
        /// </summary>
        public bool WhiteFlash;

        /// <summary>How much white the overlay blends in at its peak, 0 to 1. Light, so the sprite still reads.</summary>
        public float OverlayPeak = 0.55f;

        /// <summary>How long the overlay takes to fade from peak to nothing, in seconds. Short: it is a flash.</summary>
        public float OverlayDuration = 0.1f;

        static Material _sharedMaterial;
        static MaterialPropertyBlock _block;

        readonly List<SpriteRenderer> _renderers = new();
        float _elapsed;
        bool _flashing;
        float _written = -1f;
        float _writtenOverlay = -1f;

        bool _swapWritten;
        bool _swapOn;
        Color _swapFrom;
        Color _swapTo;

        public bool IsFlashing => _flashing;

        /// <summary>
        /// Collects the body's sprites and puts them on the flash material. Safe to call again; a
        /// pooled body that comes back to life re-collects, because its renderers may have changed.
        /// </summary>
        public void Bind(Transform visualRoot)
        {
            _renderers.Clear();
            _flashing = false;
            _written = -1f;

            if (visualRoot == null)
                return;

            visualRoot.GetComponentsInChildren(true, _renderers);

            Material material = SharedMaterial();
            if (material == null)
                return;

            for (int i = 0; i < _renderers.Count; i++)
            {
                if (_renderers[i] != null)
                    _renderers[i].sharedMaterial = material;
            }

            // A body rebound mid-flash would otherwise keep the last value it was left on.
            _written = -1f;
            _writtenOverlay = -1f;
            _swapWritten = false;
            SetSwap(false, default, default);
            if (WhiteFlash)
                WriteOverlay(0f);
            else
                Write(0f);
        }

        /// <summary>Starts the flash. Re-hitting restarts it rather than stacking.</summary>
        public void Play()
        {
            _elapsed = 0f;
            _flashing = true;

            if (WhiteFlash)
                WriteOverlay(OverlayPeak);
            else
                Write(1f);
        }

        public void Tick(float deltaTime)
        {
            if (!_flashing)
                return;

            _elapsed += deltaTime;

            if (WhiteFlash)
            {
                // A light white overlay that fades from peak to nothing over a short window, so it
                // reads as a quick flash rather than the sprite being held white.
                float duration = Mathf.Max(0.0001f, OverlayDuration);
                if (_elapsed >= duration)
                {
                    _flashing = false;
                    WriteOverlay(0f);
                    return;
                }

                WriteOverlay(OverlayPeak * (1f - _elapsed / duration));
                return;
            }

            float frameDuration = Mathf.Max(0.0001f, FrameDuration);
            int frame = Mathf.FloorToInt(_elapsed / frameDuration);

            if (frame >= Mathf.Max(1, FrameCount))
            {
                _flashing = false;
                Write(0f);
                return;
            }

            // Alternate the two palettes every frame. That alternation is the whole effect: a single
            // held colour reads as a tint, two swapping colours read as a hit.
            Write(frame % 2 == 0 ? 1f : 2f);
        }

        /// <summary>
        /// Recolours the body's main colour to <paramref name="to"/>, or stops. Called every frame by
        /// whoever owns the flash with the status colour for this instant; it only touches the renderers
        /// when the answer changed, so a burning enemy writes four times a loop, not sixty times a second.
        /// </summary>
        public void SetSwap(bool on, Color from, Color to)
        {
            if (_renderers.Count == 0)
                return;

            if (_swapWritten && on == _swapOn && (!on || (from == _swapFrom && to == _swapTo)))
                return;

            _swapWritten = true;
            _swapOn = on;
            _swapFrom = from;
            _swapTo = to;

            _block ??= new MaterialPropertyBlock();

            for (int i = 0; i < _renderers.Count; i++)
            {
                SpriteRenderer renderer = _renderers[i];
                if (renderer == null)
                    continue;

                renderer.GetPropertyBlock(_block);
                _block.SetFloat(SwapOnId, on ? 1f : 0f);
                _block.SetColor(SwapFromId, from);
                _block.SetColor(SwapToId, to);
                renderer.SetPropertyBlock(_block);
            }
        }

        void Write(float palette)
        {
            if (_renderers.Count == 0)
                return;

            // Every renderer on the body shares a value, so only write when it actually changes.
            if (Mathf.Approximately(_written, palette))
                return;
            _written = palette;

            _block ??= new MaterialPropertyBlock();

            for (int i = 0; i < _renderers.Count; i++)
            {
                SpriteRenderer renderer = _renderers[i];
                if (renderer == null)
                    continue;

                renderer.GetPropertyBlock(_block);
                _block.SetFloat(FlashPaletteId, palette);
                renderer.SetPropertyBlock(_block);
            }
        }

        void WriteOverlay(float amount)
        {
            if (_renderers.Count == 0)
                return;

            if (Mathf.Approximately(_writtenOverlay, amount))
                return;
            _writtenOverlay = amount;

            _block ??= new MaterialPropertyBlock();

            for (int i = 0; i < _renderers.Count; i++)
            {
                SpriteRenderer renderer = _renderers[i];
                if (renderer == null)
                    continue;

                renderer.GetPropertyBlock(_block);
                _block.SetFloat(FlashOverlayId, amount);
                renderer.SetPropertyBlock(_block);
            }
        }

        /// <summary>
        /// Puts one renderer on the shared flash material, for a body that does not flash but wants
        /// one of the material's swaps. Nothing else about the renderer changes.
        /// </summary>
        public static void UseSharedMaterial(SpriteRenderer renderer)
        {
            Material material = SharedMaterial();
            if (renderer != null && material != null)
                renderer.sharedMaterial = material;
        }

        /// <summary>
        /// The lit swap on one renderer: every pixel exactly <paramref name="from"/> becomes
        /// <paramref name="to"/> while on. Separate from the status swap so the two never overwrite each
        /// other. The caller decides when it changes; this writes the block every call, so call it on
        /// a change, not every frame.
        /// </summary>
        public static void SetLit(SpriteRenderer renderer, bool on, Color from, Color to)
        {
            if (renderer == null)
                return;

            _block ??= new MaterialPropertyBlock();
            renderer.GetPropertyBlock(_block);
            _block.SetFloat(LitOnId, on ? 1f : 0f);
            _block.SetColor(LitFromId, from);
            _block.SetColor(LitToId, to);
            renderer.SetPropertyBlock(_block);
        }

        /// <summary>
        /// The one material every flashing body uses. Built from the shader rather than an authored
        /// asset so no prefab has to be touched to gain the effect.
        /// </summary>
        static Material SharedMaterial()
        {
            if (_sharedMaterial != null)
                return _sharedMaterial;

            Shader shader = Shader.Find("HellWilds/SpriteHitFlash");
            if (shader == null)
            {
                Debug.LogWarning("[HitFlash] Shader 'HellWilds/SpriteHitFlash' not found. No damage flash.");
                return null;
            }

            _sharedMaterial = new Material(shader) { name = "SpriteHitFlash (shared)" };
            return _sharedMaterial;
        }
    }
}
