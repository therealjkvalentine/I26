# Joysticks that are not game pads

GitHub issue 1: "USB Joystick not recognized". A game pad worked (Godot knows a standard layout for it); a plain analog USB stick
did not. This is what the port does for such a device, what Godot gives it and what is untested.

## What Godot does with an unmapped device

Godot maps a joypad to the standard layout (left stick, right stick, triggers, A/B/X/Y...) through SDL's game controller database. A device
the database does not know (`Input.IsJoyKnown(id)` false: flight sticks, wheels, throttle quadrants, most cheap USB sticks) is
still listed by `Input.GetConnectedJoypads()` and still sends events, but **raw**: `JoyAxis` and `JoyButton` indices are the device's
own numbers. Seen on Linux with a virtual stick made through `/dev/uinput` (X, Y, twist, throttle, hat, 12 buttons):

- axes are numbered 0, 1, 2... in the order the kernel lists them (X 0, Y 1, then twist, throttle...), -1 to +1, at most 10 read;
- buttons are numbered from 0 in the kernel's order, the **hat arrives as buttons 11 to 14** (up, down, left, right: Godot's D-pad
  numbers), so a stick with more than 11 buttons shares those numbers with the hat;
- **A hat and a real button 12 to 15 cannot be told apart** (issue 22). Godot 4.5+ reads joysticks through SDL (`drivers/sdl/joypad_sdl.cpp`): a joystick button
  goes to `Input::joy_button(id, button)` with SDL's own button number, and a hat motion goes to `Input::joy_hat`, which on an unmapped device
  sends `_button_event` for `DPAD_UP/DOWN/LEFT/RIGHT` = 11/12/13/14 (`core/input/input.cpp`). Both end as the same `InputEventJoypadButton` and the same
  `IsJoyButtonPressed` state; nothing in the API says which one it was. A stick with 12 or more buttons has physical buttons 12 to 15 on
  the hat's numbers.
- `Input.GetJoyAxis`, `IsJoyButtonPressed` and the events all work with those raw numbers.

Before this change the port only listed `IsJoyKnown` devices (`GodotPads.Devices`) and read only the six standard axes, so an unmapped
stick was invisible. On Windows Godot reads such a device through DirectInput the same way (axes X, Y, Z, Rx, Ry, Rz, then sliders;
hat as the same four button numbers). That, and the numbers a given stick has, are **not tested** here: no Windows machine, no real stick.

## Telling the hat from button 12 (issue 22)

Godot cannot (see above), so `NativeSticks` asks the OS for the same stick: winmm `joyGetPosEx` on Windows (buttons and POV hat apart), `/dev/input/jsN`
on Linux (hat axes ABS_HAT0X/Y apart from buttons), matched to Godot's device by vendor and product id. With that state (`HatSplit`) the generic
stick is numbered raw 0-10 Button1-11, 11-14 the hat, 15+ Button12+, Button N = physical N, and a Godot D-pad-number event is told hat or button by
which one the OS shows held (the one that changed when both are). Not found (macOS, no joydev, no vid/pid match) = the switch below as before.
`I76_NO_NATIVE_STICK=1` turns it off. Untested on real hardware; bindings of Button12+ made in the default mode meant physical 16+ and now mean
physical 12+.

## What the port does

- `GodotPads` lists the pads Godot knows first, then every other device, so joystick1 stays the game pad when both are plugged in.
  A device without a standard layout is a *generic* device (`PadRaw.Generic`): raw axes 1 to 10 and raw buttons.
- On the virtual stick (input.map names) a generic device has `Left/Right` and `Down/Up` as axes 1 and 2 (the stick),
  `Axis1`..`Axis10` for every raw axis (the stick's two have both names), `Button1`..`Button32` and `HatUp`/`Right`/`Down`/`Left`.
  Raw buttons 0 to 10 are Button1 to 11, 11 to 14 are the hat, 15 and up are Button12 and up (the default; the hat is what most sticks have).
  Because Godot cannot say which of the two raw 11 to 14 is, the Joystick screen has a switch (row below the dead zone, `generic/hat` in
  `user://joystick.cfg`): with the hat off, raw 11 to 14 are Button12 to 15, raw 15 and up are Button16 and up (Button N is physical button N) and
  there is no hat. A stick with both a hat and buttons 12 to 15 has to choose; saved bindings to Button12 and up mean different physical buttons after the switch. The gamepad names (`Throttle`,
  `Rudder`, `5thAxis`) read 0.
- A dead zone (default 10 %, steps 0, 5, 10, 15, 20, 30 %; `user://joystick.cfg`) is cut off every raw axis and the rest is
  stretched to full; then the original's response (signed square, no further dead zone) applies. The sign of an axis row's block is
  the invert.
- The preset (`GenericPreset`): the stock JOYSTICK.MAP stick (`- Down/Up` throttle, `- Left/Right` steer), trigger = fire, Button2 cycle,
  3 target nearest, 4 target front, 5 e-brake, 6 link, 7 map, 8 horn, 9 view toggle, 10 reverse, hat = glances. It is used with no
  saved map, by Restore, and in memory when the saved map's pad side is exactly the game pad preset (a map made before the stick was
  plugged in). A throttle lever or twist has no default: bind it.
- In a mission its buttons are never read as a game pad's A, B and Start (no game menu on button 7,
  no skip on button 2); they do drive the shell's menus as a pad would (hat and stick move, trigger clicks) and they do not
  while the Joystick screen is open.
- **Joystick Configuration** (`JoystickScreen`, Control Configuration > Calibrate when joystick1 is generic): rows appear for the axes
  and buttons that move or are pressed (Godot does not say how many a device has), light up while held, and the last one touched is
  selected. A click or Enter opens the list: for an axis Accelerate / Brake, Steer, Look left / right, Look up / down, Invert this
  axis; for a button the commands. Delete or right-click clears. The dead zone is the middle button. Restore loads the preset.
  The sheet is drawn in the shell's pixels in both looks.

An SDL mapping is not needed and is not added; a user who wants a stick treated as a standard gamepad can still give Godot one with
`SDL_GAMECONTROLLERCONFIG`, and then it is a game pad here.

## The pad's shift layer

One pad button (or a hat direction) can be a shift (`BindingTable.Shift`): while it is held, every other button and hat direction that has a
second command does that command instead; one without a second command keeps its first; the shift alone does nothing (it loses any command
of its own when it becomes the shift). The axes are not layered. The layer is written into input.map as the original's own chords, so the
original reads the file to the same effect: the second command's block is the input `+` and the shift `+`, and the first command's block of an
input that has a second also has the shift `-`:

```
hardpoint3_fire {
   + joystick1  Button1
	+ joystick1	Button5
}
weapon_fire {
   + joystick1  Button1
	- joystick1	Button5
}
```

input.map has no place to say which button is the shift, so that is kept in `user://joystick.cfg`, section `pad`, key `shift` (`joystick1
Button5`, empty for none), written with the map; with no key there (the preset in memory, a file from elsewhere) the shift is the one
pad button that is the `+` second term of chords on that pad's buttons (`DeriveShift`). Pressing the shift while an input is already held
starts that input's second command, as the chord does in the original.

The pad preset has LB as the shift (its e-brake is on RB now, which used to fire as A does), after the remapper layer that players used
over the original: with LB held A fires hardpoint 3 (the rear gun on most cars) and B hardpoint 4 (the dropper), X and Y hardpoints 1 and 2,
L3 and R3 specials 1 and 2, the D-pad up binoculars, down radar range, left and right the gears; RB, Back, Start and the sticks are the same with
it held. A map saved from the preset before the shift (`PadPreset.BuildUnshifted`) is still known as the game pad's.

The Controller screen: a button's list ends with *Shift (hold for second commands)*. With a shift, holding it on the pad (or Tab, or a click on
the shift's own row) shows the second commands; an input without one shows its first in brackets. Picking in that view sets the second
command, *(none)*, Delete or right-click clears it, and clearing the shift itself takes the layer away. Tests:
`tests/I76.Formats.Tests/PadShiftTests.cs`.

## Bindings beyond input.map

input.map gives an input (or a chord) one command, and the original's file has no form for anything else. The port's further bindings
(`PadRules`, `src/I76.Formats/Input/PadRules.cs`) are kept beside it in `user://input.rules`, so the original still reads input.map as it
was: one rule a line, its kind first, `#` a comment; a line of a kind the version does not know is kept as it is. They travel with the
bindings (Control Configuration's Done writes both, Cancel drops both, Restore pad defaults takes the preset's), and an input has one
binding: a rule that takes a button loses it when the button is given a command or cleared, and takes the button's input.map blocks off.
Each frame (`Controls`, as the pads are read) the rules see the sticks, the time and whether the head looks back, and say which commands
they hold, which they press once, which commands stand for others and which inputs they take over for the frame; a press arrives as an
action event through Godot's input (`i26_rule:<command>`), so every reader of a command's press (the car's commands, the map, the views)
takes it as a key's, and a held command is asked as the bindings' are.

**Tap / hold** (`taphold joystick1 Button1 weapon_fire special1`): one button, two commands. Let go before 400 ms (a tap), it presses the
first command once, on the release; held to 400 ms it presses the second then and holds it until let go, and the tap never comes. The rule
is that of a pad layout players used over the original (A: a tap one shot, a hold the nitrous). On the Controller screen a button's list
ends with *Tap / hold...*, which asks the tap's command and then the hold's; the row then reads *Tap: ...* and *Hold: ...*. Tests:
`tests/I76.Formats.Tests/TapHoldTests.cs` (timings either side of 400 ms, one press after another, the file, the button's other bindings).

## Game pad dead zones

A pad with a standard layout used to be read with no dead zone at all (only the free look cut off 12 % of the right stick), so a worn stick that
rests off centre turned the camera and steered by itself. `PadDeadZones` (`src/I76.Formats/Input/PadDeadZones.cs`) now cuts them off in the one
place a pad is read (`GodotPads.Raw`), so driving, the camera, the cockpit free look, the menus and network play (which samples the local keys and
pad the same way) all see the same sticks:

- Each stick has a **radial** zone: its distance from centre under the zone is (0, 0), over it the distance is rescaled so the response starts from
  nothing at the zone's edge and reaches 1 at full deflection, the direction kept (no jump, and a drift on one axis does not leave the other cut off).
  A square gate's corner is held to the unit circle.
- A trigger has a plain zone, rescaled the same way.
- Defaults: left stick 15 %, right stick 15 %, trigger 5 %. Steps 0 to 50 % in 5 %. The free look keeps its own 12 % on top of the right stick's
  (so a 15 % zone is about 25 % of the raw travel before the head moves); the menus keep their 0.5 threshold on the left stick, now on the rescaled value.
- Kept in `user://joystick.cfg`, section `pad`: `left_dead`, `right_dead`, `trigger_dead` (0..0.5). A key that is missing (a file from before the
  setting) is that zone's default and the other keys and settings are untouched. A generic stick keeps its own per-axis dead zone, above.
- **Pad Dead Zones** (`PadDeadZoneScreen`, Control Configuration > Calibrate > the Dead Zones button): the three settings (click raises and wraps,
  right-click lowers; Left and Right on the keys or D-pad, A, B and Start as on the controller screen) and below them each stick as a gauge: a dot
  where the pad says the stick is, a ring the size of its zone, a red dot where the game takes it, and each trigger as a bar with its zone marked. A
  change is kept at once; Cancel puts the zones back as they were on opening. `I76_PAD_AXES="lx,ly,rx,ry,lt,rt"` stands in for a pad for captures.

Tests: `tests/I76.Formats.Tests/PadDeadZoneTests.cs` (radial, rescaled, triggers, steps, defaults, a missing key).

## Force feedback on a wheel or stick (Windows)

Play Options > Force Feedback has three settings: Off, Pad (the pad's two rumble motors, the default) and Wheel. Wheel drives a
DirectInput force-feedback device (a wheel or a force-feedback stick) from the game itself, by P/Invoke on `dinput8.dll`; there is no
driver or helper to install beyond the device's own. It is offered on Windows only: Godot has no force-feedback API for a wheel, and on
Linux and macOS the row goes Off / Pad (a wheel chosen in a shared settings file reads as Pad there).

- **Set-up.** Plug the wheel in and close its vendor control panel before a mission starts: the panel, another game or a feeder tool
  holding the device exclusively keeps it from the game (the log says which call failed). The game opens the first force-feedback
  device DirectInput lists, exclusive and in the background, turns its auto-centre off (the game supplies the centring spring) and plays
  the original's effects on it (deviations.md, Force Feedback). If that device cannot be opened it tries the next; with none, or with
  any failure, the pad's rumble is used, silently but for a line in the log. A virtual stick (vJoy and the like) is listed by
  DirectInput only if it claims force feedback, and is skipped if it takes none of the effects used.
- **Which device.** With several force-feedback devices, `ffb_device` in `user://shell.cfg`, section `[port]`, picks the first whose
  name contains it (case ignored), for example `ffb_device="T300"`; empty or absent, the first. It is kept when the settings are saved.
- **Strength.** Play Options' second page, Wheel Strength: 0 to 100 % in steps of 10 (`ffb_strength`), the device's own gain, changed
  at once. The original's effects are at their own levels under it; nothing is boosted.
- **Behaviour.** The device is driven from a thread of its own: the frame only hands over the newest effects and never waits on the
  driver. Only what changed is sent (a changed magnitude alone is one report, not oftener than every 30 ms). The forces keep playing
  while the window is in the background (the original's DLL asked for the same); a device that was lost (unplugged, taken by
  another program) is acquired again within a second and its effects started over. Everything stops under the escape menu, the map,
  the notepad and videos, and is stopped and released when the setting leaves Wheel and when the game quits.
- **Effect types.** Constant force, periodic (square, sine, triangle, sawtooth), ramp, spring and custom curves are used. A type the
  device does not report (most wheels lack custom curves, which the cannon files are, and some lack ramps or sawtooths) is played as
  one constant force the game rewrites every 10 ms from the effect's shape; the log lists the types the device reported.
- **Known devices.** Written against DirectInput's effect model and the Thrustmaster T300RS's reported effects (constant, periodic,
  spring, damper; no custom force), and checked on a desktop with DirectInput present and no force-feedback device attached: the
  enumeration finds none and the pad is used. Not yet run against a real wheel or stick.

Tests: `tests/I76.Formats.Tests/WheelForceTests.cs` (the force files' effects as DirectInput parameters, the DLL's own effects, the hits'
side, the effects' shapes over time, the rate limit, the hand-over to the thread, the model's slots).

## Checks

`tests/I76.Formats.Tests/GenericJoystickTests.cs` (button slots, virtual stick, dead zone, preset resolution, raw axes, invert, names).
A virtual stick on Linux: `/dev/uinput` (write access needed; no root otherwise) with `UI_ABS_SETUP`/`UI_DEV_SETUP` ioctls, then
`I76_PAD_FOCUS=1 tools/shot.sh ...` with `I76_SHELL_INPUT="key:escape@200;click:300,211@260;click:70,460@330"` captures the screen; with
`--mission t01 --drive` and `I76_TELEMETRY=1` the telemetry shows the stick driving (forward gives throttle, a push to the right gives
the same steer sign a game pad's left stick gives).

Verified: Godot lists and reads an unmapped Linux device raw; the port finds it, applies the preset, shows the screen, and the car
accelerates and steers from the stick. Not verified: the switch on real hardware (it is tested with the events Godot would send, `GenericJoystickTests`), Windows (DirectInput numbering, hat), real hardware, force feedback on a
real wheel or stick (see above), more than one stick.

A way to have both would be to read the hat apart from Godot (on Windows `joyGetPosEx`'s POV against its buttons, on Linux evdev), matched to Godot's device; not done, nothing here to test it on.
