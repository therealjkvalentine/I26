// The rear-view mirror's face: the view behind, rendered by the
// mirror camera, shown through the transparent pixels of the mirror's own art
// (zmiri101), which is drawn over it keyed; the art's surround is see-through,
// as the original shows it. The view is mirrored left-right,
// and turned to the art's bottom-up row order. Pre-lit like the rest of the
// cockpit: no light reaches it. The modern look cuts the frame's outline and the glass smooth (MirrorEdge: edge holds
// the masks' smooth outlines as distance fields, fill the art with its frame colours spread), blended over what is behind; the 1997 look keys the
// art's own pixels. With the side-view mirrors on (MirrorBands) the one render is wider than the glass: band is the columns this
// glass shows, and a side mirror's face is the art's two end quarters (art_crop), half as wide in the same frame.
shader_type spatial;
render_mode unshaded, cull_back, depth_draw_always;

// the art and its outline are not tiled: sampled at the picture's edge, a repeating texture blends in the opposite edge's
// texels (the frame's top row under the bottom row's surround drew thin lines under the mirror's bottom corners)
uniform sampler2D art : source_color, filter_nearest, repeat_disable;
uniform sampler2D view : source_color, filter_linear;
uniform sampler2D view_nearest : source_color, filter_nearest;
uniform bool faithful = false;
uniform bool hidden = false;
uniform sampler2D edge : filter_linear, repeat_disable;
uniform sampler2D fill : source_color, filter_nearest, repeat_disable;
uniform bool smooth_edge = false;
// the view's columns this glass shows, in the mirror's own order (0 at the driver's left): the whole picture for the rear mirror alone
uniform vec2 band = vec2(0.0, 1.0);
// a side mirror: the art's left and right quarters, a mirror half as wide with the same frame and corners (no cross-hair)
uniform bool art_crop = false;
// a side mirror's columns spread evenly by angle (I76_SIDE_MIRROR_MAP=cylinder) instead of the projection's: band_angles are the
// band's edges' angles off the render's axis, fov_tan the tan of the render's half field across
uniform bool cylindrical = false;
uniform vec2 band_angles = vec2(0.0);
uniform float fov_tan = 1.0;
// a door mirror's own glass (MirrorGlass): the band's view over the whole face (pane), UV the glass as the driver sees it (0..1 left to right, foot to top)
uniform bool pane = false;
// a door mirror's glass rounded (GlassRound, [port] side_mirror_round): in the pane's units (u times pane_aspect, v), the core's
// convex corners counter-clockwise, its radius; the view inside the rounded shape less the rim's width, the rim's moulding (shaded
// as mirror_rim.gdshader's) from there to the rounded shape, and the moulding's dark in the corners outside it
uniform bool pane_round = false;
uniform float pane_aspect = 1.0;
uniform vec2 pane_core[12];
uniform int pane_corners = 0;
uniform float pane_r = 0.0;
uniform float rim_w = 0.02;
uniform vec3 rim_colour : source_color = vec3(0.12, 0.12, 0.13);

// the modern look's rounder corners (MirrorRound, I76_MIRROR_ROUND): the frame's outline and the glass's, each a trapezoid in the art's
// pixels shrunk by its corner's radius; the rounded shape is within that radius of it
uniform bool round_corners = false;
uniform vec2 art_px = vec2(256.0, 64.0);
uniform vec2 outer_tl; uniform vec2 outer_tr; uniform vec2 outer_br; uniform vec2 outer_bl;
uniform vec2 inner_tl; uniform vec2 inner_tr; uniform vec2 inner_br; uniform vec2 inner_bl;
uniform float outer_r = 0.0;
uniform float inner_r = 0.0;
// the added side glasses: the glass is the inner shape alone (the art's frame narrowed to the inner shape's inset), not the art's glass
uniform bool glass_from_shape = false;

// the signed distance to a convex quad (negative inside), corners in order
float sd_quad(vec2 p, vec2 a, vec2 b, vec2 c, vec2 d) {
	vec2 v[4] = vec2[4](a, b, c, d);
	float dist = dot(p - v[0], p - v[0]);
	float s = 1.0;
	for (int i = 0, j = 3; i < 4; j = i, i++) {
		vec2 e = v[j] - v[i], w = p - v[i];
		vec2 q = w - e * clamp(dot(w, e) / dot(e, e), 0.0, 1.0);
		dist = min(dist, dot(q, q));
		bvec3 cond = bvec3(p.y >= v[i].y, p.y < v[j].y, e.x * w.y > e.y * w.x);
		if (all(cond) || all(not(cond))) s = -s;
	}
	return s * sqrt(dist);
}

// the signed distance to the pane's core (negative inside): a convex counter-clockwise polygon
float sd_core(vec2 p) {
	float d = 1e9;
	bool inside = true;
	for (int i = 0; i < 12; i++) {
		if (i >= pane_corners) break;
		vec2 a = pane_core[i], b = pane_core[i + 1 < pane_corners ? i + 1 : 0];
		vec2 e = b - a, w = p - a;
		vec2 q = w - e * clamp(dot(w, e) / max(dot(e, e), 1e-12), 0.0, 1.0);
		d = min(d, dot(q, q));
		if (e.x * w.y - e.y * w.x < 0.0) inside = false;
	}
	return inside ? -sqrt(d) : sqrt(d);
}

// inside a rounded shape (its core's distance less the radius), over one screen pixel
float rounded(float sd) {
	return clamp(0.5 - sd / max(fwidth(sd), 1e-4), 0.0, 1.0);
}

// a field's 0.5 contour over one screen pixel
float cut(float f) {
	return clamp((f - 0.5) / max(fwidth(f), 1e-4) + 0.5, 0.0, 1.0);
}

vec2 art_uv(vec2 uv) {
	return art_crop ? vec2(uv.x < 0.5 ? uv.x * 0.5 : 0.5 + uv.x * 0.5, uv.y) : uv;
}

// the view's column for a glass column: the band's, flipped (a mirror image)
float view_x(float u) {
	float m = cylindrical ? 0.5 + 0.5 * tan(mix(band_angles.x, band_angles.y, u)) / fov_tan : mix(band.x, band.y, u);
	return 1.0 - m;
}

void fragment() {
	if (hidden) discard;
	vec2 auv = art_uv(UV);
	vec2 vuv = vec2(view_x(UV.x), 1.0 - UV.y);
	if (pane) {
		ALBEDO = texture(view, vuv).rgb;
		if (pane_round) {
			float sd = sd_core(vec2(UV.x * pane_aspect, UV.y)) - pane_r;
			// the moulding across the rim: 0 at the rounded shape's edge, 1 at the glass's
			float t = clamp(-sd / rim_w, 0.0, 1.0);
			float shade = mix(0.55, 1.35, smoothstep(0.15, 0.8, t)) - 0.6 * smoothstep(0.88, 1.0, t);
			ALBEDO = mix(rim_colour * shade, ALBEDO, rounded(sd + rim_w));
		}
		ALPHA = 1.0;
	} else if (smooth_edge && !faithful) {
		vec2 e = texture(edge, auv).rg;
		float solid = cut(e.r), g = cut(e.g);
		if (solid <= 0.0) discard;
		// the art's own marks on the glass (the cross-hair) are drawn from it, not cut
		float glass = texture(art, auv).a >= 0.5 && e.g > 0.75 ? 0.0 : g;
		if (round_corners) {
			vec2 p = auv * art_px;
			solid = min(solid, rounded(sd_quad(p, outer_tl, outer_tr, outer_br, outer_bl) - outer_r));
			if (solid <= 0.0) discard;
			float inner = rounded(sd_quad(p, inner_tl, inner_tr, inner_br, inner_bl) - inner_r);
			glass = glass_from_shape ? inner : min(glass, inner);
		}
		ALBEDO = mix(texture(fill, auv).rgb, texture(view, vuv).rgb, glass);
		ALPHA = solid;
	} else {
		vec4 a = texture(art, auv);
		// the art's surround (its corner colour) is not drawn: only the frame and the glass are
		vec4 surround = texelFetch(art, ivec2(0, 0), 0);
		// within 0.1 of it: a pack's upscale (and its block compression) leaves the surround near, not exactly, the
		// original's colour; the art's other colours are a third of the way across the cube or more
		if (a.a >= 0.5 && distance(a.rgb, surround.rgb) < 0.1) discard;
		vec3 v = faithful ? texture(view_nearest, vuv).rgb : texture(view, vuv).rgb;
		ALBEDO = a.a >= 0.5 ? a.rgb : v;
		ALPHA = 1.0;
	}
}
