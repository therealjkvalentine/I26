// The modern look's ground at night (terrain_modern, horizon_terrain). The original lights its terrain by the light's
// direction alone, d·fade + ambient through the mission's ramp, not by the preset's intensity, which only its roads and
// objects take: a night map's darkness is painted into its ground texture, which then shows nearly as by day, while its
// roads and cars sit near the ramp's floor. The modern moon is the roads' and objects' light (Main.ApplyLightingCalibration
// sets it so); the ground takes, besides, the 1997 look's own light as emission: its colour (sRGB) times the ramp's entry,
// read between the ramp's steps (the card interpolates a face's vertex light across it), on the modern normal, so the
// relief still shows. i76_night_ground is how much of it (0 by day, and with lighting_calibration off or filmic).
global uniform float i76_night_ground;
global uniform vec3 i76_light_dir;
global uniform vec2 i76_fade;
global uniform float i76_ambient;
#include "res://Shaders/ramp.gdshaderinc"

float i76_ramp_between(float light) {
	float k = 31.0 * clamp(light, 0.0, 1.0);
	float k0 = floor(k);
	return mix(i76_ramp(k0 / 31.0).x, i76_ramp(min(k0 + 1.0, 31.0) / 31.0).x, k - k0);
}

vec3 i76_night_srgb(vec3 c) { return mix(c * 12.92, 1.055 * pow(max(c, vec3(0.0)), vec3(1.0 / 2.4)) - 0.055, step(vec3(0.0031308), c)); }
vec3 i76_night_linear(vec3 c) { return mix(c / 12.92, pow(max((c + 0.055) / 1.055, vec3(0.0)), vec3(2.4)), step(vec3(0.04045), c)); }

// albedo: linear; n: the surface's normal, Godot's world frame; view_z: the view depth (the engine's fade)
vec3 i76_night_ground_light(vec3 albedo, vec3 n, float view_z) {
	// the engine's unnormalised gradient: a slope across the light is lit as a flat
	float d = dot(n, i76_light_dir) / max(n.y, 0.25);
	float fade = clamp(view_z * i76_fade.x + i76_fade.y, 0.75, 1.0);
	float g = i76_ramp_between(d < 0.0 ? i76_ambient : min(d * fade + i76_ambient, 1.0));
	return i76_night_ground * i76_night_linear(i76_night_srgb(albedo) * g);
}
