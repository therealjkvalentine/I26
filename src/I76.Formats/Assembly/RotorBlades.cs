namespace I76.Formats.Assembly;

/// <summary>
/// A helicopter's rotor parts: flat discs textured with the animated blade flipbook (BLADE101.., the tail's BLADE201..). The
/// exterior (detail 1) has the main and the tail rotor; the Cobra's interior, which the cockpit draws (detail 5), has the main
/// rotor again at the same place (UA51ROTM over UA11ROTM), what the original shows overhead from its seat. The Huey's has none.
/// </summary>
public static class RotorBlades
{
    /// <summary>Whether a part is a rotor: every face of it wears a BLADE texture.</summary>
    public static bool Is(ModelPart p) =>
        p.Mesh.Faces.Length > 0 && p.Mesh.Faces.All(f => f.Texture.StartsWith("BLADE", StringComparison.OrdinalIgnoreCase));

    /// <summary>The rotor parts' places in the model's frame (each part's origin).</summary>
    public static List<System.Numerics.Vector3> Places(AssembledModel m) => [.. m.Parts.Where(Is).Select(p => p.ToModel.T)];
}
