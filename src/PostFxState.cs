namespace CpuScreenViewer;

internal struct PostFxState
{
    public bool Sharpen;
    public bool Color;
    public bool Vignette;
    public bool Fog;
    public bool Dof;
    public bool MotionBlur;
    public uint BufferView;
    public float FsrSharpness;
    public float CasAmount;
    public float Brightness;
    public float Contrast;
    public float Saturation;
    public float VignetteAmount;
    public float VignetteRadius;
    public float DepthMix;
    public float FogAmount;
    public float FogStart;
    public float DofAmount;
    public float DofFocus;
    public float MotionBlurAmount;

    public static PostFxState Default => new()
    {
        FsrSharpness = 0.2f,
        CasAmount = 0.4f,
        Contrast = 1f,
        Saturation = 1f,
        VignetteAmount = 0.45f,
        VignetteRadius = 0.75f,
        DepthMix = 0.25f,
        FogAmount = 0.45f,
        FogStart = 0.35f,
        DofAmount = 0.4f,
        DofFocus = 0.35f,
        MotionBlurAmount = 0.5f,
    };

    public uint Mask =>
        (Sharpen ? 1u : 0u)
        | (Color ? 2u : 0u)
        | (Vignette ? 4u : 0u)
        | (Fog ? 8u : 0u)
        | (Dof ? 16u : 0u)
        | (MotionBlur ? 32u : 0u);
}
