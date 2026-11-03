// The 1997 look's machine gun round (tracer style 2): the original's one-pixel line, drawn `px` pixels wide so it still
// reads on today's taller windows (TracerLength.PixelWidth: one pixel per 480 rows). A quad along the round's flight,
// widened across it in screen space, so it is the same width however far the round is.
shader_type spatial;
render_mode unshaded, cull_disabled, shadows_disabled;

uniform vec4 color : source_color = vec4(1.0, 0.85, 0.4, 1.0);
uniform float px = 1.0;

void vertex() {
	mat4 mvp = PROJECTION_MATRIX * MODELVIEW_MATRIX;
	vec4 a = mvp * vec4(0.0, 0.0, -0.5, 1.0);
	vec4 b = mvp * vec4(0.0, 0.0, 0.5, 1.0);
	vec4 p = mvp * vec4(0.0, 0.0, VERTEX.z, 1.0);
	// the line's direction on the screen, in pixels; across it the quad's x (-0.5..0.5) spans px pixels
	vec2 d = (b.xy / b.w - a.xy / a.w) * VIEWPORT_SIZE;
	float l = length(d);
	vec2 n = l > 1e-4 ? vec2(-d.y, d.x) / l : vec2(1.0, 0.0);
	p.xy += n * (VERTEX.x * px) * 2.0 / VIEWPORT_SIZE * p.w;
	POSITION = p;
}

void fragment() {
	ALBEDO = color.rgb;
}
