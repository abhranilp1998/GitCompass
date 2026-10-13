// Local scene sampling for the experimental glass preview.
// Build with scripts/compile-liquid-shader.ps1. WPF loads the packaged ps_2_0 bytecode.
sampler2D Input : register(s0);
float Strength : register(c0);
float AspectRatio : register(c1);
float Radius : register(c2);

float4 main(float2 uv : TEXCOORD) : COLOR
{
    float aspect = max(AspectRatio, 0.01);
    float strength = saturate(Strength);
    float radius = min(max(Radius, 0.001), min(aspect, 1.0) * 0.5);
    float2 p = (uv - 0.5) * float2(aspect, 1.0);
    float2 q = abs(p) - (float2(aspect, 1.0) * 0.5 - radius);
    float2 corner = max(q, 0.0);
    float cornerLength = length(corner);
    float distance = cornerLength + min(max(q.x, q.y), 0.0) - radius;

    // The nearest rounded boundary supplies the glass surface normal.
    float horizontal = step(q.y, q.x);
    float2 straightNormal = float2(horizontal, 1.0 - horizontal);
    float2 normal = lerp(straightNormal, corner / max(cornerLength, 0.0001),
        step(0.0001, cornerLength)) * sign(p);
    normal.x /= aspect;

    float rim = saturate(1.0 + distance / 0.14);
    float lens = rim * rim * strength;
    float2 sampledUv = (uv - 0.5) * (1.0 - strength * 0.028) + 0.5;
    sampledUv -= normal * lens * 0.052;

    // Three close samples add restrained optical dispersion only at the rim.
    float2 dispersion = normal * lens * 0.0028;
    float4 center = tex2D(Input, saturate(sampledUv));
    float red = tex2D(Input, saturate(sampledUv + dispersion)).r;
    float blue = tex2D(Input, saturate(sampledUv - dispersion)).b;
    float mask = saturate(0.5 - distance * 500.0);
    return float4(red, center.g, blue, center.a) * mask;
}
