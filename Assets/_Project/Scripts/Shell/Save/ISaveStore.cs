namespace KeepyUppy.Shell.Save
{
    /// <summary>
    /// Where the save blob lives. Two members, deliberately: this knows about a string, not
    /// about save data — every rule is in <see cref="SaveModel"/>, so the only untested code
    /// in the whole feature is the part that cannot be tested.
    ///
    /// Mirrors ILevelSource / ITuningSource, the persistence idiom this project already reads.
    /// </summary>
    public interface ISaveStore
    {
        /// <summary>The stored blob, or null when nothing has been saved yet.</summary>
        string Read();

        void Write(string json);
    }

    /// <summary>A store in memory. For tests; not a stub.</summary>
    public sealed class MemorySaveStore : ISaveStore
    {
        public string Contents { get; set; }

        public string Read() => Contents;

        public void Write(string json) => Contents = json;
    }
}
