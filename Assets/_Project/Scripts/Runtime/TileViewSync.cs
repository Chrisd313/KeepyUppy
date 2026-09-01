using System.Collections.Generic;
using KeepyUppy.Sim;
using UnityEngine;

namespace KeepyUppy.Runtime
{
    /// <summary>
    /// Keeps the tile quads matching the grid.
    ///
    /// The bootstrap used to build these once at startup, which was fine when the room was
    /// hardcoded. The editor paints tiles, so the view has to follow — it watches
    /// <see cref="TileGrid.Version"/> and rebuilds when it changes.
    ///
    /// A rebuild is 576 quads at worst and only happens on an actual change, so there is no
    /// reason to be cleverer than this.
    /// </summary>
    public sealed class TileViewSync : MonoBehaviour
    {
        public TileGrid Grid;

        readonly List<GameObject> _views = new List<GameObject>();
        int _seenVersion = -1;

        void LateUpdate()
        {
            if (Grid == null || Grid.Version == _seenVersion) return;

            Rebuild();
            _seenVersion = Grid.Version;
        }

        void Rebuild()
        {
            foreach (GameObject view in _views) Destroy(view);
            _views.Clear();

            for (int y = 0; y < TileGrid.Height; y++)
            for (int x = 0; x < TileGrid.Width; x++)
            {
                TileKind kind = Grid[x, y];
                if (kind == TileKind.Empty) continue;

                Color colour = SimPalette.ForTile(kind);

                // Static geometry snaps to the tile grid (level-editor-spec 3); the tile
                // centre sits at (x + 0.5, y + 0.5).
                _views.Add(QuadView.Create(transform, $"tile_{x}_{y}", colour,
                                           new Vector2(x + 0.5f, y + 0.5f), Vector2.one, 0f));
            }
        }
    }
}
