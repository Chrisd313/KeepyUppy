using KeepyUppy.Sim;
using UnityEngine;

namespace KeepyUppy.Runtime
{
    /// <summary>
    /// The gate (GDD 7.4). Placeholder rendering: an aperture-sized quad.
    ///
    /// Follows the world's gate rather than being built once, so moving it in the editor
    /// shows up immediately — the same contract the tiles and hazards have.
    ///
    /// The fill states whether the balloon currently fits. GDD 7.4 wants the shape of the
    /// exit to state the level's answer visually on one screen, and "you are the wrong size
    /// for this" is the half of that the player most needs to know.
    /// </summary>
    public sealed class ExitView : MonoBehaviour
    {
        public SimWorld World;

        GameObject _view;
        ExitShape _seenShape;
        float _seenDiameter = -1f;

        void LateUpdate()
        {
            if (World == null) return;

            ExitGate gate = World.Exit;

            if (!gate.Present)
            {
                if (_view != null) _view.SetActive(false);
                return;
            }

            if (_view == null || _seenDiameter != gate.Diameter || _seenShape != gate.Shape) Rebuild(gate);

            _view.SetActive(true);
            _view.transform.localPosition = new Vector3(gate.Position.x, gate.Position.y, 0.5f);
        }

        void Rebuild(ExitGate gate)
        {
            if (_view != null) Destroy(_view);

            _view = QuadView.Create(transform, "exit", SimPalette.Exit,
                                    gate.Position, gate.Size, 0.5f);

            _seenDiameter = gate.Diameter;
            _seenShape = gate.Shape;
        }
    }
}
