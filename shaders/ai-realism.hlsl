// AI realism — two-pass capture enhancer.
// Pass 0: texture structure, shadows, skin / metal detect.
// Pass 1: extra reflections + AgX real-life tonemap (needs presenter PassIndex).
// Required entry: float3 Shader(float2 uv, float3 color)

static const float AiStrength = 0.82;
static const float AiDenoise = 0.24;
static const float AiDetail = 0.34;
static const float AiBloom = 0.14;
static const float AiHaze = 0.12;
static const float AiGrain = 0.012;
static const float AiBump = 3.1;
static const float AiStructure = 0.64;
static const float AiShadow = 0.60;
static const float AiReflect = 0.52;
static const float AiExposure = 1.05;

float AiLuma(float3 c)
{
    return dot(c, float3(0.2126, 0.7152, 0.0722));
}

float3 AiToLin(float3 c)
{
    return pow(max(c, 0.0), 2.2);
}

float3 AiToSrgb(float3 c)
{
    return pow(max(c, 0.0), 1.0 / 2.2);
}

float AiHash(float2 p)
{
    return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453);
}

float3 AiCurve(float3 x, float a)
{
    [unroll]
    for (int i = 0; i < 8; i++)
        x = x + a * x * (1.0 - x);
    return saturate(x);
}

float3 AiSample(float2 uv)
{
    return saturate(SampleTex(saturate(uv)));
}

float AiHeight(float2 uv)
{
    return AiLuma(AiToLin(AiSample(uv)));
}

float3 AiNormalFrom(float hL, float hR, float hU, float hD, float bump)
{
    return normalize(float3((hL - hR) * bump, (hU - hD) * bump, 1.0));
}

float3 AiAgX(float3 val)
{
    const float3x3 mat = float3x3(
        0.84247906, 0.0784336, 0.07922375,
        0.07732807, 0.87846896, 0.07916613,
        0.07732046, 0.07891834, 0.87901266);
    const float3x3 inv = float3x3(
        1.1968790, -0.09802088, -0.09902975,
        -0.08357936, 1.1765763, -0.08463232,
        -0.08233705, -0.08180723, 1.1658626);
    const float minEv = -12.47393;
    const float maxEv = 4.026069;
    val = max(mul(mat, val * AiExposure), 1e-10);
    val = (clamp(log2(val), minEv, maxEv) - minEv) / (maxEv - minEv);
    float3 x2 = val * val;
    float3 x4 = x2 * x2;
    val = 15.5 * x4 * x2 - 40.14 * x4 * val + 31.96 * x4
        - 6.868 * x2 * val + 0.4298 * x2 + 0.1191 * val - 0.00232;
    val = mul(inv, val);
    return max(val, 0.0);
}

float3 AiDetect(float3 srgb, float rough, float edge)
{
    float r = srgb.r, g = srgb.g, b = srgb.b;
    float y = 0.299 * r + 0.587 * g + 0.114 * b;
    float cb = (b - y) * 0.565 + 0.5;
    float cr = (r - y) * 0.713 + 0.5;
    float skin = saturate(1.0 - abs(cr - 0.57) * 11.0) * saturate(1.0 - abs(cb - 0.41) * 9.0);
    skin *= saturate((r - g) * 9.0 + 0.15) * saturate((g - b) * 7.0 + 0.25);
    skin *= smoothstep(0.07, 0.18, y) * smoothstep(0.93, 0.78, y);
    skin *= lerp(1.0, 0.35, edge);
    float face = skin * saturate(1.15 - rough * 2.4) * saturate(1.0 - edge * 1.6);

    float chroma = length(srgb - y.xxx);
    float steel = saturate((0.055 - chroma) * 22.0) * smoothstep(0.12, 0.48, y);
    float gold = saturate((r - g) * 5.0) * saturate((g - b) * 4.0)
        * saturate((chroma - 0.04) * 14.0) * saturate((0.20 - chroma) * 10.0);
    float metal = saturate(max(steel, gold * 0.9)) * (1.0 - skin) * lerp(0.55, 1.0, 1.0 - rough);
    return float3(skin, face, metal);
}

float3 AiLight(float2 uv, float3 color, bool refine)
{
    color = saturate(color);
    float2 texel = 1.0 / max(InputSize, 1.0);
    float3 hist = saturate(PrevTex.SampleLevel(Samp, uv, 0).rgb);
    float jump = length(color - hist);
    float keep = exp(-jump * 10.0);
    float3 stable = lerp(color, hist, AiDenoise * keep * (refine ? 0.2 : 0.45));

    float lum[9];
    float3 acc = 0;
    float wsum = 0;
    float meanL = 0;
    float3 c0 = AiToLin(stable);
    float l0 = AiLuma(c0);
    int idx = 0;
    [unroll]
    for (int y = -1; y <= 1; y++)
    {
        [unroll]
        for (int x = -1; x <= 1; x++)
        {
            float2 off = float2(x, y);
            float3 nbr = AiToLin(AiSample(uv + off * texel));
            float dl = AiLuma(nbr) - l0;
            lum[idx] = AiLuma(nbr);
            meanL += lum[idx];
            float w = exp(-dot(off, off) * 0.55 - dl * dl * 48.0);
            acc += nbr * w;
            wsum += w;
            idx++;
        }
    }
    lum[4] = l0;
    meanL /= 9.0;
    float varL =
        (lum[0] - meanL) * (lum[0] - meanL) + (lum[1] - meanL) * (lum[1] - meanL) +
        (lum[2] - meanL) * (lum[2] - meanL) + (lum[3] - meanL) * (lum[3] - meanL) +
        (lum[4] - meanL) * (lum[4] - meanL) + (lum[5] - meanL) * (lum[5] - meanL) +
        (lum[6] - meanL) * (lum[6] - meanL) + (lum[7] - meanL) * (lum[7] - meanL) +
        (lum[8] - meanL) * (lum[8] - meanL);
    float3 blur = acc / max(wsum, 1e-5);
    float3 denoise = lerp(c0, blur, AiDenoise * (refine ? 0.25 : 0.5));
    float rough = saturate(varL * 22.0);
    float edge = saturate(length(denoise - blur) * 6.0);
    float3 cls = AiDetect(stable, rough, edge);
    float skin = cls.x, face = cls.y, metal = cls.z;

    float bump = AiBump * lerp(1.0, 0.35, face) * lerp(1.0, 1.25, metal);
    float3 nFine = AiNormalFrom(lum[3], lum[5], lum[1], lum[7], bump);
    float hL = AiHeight(uv + float2(-3, 0) * texel);
    float hR = AiHeight(uv + float2( 3, 0) * texel);
    float hU = AiHeight(uv + float2(0, -3) * texel);
    float hD = AiHeight(uv + float2(0,  3) * texel);
    float3 nBroad = AiNormalFrom(hL, hR, hU, hD, bump * 0.55);
    float3 n = normalize(lerp(nBroad, nFine, lerp(0.62, 0.38, face)));

    float exposure = saturate(0.42 - AiLuma(blur));
    float3 lifted = AiCurve(denoise, lerp(0.04, 0.16, exposure));
    float3 detail = denoise - blur;
    float structAmt = AiStructure * lerp(1.15, 0.4, edge) * lerp(1.0, 0.45, face);
    float3 residual = lifted + detail * AiDetail * lerp(1.2, 0.45, edge) * lerp(1.0, 0.55, face);

    float3 light = normalize(float3(-0.42, 0.58, 0.70));
    float ndotl = saturate(dot(n, light));
    float wrap = saturate(dot(n, light) * 0.5 + 0.5);
    float shade = lerp(0.70, 1.12, lerp(wrap, ndotl, lerp(0.62, 0.28, face)));
    residual *= lerp(1.0, shade, structAmt);
    residual *= lerp(1.0, lerp(0.62, 0.92, ndotl), metal * 0.85);
    residual *= lerp(float3(1, 1, 1), float3(1.08, 0.94, 0.90), face * 0.4);
    residual = lerp(residual, residual * float3(1.10, 0.78, 0.72), saturate(1.0 - wrap) * face * 0.45);

    float3 view = float3(0.0, 0.0, 1.0);
    float3 halfV = normalize(light + view);
    float specPow = lerp(10.0, lerp(36.0, 64.0, metal), 1.0 - rough);
    float spec = pow(saturate(dot(n, halfV)), specPow) * lerp(0.18, lerp(0.06, 0.55, metal), rough);
    spec *= lerp(1.0, 0.08, face);
    residual += spec * structAmt * lerp(0.7, 1.2, saturate(l0 * 1.4));

    float ao = saturate((l0 - lum[0]) * 5.0)
        + saturate((l0 - lum[1]) * 5.0)
        + saturate((l0 - lum[2]) * 5.0)
        + saturate((l0 - lum[3]) * 5.0)
        + saturate((l0 - lum[5]) * 5.0)
        + saturate((l0 - lum[6]) * 5.0)
        + saturate((l0 - lum[7]) * 5.0)
        + saturate((l0 - lum[8]) * 5.0);
    ao = saturate(ao / 8.0) * lerp(1.0, 0.4, face);

    float2 sdir = light.xy;
    if (dot(sdir, sdir) < 1e-6)
        sdir = float2(-0.4, 0.6);
    sdir = normalize(sdir) * texel * (refine ? 3.2 : 2.2);
    float contact = 1.0;
    [unroll]
    for (int s = 1; s <= 5; s++)
    {
        float hs = AiHeight(uv + sdir * s);
        float rise = hs - l0 - 0.018 * s;
        float occ = saturate(rise * 3.2) * (1.15 - s / 6.0);
        contact *= 1.0 - occ * lerp(0.85, 1.05, metal);
    }
    contact = saturate(contact);
    float depth = refine ? saturate(l0) : BufDepth(uv);
    float near = saturate(1.0 - depth);
    float shadow = lerp(1.0, contact * (1.0 - ao * 0.55), AiShadow * lerp(0.75, 1.15, near) * lerp(1.0, 0.55, face));
    residual *= shadow;

    float3 eye = float3(0.0, 0.0, -1.0);
    float3 rd = reflect(eye, n);
    float rxy = length(rd.xy);
    float fres = pow(saturate(1.0 - n.z), lerp(2.15, 1.35, metal));
    float glossy = saturate((1.0 - rough * 1.35) * (1.0 - edge * 0.8));
    float reflAmt = AiReflect * fres * glossy * lerp(0.55, 1.05, saturate(0.2 + l0));
    reflAmt *= lerp(0.8, 1.2, depth);
    reflAmt *= lerp(1.0, 0.04, face);
    reflAmt *= lerp(1.0, 2.15, metal);
    if (refine)
        reflAmt *= 1.35;
    if (reflAmt > 0.015 && rxy > 0.02)
    {
        float2 rdir = (rd.xy / rxy) * texel;
        float3 refl = 0;
        float rsum = 0;
        float span = refine ? 5.5 : 4.0;
        [unroll]
        for (int t = 1; t <= 8; t++)
        {
            float skip = (refine || t <= 6) ? 1.0 : 0.0;
            float2 p = uv + rdir * (1.5 + t * span);
            float border = smoothstep(0.0, 0.06, min(min(p.x, p.y), min(1.0 - p.x, 1.0 - p.y)));
            p = saturate(p);
            float3 sc = AiToLin(AiSample(p));
            float hs = AiLuma(sc);
            float hit = saturate((hs - (l0 + rd.z * t * 0.035) - 0.02) * 6.0);
            float w = (1.0 / t) * lerp(0.45, 1.35, hit) * border * skip;
            refl += sc * w;
            rsum += w;
        }
        refl /= max(rsum, 1e-5);
        residual = lerp(residual, lerp(residual, refl, lerp(0.82, 0.95, metal)), reflAmt);
    }

    float3 bloom = 0;
    [unroll]
    for (int b = 0; b < 4; b++)
    {
        float2 dir = b < 2 ? float2(b == 0 ? 1 : -1, 0) : float2(0, b == 2 ? 1 : -1);
        bloom += max(AiToLin(AiSample(uv + dir * texel * 2.5)) - 0.62, 0.0);
    }
    residual += (bloom / 4.0) * AiBloom * lerp(1.0, 1.4, metal) * lerp(1.0, 0.5, face);

    if (!refine)
    {
        float3 hazeCol = float3(0.55, 0.62, 0.72);
        residual = lerp(residual, lerp(residual, hazeCol * max(AiLuma(residual), 0.08), 0.5), smoothstep(0.45, 1.0, depth) * AiHaze * (1.0 - face));
    }

    return residual;
}

float3 AiFinish(float3 linearColor, float2 uv, float3 srgbHint)
{
    float3 mapped = AiAgX(linearColor);
    mapped = AiToSrgb(mapped);
    float l = AiLuma(mapped);
    mapped *= lerp(float3(0.97, 0.99, 1.04), float3(1, 1, 1), smoothstep(0.12, 0.48, l));
    mapped *= lerp(float3(1, 1, 1), float3(1.04, 1.02, 0.96), smoothstep(0.55, 0.95, l));
    float3 cls = AiDetect(srgbHint, 0.2, 0.2);
    mapped = lerp(mapped, mapped * float3(1.04, 0.99, 0.96), cls.y * 0.25);
    mapped = lerp(mapped, lerp(AiLuma(mapped).xxx, mapped, 0.92), cls.z * 0.35);
    mapped = lerp(l.xxx, mapped, 1.02);
    float gn = AiHash(uv * OutputSize + l * 17.0);
    mapped += (gn - 0.5) * AiGrain * lerp(1.0, 0.4, cls.y);
    return saturate(mapped);
}

float3 Shader(float2 uv, float3 color)
{
    bool refine = PassIndex >= 0.5;
    float3 lit = AiLight(uv, color, refine);
    float3 film = AiFinish(lit, uv, color);
    if (refine)
        film = saturate(lerp(color, film, 0.72));
    return saturate(lerp(color, film, AiStrength));
}
