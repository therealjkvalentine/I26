// The loading card's wheel (LoadProgressView): the player's car's ten-hole steel wheel seen side-on (tyre, rim, the ring of holes,
// the hub), filled clockwise from 12 o'clock as a pie as the load goes on: the part loaded in the menu's lit white, the rest in its
// dim grey. style 1: the tyre and the rim dim all round, the pie a solid white wedge inside the rim. pixels > 0 is the 1997 look:
// the wheel sampled at that many of the 640x480 frame's pixels across, without smoothing, as the shell's own pictures are.
shader_type canvas_item;

uniform float fill : hint_range(0.0, 1.0) = 0.0;
uniform float pixels = 0.0;
uniform int style = 0;
uniform vec4 lit_colour : source_color = vec4(1.0);
uniform vec4 dim_colour : source_color = vec4(0.427, 0.427, 0.427, 1.0);

// coverage of r < edge, smoothed over w (w 0: a step)
float inside(float r, float edge, float w) { return w > 0.0 ? 1.0 - smoothstep(edge - w, edge + w, r) : step(r, edge); }

void fragment() {
	vec2 uv = UV;
	float w = 0.0;
	if (pixels > 0.0) uv = (floor(uv * pixels) + 0.5) / pixels;
	vec2 p = uv * 2.0 - 1.0;            // -1..1 across the wheel, y down
	float r = length(p);
	if (pixels <= 0.0) w = max(fwidth(r), 1e-4) * 0.75;
	float t = atan(p.x, -p.y) / TAU;     // 0 at 12 o'clock, clockwise
	if (t < 0.0) t += 1.0;

	// the wheel's shade: tyre, sidewall, rim lip, dish, the ten holes, hub ring, centre cap
	float disc = inside(r, 1.0, w);
	float tread = step(0.93, r) * step(0.5, fract(t * 40.0));              // the tread's blocks round the edge
	float lum = mix(0.20, 0.30, inside(r, 0.88, w) - inside(r, 0.80, w));   // the tyre, a lighter band on its wall
	lum = mix(lum, 0.12, tread);
	float rim = inside(r, 0.70, w);
	lum = mix(lum, 1.0, rim);                                                // the rim's lip
	lum = mix(lum, 0.80, inside(r, 0.64, w));                                // the dish
	vec2 hole = vec2(sin(round(t * 10.0) / 10.0 * TAU), -cos(round(t * 10.0) / 10.0 * TAU)) * 0.45;
	float holes = inside(length(p - hole), 0.075, w);
	lum = mix(lum, 0.92, inside(r, 0.25, w));                                // the hub's ring
	lum = mix(lum, 0.62, inside(r, 0.16, w));                                // the cap
	lum = mix(lum, 0.0, holes);

	// the pie: loaded where t < fill, the edge smoothed across the radius's pixels
	float along = (fill - t) * TAU * max(r, 0.05);
	float loaded = fill >= 1.0 ? 1.0 : fill <= 0.0 ? 0.0 : (w > 0.0 ? smoothstep(-w, w, along) : step(0.0, along));

	vec3 c;
	float a = disc;
	if (style == 1) {
		// the tyre and rim dim all round; inside the rim's lip a solid wedge of white, the rest black but for the holes' outline
		float wedge = inside(r, 0.64, w);
		vec3 ring = dim_colour.rgb * lum;
		vec3 inner = mix(vec3(0.0), lit_colour.rgb, loaded) * (1.0 - holes) + dim_colour.rgb * 0.35 * holes;
		c = mix(ring, inner, wedge);
	} else {
		c = mix(dim_colour.rgb * mix(0.35, 1.0, lum), lit_colour.rgb * lum, loaded);   // the dim side a grey wheel still, its tyre not lost in the black
	}
	COLOR = vec4(c, a);
}
