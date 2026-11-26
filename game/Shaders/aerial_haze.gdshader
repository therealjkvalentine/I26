// Graphic Detail's Haze (the modern look's, Off by default; AirRows, EnvExtras): aerial perspective over everything drawn, the map
// and the horizon ring alike. A full-screen pass over the opaque scene (depth texture), as sky_fog is: past `start` each pixel takes
// amount * (1 - exp(-(d - start) / reach)) of the ring's own haze colour in its direction (haze.gdshaderinc, the ring's uniforms
// copied each frame), on top of what the ring already hazes. One sum for every pixel by distance alone, so the map's edge, where the
// ring takes over, shows no band. The sky (depth at the far plane) is left alone.
shader_type spatial;
render_mode unshaded, blend_mix, depth_test_disabled, depth_draw_never, cull_disabled, shadows_disabled, fog_disabled;

#include "res://Shaders/haze.gdshaderinc"

uniform sampler2D depth_tex : hint_depth_texture, filter_nearest;
uniform vec3 horizon_color : source_color = vec3(0.7, 0.75, 0.8);
uniform float haze_gain = 1.0;
uniform vec3 sprite_dir = vec3(0.0, 1.0, 0.0);
uniform float light_strength = 1.0;
uniform float start = 600.0;
uniform float amount = 0.0;
uniform float reach = 3000.0;

void vertex() {
	POSITION = vec4(VERTEX.xy * 2.0, 0.5, 1.0);
}

void fragment() {
	float depth = texture(depth_tex, SCREEN_UV).r;
	if (depth <= 0.0) discard;   // the sky (reversed Z: the far plane is 0)
	vec4 view = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, depth, 1.0);
	view.xyz /= view.w;
	float d = length(view.xyz);
	if (d <= start) discard;
	vec3 dir = normalize((INV_VIEW_MATRIX * vec4(view.xyz, 0.0)).xyz);
	ALBEDO = i76_haze_colour(dir, horizon_color, sprite_dir, light_strength) * haze_gain;
	ALPHA = amount * (1.0 - exp(-(d - start) / reach));
}
