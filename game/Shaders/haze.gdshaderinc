// The modern look's haze colour in a view direction (world frame): the sky's horizon colour, warmed toward the sun as the sky
// (sky_modern.gdshaderinc) draws it below the horizon. The horizon ring hazes toward it (horizon_terrain.gdshaderinc) and Graphic
// Detail's Haze (aerial_haze.gdshader) adds its air toward the same colour, so the map and the ring meet in one colour.
vec3 i76_haze_colour(vec3 view_w, vec3 horizon, vec3 sprite, float light) {
	float sun = max(dot(view_w, normalize(sprite)), 0.0);
	return horizon + vec3(1.0, 0.85, 0.6) * (pow(sun, 8.0) * 0.18 + pow(sun, 64.0) * 0.35) * light;
}
