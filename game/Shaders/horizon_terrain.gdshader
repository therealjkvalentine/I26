// The modern look's distant terrain (HorizonTerrain.cs): horizon_terrain.gdshaderinc's main pass, with the ring's masks and no depth write (its rim pass: horizon_rim.gdshader).
shader_type spatial;
render_mode cull_back, specular_disabled, diffuse_lambert;
#define HORIZON_RING
#include "res://Shaders/horizon_terrain.gdshaderinc"
