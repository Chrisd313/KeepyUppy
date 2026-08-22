using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace KeepyUppy.SimTests
{
    /// <summary>
    /// The determinism rules in GDD 6.1 are architectural, not behavioural — a single
    /// Time.deltaTime added in a hurry breaks the whole genre contract and no physics
    /// assertion would catch it. So they are asserted against the source itself.
    /// </summary>
    public sealed class SimContractTests
    {
        static readonly string[] BannedInSim =
        {
            "Time.deltaTime",
            "Time.time",
            "Time.fixedTime",
            "Time.frameCount",
            "Time.realtimeSinceStartup",
            "UnityEngine.Random",
            "System.Random",
            "DateTime",
            "Stopwatch",

            // The sim is handed a TuningProfile; it never reads one off a disk. File IO is a
            // platform concern (StreamingAssets is a URL on web, an APK entry on Android) and
            // belongs behind ITuningSource / ILevelSource.
            "System.IO",
            "File.ReadAll",
            "File.WriteAll",
        };

        /// <summary>
        /// Raw device APIs. The GDD 5.5 abstraction exists so that no code below the input
        /// layer can discover which device produced an intent.
        /// </summary>
        static readonly string[] BannedOutsideDevices =
        {
            "UnityEngine.InputSystem",
            "Keyboard.current",
            "Gamepad.current",
            "Touchscreen.current",
        };

        static string SimDirectory => Path.Combine(Application.dataPath, "_Project", "Scripts", "Sim");
        static string RuntimeDirectory => Path.Combine(Application.dataPath, "_Project", "Scripts", "Runtime");
        static string InputDirectory => Path.Combine(Application.dataPath, "_Project", "Scripts", "Input");
        static string DevicesDirectory => Path.Combine(InputDirectory, "Devices");

        static IEnumerable<string> CsFiles(string directory)
            => Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories);

        [Test]
        public void SimSources_ContainNoWallClockOrRandom()
        {
            Assert.IsTrue(Directory.Exists(SimDirectory), $"missing {SimDirectory}");

            foreach (string file in CsFiles(SimDirectory))
            {
                string text = File.ReadAllText(file);
                foreach (string banned in BannedInSim)
                {
                    Assert.IsFalse(text.Contains(banned),
                        $"{Path.GetFileName(file)} uses '{banned}'. " +
                        "Sim code reads the fixed-step counter and SimClock.Dt only (GDD 6.1).");
                }
            }
        }

        [Test]
        public void RuntimeSources_DoNotReferenceUnityEditor()
        {
            foreach (string file in CsFiles(RuntimeDirectory))
            {
                Assert.IsFalse(File.ReadAllText(file).Contains("using UnityEditor"),
                    $"{Path.GetFileName(file)} references UnityEditor and would not survive a build.");
            }
        }

        [Test]
        public void SimSources_DoNotReferenceUnityEditor()
        {
            foreach (string file in CsFiles(SimDirectory))
            {
                Assert.IsFalse(File.ReadAllText(file).Contains("using UnityEditor"),
                    $"{Path.GetFileName(file)} references UnityEditor.");
            }
        }

        [Test]
        public void OnlyTheDevicesFolder_TouchesRawInputApis()
        {
            // LOCKED GDD 5.5: the input layer emits intents, never raw keys. Exactly one
            // folder is allowed to know a keyboard exists.
            string devices = Path.GetFullPath(DevicesDirectory);

            foreach (string file in CsFiles(SimDirectory))
                AssertNoRawInput(file);

            foreach (string file in CsFiles(InputDirectory))
            {
                if (Path.GetFullPath(file).StartsWith(devices)) continue;
                AssertNoRawInput(file);
            }
        }

        [Test]
        public void SimSources_DoNotReadInputDirectly()
        {
            // The sim consumes IInputSource. If it ever names a device type, the abstraction
            // has been bypassed.
            foreach (string file in CsFiles(SimDirectory))
                AssertNoRawInput(file);
        }

        static void AssertNoRawInput(string file)
        {
            string text = File.ReadAllText(file);
            foreach (string banned in BannedOutsideDevices)
            {
                Assert.IsFalse(text.Contains(banned),
                    $"{Path.GetFileName(file)} uses '{banned}'. Only Input/Devices/ may name a device (GDD 5.5).");
            }
        }
    }
}
