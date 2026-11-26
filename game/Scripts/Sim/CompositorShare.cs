using System.Collections.Generic;
using Godot;

namespace I76.Game.Sim;

/// <summary>
/// A camera's compositor shared by the effects that run on it: each effect is added to or taken out of the list the camera has,
/// never the camera given a compositor of one effect's own. A mirror camera carries the smoke layer (SmokeLayer) and, under Mirror
/// Rate's Smooth values, its depth (MirrorDepth); the main camera the smoke layer and the heat shimmer. When the smoke layer set the
/// mirror camera's compositor to its own, the depth effect no longer ran, and the re-projection read the empty texture's placeholder
/// (a checkerboard of 0 and 1 metres): flat brown blocks over the glass.
/// </summary>
public static class CompositorShare
{
    /// <summary>The effect put in or taken out of a list of effects; whether the list changed. The others are kept, in their order.</summary>
    public static bool Edit<T>(IList<T> effects, T effect, bool on) where T : class
    {
        bool has = effects.Contains(effect);
        if (has == on) return false;
        if (on) effects.Add(effect); else effects.Remove(effect);
        return true;
    }

    /// <summary>The effect in (or out of) the camera's compositor beside the others there; a camera without one gets one. Whether it is in after.</summary>
    public static bool Set(Camera3D cam, CompositorEffect effect, bool on)
    {
        if (cam.Compositor is not { } c)
        {
            if (on) cam.Compositor = new Compositor { CompositorEffects = [effect] };
            return on;
        }
        var list = c.CompositorEffects;
        if (Edit(list, effect, on)) c.CompositorEffects = list;
        return on;
    }

    /// <summary>
    /// One effect's place on one camera, kept each frame cheaply: the camera's list is read again only when its compositor changed,
    /// the camera did, or once a second (another owner may have edited the list), so no array is copied out of the engine each frame.
    /// </summary>
    public sealed class Slot
    {
        private Camera3D? _cam;
        private Compositor? _seen;
        private bool _on;
        private int _age;

        /// <summary>The effect in (or out of) the camera's compositor; whether it is in.</summary>
        public bool Keep(Camera3D cam, CompositorEffect effect, bool on)
        {
            if (cam == _cam && on == _on && cam.Compositor == _seen && (_seen is not null || !on) && ++_age % 60 != 0) return on;
            Set(cam, effect, on);
            (_cam, _seen, _on, _age) = (cam, cam.Compositor, on, 0);
            return on;
        }
    }
}
