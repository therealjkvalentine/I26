// The modern look's cockpit trim (CockpitTrim, [port] cockpit_trim): opaque, back-face culled, the flat faces of the cockpit's roof
// and side parts with a procedural vinyl (cockpit_trim.gdshaderinc). The 1997 look draws these faces with object_back_1997.
shader_type spatial;
render_mode cull_back;
#define TRIM
#include "res://Shaders/object.gdshaderinc"
