// An upscaled .ogv movie's picture as its decoder thread packs it (I76.Formats.Video.OgvDecoder): one R8 texture, the luma
// plane on top, under it each chroma row as Cb then Cr side by side. The colour made here (BT.601, limited range, as Theora's
// own and the engine's player convert), chroma bilinear from its centred samples; each plane's lookups held inside its own
// region so the filter never mixes neighbours (luma with chroma, Cb with Cr).
shader_type canvas_item;

uniform vec2 luma_size = vec2(1920.0, 1440.0);
uniform vec2 packed_size = vec2(1920.0, 2160.0);

void fragment() {
	vec2 p = UV * luma_size;
	vec2 yp = clamp(p, vec2(0.5), luma_size - vec2(0.5));
	float y = texture(TEXTURE, yp / packed_size).r;
	vec2 chroma = vec2(packed_size.x * 0.5, packed_size.y - luma_size.y);
	vec2 c = clamp(p * 0.5, vec2(0.5), chroma - vec2(0.5));
	float u = texture(TEXTURE, vec2(c.x, luma_size.y + c.y) / packed_size).r;
	float v = texture(TEXTURE, vec2(chroma.x + c.x, luma_size.y + c.y) / packed_size).r;
	float l = (y - 16.0 / 255.0) * 1.164383;
	u -= 128.0 / 255.0;
	v -= 128.0 / 255.0;
	vec3 rgb = vec3(l + 1.596027 * v, l - 0.391762 * u - 0.812968 * v, l + 2.017232 * u);
	COLOR = vec4(clamp(rgb, 0.0, 1.0), 1.0);
}
