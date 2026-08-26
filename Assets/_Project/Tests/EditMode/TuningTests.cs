using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using KeepyUppy.Runtime;
using KeepyUppy.Sim;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KeepyUppy.SimTests
{
    /// <summary>
    /// Tuning profiles (CLAUDE.md, level-editor-spec 6). JSON on disk is the source of truth,
    /// so a profile that does not survive a round trip is a profile that silently changes the
    /// feel between sessions.
    /// </summary>
    public sealed class TuningTests
    {
        string _directory;
        FileTuningSource _source;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "keepyuppy-tuning-" + Path.GetRandomFileName());
            _source = new FileTuningSource(_directory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }

        [Test]
        public void Profile_RoundTripsExactly()
        {
            TuningProfile written = TuningProfile.Default;
            written.balloon.gravity = 11.37f;
            written.balloon.dragKy = 2.45f;
            written.balloon.restitutionWall = 0.6125f;
            written.balloon.playerBumpRadiusBonus = 0.185f;
            written.player.runSpeed = 6.75f;
            written.player.jumpHeightTiles = 3.85f;
            written.player.coyoteFrames = 9;
            written.player.deathRestartFrames = 3;

            _source.Save("feel-test", written);

            Assert.IsTrue(_source.TryLoad("feel-test", out TuningProfile read), "profile did not load");

            AssertBits(written.balloon.gravity, read.balloon.gravity, "gravity");
            AssertBits(written.balloon.dragKy, read.balloon.dragKy, "dragKy");
            AssertBits(written.balloon.restitutionWall, read.balloon.restitutionWall, "restitutionWall");
            AssertBits(written.balloon.playerBumpRadiusBonus, read.balloon.playerBumpRadiusBonus, "playerBumpRadiusBonus");
            AssertBits(written.player.runSpeed, read.player.runSpeed, "runSpeed");
            AssertBits(written.player.jumpHeightTiles, read.player.jumpHeightTiles, "jumpHeightTiles");

            Assert.AreEqual(9, read.player.coyoteFrames, "coyoteFrames");
            Assert.AreEqual(3, read.player.deathRestartFrames, "deathRestartFrames");

            // Every remaining field, in one comparison.
            Assert.AreEqual(JsonUtility.ToJson(written), JsonUtility.ToJson(read));
        }

        [Test]
        public void SavedProfile_IsReadableJson()
        {
            // CLAUDE.md: profiles are committed and the diffs are the point.
            _source.Save("readable", TuningProfile.Default);

            string json = File.ReadAllText(Path.Combine(_directory, "readable.json"));

            StringAssert.Contains("\"gravity\"", json);
            StringAssert.Contains("\n", json, "profiles must be pretty-printed, not minified");
        }

        [Test]
        public void MissingProfile_FailsCleanly()
        {
            Assert.IsFalse(_source.TryLoad("nope", out TuningProfile profile));
            Assert.IsNull(profile);
        }

        [Test]
        public void CorruptProfile_FallsBackRatherThanThrowing()
        {
            // A corrupt profile must never take the game down mid-playtest.
            Directory.CreateDirectory(_directory);
            File.WriteAllText(Path.Combine(_directory, "broken.json"), "{ this is not json");

            LogAssert.ignoreFailingMessages = true;
            bool loaded = _source.TryLoad("broken", out TuningProfile profile);
            LogAssert.ignoreFailingMessages = false;

            Assert.IsFalse(loaded);
            Assert.IsNull(profile);
        }

        [Test]
        public void SaveIfAbsent_DoesNotOverwrite()
        {
            TuningProfile mine = TuningProfile.Default;
            mine.balloon.gravity = 3f;
            _source.Save("default", mine);

            Assert.IsFalse(_source.SaveIfAbsent("default", TuningProfile.Default), "should not have written");

            _source.TryLoad("default", out TuningProfile read);
            Assert.AreEqual(3f, read.balloon.gravity, 1e-6f, "existing profile was clobbered");
        }

        [Test]
        public void List_IsSortedAndExtensionless()
        {
            _source.Save("zulu", TuningProfile.Default);
            _source.Save("alpha", TuningProfile.Default);
            _source.Save("mike", TuningProfile.Default);

            CollectionAssert.AreEqual(new[] { "alpha", "mike", "zulu" }, _source.List());
        }

        [Test]
        public void ProfileNames_AreSanitisedIntoFileNames()
        {
            Assert.AreEqual("my-feel", FileTuningSource.Sanitise("my feel"));
            Assert.AreEqual("chris-2026-08-22", FileTuningSource.Sanitise("chris/2026-08-22"));
            Assert.AreEqual("default", FileTuningSource.Sanitise("   "));
            Assert.AreEqual("default", FileTuningSource.Sanitise(null));
        }

        [Test]
        public void CodeDefaults_StillMatchTheGddTables()
        {
            // If a default is retuned, the GDD table must move with it (CLAUDE.md). This test
            // is what makes that a conscious edit rather than a silent drift.
            BalloonTuning b = BalloonTuning.Default;
            Assert.AreEqual(1.0f, b.gravity, 1e-6f, "GDD 6.4 gravity");
            Assert.AreEqual(0.25f, b.dragKy, 1e-6f, "GDD 6.4 drag_ky");
            Assert.AreEqual(0.09f, b.dragKx, 1e-6f, "GDD 6.4 drag_kx");
            Assert.AreEqual(0.75f, b.restitutionWall, 1e-6f, "GDD 6.4 restitution_wall");
            Assert.AreEqual(0.5f, b.restitutionPlayerPassive, 1e-6f, "GDD 4.2 passive restitution");
            Assert.AreEqual(20.0f, b.bumpVelocity, 1e-6f, "GDD 6.4 bump_velocity");
            Assert.AreEqual(0.3f, b.minBounceSpeed, 1e-6f, "GDD 6.4 min_bounce_speed");
            Assert.AreEqual(24f, b.maxSpeed, 1e-6f, "GDD 6.4 max_speed");
            Assert.AreEqual(40f, b.aimOffsetMaxAngle, 1e-6f, "GDD 6.5 aim_offset_max_angle");
            Assert.AreEqual(2, b.fastForwardFactor, "GDD 6.4 fast_forward_factor");

            PlayerTuning p = PlayerTuning.Default;
            Assert.AreEqual(6.0f, p.runSpeed, 1e-6f, "GDD 5.7 run speed");
            Assert.AreEqual(3.2f, p.jumpHeightTiles, 1e-6f, "GDD 5.7 jump height");
            Assert.AreEqual(6, p.coyoteFrames, "GDD 5.7 coyote frames");
            Assert.AreEqual(10, p.jumpBufferFrames, "GDD 5.7 input buffer");
            Assert.AreEqual(40f, p.gravity, 1e-6f, "GDD 5.7 gravity");
            Assert.AreEqual(0.39f, p.jumpCutMultiplier, 1e-6f, "GDD 5.7 jump cut on release");

            // The player is heavy and the balloon is not; 6.4 makes that split the whole feel.
            Assert.Greater(p.gravity, b.gravity * 10f, "the player/balloon gravity contrast collapsed");
        }

        [Test]
        public void ShippedDefaultProfile_CarriesEveryTuningField()
        {
            // JsonUtility.FromJson silently fills an absent field with 0 on load rather than
            // erroring — exitAcceptBonus and fastForwardFactor both did exactly this in the
            // committed default.json before this test existed, and it was invisible: the
            // round-trip test above only exercises Save/Load through a temp directory, never
            // the file that actually ships.
            //
            // A value comparison against TuningProfile.Default cannot catch this: a missing
            // field reads back as 0 and JsonUtility.ToJson then writes that 0 back out under
            // its key, so both sides would carry the same keys and a ToJson-vs-ToJson
            // comparison passes vacuously. And a value comparison is the wrong tool anyway —
            // default.json is *supposed* to differ from code (jumpCutMultiplier is 0.3878 in
            // the file against 0.39 in code), so asserting equality would fail the moment
            // anyone legitimately tuned a value.
            //
            // So this compares only the KEY SET: every field name the code knows about must
            // appear somewhere in the file's raw text. Extra or stale keys are fine — an old
            // field nobody has cleaned up yet is not a bug this test cares about.
            string path = Path.Combine(Application.dataPath, "StreamingAssets", "levels", "_tuning", "default.json");
            Assert.IsTrue(File.Exists(path), $"missing {path}");

            HashSet<string> fileKeys = ExtractKeys(File.ReadAllText(path));
            HashSet<string> defaultKeys = ExtractKeys(JsonUtility.ToJson(TuningProfile.Default));

            List<string> missing = defaultKeys.Except(fileKeys).OrderBy(k => k).ToList();
            Assert.IsEmpty(missing,
                $"default.json is missing field(s), which JsonUtility will silently read back as 0: {string.Join(", ", missing)}");
        }

        static HashSet<string> ExtractKeys(string json)
        {
            var keys = new HashSet<string>();
            foreach (Match match in Regex.Matches(json, "\"([A-Za-z_][A-Za-z0-9_]*)\"\\s*:"))
                keys.Add(match.Groups[1].Value);
            return keys;
        }

        static void AssertBits(float expected, float actual, string field)
            => Assert.AreEqual(SimTestHelpers.Bits(expected), SimTestHelpers.Bits(actual),
                               $"{field} did not survive the round trip exactly");
    }
}
