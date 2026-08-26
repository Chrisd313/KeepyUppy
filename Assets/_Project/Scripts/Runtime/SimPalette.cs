using UnityEngine;

namespace KeepyUppy.Runtime
{
    /// <summary>
    /// Placeholder palette obeying the reserved-colour rule (GDD 13.1) from day one:
    /// muted background, dark silhouette geometry, one colour reserved for hazards and one
    /// for the balloon. The balloon's colour is never used for anything else, ever.
    /// </summary>
    public static class SimPalette
    {
        public static readonly Color Background = new Color32(0x2B, 0x30, 0x3C, 0xFF);
        public static readonly Color Solid      = new Color32(0x14, 0x17, 0x1F, 0xFF);

        /// <summary>Reserved: hazards only.</summary>
        public static readonly Color Hazard     = new Color32(0xE0, 0x3A, 0x3A, 0xFF);

        /// <summary>Reserved: the balloon only. Highest contrast object on screen.</summary>
        public static readonly Color Balloon    = new Color32(0xFF, 0xD8, 0x4D, 0xFF);

        /// <summary>
        /// Reserved: the exit only (GDD 13.1). The one thing on screen the player is trying
        /// to reach must never share a colour with something they are trying to avoid.
        /// </summary>
        public static readonly Color Exit       = new Color32(0x5A, 0xD8, 0x8A, 0x88);

        /// <summary>Not reserved. Deliberately quieter than the balloon.</summary>
        public static readonly Color Player     = new Color32(0x7F, 0x8C, 0xA8, 0xFF);

        /// <summary>UI text. Not a reserved colour — deliberately not the balloon's.</summary>
        public static readonly Color Text    = new Color32(0xF2, 0xF2, 0xF2, 0xFF);
        public static readonly Color TextDim = new Color32(0x7F, 0x8C, 0xA8, 0xFF);
    }
}
