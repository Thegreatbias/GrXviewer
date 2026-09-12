using System.Runtime.InteropServices;
using Vortice.D3DCompiler;

namespace CpuScreenViewer;

internal static class UpscaleShaders
{
    public const string Hlsl = """
cbuffer Params : register(b0)
{
    float2 InputSize;
    float2 OutputSize;
    float Sharpness;
    uint Mode;
    float BlendT;
    uint FxMask;
    float CasAmount;
    float Brightness;
    float Contrast;
    float Saturation;
    float VignetteAmount;
    float VignetteRadius;
    uint BufferView;
    float DepthMix;
    float FogAmount;
    float FogStart;
    float DofAmount;
    float DofFocus;
    float MotionBlur;
    uint UserMask;
    float PassIndex;
    float _padPass;
};

Texture2D InputTex : register(t0);
Texture2D PrevTex : register(t1);
SamplerState Samp : register(s0);

struct VSOut
{
    float4 pos : SV_Position;
    float2 uv : TEXCOORD0;
};

VSOut VSMain(uint id : SV_VertexID)
{
    float2 uv = float2((id << 1) & 2, id & 2);
    VSOut o;
    o.pos = float4(uv * float2(2.0, -2.0) + float2(-1.0, 1.0), 0.0, 1.0);
    o.uv = uv;
    return o;
}

float3 SampleTex(float2 uv)
{
    uv = saturate(uv);
    float3 curr = InputTex.SampleLevel(Samp, uv, 0).rgb;
    if (BlendT >= 0.999)
        return curr;
    float3 prev = PrevTex.SampleLevel(Samp, uv, 0).rgb;
    return lerp(prev, curr, saturate(BlendT));
}

float Lanczos(float x)
{
    x = abs(x);
    if (x < 1e-5)
        return 1.0;
    if (x >= 3.0)
        return 0.0;
    const float pi = 3.14159265;
    float pix = pi * x;
    return (3.0 * sin(pix) * sin(pix / 3.0)) / (pix * pix);
}

float3 Lanczos3(float2 uv)
{
    float2 pixel = uv * InputSize - 0.5;
    float2 center = floor(pixel);
    float3 acc = 0;
    float wsum = 0;
    [unroll]
    for (int y = -2; y <= 3; y++)
    {
        [unroll]
        for (int x = -2; x <= 3; x++)
        {
            float2 pos = center + float2(x, y);
            float2 d = pixel - pos;
            float w = Lanczos(d.x) * Lanczos(d.y);
            acc += SampleTex((pos + 0.5) / InputSize) * w;
            wsum += w;
        }
    }
    return acc / max(wsum, 1e-5);
}

float FsrLuma(float3 c)
{
    return c.g * 0.5 + (c.r + c.b) * 0.25;
}

void FsrEasuTap(inout float3 aC, inout float aW, float2 off, float2 dir, float2 len, float lob, float clp, float3 c)
{
    float2 v = float2(dot(off, dir), dot(off, float2(-dir.y, dir.x)));
    v *= len;
    float d2 = min(dot(v, v), clp);
    float wB = 0.4 * d2 - 1.0;
    float wA = lob * d2 - 1.0;
    wB *= wB;
    wA *= wA;
    float w = (1.5625 * wB - 0.5625) * wA;
    aC += c * w;
    aW += w;
}

void FsrEasuSet(inout float2 dir, inout float len, float2 pp, bool biS, bool biT, bool biU, bool biV, float lA, float lB, float lC, float lD, float lE)
{
    float w = 0;
    if (biS) w = (1.0 - pp.x) * (1.0 - pp.y);
    if (biT) w = pp.x * (1.0 - pp.y);
    if (biU) w = (1.0 - pp.x) * pp.y;
    if (biV) w = pp.x * pp.y;
    float dcx = lD - lB;
    float dcy = lC - lA;
    dir.x += dcx * w;
    dir.y += dcy * w;
    len += w * sqrt(dcx * dcx + dcy * dcy);
}

float3 FsrEasu(float2 uv)
{
    float2 con0 = InputSize / OutputSize;
    float2 p = uv * OutputSize * con0 - 0.5;
    float2 fp = floor(p);
    float2 pp = p - fp;
    float2 base = (fp + 0.5) / InputSize;
    float2 texel = 1.0 / InputSize;

    float3 b = SampleTex(base + float2( 0, -1) * texel);
    float3 c = SampleTex(base + float2( 1, -1) * texel);
    float3 e = SampleTex(base + float2(-1,  0) * texel);
    float3 f = SampleTex(base);
    float3 g = SampleTex(base + float2( 1,  0) * texel);
    float3 h = SampleTex(base + float2( 2,  0) * texel);
    float3 i = SampleTex(base + float2(-1,  1) * texel);
    float3 j = SampleTex(base + float2( 0,  1) * texel);
    float3 k = SampleTex(base + float2( 1,  1) * texel);
    float3 l = SampleTex(base + float2( 2,  1) * texel);
    float3 n = SampleTex(base + float2( 0,  2) * texel);
    float3 o = SampleTex(base + float2( 1,  2) * texel);

    float lb = FsrLuma(b);
    float lc = FsrLuma(c);
    float le = FsrLuma(e);
    float lf = FsrLuma(f);
    float lg = FsrLuma(g);
    float lh = FsrLuma(h);
    float li = FsrLuma(i);
    float lj = FsrLuma(j);
    float lk = FsrLuma(k);
    float ll = FsrLuma(l);
    float ln = FsrLuma(n);
    float lo = FsrLuma(o);

    float2 dir = 0;
    float len = 0;
    FsrEasuSet(dir, len, pp, true,  false, false, false, lb, le, lf, lg, lj);
    FsrEasuSet(dir, len, pp, false, true,  false, false, lc, lf, lg, lh, lk);
    FsrEasuSet(dir, len, pp, false, false, true,  false, lf, li, lj, lk, ln);
    FsrEasuSet(dir, len, pp, false, false, false, true,  lg, lj, lk, ll, lo);

    float2 dir2 = dir * dir;
    float dirL = dir2.x + dir2.y;
    if (dirL < 1.0 / 32768.0)
        dir = float2(1, 0);
    else
        dir *= rsqrt(dirL);
    len = len * 0.5;
    len *= len;
    float stretch = dot(dir, dir) / max(max(abs(dir.x), abs(dir.y)), 1e-5);
    float2 len2 = float2(1.0 + (stretch - 1.0) * len, 1.0 - 0.5 * len);
    float lob = 0.5 - 0.29 * len;
    float clp = 1.0 / lob;

    float3 aC = 0;
    float aW = 0;
    FsrEasuTap(aC, aW, float2( 0,-1) - pp, dir, len2, lob, clp, b);
    FsrEasuTap(aC, aW, float2( 1,-1) - pp, dir, len2, lob, clp, c);
    FsrEasuTap(aC, aW, float2(-1, 0) - pp, dir, len2, lob, clp, e);
    FsrEasuTap(aC, aW, float2( 0, 0) - pp, dir, len2, lob, clp, f);
    FsrEasuTap(aC, aW, float2( 1, 0) - pp, dir, len2, lob, clp, g);
    FsrEasuTap(aC, aW, float2( 2, 0) - pp, dir, len2, lob, clp, h);
    FsrEasuTap(aC, aW, float2(-1, 1) - pp, dir, len2, lob, clp, i);
    FsrEasuTap(aC, aW, float2( 0, 1) - pp, dir, len2, lob, clp, j);
    FsrEasuTap(aC, aW, float2( 1, 1) - pp, dir, len2, lob, clp, k);
    FsrEasuTap(aC, aW, float2( 2, 1) - pp, dir, len2, lob, clp, l);
    FsrEasuTap(aC, aW, float2( 0, 2) - pp, dir, len2, lob, clp, n);
    FsrEasuTap(aC, aW, float2( 1, 2) - pp, dir, len2, lob, clp, o);
    return aC / max(aW, 1e-5);
}

float3 FsrRcas(float2 uv, float3 e, float amount)
{
    float2 texel = 1.0 / InputSize;
    float3 b = SampleTex(uv + float2(0, -texel.y));
    float3 d = SampleTex(uv + float2(-texel.x, 0));
    float3 f = SampleTex(uv + float2(texel.x, 0));
    float3 h = SampleTex(uv + float2(0, texel.y));

    float bL = FsrLuma(b);
    float dL = FsrLuma(d);
    float eL = FsrLuma(e);
    float fL = FsrLuma(f);
    float hL = FsrLuma(h);

    float mn4 = min(bL, min(dL, min(eL, min(fL, hL))));
    float mx4 = max(bL, max(dL, max(eL, max(fL, hL))));
    float2 peak = float2(1.0 - mx4, mn4);
    float amp = saturate(min(peak.x, peak.y) / max(mx4, 1e-5));
    amp = sqrt(amp);
    float lobe = amp * lerp(-0.22, -0.11, saturate(amount));
    float nz = 0.25 * bL + 0.25 * dL + 0.25 * fL + 0.25 * hL - eL;
    nz = saturate(abs(nz) * 8.0);
    lobe *= nz + 1.0 - nz;

    return clamp((b + d + h + f) * lobe + e, 0.0, 1.0) / (1.0 + 4.0 * lobe);
}

float3 ApplyColor(float3 color)
{
    color += Brightness;
    color = (color - 0.5) * Contrast + 0.5;
    float g = dot(color, float3(0.2126, 0.7152, 0.0722));
    color = lerp(g.xxx, color, Saturation);
    return saturate(color);
}

float3 ApplyVignette(float2 uv, float3 color)
{
    float r = length(uv * 2.0 - 1.0);
    float v = smoothstep(VignetteRadius, 1.15, r);
    return color * (1.0 - saturate(VignetteAmount) * v);
}

static const float3 kLuma = float3(0.2126, 0.7152, 0.0722);

float LumaCurr(float2 uv)
{
    return dot(InputTex.SampleLevel(Samp, saturate(uv), 0).rgb, kLuma);
}

float LumaPrev(float2 uv)
{
    return dot(PrevTex.SampleLevel(Samp, saturate(uv), 0).rgb, kLuma);
}

float2 BufFlow(float2 uv)
{
    float2 texel = 1.0 / max(InputSize, 1.0);
    float cur = LumaCurr(uv);
    int2 best = int2(0, 0);
    float bestE = abs(cur - LumaPrev(uv));
    [unroll]
    for (int s = 0; s < 3; s++)
    {
        int rad = s == 0 ? 6 : s == 1 ? 2 : 1;
        [unroll]
        for (int i = 0; i < 8; i++)
        {
            int2 dir;
            if (i < 2) dir = int2(i == 0 ? -1 : 1, 0);
            else if (i < 4) dir = int2(0, i == 2 ? -1 : 1);
            else if (i == 4) dir = int2(-1, -1);
            else if (i == 5) dir = int2(1, -1);
            else if (i == 6) dir = int2(-1, 1);
            else dir = int2(1, 1);
            int2 mv = best + dir * rad;
            float e = abs(cur - LumaPrev(uv - float2(mv) * texel));
            if (e < bestE)
            {
                bestE = e;
                best = mv;
            }
        }
    }
    return float2(best);
}

float BufDepth(float2 uv)
{
    float mag = length(BufFlow(uv)) / 16.0;
    float motionFar = saturate(1.0 - mag);
    float lumaFar = LumaCurr(uv);
    return saturate(lerp(motionFar, lumaFar, saturate(DepthMix)));
}

float3 HsvRgb(float3 c)
{
    float4 k = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
    float3 p = abs(frac(c.xxx + k.xyz) * 6.0 - k.www);
    return c.z * lerp(k.xxx, saturate(p - k.xxx), c.y);
}

float3 VizMotion(float2 uv)
{
    float2 mv = BufFlow(uv);
    float mag = length(mv);
    if (mag < 0.25)
        return float3(0.07, 0.07, 0.08);
    float ang = atan2(mv.y, mv.x);
    return HsvRgb(float3(ang * 0.159154943 + 0.5, 0.9, saturate(mag / 16.0)));
}

float3 VizHistory(float2 uv)
{
    float2 src = float2(uv.x < 0.5 ? uv.x * 2.0 : (uv.x - 0.5) * 2.0, uv.y);
    float3 rgb = uv.x < 0.5
        ? InputTex.SampleLevel(Samp, saturate(src), 0).rgb
        : PrevTex.SampleLevel(Samp, saturate(src), 0).rgb;
    if (abs(uv.x - 0.5) < 0.002)
        rgb = float3(1.0, 1.0, 1.0);
    return rgb;
}

float3 VizAll(float2 uv)
{
    bool left = uv.x < 0.5;
    bool top = uv.y < 0.5;
    float2 src = float2(left ? uv.x * 2.0 : (uv.x - 0.5) * 2.0, top ? uv.y * 2.0 : (uv.y - 0.5) * 2.0);
    float3 rgb;
    if (top && left)
        rgb = InputTex.SampleLevel(Samp, saturate(src), 0).rgb;
    else if (top)
        rgb = VizMotion(src);
    else if (left)
        rgb = BufDepth(src).xxx;
    else
        rgb = PrevTex.SampleLevel(Samp, saturate(src), 0).rgb;
    if (abs(uv.x - 0.5) < 0.002 || abs(uv.y - 0.5) < 0.002)
        rgb = float3(1.0, 1.0, 1.0);
    return rgb;
}

float3 ApplyFog(float2 uv, float3 color)
{
    float d = BufDepth(uv);
    float t = saturate(FogAmount) * smoothstep(FogStart, 1.0, d);
    return lerp(color, float3(0.62, 0.68, 0.74), t);
}

float3 ApplyDof(float2 uv, float3 color)
{
    float d = BufDepth(uv);
    float coc = saturate(abs(d - DofFocus) * DofAmount * 4.0);
    if (coc < 0.02)
        return color;
    float2 texel = 1.0 / max(InputSize, 1.0);
    float span = coc * 6.0;
    float3 acc = color;
    acc += SampleTex(uv + texel * float2( span, 0));
    acc += SampleTex(uv + texel * float2(-span, 0));
    acc += SampleTex(uv + texel * float2(0,  span));
    acc += SampleTex(uv + texel * float2(0, -span));
    acc += SampleTex(uv + texel * float2( span,  span) * 0.7);
    acc += SampleTex(uv + texel * float2(-span,  span) * 0.7);
    acc += SampleTex(uv + texel * float2( span, -span) * 0.7);
    acc += SampleTex(uv + texel * float2(-span, -span) * 0.7);
    return lerp(color, acc / 9.0, coc);
}

float3 ApplyMotionBlur(float2 uv, float3 color)
{
    float2 mv = BufFlow(uv) / max(InputSize, 1.0);
    float amt = saturate(MotionBlur);
    if (dot(mv, mv) < 1e-8 || amt < 0.01)
        return color;
    float3 acc = color;
    [unroll]
    for (int i = 1; i <= 4; i++)
        acc += SampleTex(uv - mv * amt * (i / 4.0));
    return acc / 5.0;
}

float4 PSMain(VSOut i) : SV_Target
{
    float2 uv = i.uv;
    if (BufferView == 1)
        return float4(InputTex.SampleLevel(Samp, saturate(uv), 0).rgb, 1.0);
    if (BufferView == 2)
        return float4(BufDepth(uv).xxx, 1.0);
    if (BufferView == 3)
        return float4(VizMotion(uv), 1.0);
    if (BufferView == 4)
        return float4(VizHistory(uv), 1.0);
    if (BufferView == 5)
        return float4(VizAll(uv), 1.0);
    float3 color;
    if (PassIndex >= 0.5)
        color = InputTex.SampleLevel(Samp, saturate(uv), 0).rgb;
    else if (Mode == 1)
        color = Lanczos3(uv);
    else if (Mode == 2)
    {
        bool oneToOne = all(abs(InputSize - OutputSize) < 0.5);
        color = oneToOne ? SampleTex(uv) : FsrEasu(uv);
        color = FsrRcas(uv, color, Sharpness);
    }
    else
        color = SampleTex(uv);
    if (PassIndex < 0.5)
    {
        if (FxMask & 1)
            color = FsrRcas(uv, color, CasAmount);
        if (FxMask & 32)
            color = ApplyMotionBlur(uv, color);
        if (FxMask & 16)
            color = ApplyDof(uv, color);
        if (FxMask & 8)
            color = ApplyFog(uv, color);
        if (FxMask & 2)
            color = ApplyColor(color);
        if (FxMask & 4)
            color = ApplyVignette(uv, color);
    }
    // USER_SHADERS
    return float4(color, 1.0);
}

float4 BlitPS(VSOut i) : SV_Target
{
    return InputTex.SampleLevel(Samp, saturate(i.uv), 0);
}

cbuffer StabParams : register(b1)
{
    uint StabDead;
    uint StabSoft;
    float2 OutSize;
    float4 CursorRect;
};

// Sample crop using destination pixel coords so a center viewport still maps the correct FOV.
float4 FoveaCenterPS(VSOut i) : SV_Target
{
    float2 uv = i.pos.xy / max(OutSize, 1.0);
    return InputTex.SampleLevel(Samp, saturate(uv), 0);
}

// Temporal checkerboard: update every other pixel each frame; reconstruct the rest.
float4 CheckerPS(VSOut i) : SV_Target
{
    float2 uv = saturate(i.pos.xy / max(OutSize, 1.0));
    uint2 p = uint2(i.pos.xy);
    bool update = ((p.x + p.y) & 1u) == (StabDead & 1u);
    float4 cur = InputTex.SampleLevel(Samp, uv, 0);
    if (update)
        return cur;

    float4 hist = PrevTex.SampleLevel(Samp, uv, 0);
    float2 texel = 1.0 / max(OutSize, 1.0);
    float4 n = InputTex.SampleLevel(Samp, saturate(uv + float2(texel.x, 0)), 0);
    float4 s = InputTex.SampleLevel(Samp, saturate(uv - float2(texel.x, 0)), 0);
    float4 e = InputTex.SampleLevel(Samp, saturate(uv + float2(0, texel.y)), 0);
    float4 w = InputTex.SampleLevel(Samp, saturate(uv - float2(0, texel.y)), 0);
    float4 spat = (n + s + e + w) * 0.25;
    return lerp(hist, spat, 0.4);
}

float4 StabPS(VSOut i) : SV_Target
{
    float4 c = InputTex.SampleLevel(Samp, saturate(i.uv), 0);
    float4 h = PrevTex.SampleLevel(Samp, saturate(i.uv), 0);
    if (StabSoft == 0)
        return c;
    float3 d = abs(c.rgb - h.rgb) * 255.0;
    float m = max(d.r, max(d.g, d.b));
    float dead = (float)StabDead;
    float soft = max((float)StabSoft, 1.0);
    if (m <= dead)
        return h;
    if (m >= dead + soft)
        return c;
    return lerp(h, c, saturate((m - dead) / soft));
}

float4 CursorPS(VSOut i) : SV_Target
{
    float2 pix = i.uv * OutSize;
    float2 local = (pix - CursorRect.xy) / max(CursorRect.zw, 1.0);
    if (any(local < 0.0) || any(local > 1.0))
        discard;
    float4 s = InputTex.SampleLevel(Samp, saturate(local), 0);
    if (s.a < 0.02)
        discard;
    return s;
}
""";

    public static ReadOnlyMemory<byte> Compile(string entry, string profile)
    {
        return CompileSource(Hlsl, entry, profile);
    }

    public static ReadOnlyMemory<byte> CompileSource(string source, string entry, string profile)
    {
        return Compiler.Compile(source, entry, "upscale.hlsl", profile, ShaderFlags.OptimizationLevel3);
    }

    public static byte[] CompileBytes(string entry, string profile)
    {
        return Compile(entry, profile).ToArray();
    }

    public static byte[] CompilePresentPs()
    {
        return UserShaders.TryCompilePixel() ?? CompileBytes("PSMain", "ps_5_0");
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct UpscaleCB
{
    public float InputW;
    public float InputH;
    public float OutputW;
    public float OutputH;
    public float Sharpness;
    public uint Mode;
    public float BlendT;
    public uint FxMask;
    public float CasAmount;
    public float Brightness;
    public float Contrast;
    public float Saturation;
    public float VignetteAmount;
    public float VignetteRadius;
    public uint BufferView;
    public float DepthMix;
    public float FogAmount;
    public float FogStart;
    public float DofAmount;
    public float DofFocus;
    public float MotionBlur;
    public uint UserMask;
    public float PassIndex;
    public float Pad2;

    public static UpscaleCB From(int inW, int inH, int outW, int outH, UpscaleMode mode, float blend, in PostFxState fx) => new()
    {
        InputW = inW,
        InputH = inH,
        OutputW = outW,
        OutputH = outH,
        Sharpness = fx.FsrSharpness,
        Mode = (uint)mode,
        BlendT = blend,
        FxMask = fx.Mask,
        CasAmount = fx.CasAmount,
        Brightness = fx.Brightness,
        Contrast = fx.Contrast,
        Saturation = fx.Saturation,
        VignetteAmount = fx.VignetteAmount,
        VignetteRadius = fx.VignetteRadius,
        BufferView = fx.BufferView,
        DepthMix = fx.DepthMix,
        FogAmount = fx.FogAmount,
        FogStart = fx.FogStart,
        DofAmount = fx.DofAmount,
        DofFocus = fx.DofFocus,
        MotionBlur = fx.MotionBlurAmount,
        UserMask = UserShaders.Mask,
        PassIndex = 0,
    };
}
