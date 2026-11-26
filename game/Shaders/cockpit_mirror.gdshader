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
// the ring's outer edge's shape (GlassRound.OuterShape): its own core and radius, the glass's or tighter in a corner
uniform vec2 pane_ocore[12];
uniform int pane_ocorners = 0;
uniform float pane_or = 0.0;
uniform float rim_w = 0.02;
uniform vec3 rim_colour : source_color = vec3(0.12, 0.12, 0.13);
// the moulding's outer edge rounded too (GlassRound.RimOut, [port] side_mirror_rim_out): the rounded outline widened by rim_out (the
// glass's mesh grown past the housing's outline to hold it, in the glass's plane), the moulding's shading spread across the whole
// ring; outside it nothing is drawn (the housing's painted glass is hidden), or with rim_dark the moulding's dark out to the housing's
// own outline (pane_hull, the pane's convex outline counter-clockwise, as pane_core)
uniform bool rim_round = false;
uniform float rim_out = 0.0;
// the ring's width from its outer edge to the glass's, in the glass's heights (MirrorRim.RingWidth: half the ring before the halving, the
// outer edge kept where it covered the housing's edges)
uniform float ring_w = 0.03;
uniform bool rim_dark = false;
// the band measured in from the outer edge's own outline (GlassRound.EvenShape: one width all round, the glass's edge the outer
// edge inset by it) rather than from the glass's rounded shape (the outer edge's corners tighter, the band thicker there)
uniform bool ring_from_outer = false;
// the glass's UV box the view is spread over (u0, v0, u1, v1): 0..1, or wider where the glass grows past its pane (GlassRound.PictureBox)
uniform vec4 view_box = vec4(0.0, 0.0, 1.0, 1.0);
uniform vec2 pane_hull[12];
uniform int pane_hull_n = 0;
// the ring the same width on screen all round (perspective stretches a glass near the screen's edge across its sight line; the
// sides of the Piranha's left glass drew half as wide again as its top): the outer edge is the rounded outline widened by rim_out,
// the glass's edge that many screen pixels inside it, the ring's width ring_w at the glass's scale as seen. The added
// glasses' frame the same (frame_t of the art's pixels). pane_mid, pane_a, pane_b: the glass's plane in the mesh's
// frame, its middle and the steps of one shape unit across and up (a picture unit of u times pane_aspect, v; or an art pixel), and
// half its box in those units
uniform vec3 pane_mid;
uniform vec2 pane_half = vec2(0.5);
uniform vec3 pane_a;
uniform vec3 pane_b;
uniform float frame_t = 0.0;
// screen pixels per shape unit across the glass's outline as seen (the mean round it): the same at every vertex
varying flat float px_per_unit;

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
// I76_MIRROR_RIM_KEY: the side mirrors' rim (the door glasses' moulding, the added glasses' frame) flat magenta, for measuring its width
uniform bool rim_key = false;
const vec3 KEY = vec3(0.5, 0.0, 0.5);
// and the pane's outline outside the ring (the housing's painted glass, hidden: what shows there is behind the housing) flat cyan
const vec3 KEY_GAP = vec3(0.0, 0.5, 0.5);
// and the ring where it reaches past that outline flat blue, for measuring how far past the housing it stands
const vec3 KEY_OVER = vec3(0.0, 0.0, 0.5);
// and the glass's view inside the ring flat dark green, so the glass's edge is a blend of two known colours
const vec3 KEY_GLASS = vec3(0.0, 0.35, 0.0);
// and dark yellow where the glass reaches past the outline, so the outline shows under the glass too
const vec3 KEY_GLASS_OUT = vec3(0.35, 0.35, 0.0);

#include "res://Shaders/mirror_view.gdshaderinc"

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

// the signed distance to the ring's outer core (negative inside): a convex counter-clockwise polygon
float sd_ocore(vec2 p) {
	float d = 1e9;
	bool inside = true;
	for (int i = 0; i < 12; i++) {
		if (i >= pane_ocorners) break;
		vec2 a = pane_ocore[i], b = pane_ocore[i + 1 < pane_ocorners ? i + 1 : 0];
		vec2 e = b - a, w = p - a;
		vec2 q = w - e * clamp(dot(w, e) / max(dot(e, e), 1e-12), 0.0, 1.0);
		d = min(d, dot(q, q));
		if (e.x * w.y - e.y * w.x < 0.0) inside = false;
	}
	return inside ? -sqrt(d) : sqrt(d);
}

// the signed distance to the pane's own outline (negative inside)
float sd_hull(vec2 p) {
	float d = 1e9;
	bool inside = true;
	for (int i = 0; i < 12; i++) {
		if (i >= pane_hull_n) break;
		vec2 a = pane_hull[i], b = pane_hull[i + 1 < pane_hull_n ? i + 1 : 0];
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

vec2 screen_px(vec3 local, mat4 modelview, mat4 projection, vec2 viewport) {
	vec4 c = projection * (modelview * vec4(local, 1.0));
	return c.xy / c.w * 0.5 * viewport;
}

void vertex() {
	px_per_unit = 0.0;
	if ((pane && rim_round) || (glass_from_shape && round_corners)) {
		// the scale across the outline at eight points round the glass's box (its edges' middles and corners), averaged, weighed
		// by how far they are on screen (fading out over 3 % of the window past its edge): the mean width a band of even
		// picture units drew round the glass as seen, so the even ring is that band's width there scaled, and a glass cut by
		// the window's edge (the Piranha's left at the foot of a 21:9 one) is measured where it is seen
		const float STEP = 0.01;
		float sum = 0.0, wsum = 0.0, mid_scale = 0.0;
		for (int i = -1; i <= 1; i++)
			for (int j = -1; j <= 1; j++) {
				vec3 at = pane_mid + pane_a * (float(i) * pane_half.x) + pane_b * (float(j) * pane_half.y);
				vec4 c = PROJECTION_MATRIX * (MODELVIEW_MATRIX * vec4(at, 1.0));
				if (c.w <= 0.0) continue;
				vec2 s = c.xy / c.w * 0.5 * VIEWPORT_SIZE;
				vec2 su = (screen_px(at + pane_a * STEP, MODELVIEW_MATRIX, PROJECTION_MATRIX, VIEWPORT_SIZE) - s) / STEP;
				vec2 sv = (screen_px(at + pane_b * STEP, MODELVIEW_MATRIX, PROJECTION_MATRIX, VIEWPORT_SIZE) - s) / STEP;
				float k = sqrt(abs(su.x * sv.y - su.y * sv.x));
				if (i == 0 && j == 0) { mid_scale = k; continue; }
				// at an edge's middle or a corner, the scale across the outline there (the glass's units across it per screen
				// pixel, inverted): the width a band of even picture units drew there before
				vec2 n = normalize(vec2(float(i), float(j)));
				mat2 jac = mat2(su, sv);
				if (abs(determinant(jac)) > 1e-6) k = 1.0 / length(transpose(inverse(jac)) * n);
				vec2 out_by = max(abs(s) - 0.5 * VIEWPORT_SIZE, vec2(0.0)) / (0.03 * VIEWPORT_SIZE);
				float wt = clamp(1.0 - max(out_by.x, out_by.y), 0.0, 1.0);
				sum += k * wt; wsum += wt;
			}
		px_per_unit = wsum > 0.0 ? sum / wsum : mid_scale;
	}
}

void fragment() {
	if (hidden) discard;
	vec2 auv = art_uv(UV);
	vec2 vuv = vec2(view_x(UV.x), 1.0 - UV.y);
	if (pane) {
		vec2 pv = (UV - view_box.xy) / (view_box.zw - view_box.xy);
		vuv = vec2(view_x(pv.x), 1.0 - pv.y);
		ALBEDO = view_at(vuv);
		ALPHA = 1.0;
		if (pane_round && rim_round) {
			vec2 p = vec2(UV.x * pane_aspect, UV.y);
			float sdo = sd_ocore(p) - pane_or;
			float sd = ring_from_outer ? sdo : sd_core(p) - pane_r;
			// screen pixels in from the ring's outer edge: the outline's distance over its gradient on screen
			float px = (rim_out - sd) / max(length(vec2(dFdx(sd), dFdy(sd))), 1e-7);
			float w = max(ring_w * px_per_unit, 1.0);
			// the moulding across the whole ring: 0 at its outer edge, 1 at the glass's
			float t = clamp(px / w, 0.0, 1.0);
			float shade = mix(0.55, 1.35, smoothstep(0.15, 0.8, t)) - 0.6 * smoothstep(0.88, 1.0, t);
			float hull_in = rounded(sd_hull(p));
			if (rim_key) ALBEDO = mix(KEY_GLASS_OUT, KEY_GLASS, hull_in);
			ALBEDO = mix(rim_key ? mix(KEY_OVER, KEY, hull_in) : rim_colour * shade, ALBEDO, clamp(px - w + 0.5, 0.0, 1.0));
			// the outer edge: the outline eroded by pane_or and widened by it and rim_out (GlassRound.OuterShape), its corners tighter
			// than the glass's where the outline's corners (the housing's edge) would otherwise stick out past it as points; along
			// the straight runs it is the glass's rounded shape's, so the band is as wide there and thicker in such a corner; with
			// ring_from_outer (the default, GlassRound.EvenShape) the glass's edge above is measured from this edge, the band one width
			float px_out = (rim_out - sdo) / max(length(vec2(dFdx(sdo), dFdy(sdo))), 1e-7);
			float solid = clamp(px_out + 0.5, 0.0, 1.0);
			// keyed: the outline outside the ring cyan (none should be left)
			if (rim_key) { ALBEDO = mix(KEY_GAP, ALBEDO, solid); solid = max(solid, hull_in); }
			ALPHA = rim_dark ? max(solid, hull_in) : solid;
			if (ALPHA <= 0.0) discard;
		} else if (pane_round) {
			float sd = sd_core(vec2(UV.x * pane_aspect, UV.y)) - pane_r;
			// the moulding across the rim: 0 at the rounded shape's edge, 1 at the glass's
			float t = clamp(-sd / rim_w, 0.0, 1.0);
			float shade = mix(0.55, 1.35, smoothstep(0.15, 0.8, t)) - 0.6 * smoothstep(0.88, 1.0, t);
			ALBEDO = mix(rim_key ? KEY : rim_colour * shade, ALBEDO, rounded(sd + rim_w));
		}
	} else if (smooth_edge && !faithful && glass_from_shape && round_corners) {
		// the added glasses: the rounded outline, the frame frame_t of the art's pixels wide at the glass's scale as seen and that
		// many screen pixels all round (the art's own outline and its frame's narrow foot not used)
		float so = sd_quad(auv * art_px, outer_tl, outer_tr, outer_br, outer_bl) - outer_r;
		float px = -so / max(length(vec2(dFdx(so), dFdy(so))), 1e-7);
		float w = max(frame_t * px_per_unit, 1.0);
		float solid = clamp(px + 0.5, 0.0, 1.0);
		if (solid <= 0.0) discard;
		// the frame's own colours where the art has its frame, the rear frame's mean colour where the rounded outline reaches past it
		vec3 frame = mix(rim_colour, texture(fill, auv).rgb, cut(texture(edge, auv).r));
		ALBEDO = mix(rim_key ? KEY : frame, view_at(vuv), clamp(px - w + 0.5, 0.0, 1.0));
		ALPHA = solid;
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
		ALBEDO = mix(rim_key && glass_from_shape ? KEY : texture(fill, auv).rgb, view_at(vuv), glass);
		ALPHA = solid;
	} else {
		vec4 a = texture(art, auv);
		// the art's surround (its corner colour) is not drawn: only the frame and the glass are
		vec4 surround = texelFetch(art, ivec2(0, 0), 0);
		// within 0.1 of it: a pack's upscale (and its block compression) leaves the surround near, not exactly, the
		// original's colour; the art's other colours are a third of the way across the cube or more
		if (a.a >= 0.5 && distance(a.rgb, surround.rgb) < 0.1) discard;
		vec3 v = faithful ? texture(view_nearest, vuv).rgb : view_at(vuv);
		ALBEDO = a.a >= 0.5 ? a.rgb : v;
		ALPHA = 1.0;
	}
}
