// The modern look's procedural plants with alpha-to-coverage (Anti-Aliasing 4x and 8x, World/VegShaders.cs): vegetation.gdshaderinc.
shader_type spatial;
render_mode cull_disabled, world_vertex_coords, alpha_to_coverage;

#define VEG_COVERAGE
#include "res://Shaders/vegetation.gdshaderinc"
