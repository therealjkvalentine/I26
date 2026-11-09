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
		ALPHA = 1.0;
	} else if (smooth_edge && !faithful) {
		vec2 e = texture(edge, auv).rg;
		float solid = cut(e.r), g = cut(e.g);
		if (solid <= 0.0) discard;
		// the art's own marks on the glass (the cross-hair) are drawn from it, not cut
		float glass = texture(art, auv).a >= 0.5 && e.g > 0.75 ? 0.0 : g;
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
