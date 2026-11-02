// Graphic Detail's Map Edge (MapEdge.cs; the modern look's terrain, its ring and plants): On (EdgeWalls.cs) holds the drawn
// ground over the boundary walls and the plateau behind them down to the plain at their foot. Drawing only: the
// heightfield and its collision are unchanged. Off (the original's): i76_edge_hide = 0.

// Hide: per sample (5 m texels, centred on the samples) the plain's height (R) and 1 over hidden ground (G)
global uniform sampler2D i76_edge_foot : filter_linear, repeat_disable;
global uniform vec4 i76_edge_foot_rect;   // the grid's low corner (game x, z, half a texel out) and its size in metres; z = 0: none
global uniform float i76_edge_hide;       // 1 at On, else 0

// the height ground at game xz is drawn at under Hide, given its height y otherwise: over hidden ground the lower of y and the
// plain, blended in across the sample from the drivable ground; `up` is how far the normal goes to straight up
float map_edge_hide_y(vec2 game_xz, float y, out float up) {
	up = 0.0;
	if (i76_edge_hide <= 0.0 || i76_edge_foot_rect.z <= 0.0) return y;
	vec2 uv = (game_xz - i76_edge_foot_rect.xy) / i76_edge_foot_rect.zw;
	if (uv.x < 0.0 || uv.y < 0.0 || uv.x > 1.0 || uv.y > 1.0) return y;
	vec2 fm = textureLod(i76_edge_foot, uv, 0.0).rg;
	float k = fm.g * i76_edge_hide;
	up = k * clamp((y - fm.r) * 0.5, 0.0, 1.0);
	return y + (min(y, fm.r) - y) * k;
}
