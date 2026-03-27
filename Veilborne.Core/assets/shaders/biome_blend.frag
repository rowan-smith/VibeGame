#version 330

in vec2 fragTexCoord;
in vec4 fragColor; // vertex color, use alpha as blend weight

uniform sampler2D texture0; // primary albedo (MATERIAL_MAP_ALBEDO)
uniform sampler2D texture1; // secondary albedo (MATERIAL_MAP_METALNESS reused)

out vec4 finalColor;

void main()
{
    vec4 c0 = texture(texture0, fragTexCoord);
    vec4 c1 = texture(texture1, fragTexCoord);
    float w = clamp(fragColor.a, 0.0, 1.0);
    finalColor = mix(c0, c1, w);
}
