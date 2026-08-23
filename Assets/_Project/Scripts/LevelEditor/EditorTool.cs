namespace KeepyUppy.LevelEditing
{
    /// <summary>
    /// Tools from level-editor-spec 4. Only the phase-1 set is implemented; zone, path and
    /// link keep their numbers reserved so the bindings never shuffle under the author's
    /// fingers when phases 3 and 4 land.
    /// </summary>
    public enum EditorTool
    {
        /// <summary>1 — click-drag paints, right-drag erases.</summary>
        Paint = 1,

        /// <summary>2 — drag a rectangle of the selected tile.</summary>
        RectFill = 2,

        /// <summary>3 — click an entity to select, drag to move.</summary>
        Select = 3,

        /// <summary>4 — phase 4.</summary>
        Zone = 4,

        /// <summary>5 — phase 4.</summary>
        Path = 5,

        /// <summary>6 — phase 4.</summary>
        Link = 6,
    }

    public enum EditorMode
    {
        Edit = 0,
        Play = 1,
    }
}
