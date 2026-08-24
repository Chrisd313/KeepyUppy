using KeepyUppy.Shell;
using KeepyUppy.Sim.Level;
using NUnit.Framework;
using UnityEngine;

namespace KeepyUppy.SimTests
{
    /// <summary>
    /// The level-select cursor. Worlds are tabs (GDD 15 wants a grid per world), so
    /// left/right changes world and up/down changes level.
    /// </summary>
    public sealed class LevelSelectModelTests
    {
        static LevelIndex Index() => new LevelIndex
        {
            version = LevelIndex.CurrentVersion,
            worlds = new[]
            {
                new LevelWorld { id = "w1", name = "One", levels = new[] { "w1_01", "w1_02" } },
                new LevelWorld { id = "w2", name = "Two", levels = new[] { "w2_01" } },
            },
        };

        [Test]
        public void StartsOnTheFirstLevelOfTheFirstWorld()
        {
            var model = new LevelSelectModel(Index());

            Assert.AreEqual("w1_01", model.CurrentLevel);
            Assert.AreEqual("w1", model.CurrentWorld.id);
        }

        [Test]
        public void UpAndDownMoveWithinAWorld()
        {
            var model = new LevelSelectModel(Index());

            model.Move(Vector2Int.down);
            Assert.AreEqual("w1_02", model.CurrentLevel);

            model.Move(Vector2Int.up);
            Assert.AreEqual("w1_01", model.CurrentLevel);
        }

        [Test]
        public void TheCursorStopsAtTheEndsRatherThanWrapping()
        {
            // Wrapping a short list is disorienting: the cursor appears to jump.
            var model = new LevelSelectModel(Index());

            model.Move(Vector2Int.up);
            Assert.AreEqual("w1_01", model.CurrentLevel, "already at the top");

            model.Move(Vector2Int.down);
            model.Move(Vector2Int.down);
            Assert.AreEqual("w1_02", model.CurrentLevel, "already at the bottom");
        }

        [Test]
        public void LeftAndRightChangeWorld()
        {
            var model = new LevelSelectModel(Index());

            model.Move(Vector2Int.right);
            Assert.AreEqual("w2", model.CurrentWorld.id);
            Assert.AreEqual("w2_01", model.CurrentLevel);

            model.Move(Vector2Int.left);
            Assert.AreEqual("w1", model.CurrentWorld.id);
        }

        [Test]
        public void ChangingWorldClampsALevelCursorPastTheEnd()
        {
            // World 1 has two levels and world 2 has one. Moving across from the second
            // level must not point past the end of the shorter world.
            var model = new LevelSelectModel(Index());

            model.Move(Vector2Int.down);
            model.Move(Vector2Int.right);

            Assert.AreEqual("w2_01", model.CurrentLevel);
        }

        [Test]
        public void AnEmptyWorldIsSurvivable()
        {
            // Reachable today: the editor can list a world before filling it, and the
            // validator warns rather than forbidding it.
            var index = new LevelIndex
            {
                worlds = new[] { new LevelWorld { id = "w1", name = "One", levels = new string[0] } },
            };

            var model = new LevelSelectModel(index);

            Assert.IsNull(model.CurrentLevel);
            Assert.DoesNotThrow(() => model.Move(Vector2Int.down));
        }

        [Test]
        public void AnEmptyManifestIsSurvivable()
        {
            var model = new LevelSelectModel(new LevelIndex());

            Assert.IsTrue(model.IsEmpty);
            Assert.IsNull(model.CurrentLevel);
            Assert.DoesNotThrow(() => model.Move(Vector2Int.right));
        }
    }
}
