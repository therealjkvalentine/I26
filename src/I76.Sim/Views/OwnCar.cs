namespace I76.Sim.Views;

/// <summary>
/// Whether the player's own car is drawn. The original's object pass skips it while the camera is in the cockpit or is the
/// hood view (F6), whose eye stands inside the body's bounds on a long car; a script camera sees it from outside whatever
/// the player's view. Presentation only.
/// </summary>
public static class OwnCar
{
    public static bool Drawn(bool cockpit, bool hoodView, bool scriptCamera) => scriptCamera || !(cockpit || hoodView);

    /// <summary>
    /// Whether one of a helicopter's rotors is drawn: with the body, or from the seat with the 3D dash when the interior carries
    /// that rotor too (the original's cockpit model has the main rotor, not the tail's). The modern look's disc follows this.
    /// </summary>
    public static bool RotorDrawn(bool cockpit, bool hoodView, bool scriptCamera, bool dash3d, bool inInterior) =>
        Drawn(cockpit, hoodView, scriptCamera) || (cockpit && !scriptCamera && dash3d && inInterior);
}
