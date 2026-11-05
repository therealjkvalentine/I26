namespace I76.Game.Sim;

/// <summary>
/// The rear-view mirror camera's field of view (across its width) and far clip. The original asks for 30 degrees but its
/// camera set-up keeps the field of view within 45..135 degrees and the far clip at 100 m or more, so the mirror shows
/// 45 degrees across and nothing past 100 m (measured in the original: about 43 degrees, sky where the main view has terrain).
/// </summary>
public static class MirrorCameraSettings
{
    public const float MinFov = 45f, MaxFov = 135f;
    public const float FovDegrees = 45f, FarMetres = 100f;
    /// <summary>[port] mirror_range=full: the far clip the original asked for, the whole map (OriginalBehavior.MirrorFar).</summary>
    public const float FullFarMetres = 20000f;
    /// <summary>The modern look's mirror at the original's 100 m: where its depth fog into the sky starts (it is whole at the clip; Cockpit.MirrorEnvironment).</summary>
    public const float FogBeginMetres = 55f;

    /// <summary>The field of view the camera set-up ends up with for a requested one.</summary>
    public static float ClampFov(float requested) => System.Math.Clamp(requested, MinFov, MaxFov);
}
