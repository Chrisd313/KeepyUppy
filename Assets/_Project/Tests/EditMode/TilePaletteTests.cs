using KeepyUppy.Runtime;
using KeepyUppy.Sim;
using NUnit.Framework;
using UnityEngine;

namespace KeepyUppy.SimTests
{
    /// <summary>
    /// GDD 13.1's reserved-colour rule, applied to static geometry. The tile view and the
    /// panel-debris view both colour tiles, and both used to inline the same
    /// "Solid or else hazard-red" expression - which is wrong the moment a tile is neither.
    /// </summary>
    public sealed class TilePaletteTests
    {
        [Test]
        public void Rubber_IsNotDrawnInTheReservedHazardColour()
        {
            Assert.AreNotEqual(SimPalette.Hazard, SimPalette.ForTile(TileKind.Rubber),
                "rubber is harmless; hazard red is reserved for things that kill");
        }

        [Test]
        public void Rubber_IsNotDrawnAsPlainGeometryEither()
        {
            Assert.AreNotEqual(SimPalette.Solid, SimPalette.ForTile(TileKind.Rubber),
                "a tile that behaves differently must read differently");
        }

        [Test]
        public void Rubber_NeverBorrowsAReservedColour()
        {
            Color rubber = SimPalette.ForTile(TileKind.Rubber);

            Assert.AreNotEqual(SimPalette.Balloon, rubber, "the balloon's colour is its own");
            Assert.AreNotEqual(SimPalette.Exit, rubber, "the exit's colour is its own");
        }

        [Test]
        public void LethalTiles_StillDrawInTheHazardColour(
            [Values(TileKind.Rough, TileKind.SpikeUp, TileKind.SpikeDown,
                    TileKind.SpikeLeft, TileKind.SpikeRight, TileKind.PlayerLethal)] TileKind kind)
        {
            Assert.AreEqual(SimPalette.Hazard, SimPalette.ForTile(kind));
        }

        [Test]
        public void SolidGeometry_StillDrawsAsGeometry()
        {
            Assert.AreEqual(SimPalette.Solid, SimPalette.ForTile(TileKind.Solid));
        }
    }
}
