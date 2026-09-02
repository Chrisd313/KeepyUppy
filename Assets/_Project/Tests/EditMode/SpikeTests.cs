using KeepyUppy.Sim;
using NUnit.Framework;
using UnityEngine;

namespace KeepyUppy.SimTests
{
    /// <summary>
    /// Spikes are lethal on every face, not only the one they point out of.
    ///
    /// This reverses the original rule, where the flat back of a spike was ordinary geometry
    /// you could stand beside or slide down. Direction now chooses which way the sprite will
    /// point and nothing else.
    ///
    /// The numbers below are written the way the sim actually produces them: collision holds
    /// the player a skin's width off a surface it blocks, so a kill volume that stopped at
    /// the tile boundary would never touch anyone. That is what the bleed is for, and it is
    /// why these tests press the player flush against a face rather than overlapping it.
    /// </summary>
    public sealed class SpikeTests
    {
        const int Tx = 10;
        const int Ty = 5;

        /// <summary>The gap SweptAabb leaves between the player and a surface.</summary>
        const float Skin = 1e-4f;

        const float Shrink = 0.15f;   // hazard_pop_radius_shrink
        const float HalfWidth = 0.4f;
        const float HalfHeight = 0.8f;
        const float BalloonRadius = 0.4f;

        static readonly TileKind[] AllSpikes =
        {
            TileKind.SpikeUp, TileKind.SpikeDown, TileKind.SpikeLeft, TileKind.SpikeRight,
        };

        static TileGrid GridWith(TileKind kind)
        {
            var grid = new TileGrid();
            grid.FillBorder();
            grid[Tx, Ty] = kind;
            return grid;
        }

        static bool PlayerDiesAt(TileKind kind, Vector2 centre)
            => Hazards.PlayerOverlapsLethal(GridWith(kind), centre, HalfWidth, HalfHeight, Shrink);

        static bool BalloonDiesAt(TileKind kind, Vector2 centre)
            => Hazards.BalloonOverlapsLethal(GridWith(kind), centre, BalloonRadius, Shrink);

        // ------------------------------------------------------------------ the player

        [Test]
        public void APlayerPressedAgainstASpikesLeftFlank_Dies([ValueSource(nameof(AllSpikes))] TileKind kind)
        {
            var centre = new Vector2(Tx - HalfWidth - Skin, Ty + 0.5f);
            Assert.IsTrue(PlayerDiesAt(kind, centre), $"{kind} let the player stand against its side");
        }

        [Test]
        public void APlayerPressedAgainstASpikesRightFlank_Dies([ValueSource(nameof(AllSpikes))] TileKind kind)
        {
            var centre = new Vector2(Tx + 1f + HalfWidth + Skin, Ty + 0.5f);
            Assert.IsTrue(PlayerDiesAt(kind, centre), $"{kind} let the player stand against its side");
        }

        [Test]
        public void APlayerStandingOnASpike_Dies([ValueSource(nameof(AllSpikes))] TileKind kind)
        {
            var centre = new Vector2(Tx + 0.5f, Ty + 1f + HalfHeight + Skin);
            Assert.IsTrue(PlayerDiesAt(kind, centre), $"{kind} was safe to stand on");
        }

        [Test]
        public void APlayerUnderASpike_Dies([ValueSource(nameof(AllSpikes))] TileKind kind)
        {
            var centre = new Vector2(Tx + 0.5f, Ty - HalfHeight - Skin);
            Assert.IsTrue(PlayerDiesAt(kind, centre), $"{kind} was safe to stand under");
        }

        [Test]
        public void APlayerAClearTileAway_Lives([ValueSource(nameof(AllSpikes))] TileKind kind)
        {
            // GDD 8 stays intact in the other direction: the volume bleeds through the
            // surface, it does not reach across open space.
            var centre = new Vector2(Tx - 1.5f, Ty + 0.5f);
            Assert.IsFalse(PlayerDiesAt(kind, centre), $"{kind} killed from a tile away");
        }

        // ----------------------------------------------------------------- the balloon

        [Test]
        public void ABalloonTouchingASpikesFlatBack_Pops()
        {
            // The bottom of a SpikeUp - ordinary geometry under the old rule.
            Assert.IsTrue(BalloonDiesAt(TileKind.SpikeUp, new Vector2(Tx + 0.5f, Ty - 0.2f)));
        }

        [Test]
        public void ABalloonTouchingASpikesFlank_Pops([ValueSource(nameof(AllSpikes))] TileKind kind)
        {
            Assert.IsTrue(BalloonDiesAt(kind, new Vector2(Tx - 0.2f, Ty + 0.5f)),
                          $"{kind} let the balloon graze its side");
        }

        [Test]
        public void ABalloonNearButNotTouching_Survives([ValueSource(nameof(AllSpikes))] TileKind kind)
        {
            // The shrink still buys the balloon its GDD 8 margin: the pop volume must stay
            // smaller than the visual even now that it covers the whole tile.
            Assert.IsFalse(BalloonDiesAt(kind, new Vector2(Tx + 0.5f, Ty - 0.5f)),
                           $"{kind} popped a balloon that had not reached it");
        }

        // ------------------------------------------------------------------- kill box

        [Test]
        public void EverySpikeKind_HasTheSameKillBox()
        {
            Hazards.KillBox(TileKind.SpikeUp, Tx, Ty, Shrink,
                            out float ax0, out float ay0, out float ax1, out float ay1);

            foreach (TileKind kind in AllSpikes)
            {
                Hazards.KillBox(kind, Tx, Ty, Shrink,
                                out float bx0, out float by0, out float bx1, out float by1);

                Assert.AreEqual(ax0, bx0, 1e-5f, $"{kind} x0");
                Assert.AreEqual(ay0, by0, 1e-5f, $"{kind} y0");
                Assert.AreEqual(ax1, bx1, 1e-5f, $"{kind} x1");
                Assert.AreEqual(ay1, by1, 1e-5f, $"{kind} y1");
            }
        }

        [Test]
        public void ASpikesKillBox_CoversTheWholeTileAndBleedsOutOfEveryFace()
        {
            Hazards.KillBox(TileKind.SpikeUp, Tx, Ty, Shrink,
                            out float bx0, out float by0, out float bx1, out float by1);

            Assert.Less(bx0, Tx, "left face");
            Assert.Less(by0, Ty, "bottom face");
            Assert.Greater(bx1, Tx + 1f, "right face");
            Assert.Greater(by1, Ty + 1f, "top face");
        }

        [Test]
        public void NonSpikeHazards_AreStillInsetByTheShrink(
            [Values(TileKind.Rough, TileKind.PlayerLethal)] TileKind kind)
        {
            Hazards.KillBox(kind, Tx, Ty, Shrink,
                            out float bx0, out float by0, out float bx1, out float by1);

            Assert.AreEqual(Tx + Shrink, bx0, 1e-5f);
            Assert.AreEqual(Ty + Shrink, by0, 1e-5f);
            Assert.AreEqual(Tx + 1f - Shrink, bx1, 1e-5f);
            Assert.AreEqual(Ty + 1f - Shrink, by1, 1e-5f);
        }
    }
}
