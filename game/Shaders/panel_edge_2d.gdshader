// A keyed panel of the 2D dash in the modern look (the compass): its live art cut along the smooth outline of the art's
// mask (MirrorEdge.KeyField) at half over one screen pixel and blended over what is behind; a keyed texel inside the
// outline takes the colour of the art's beside it (KeyFill). The art's top left clip of art_size (original pixels), its
// rows stored bottom-up. The 1997 look draws the art's own pixels (ExtInstruments._Draw).
shader_type canvas_item;

uniform sampler2D art : source_color, filter_nearest;
uniform sampler2D edge : filter_linear;
uniform sampler2D fill : source_color, filter_nearest;
uniform vec2 clip = vec2(64.0, 64.0);
uniform vec2 art_size = vec2(64.0, 64.0);

float cut(float f) {
	return clamp((f - 0.5) / max(fwidth(f), 1e-4) + 0.5, 0.0, 1.0);
}

void fragment() {
	vec2 auv = vec2(UV.x * clip.x / art_size.x, 1.0 - UV.y * clip.y / art_size.y);
	vec4 a = texture(art, auv);
	COLOR = vec4(a.a >= 0.5 ? a.rgb : texture(fill, auv).rgb, cut(texture(edge, auv).r));
}
