// Graphic Detail's Map Edge (MapEdge.cs; the modern look's terrain and its ring): a distance haze toward the modern
// sky's horizon colour over the ground in a band inside the map's edge and over the ring beyond it, so the boundary
// walls fade like far land. Drawing only: the heightfield and its collision are unchanged. Off (Original): x = 0.

// R: metres from the nearest ground with no tile (0 there and past the tiles' bounds), 10 m texels
global uniform sampler2D i76_edge_dist : filter_linear, repeat_disable;
global uniform vec4 i76_edge_rect;     // the grid's low corner (game x, z) and its size in metres
global uniform vec4 i76_edge_haze;     // x: amount at full haze (0 off), y: band inside the edge, whole for its outer half (m), z: clear air (m), w: reach (m)
global uniform vec4 i76_edge_color;    // the modern sky's horizon colour; a: the light's strength (the sun's glow)
global uniform vec3 i76_edge_sun;      // toward the sun or moon, Godot frame

// 1 at the edge and beyond it and over the band's outer half (the rims stand back from the edge: m01's 440 m), 0 the band's width in
float map_edge_band(vec2 game_xz) {
	if (i76_edge_rect.z <= 0.0) return 0.0;
	vec2 uv = (game_xz - i76_edge_rect.xy) / i76_edge_rect.zw;
	float d = (uv.x >= 0.0 && uv.y >= 0.0 && uv.x <= 1.0 && uv.y <= 1.0) ? textureLod(i76_edge_dist, uv, 0.0).r : 0.0;
	return 1.0 - smoothstep(0.5 * i76_edge_haze.y, i76_edge_haze.y, d);
}

// how much of a point `band` toward the edge and `dist` from the eye goes to the haze
float map_edge_amount(float band, float dist) {
	float t = max(dist - i76_edge_haze.z, 0.0) / i76_edge_haze.w;
	return i76_edge_haze.x * band * (1.0 - exp(-t * t));
}

// the haze in a world (Godot) view direction: the horizon colour with the sun's glow, as horizon_terrain.gdshader's
vec3 map_edge_color(vec3 view_w) {
	float sun = max(dot(view_w, normalize(i76_edge_sun)), 0.0);
	return i76_edge_color.rgb + vec3(1.0, 0.85, 0.6) * (pow(sun, 8.0) * 0.18 + pow(sun, 64.0) * 0.35) * i76_edge_color.a;
}
