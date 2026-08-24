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

        /// <summary>5 — click adds a waypoint, drag moves one, right-click deletes.</summary>
        Path = 5,

        /// <summary>6 — phase 4.</summary>
        Link = 6,

        /// <summary>
        /// P — click drops the palette entity where the cursor is.
        ///
        /// Numbered past the reserved phase-4 tools rather than taking 4, so the digit
        /// bindings still mean what spec 4 says they mean when zones and links arrive.
        /// </summary>
        Place = 7,
    }

    public enum EditorMode
    {
        Edit = 0,
        Play = 1,
    }
}
