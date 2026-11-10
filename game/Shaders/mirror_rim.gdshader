// The moulding inside a side mirror's housing (MirrorRim): the rear mirror frame's colour, dark at the housing's edge, rising to a lit
// lip at the glass with a thin shadow line where the glass sits (UV.y 0 at the housing's edge, 1 at the glass). Pre-lit like the rest
// of the cockpit.
shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_always;

uniform vec3 colour : source_color = vec3(0.12, 0.12, 0.13);
uniform bool hidden = false;

void fragment() {
	if (hidden) discard;
	float t = UV.y;
	// dark against the housing, the lip lit toward the glass, a shadow line at the glass's edge
	float shade = mix(0.55, 1.35, smoothstep(0.15, 0.8, t)) - 0.6 * smoothstep(0.88, 1.0, t);
	ALBEDO = colour * shade;
	ALPHA = 1.0;
}
