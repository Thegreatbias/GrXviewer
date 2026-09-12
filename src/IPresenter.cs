namespace CpuScreenViewer;

internal interface IPresenter : IDisposable
{
    bool Ok { get; }
    bool HasImage { get; }
    string Error { get; }
    string Status { get; }
    IntPtr Hwnd { get; }
    int Width { get; }
    int Height { get; }
    void FocusHost();
    void PlaceHost();
    bool Resize(int hostW, int hostH, int bufferW, int bufferH);
    void RecreateDevice(bool hardware, int adapterIndex, int hostW, int hostH, int bufferW, int bufferH);
    bool Upload(IntPtr bits, int width, int height, int stride);
    bool UploadShared(IntPtr handle, int width, int height);
    bool Present();
    void SetUpscale(UpscaleMode mode);
    void SetPostFx(in PostFxState fx);
    void SetFrameBlend(float t);
    void ReloadPipeline();
    void Idle();
}
