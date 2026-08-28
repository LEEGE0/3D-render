shader_type spatial;
render_mode unshaded, cull_disabled;

uniform vec4 base_color : source_color = vec4(1.0);
uniform float current_time_normalized : hint_range(0.0, 1.0) = 0.0;

void fragment() {
	float brightness = UV.x <= current_time_normalized ? 1.0 : 0.45;
	ALBEDO = base_color.rgb * brightness;
	ALPHA = base_color.a;
}
