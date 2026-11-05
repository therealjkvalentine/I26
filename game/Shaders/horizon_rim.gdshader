// The modern look's distant terrain (HorizonTerrain.cs): horizon_terrain.gdshaderinc's rim pass, the backstop over the
// outermost 10 m of a tile pushed behind the terrain's edge (on the ring's cells that reach them; the only pass of the ring that
// writes depth, so the main pass is tested early).
shader_type spatial;
render_mode cull_back, specular_disabled, diffuse_lambert;
#define HORIZON_RING
#define HORIZON_RIM_PASS
#include "res://Shaders/horizon_terrain.gdshaderinc"
