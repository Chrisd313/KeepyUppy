using KeepyUppy.Shell.Save;
using UnityEngine;

namespace KeepyUppy.Runtime
{
    /// <summary>
    /// The save blob in PlayerPrefs — IndexedDB on WebGL, a file everywhere else, so there is
    /// no platform-specific code to write. A JSON file under Application.persistentDataPath
    /// would be nicer to inspect, but that path does not exist on WebGL (GDD 17.2's primary
    /// target), so it would need this backend anyway and become two backends for one blob.
    ///
    /// These are the untested lines ISaveStore exists to isolate. Everything above it — every
    /// rule about what the blob means — is plain C# with EditMode tests.
    /// </summary>
    public sealed class PlayerPrefsSaveStore : ISaveStore
    {
        public const string Key = "keepyuppy.save";

        // "" rather than null: SaveService.Load treats both as "nothing saved yet" via
        // string.IsNullOrEmpty, and a null default through PlayerPrefs' native marshalling is
        // not obviously safe across Unity versions - this is the untestable path every
        // player's first boot takes, so there is no test run to catch it if it ever isn't.
        public string Read() => PlayerPrefs.GetString(Key, "");

        public void Write(string json)
        {
            PlayerPrefs.SetString(Key, json);

            // Explicit, not incidental: on WebGL the IndexedDB flush otherwise happens whenever
            // the page decides to, and a player who closes the tab straight after a clear loses
            // it. This is the one line that makes a write actually mean saved.
            PlayerPrefs.Save();
        }
    }
}
