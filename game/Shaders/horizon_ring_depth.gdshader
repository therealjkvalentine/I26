// I76_HORIZON_SPLIT=0 only (HorizonTerrain): the ring as one pass writing DEPTH on every path, as upstream draws it, to measure the split.
shader_type spatial;
render_mode cull_back, specular_disabled, diffuse_lambert;
#define HORIZON_RING
#define HORIZON_DEPTH_EVERYWHERE
#include "res://Shaders/horizon_terrain.gdshaderinc"
