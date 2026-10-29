using System;

namespace KeepyUppy.Sim.Level
{
    /// <summary>
    /// One level, exactly as it appears on disk (level-editor-spec 3).
    ///
    /// Field names are snake_case because JsonUtility maps JSON keys to member names
    /// literally, and the spec defines the on-disk format. The format wins over C#
    /// convention here — this file is the contract, not an implementation detail.
    ///
    /// The whole schema is modelled, including entities, triggers and links that nothing
    /// reads yet. JsonUtility silently discards fields it has no member for, so a partial
    /// model would mean the editor deleting every entity in a file the moment it saved one.
    /// "The moment they diverge, the editor stops being trustworthy."
    /// </summary>
    [Serializable]
    public sealed class LevelData
    {
        public string id = "w1_01";
        public string name = "Untitled";
        public int world = 1;
        public int index = 1;

        public LevelSpawn spawn = new LevelSpawn();
        public LevelExit exit = new LevelExit();

        /// <summary>
        /// Static geometry, one string per row, row 0 is the TOP of the room.
        /// Chars: '.' empty  '#' solid  '=' oneway  '~' rough  '^' spike-up
        ///        'v' spike-down  '&lt;' '&gt;' spike-side  'x' player-lethal.
        /// Plain rows, not run-length encoded: the readable diff is the point.
        /// </summary>
        public string[] tiles = new string[0];

        public LevelEntity[] entities = new LevelEntity[0];
        public LevelZone[] zones = new LevelZone[0];
        public LevelPanel[] panels = new LevelPanel[0];
        public LevelLink[] links = new LevelLink[0];
        public LevelTrigger[] triggers = new LevelTrigger[0];

        public LevelMeta meta = new LevelMeta();
    }

    [Serializable]
    public sealed class LevelSpawn
    {
        /// <summary>[x, y] in tiles. Entities snap to half-tile.</summary>
        public float[] player = { 4f, 2f };
        public float[] balloon = { 6f, 13f };

        /// <summary>shrivelled | slack | nominal | full | critical</summary>
        public string balloon_inflation = "nominal";
    }

    [Serializable]
    public sealed class LevelExit
    {
        public float[] pos = { 29f, 4f };

        /// <summary>ring | slot</summary>
        public string shape = "ring";

        /// <summary>Metres. Gates the required inflation band.</summary>
        public float diameter = 1.6f;

        /// <summary>Derived from diameter; stored for lint and the level-select icon.</summary>
        public string required_band = "nominal";
    }

    [Serializable]
    public sealed class LevelEntity
    {
        public string id = "e01";
        public string type = "";
        public float[] pos = { 0f, 0f };
        public EntityParams @params = new EntityParams();

        /// <summary>
        /// Waypoints as a FLAT list of x,y pairs: [12,9, 20,9] is two waypoints.
        /// JsonUtility cannot serialise nested arrays, so the spec's [[12,9],[20,9]] is
        /// flattened. Documented in level-editor-spec 3.
        /// </summary>
        public float[] path = new float[0];

        /// <summary>pingpong | loop | once</summary>
        public string path_mode = "pingpong";

        /// <summary>0..1 offset so periodic traps can be desynced. Easy to forget, essential.</summary>
        public float phase;

        /// <summary>active | dormant</summary>
        public string start_state = "active";
    }

    /// <summary>
    /// Every entity parameter in one struct.
    ///
    /// JsonUtility has no free-form maps, so per-type params cannot be a dictionary. A typed
    /// superset round-trips losslessly at the cost of writing unset fields as zero. If this
    /// grows unwieldy, that is the signal to bring in Newtonsoft.
    /// </summary>
    [Serializable]
    public sealed class EntityParams
    {
        public float radius;
        public float speed;
        public float battery;
        public float aggro_radius;
        public float patience;
        public float power;
        public float angle;
        public float length;
        public float delay;
        public float duration;
    }

    /// <summary>
    /// A rectangle of authored tiles a link can take away (GDD 9.3's falling floor).
    ///
    /// The tiles themselves live in <see cref="LevelData.tiles"/> like any others; a panel
    /// only says which of them can be switched. start_state "off" hides them at load, which
    /// is how an *appearing* platform is authored — the same object, inverted.
    /// </summary>
    [Serializable]
    public sealed class LevelPanel
    {
        public string id = "p01";

        /// <summary>[x, y, w, h] in tiles. Snapped to whole tiles, as a zone rect is.</summary>
        public float[] rect = { 0f, 0f, 1f, 1f };

        /// <summary>on | off — whether its tiles are present at the start of the run.</summary>
        public string start_state = "on";

        /// <summary>
        /// down | up | left | right — which way the tiles go when the panel switches off,
        /// and which way they slide back in from when it switches on. Presentation only:
        /// collision ends and returns on the step the panel switches, whatever this says.
        /// "down" is the default because it is what every level authored before the field
        /// existed did.
        /// </summary>
        public string exit_dir = "down";

        /// <summary>
        /// Waypoints as a FLAT list of x,y pairs of tile OFFSETS from <see cref="rect"/>:
        /// [0,3, 0,0] is "up three, then home". Offsets rather than absolute positions so
        /// moving the panel in the editor cannot invalidate its path.
        ///
        /// A panel with fewer than two waypoints is the mask panel that has always existed.
        /// Flattened because JsonUtility cannot serialise nested arrays (see LevelEntity.path).
        /// </summary>
        public float[] path = new float[0];

        /// <summary>pingpong | loop | once</summary>
        public string path_mode = "once";

        /// <summary>Tiles per second along the path.</summary>
        public float speed;

        /// <summary>Seconds paused at EVERY waypoint, both ends included.</summary>
        public float hold;

        /// <summary>0..1 offset so two identical panels can be desynced, as an entity's is.</summary>
        public float phase;
    }

    [Serializable]
    public sealed class LevelZone
    {
        public string id = "z01";

        /// <summary>air | heat | cold | kill | static | light</summary>
        public string type = "air";

        /// <summary>[x, y, w, h] in tiles. Zone rects snap to tile.</summary>
        public float[] rect = { 0f, 0f, 1f, 1f };

        public ZoneParams @params = new ZoneParams();

        /// <summary>on | off</summary>
        public string start_state = "on";
    }

    [Serializable]
    public sealed class ZoneParams
    {
        /// <summary>v_air for air zones (GDD 6.2). Fans, vents and thermals are all this.</summary>
        public float vx;
        public float vy;

        /// <summary>Inflation units per second for heat and cold zones.</summary>
        public float rate;
    }

    [Serializable]
    public sealed class LevelLink
    {
        public string from = "";
        public string[] to = new string[0];

        /// <summary>toggle | on | off | arm | fire</summary>
        public string action = "toggle";

        public float delay;
    }

    [Serializable]
    public sealed class LevelTrigger
    {
        public string id = "trigger_a";

        /// <summary>
        /// player_region | balloon_region | timer | lever | balloon_touches |
        /// player_grounded | on_first_entry
        /// </summary>
        public string type = "player_region";

        public float[] rect = { 0f, 0f, 1f, 1f };

        /// <summary>once:true player_region triggers are the betrayal-trap workhorse.</summary>
        public bool once = true;

        /// <summary>
        /// Drawn to the player, not only to the author. Defaults false, which is the GDD 9.3
        /// workhorse: an unmarked region that fires when the player commits.
        /// </summary>
        public bool visible;

        /// <summary>
        /// Timer triggers only: seconds from the start of the run. Converted to whole fixed
        /// steps at load (SimClock.StepsIn), so nothing multiplies seconds during a run.
        /// Ignored by the region types.
        /// </summary>
        public float seconds;
    }

    [Serializable]
    public sealed class LevelMeta
    {
        /// <summary>Par for the touch-count medal (GDD 15). Set from metrics, never guesswork.</summary>
        public int par_touches;

        public string author_note = "";

        /// <summary>Profile name under levels/_tuning/. Lets a world prototype its own feel.</summary>
        public string tuning_profile = "default";
    }
}
