using Godot;

namespace I76.Game;

/// <summary>Lives under the root, outside the scene, so it leaves the tree only when the game quits: then the force-feedback wheel is stopped and released (<see cref="DirectInputForce"/>).</summary>
public partial class WheelForceKeeper : Node
{
    public override void _ExitTree() => DirectInputForce.Close();
}
