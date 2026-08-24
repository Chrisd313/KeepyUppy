using UnityEngine;

namespace KeepyUppy.Sim
{
    /// <summary>How the gate is shaped. Maps to the schema's exit.shape.</summary>
    public enum ExitShape
    {
        /// <summary>Circular hoop. Diameter is the aperture in both axes.</summary>
        Ring = 0,

        /// <summary>Letterbox. Diameter is the *height*; the slot is wider than it is tall.</summary>
        Slot = 1,
    }

    /// <summary>
    /// The way out (GDD 3, 7.4 LOCKED).
    ///
    /// The gate has a diameter and the balloon has to fit through it. That single rule is
    /// what turns inflation from a modifier into the goal: a floor-level slot can only be
    /// taken Shrivelled, and a ring near the ceiling can only be *reached* by a balloon
    /// buoyant enough to get there. The shape states the level's answer on one screen.
    ///
    /// Nothing here is solid. A balloon too big to fit passes over the gate rather than
    /// bouncing off its rim — see SimWorld.CheckExit for why that is the right call while
    /// inflation is unimplemented, and what has to change when it lands.
    /// </summary>
    public struct ExitGate
    {
        public Vector2 Position;
        public ExitShape Shape;

        /// <summary>Metres. The aperture the balloon has to fit through.</summary>
        public float Diameter;

        /// <summary>A level with no exit is a physics sandbox, which the tests make heavy use of.</summary>
        public bool Present;

        /// <summary>Slot width as a multiple of its height. Wide enough to read as a letterbox.</summary>
        public const float SlotAspect = 3f;

        public static ExitGate None => new ExitGate { Present = false };

        public Vector2 Size => Shape == ExitShape.Slot
            ? new Vector2(Diameter * SlotAspect, Diameter)
            : new Vector2(Diameter, Diameter);

        /// <summary>
        /// Whether a balloon of this radius physically fits the aperture.
        ///
        /// Measured against the *narrow* axis, which for a slot is its height — the whole
        /// point of a letterbox is that width does not help you.
        /// </summary>
        public bool Fits(float balloonRadius) => balloonRadius * 2f <= Diameter;

        /// <summary>
        /// Whether the balloon has gone through.
        ///
        /// GDD 8 is generous to the player, so the accept volume is *larger* than the visual
        /// by <paramref name="bonus"/> — the mirror of the shrink every hazard gets. Judged on
        /// the balloon's centre rather than its edge: clipping the rim is not arriving, and a
        /// centre test is what makes a near miss feel like a near miss.
        /// </summary>
        public bool Accepts(Vector2 balloonCentre, float balloonRadius, float bonus)
        {
            if (!Present || !Fits(balloonRadius)) return false;

            Vector2 half = Size * 0.5f + Vector2.one * Mathf.Max(bonus, 0f);

            return Mathf.Abs(balloonCentre.x - Position.x) <= half.x &&
                   Mathf.Abs(balloonCentre.y - Position.y) <= half.y;
        }

        /// <summary>
        /// The largest band that fits this gate. Schema lint and the level-select icon; the
        /// simulation compares diameters and never reads a band name.
        /// </summary>
        public InflationBand LargestBandThatFits()
        {
            if (Diameter >= InflationBands.RadiusOf(InflationBand.Critical) * 2f) return InflationBand.Critical;
            if (Diameter >= InflationBands.RadiusOf(InflationBand.Full) * 2f) return InflationBand.Full;
            if (Diameter >= InflationBands.RadiusOf(InflationBand.Nominal) * 2f) return InflationBand.Nominal;
            if (Diameter >= InflationBands.RadiusOf(InflationBand.Slack) * 2f) return InflationBand.Slack;

            return InflationBand.Shrivelled;
        }
    }
}
