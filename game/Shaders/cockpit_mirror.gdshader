// The rear-view mirror's face: the view behind, rendered by the
// mirror camera, shown through the transparent pixels of the mirror's own art
// (zmiri101), which is drawn over it keyed; the art's surround is see-through,
// as the original shows it. The view is mirrored left-right,
// and turned to the art's bottom-up row order. Pre-lit like the rest of the
// cockpit: no light reaches it. The modern look cuts the frame's outline and the glass smooth (MirrorEdge: edge holds
// the masks' smooth outlines as distance fields, fill the art with its frame colours spread), blended over what is behind; the 1997 look keys the
// art's own pixels.
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

// a field's 0.5 contour over one screen pixel
float cut(float f) {
	return clamp((f - 0.5) / max(fwidth(f), 1e-4) + 0.5, 0.0, 1.0);
}

void fragment() {
	if (hidden) discard;
	vec2 vuv = vec2(1.0 - UV.x, 1.0 - UV.y);
	if (smooth_edge && !faithful) {
		vec2 e = texture(edge, UV).rg;
		float solid = cut(e.r), g = cut(e.g);
		if (solid <= 0.0) discard;
		// the art's own marks on the glass (the cross-hair) are drawn from it, not cut
		float glass = texture(art, UV).a >= 0.5 && e.g > 0.75 ? 0.0 : g;
		ALBEDO = mix(texture(fill, UV).rgb, texture(view, vuv).rgb, glass);
		ALPHA = solid;
	} else {
		vec4 a = texture(art, UV);
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
