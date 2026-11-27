// The modern look's cockpit trim (CockpitTrim.cs, [port] cockpit_trim): a procedural vinyl on the cockpit model's flat grey faces,
// projected in the mesh's own frame (the car's, Godot axes, metres) so it stays put as the head moves. Included by object.gdshaderinc
// with TRIM (cockpit_trim.gdshader); the 1997 look never draws it. Every pattern is generated here (no texture), each band faded to
// its mean as its wavelength nears two pixels (trim_band), so nothing finer than the screen shows sparkles or swims; the albedo
// keeps the face's own luminance on average (the holes' and seams' dark paid back across the rest), so the cabin is as dark as before.

// trim_part, trim_style and trim_knobs are declared in object.gdshaderinc (the material library's parameters are its uniforms)

const float TRIM_PI = 3.14159265;
const float TRIM_RELIEF = 0.6;   // the relief's overall scale (the slopes below are a real vinyl's; the screen wants them softer)

float trim_hash(vec3 p) {
	// an integer hash of the lattice corner (a float sine or fract hash shows streaks this far from the origin in cells)
	uvec3 q = uvec3(ivec3(p) + ivec3(1 << 20));
	uint h = (q.x * 73856093u) ^ (q.y * 19349663u) ^ (q.z * 83492791u);
	h = (h ^ (h >> 16u)) * 0x7feb352du;
	h = (h ^ (h >> 15u)) * 0x846ca68bu;
	h ^= h >> 16u;
	return float(h) * (1.0 / 4294967295.0);
}

// value noise in -1..1, quintic between the lattice's corners
float trim_noise(vec3 x) {
	vec3 i = floor(x);
	vec3 f = fract(x);
	vec3 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
	float a = trim_hash(i), b = trim_hash(i + vec3(1, 0, 0)), c = trim_hash(i + vec3(0, 1, 0)), d = trim_hash(i + vec3(1, 1, 0));
	float e = trim_hash(i + vec3(0, 0, 1)), g = trim_hash(i + vec3(1, 0, 1)), h = trim_hash(i + vec3(0, 1, 1)), k = trim_hash(i + vec3(1, 1, 1));
	return mix(mix(mix(a, b, u.x), mix(c, d, u.x), u.y), mix(mix(e, g, u.x), mix(h, k, u.x), u.y), u.z) * 2.0 - 1.0;
}

// a band of wavelength lambda (metres) at fw metres a pixel: whole while it spans four pixels or more, gone at two
float trim_band(float lambda, float fw) { return 1.0 - smoothstep(0.25, 0.5, fw / max(lambda, 1e-6)); }

// a grained vinyl's height (around 0, about -0.5..0.5): pebbles with fine creases between (ridged value noise), two octaves
float trim_grain(vec3 p, float cell, float fw) {
	float w1 = trim_band(cell, fw), w2 = trim_band(cell * 0.45, fw);
	float a = 1.0 - abs(trim_noise(p / cell));
	float b = 1.0 - abs(trim_noise(p / (cell * 0.45) + vec3(7.1, 3.3, 1.7)));
	return w1 * (a - 0.55) + 0.5 * w2 * (b - 0.55);
}

// the perforations' coverage at q (the roof's plane, metres): holes of radius r on a staggered lattice; mean the share they cover
float trim_perf(vec2 q, float pitch, float r, float fw, out float mean) {
	vec2 g = q / pitch;
	float row = floor(g.y);
	g.x += mod(row, 2.0) * 0.5;
	float dist = length((fract(g) - 0.5) * pitch);
	float cov = 1.0 - smoothstep(r - fw * 0.7, r + fw * 0.7, dist);
	mean = TRIM_PI * r * r / (pitch * pitch);
	return mix(mean, cov, trim_band(pitch, fw));
}

// a basket weave's height at q (0..1): square cells, each of three rounded strands, the strands' direction alternating cell by cell
float trim_weave(vec2 q, float cell) {
	vec2 g = q / cell;
	vec2 c = floor(g);
	vec2 f = fract(g);
	bool alt = mod(c.x + c.y, 2.0) > 0.5;
	float across = alt ? f.x : f.y;
	float along = alt ? f.y : f.x;
	return sin(fract(across * 3.0) * TRIM_PI) * (0.8 + 0.2 * sin(along * TRIM_PI));
}

// a seam line every `spacing` metres along s: the groove's darkness 0..1 (half-width hw), and the vinyl's puff between seams as a slope
float trim_seam(float s, float spacing, float hw, float puff, float fw, out float slope) {
	float t = fract(s / spacing);
	float dist = min(t, 1.0 - t) * spacing;
	slope = puff * TRIM_PI / spacing * cos(t * TRIM_PI);
	// a groove narrower than a pixel spread over the pixel: its darkness by the share of the pixel it covers
	float w = max(hw, fw * 0.5);
	return (1.0 - smoothstep(w - fw * 0.5, w + fw * 0.5, dist)) * min(1.0, hw / max(fw * 0.5, 1e-6));
}

// a door pleat's depth at height y (metres, negative into the panel): rounded down into the grooves at y1 and y2 over 8 mm
float trim_pleat(float y, float y1, float y2) {
	return -0.0015 * (1.0 - smoothstep(0.0, 0.008, min(abs(y - y1), abs(y - y2))));
}

// the style's tint, at the face's own luminance (the original's grey kept on average)
vec3 trim_tint(vec3 base, vec3 tint) {
	float l = dot(base, vec3(0.2126, 0.7152, 0.0722));
	return tint * (l / max(dot(tint, vec3(0.2126, 0.7152, 0.0722)), 1e-6));
}

// p and nl the fragment's point and normal in the mesh's frame, fw its size in metres a pixel; out the albedo, roughness, specular and
// the normal's change in the mesh's frame (added to the normal)
void trim_surface(vec3 base, float base_rough, float base_spec, vec3 p, vec3 nl, float fw, out vec3 alb, out float rough, out float spec, out vec3 dn) {
	vec3 n = normalize(nl);
	vec3 t1 = abs(n.y) < 0.9 ? normalize(cross(vec3(0.0, 1.0, 0.0), n)) : normalize(cross(n, vec3(0.0, 0.0, 1.0)));
	vec3 t2 = cross(n, t1);
	bool roof = trim_part == 1, pillar = trim_part == 2;
	// the styles: tints (linear, at the grey's luminance), grain, and the sheen over the flat face's own (the material's base_roughness
	// and base_specular, as ForColour weighs that grey): a roughness taken off and a specular's factor
	vec3 tint = vec3(1.0);
	float cell = 0.0022, grain_k = 0.08, grain_a = 0.07;
	float dr = 0.0, ks = 1.0;
	if (trim_style == 1) {          // B: black vinyl: neutral, a hair cool
		tint = vec3(0.96, 1.0, 1.06);
		dr = roof ? 0.0 : 0.2;
		ks = roof ? 1.0 : 1.1;
	} else if (trim_style == 2) {   // C: dark charcoal, a touch warmer and shinier
		tint = vec3(1.05, 1.0, 0.95);
		dr = roof ? 0.15 : 0.32;
		ks = roof ? 1.15 : 1.3;
		grain_a = 0.06;
	} else if (trim_style == 3) {   // D: basket-weave headliner
		tint = vec3(0.98, 1.0, 1.03);
		dr = roof ? 0.0 : 0.2;
		ks = roof ? 1.0 : 1.1;
	} else if (trim_style == 4) {   // E: a muted colour-keyed headliner (a desaturated saddle), black pillars and doors
		tint = roof ? vec3(1.28, 1.0, 0.72) : vec3(0.97, 1.0, 1.04);
		dr = roof ? 0.0 : 0.24;
		ks = roof ? 1.0 : 1.15;
	} else if (trim_style == 5) {   // F: moulded: satin pillars with a faint fine grain, a soft perforated liner
		tint = vec3(1.0, 1.0, 1.03);
		cell = 0.0016;
		grain_k = 0.06;
		grain_a = roof ? 0.06 : 0.05;
		dr = roof ? 0.0 : 0.36;
		ks = roof ? 1.0 : 1.3;
	}
	// the door panels half the pillars' sheen: big panels facing the side windows, their sheen lifted the doors' mean (+4 to +8 % luma)
	if (trim_part == 3) { dr *= 0.5; ks = 1.0 + (ks - 1.0) * 0.5; }
	rough = base_rough - dr;
	spec = base_spec * ks;
	alb = trim_tint(base, tint);
	// E's pillars and doors in black vinyl: a little darker than the grey, the headliner's colour about the grey's luminance
	if (trim_style == 4 && !roof) alb *= 0.9;
	float mod_mean = 1.0;   // the pattern's mean darkening, paid back below
	float m = 1.0;
	vec3 grad = vec3(0.0);
	// the grain, everywhere but the basket weave's liner: albedo and normal (its slope by differences across the face's plane)
	if (!(roof && trim_style == 3)) {
		float e = cell * 0.25;
		float g0 = trim_grain(p, cell, fw);
		float gu = trim_grain(p + t1 * e, cell, fw);
		float gv = trim_grain(p + t2 * e, cell, fw);
		float cell_scale = roof ? 0.6 : 1.0;   // the headliner's grain finer in effect, under the perforations
		m *= 1.0 + grain_k * cell_scale * g0;
		grad += (t1 * (gu - g0) + t2 * (gv - g0)) / e * grain_a * cell_scale * cell;
		rough += 0.06 * (1.0 - trim_band(cell, fw));
	}
	if (roof) {
		vec2 q = vec2(p.x, p.z);
		if (trim_style == 3) {
			float wc = 0.0048;
			float ww = trim_band(wc / 3.0, fw);
			float e = wc * 0.02;
			float h0 = trim_weave(q, wc), hx = trim_weave(q + vec2(e, 0.0), wc), hz = trim_weave(q + vec2(0.0, e), wc);
			m *= mix(1.0, 0.93 + 0.1 * h0, ww);
			mod_mean *= mix(1.0, 0.93 + 0.1 * 0.6, ww);
			vec3 gq = (vec3(hx - h0, 0.0, 0.0) + vec3(0.0, 0.0, hz - h0)) / e * 0.00025 * ww;
			grad += gq - n * dot(gq, n);
			rough += 0.05 * (1.0 - ww);
		} else {
			float mean;
			float pitch = trim_style == 5 ? 0.004 : 0.0048, r = trim_style == 5 ? 0.0005 : 0.0006;
			float holes = trim_perf(q, pitch, r, fw, mean);
			m *= 1.0 - 0.55 * holes;
			mod_mean *= 1.0 - 0.55 * mean;
			rough += 0.1 * holes;
		}
		// the bows: a seam across the car every so often, the vinyl puffed between them
		float slope;
		float spacing = trim_style == 5 ? 0.16 : 0.2, puff = trim_style == 5 ? 0.002 : 0.003;
		float seam = trim_seam(p.z, spacing, 0.0018, puff, fw, slope);
		m *= 1.0 - 0.3 * seam;
		mod_mean *= 1.0 - 0.3 * (2.0 * 0.0018 / spacing);
		vec3 gz = vec3(0.0, 0.0, slope);
		grad += gz - n * dot(gz, n);
	} else if (trim_part == 3) {
		// the door panel's pleats: horizontal grooves at fixed heights in the car's frame, with a rolled edge above each
		float y1 = trim_style == 5 ? 0.66 : 0.56, y2 = trim_style == 5 ? -10.0 : 0.76;
		float d = min(abs(p.y - y1), abs(p.y - y2));
		float hw = 0.002;
		float w = max(hw, fw * 0.5);
		float groove = (1.0 - smoothstep(w - fw * 0.5, w + fw * 0.5, d)) * min(1.0, hw / max(fw * 0.5, 1e-6));
		m *= 1.0 - 0.35 * groove;
		mod_mean *= 1.0 - 0.35 * (2.0 * hw / 0.25);
		// the roll: the panel rounds down into the groove over 8 mm either side
		float e = 0.0005;
		float slope = (trim_pleat(p.y + e, y1, y2) - trim_pleat(p.y - e, y1, y2)) / (2.0 * e) * trim_band(0.008, fw);
		vec3 gy = vec3(0.0, slope, 0.0);
		grad += gy - n * dot(gy, n);
	}
	// the sheen and the relief's lit facets brighten the face as seen; the albedo pays it back (measured from the seat, 3440x1440, the
	// region's mean displayed luma within a few percent of the flat grey's: tools of the comps' sheet)
	float comp = trim_style == 2 ? 0.86 : trim_style == 4 ? 1.0 : 0.89;
	alb *= m / mod_mean * comp * trim_knobs.w;
	if (trim_knobs.y >= 0.0) rough = trim_knobs.y;
	if (trim_knobs.z >= 0.0) spec = trim_knobs.z;
	rough = clamp(rough, 0.2, 1.0);
	dn = -(grad - n * dot(grad, n)) * TRIM_RELIEF * trim_knobs.x;
}

// object.gdshaderinc's fragment, at the end of the flat faces' branch: the vinyl over the face's colour, its size a pixel in the mesh's metres
#define TRIM_APPLY { vec3 trim_dn; trim_surface(c.rgb, base_roughness, base_specular, v_local, v_nlocal, max(length(dFdx(v_local)), length(dFdy(v_local))), ALBEDO, ROUGHNESS, SPECULAR, trim_dn); NORMAL = normalize(NORMAL + mat3(VIEW_MATRIX) * mat3(MODEL_MATRIX) * trim_dn); }
