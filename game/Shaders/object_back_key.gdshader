// Cut-out faces drawn from their front only (a sign's two back-to-back faces, MeshBuilder.BackToBack; object.gdshaderinc);
// modern look. Keyed as object_disabled keys: the smooth outline or the alpha, through alpha-to-coverage.
shader_type spatial;
render_mode cull_back, alpha_to_coverage;
#define KEY_COVERAGE
#include "res://Shaders/object.gdshaderinc"
