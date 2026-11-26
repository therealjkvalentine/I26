// The mirrors' picture as their glass shows it (cockpit_mirror.gdshader, mirror_2d.gdshader): the view sampler is the includer's.
// Mirror Rate's Smooth values (MirrorWarp): between the camera's draws the picture re-projected for its turn since: view_warp maps
// the picture's coordinates now (u right, v down over the glass's field) to the texture drawn then, a homography (the render is a
// little wider than the glass, so a turn brings in drawn picture; past its edge the edge is stretched). With warp_depth the move as
// well: view_depth is the render's depth in metres ahead (MirrorDepth), and each point is found along its ray in three steps
// (warp_turn the turn from now to then, warp_shift the camera's place now in its frame then, the tangents the fields'). With
// attach_m (the door mirrors' cameras, which see the car's own flank) a pixel of the render nearer than that many metres is the
// car itself, which moves with the camera: it stays where it was drawn (view_scale, the overscan's crop alone).
uniform bool warp_on = false;
uniform float attach_m = 0.0;
uniform vec2 view_scale = vec2(1.0);
uniform mat3 view_warp = mat3(1.0);
uniform bool warp_depth = false;
uniform mat3 warp_turn = mat3(1.0);
uniform vec3 warp_shift = vec3(0.0);
uniform vec2 tan_now = vec2(0.414, 0.1);
uniform vec2 tan_then = vec2(0.414, 0.1);
uniform sampler2D view_depth : filter_nearest;
// Mirror Haze (MirrorHazeRow): the glass a touch milky, as no mirror is perfect or perfectly clean: a flat veil, the same over the
// whole glass (haze_veil of haze_tint mixed over the picture: its blacks lifted, its contrast a little less), and the picture
// haze_blur texels soft. No gradient, smudge or pattern.
uniform float haze_veil = 0.0;
uniform float haze_blur = 0.0;
uniform vec3 haze_tint : source_color = vec3(1.0);

vec2 warp_uv(vec2 uv) {
	vec2 r0 = 0.5 + (uv - 0.5) * view_scale;
	if (attach_m > 0.0 && texture(view_depth, r0).r < attach_m) return r0;
	vec3 h = view_warp * vec3(uv, 1.0);
	vec2 r = h.xy / max(h.z, 1e-5);
	if (warp_depth) {
		vec3 d = warp_turn * vec3((2.0 * uv.x - 1.0) * tan_now.x, (1.0 - 2.0 * uv.y) * tan_now.y, -1.0);
		for (int i = 0; i < 3; i++) {
			float z = texture(view_depth, clamp(r, 0.0, 1.0)).r;
			float s = (z + warp_shift.z) / max(-d.z, 1e-4);
			vec3 p = d * s + warp_shift;
			r = vec2(0.5 + 0.5 * (p.x / max(-p.z, 1e-3)) / tan_then.x, 0.5 - 0.5 * (p.y / max(-p.z, 1e-3)) / tan_then.y);
		}
	}
	r = clamp(r, 0.0, 1.0);
	// a point of the world that was behind the car's own flank when drawn: the drawn world's last pixel before the flank, on the
	// way from where this pixel itself was drawn (the flank's edge stretched over the gap a turn opens, not the flank twice)
	if (attach_m > 0.0 && texture(view_depth, r).r < attach_m) {
		vec2 lo = r0, hi = r;
		for (int i = 0; i < 5; i++) {
			vec2 m = 0.5 * (lo + hi);
			if (texture(view_depth, m).r < attach_m) hi = m; else lo = m;
		}
		r = lo;
	}
	return r;
}

// the glass's picture at a view coordinate: re-projected (warp_on), then hazed
vec3 view_at(vec2 uv) {
	vec2 r = warp_on ? warp_uv(uv) : uv;
	if (haze_veil <= 0.0 && haze_blur <= 0.0) return texture(view, r).rgb;
	vec2 px = haze_blur / vec2(textureSize(view, 0));
	vec3 c = (texture(view, r).rgb * 2.0 + texture(view, r + vec2(px.x, px.y)).rgb + texture(view, r + vec2(-px.x, px.y)).rgb
		+ texture(view, r + vec2(px.x, -px.y)).rgb + texture(view, r + vec2(-px.x, -px.y)).rgb) / 6.0;
	return mix(c, haze_tint, haze_veil);
}
