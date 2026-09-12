// Folder shaders — each .hlsl file is one Home overlay technique.
// Required entry:
//   float3 Shader(float2 uv, float3 color)
// Available:
//   InputTex, PrevTex, Samp, SampleTex(uv)
//   InputSize, OutputSize, BufDepth(uv), BufFlow(uv)

float3 Shader(float2 uv, float3 color)
{
    float3 tint = float3(1.05, 1.00, 0.96);
    return saturate(color * tint);
}
