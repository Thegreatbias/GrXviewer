using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace CpuScreenViewer;

internal sealed class D3DPresenter : IPresenter
{
    private const string ClassName = "GrXviewerD3DHost";
    private static Native.WndProc? _wndProc;
    private static bool _classRegistered;

    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;
    private IDXGISwapChain? _swap;
    private ID3D11Texture2D? _gpu;
    private ID3D11Texture2D? _gpuPrev;
    private ID3D11Texture2D? _sharedTex;
    private IDXGIKeyedMutex? _sharedMutex;
    private IntPtr _sharedHandle;
    private ID3D11Texture2D? _back;
    private ID3D11ShaderResourceView? _srv;
    private ID3D11ShaderResourceView? _srvPrev;
    private ID3D11RenderTargetView? _rtv;
    private ID3D11Texture2D? _ping;
    private ID3D11RenderTargetView? _pingRtv;
    private ID3D11ShaderResourceView? _pingSrv;
    private ID3D11VertexShader? _vs;
    private ID3D11PixelShader? _ps;
    private ID3D11Buffer? _cbuf;
    private ID3D11SamplerState? _sampler;
    private int _width;
    private int _height;
    private int _outW;
    private int _outH;
    private UpscaleMode _upscale = UpscaleMode.Bilinear;
    private PostFxState _fx = PostFxState.Default;
    private float _blendT = 1;
    private IntPtr _parent;
    private IntPtr _hwnd;
    private bool _hasImage;
    private bool _hardware = true;
    private int _adapterIndex;
    private bool _tearing;
    public bool Ok { get; private set; }
    public bool HasImage => _hasImage;
    public string Error { get; private set; } = "";
    public string Status => _tearing ? "tear" : "vsync";
    public IntPtr Hwnd => _hwnd;
    public int Width => _width;
    public int Height => _height;

    public D3DPresenter(IntPtr parentHwnd, bool hardware = true, int adapterIndex = 0)
    {
        _parent = parentHwnd;
        _hardware = hardware;
        _adapterIndex = adapterIndex;
        try
        {
            Native.GetClientRect(parentHwnd, out var rect);
            var w = Math.Max(8, rect.Width);
            var h = Math.Max(8, rect.Height);
            CreateHost(w, h);
            CreateDevice(w, h, w, h);
            Ok = true;
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            Dispose();
        }
    }

    public void FocusHost()
    {
        if (_hwnd != IntPtr.Zero)
            Native.SetFocus(_hwnd);
    }

    public void PlaceHost() { }

    /// <summary>When true, D3D host returns HTTRANSPARENT so clicks fall through (no WS_EX style flips).</summary>
    public static volatile bool PassClicks;

    /// <summary>Raised on left/right mouse down on the D3D host (UI thread not guaranteed).</summary>
    public static Action? ContentClicked;

    private static IntPtr HostWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        // WM_LBUTTONDOWN / WM_RBUTTONDOWN — used to dismiss menu / enable click-through.
        if (msg is 0x0201 or 0x0204)
            ContentClicked?.Invoke();
        // WM_NCHITTEST — never toggle WS_EX_TRANSPARENT/LAYERED; that AVs flip-model DXGI.
        if (msg == 0x0084 && PassClicks)
            return (IntPtr)(-1); // HTTRANSPARENT
        if (msg == 0x000F)
        {
            Native.ValidateRect(hWnd, IntPtr.Zero);
            return IntPtr.Zero;
        }
        if (msg == 0x0014)
            return 1;
        return Native.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private static void RegisterClass()
    {
        if (_classRegistered)
            return;
        _wndProc = HostWndProc;
        var wc = new Native.WndClassEx
        {
            Size = (uint)Marshal.SizeOf<Native.WndClassEx>(),
            Style = Native.CsOwndc,
            lpfnWndProc = _wndProc,
            Instance = Native.GetModuleHandle(null),
            ClassName = ClassName,
        };
        var atom = Native.RegisterClassEx(ref wc);
        if (atom == 0)
        {
            var err = Marshal.GetLastWin32Error();
            if (err != 1410)
                throw new InvalidOperationException("RegisterClassEx failed: " + err);
        }
        _classRegistered = true;
    }

    private void CreateHost(int width, int height)
    {
        RegisterClass();
        _hwnd = Native.CreateWindowEx(
            Native.WsExNoRedirectionBitmap | (PassClicks ? Native.WsExTransparent : 0),
            ClassName,
            "",
            Native.WsChild | Native.WsVisible | Native.WsClipSiblings | Native.WsTabStop,
            0,
            0,
            width,
            height,
            _parent,
            IntPtr.Zero,
            Native.GetModuleHandle(null),
            IntPtr.Zero);
        if (_hwnd == IntPtr.Zero)
            throw new InvalidOperationException("Failed to create D3D window: " + Marshal.GetLastWin32Error());
        Native.SetWindowPos(_hwnd, Native.HwndBottom, 0, 0, 0, 0, Native.SwpNoMove | Native.SwpNoSize);
        Native.SetWindowDisplayAffinity(_hwnd, Native.WdaExcludeFromCapture);
    }

    private void CreateDevice(int hostW, int hostH, int bufferW, int bufferH)
    {
        Native.MoveWindow(_hwnd, 0, 0, hostW, hostH, false);
        _width = bufferW;
        _height = bufferH;

        var levels = new[] { FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0 };
        IDXGIAdapter? chosen = null;
        var type = DriverType.Warp;
        if (_hardware)
        {
            using var enumFactory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
            if (enumFactory.EnumAdapters1((uint)_adapterIndex, out var picked).Success)
                chosen = picked;
            type = chosen is null ? DriverType.Hardware : DriverType.Unknown;
        }

        try
        {
            D3D11.D3D11CreateDevice(
                chosen,
                type,
                DeviceCreationFlags.BgraSupport,
                levels,
                out _device,
                out _,
                out _context).CheckError();
        }
        finally
        {
            chosen?.Dispose();
        }

        using var dxgiDevice = _device.QueryInterface<IDXGIDevice>();
        using var adapter = dxgiDevice.GetAdapter();
        using var factory = adapter.GetParent<IDXGIFactory2>();
        _tearing = false;
        try
        {
            using var factory5 = factory.QueryInterface<IDXGIFactory5>();
            _tearing = factory5.PresentAllowTearing;
        }
        catch
        {
            _tearing = false;
        }
        var desc = new SwapChainDescription1
        {
            Width = (uint)hostW,
            Height = (uint)hostH,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            BufferUsage = Usage.RenderTargetOutput,
            BufferCount = 3,
            Scaling = Scaling.Stretch,
            SwapEffect = SwapEffect.FlipDiscard,
            AlphaMode = AlphaMode.Ignore,
            Flags = SwapChainFlags.AllowTearing,
        };
        try
        {
            _swap = factory.CreateSwapChainForHwnd(_device, _hwnd, desc);
            _tearing = true;
        }
        catch
        {
            desc.Flags = SwapChainFlags.None;
            _tearing = false;
            _swap = factory.CreateSwapChainForHwnd(_device, _hwnd, desc);
        }
        try
        {
            factory.MakeWindowAssociation(_hwnd, WindowAssociationFlags.IgnoreAltEnter);
        }
        catch { /* optional */ }
        try
        {
            using var dxgiDevice1 = _device.QueryInterface<IDXGIDevice1>();
            dxgiDevice1.MaximumFrameLatency = 1;
        }
        catch { /* optional */ }
        _outW = hostW;
        _outH = hostH;
        CreatePipeline();
        RecreateTargets();
    }

    public void SetUpscale(UpscaleMode mode) => _upscale = mode;
    public void SetPostFx(in PostFxState fx) => _fx = fx;
    public void SetFrameBlend(float t) => _blendT = Math.Clamp(t, 0, 1);

    public bool Resize(int hostW, int hostH, int bufferW, int bufferH)
    {
        if (!Ok || _swap is null || _device is null || _hwnd == IntPtr.Zero)
            return false;
        hostW = Math.Max(8, hostW);
        hostH = Math.Max(8, hostH);
        bufferW = Math.Max(8, bufferW);
        bufferH = Math.Max(8, bufferH);
        try
        {
            if (!Native.IsWindow(_hwnd))
                return false;
            Native.MoveWindow(_hwnd, 0, 0, hostW, hostH, false);
        }
        catch
        {
            return false;
        }
        if (bufferW == _width && bufferH == _height && hostW == _outW && hostH == _outH)
            return false;
        var oldW = _width;
        var oldH = _height;
        var oldOutW = _outW;
        var oldOutH = _outH;
        var flags = _tearing ? SwapChainFlags.AllowTearing : SwapChainFlags.None;
        try
        {
            _context?.ClearState();
            _context?.Flush();
            ReleaseTargets();
            try
            {
                _swap.ResizeBuffers(0, (uint)hostW, (uint)hostH, Format.B8G8R8A8_UNorm, flags);
            }
            catch
            {
                // Retry without tearing flags after fullscreen/window transitions.
                _tearing = false;
                _swap.ResizeBuffers(0, (uint)hostW, (uint)hostH, Format.B8G8R8A8_UNorm, SwapChainFlags.None);
            }
            _width = bufferW;
            _height = bufferH;
            _outW = hostW;
            _outH = hostH;
            RecreateTargets();
        }
        catch
        {
            _width = oldW;
            _height = oldH;
            _outW = oldOutW;
            _outH = oldOutH;
            try { RecreateTargets(); }
            catch
            {
                Ok = false;
            }
            return false;
        }
        _hasImage = false;
        return true;
    }

    public void RecreateDevice(bool hardware, int adapterIndex, int hostW, int hostH, int bufferW, int bufferH)
    {
        if (_hwnd == IntPtr.Zero)
            return;
        _hardware = hardware;
        _adapterIndex = adapterIndex;
        Ok = false;
        ReleaseTargets();
        ReleasePipeline();
        _swap?.Dispose();
        _context?.Dispose();
        _device?.Dispose();
        _swap = null;
        _context = null;
        _device = null;
        try
        {
            CreateDevice(hostW, hostH, bufferW, bufferH);
            Ok = true;
            Error = "";
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
    }

    private void RecreateTargets()
    {
        if (_device is null || _swap is null)
            return;
        ReleaseTargets();
        var desc = new Texture2DDescription
        {
            Width = (uint)_width,
            Height = (uint)_height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ShaderResource,
            CPUAccessFlags = CpuAccessFlags.None,
        };
        _gpu = _device.CreateTexture2D(desc);
        _gpuPrev = _device.CreateTexture2D(desc);
        _srv = _device.CreateShaderResourceView(_gpu);
        _srvPrev = _device.CreateShaderResourceView(_gpuPrev);
        _back = _swap.GetBuffer<ID3D11Texture2D>(0);
        _rtv = _device.CreateRenderTargetView(_back);
        var pingDesc = new Texture2DDescription
        {
            Width = (uint)Math.Max(8, _outW),
            Height = (uint)Math.Max(8, _outH),
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
            CPUAccessFlags = CpuAccessFlags.None,
        };
        _ping = _device.CreateTexture2D(pingDesc);
        _pingRtv = _device.CreateRenderTargetView(_ping);
        _pingSrv = _device.CreateShaderResourceView(_ping);
    }

    private void CreatePipeline()
    {
        if (_device is null)
            return;
        ReleasePipeline();
        var vs = UpscaleShaders.CompileBytes("VSMain", "vs_5_0");
        var ps = UpscaleShaders.CompilePresentPs();
        _vs = _device.CreateVertexShader(vs);
        _ps = _device.CreatePixelShader(ps);
        _cbuf = _device.CreateBuffer((uint)Marshal.SizeOf<UpscaleCB>(), BindFlags.ConstantBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write);
        _sampler = _device.CreateSamplerState(new SamplerDescription
        {
            Filter = Filter.MinMagMipLinear,
            AddressU = TextureAddressMode.Clamp,
            AddressV = TextureAddressMode.Clamp,
            AddressW = TextureAddressMode.Clamp,
            ComparisonFunc = ComparisonFunction.Never,
            MinLOD = 0,
            MaxLOD = float.MaxValue,
        });
    }

    public void ReloadPipeline()
    {
        if (_device is null)
            return;
        var bytes = UserShaders.TryCompilePixel();
        if (bytes is null)
        {
            if (_ps != null)
                return;
            bytes = UpscaleShaders.CompileBytes("PSMain", "ps_5_0");
        }
        var ps = _device.CreatePixelShader(bytes);
        _context?.Flush();
        _ps?.Dispose();
        _ps = ps;
    }

    private void ReleasePipeline()
    {
        _sampler?.Dispose();
        _cbuf?.Dispose();
        _ps?.Dispose();
        _vs?.Dispose();
        _sampler = null;
        _cbuf = null;
        _ps = null;
        _vs = null;
    }

    private void ReleaseTargets()
    {
        if (_context != null)
        {
            _context.OMSetRenderTargets((ID3D11RenderTargetView?)null!);
            _context.PSSetShaderResource(0, (ID3D11ShaderResourceView?)null!);
            _context.PSSetShaderResource(1, (ID3D11ShaderResourceView?)null!);
            _context.Flush();
        }
        _rtv?.Dispose();
        _pingSrv?.Dispose();
        _pingRtv?.Dispose();
        _ping?.Dispose();
        _srvPrev?.Dispose();
        _srv?.Dispose();
        _back?.Dispose();
        _gpuPrev?.Dispose();
        _gpu?.Dispose();
        _sharedMutex?.Dispose();
        _sharedTex?.Dispose();
        _rtv = null;
        _pingSrv = null;
        _pingRtv = null;
        _ping = null;
        _srvPrev = null;
        _srv = null;
        _back = null;
        _gpuPrev = null;
        _gpu = null;
        _sharedMutex = null;
        _sharedTex = null;
        _sharedHandle = IntPtr.Zero;
    }

    public bool Upload(IntPtr bits, int width, int height, int stride)
    {
        if (!Ok || _context is null || _gpu is null || _gpuPrev is null || bits == IntPtr.Zero)
            return false;
        if (width != _width || height != _height)
            return false;
        if (_hasImage)
            _context.CopyResource(_gpuPrev, _gpu);
        _context.UpdateSubresource(_gpu, 0, null, bits, (uint)stride, 0);
        if (!_hasImage)
            _context.CopyResource(_gpuPrev, _gpu);
        _hasImage = true;
        return true;
    }

    public bool UploadShared(IntPtr handle, int width, int height)
    {
        if (!Ok || _context is null || _device is null || _gpu is null || _gpuPrev is null || handle == IntPtr.Zero)
            return false;
        if (width != _width || height != _height)
            return false;
        if (!OpenShared(handle))
            return false;
        if (_sharedTex is null || _sharedMutex is null)
            return false;
        try
        {
            _sharedMutex.AcquireSync(1, 8);
        }
        catch
        {
            return false;
        }
        try
        {
            if (_hasImage)
                _context.CopyResource(_gpuPrev, _gpu);
            _context.CopyResource(_gpu, _sharedTex);
            if (!_hasImage)
                _context.CopyResource(_gpuPrev, _gpu);
            _hasImage = true;
            return true;
        }
        finally
        {
            try { _sharedMutex.ReleaseSync(0); } catch { }
        }
    }

    private bool OpenShared(IntPtr handle)
    {
        if (_sharedHandle == handle && _sharedTex != null)
            return true;
        _sharedMutex?.Dispose();
        _sharedTex?.Dispose();
        _sharedMutex = null;
        _sharedTex = null;
        _sharedHandle = IntPtr.Zero;
        if (_device is null)
            return false;
        try
        {
            using var d1 = _device.QueryInterface<ID3D11Device1>();
            _sharedTex = d1.OpenSharedResource1<ID3D11Texture2D>(handle);
            _sharedMutex = _sharedTex.QueryInterface<IDXGIKeyedMutex>();
            _sharedHandle = handle;
            return true;
        }
        catch
        {
            _sharedMutex?.Dispose();
            _sharedTex?.Dispose();
            _sharedMutex = null;
            _sharedTex = null;
            return false;
        }
    }

    public void Idle()
    {
        try
        {
            _context?.ClearState();
            _context?.Flush();
        }
        catch
        {
            // device may already be lost
        }
    }

    public bool Present()
    {
        if (!Ok || _swap is null)
            return false;
        if (!DrawToBack())
            return false;
        try
        {
            if (_tearing)
            {
                var torn = _swap.Present(0, PresentFlags.AllowTearing);
                if (torn.Success)
                    return true;
                if (torn.Code == unchecked((int)0x887A0001))
                    _tearing = false;
            }
            return _swap.Present(0, PresentFlags.None).Success;
        }
        catch
        {
            Ok = false;
            return false;
        }
    }

    private bool DrawToBack()
    {
        if (!Ok || _context is null || _swap is null || _gpu is null || !_hasImage)
            return false;
        var srv = _srv;
        var inputW = _width;
        var inputH = _height;
        var mode = _upscale;
        if (_vs is null || _ps is null || srv is null || _srvPrev is null || _rtv is null || _cbuf is null || _sampler is null)
        {
            if (_back is null)
                return false;
            _context.CopyResource(_back, _gpu);
            return true;
        }
        var refine = UserShaders.Mask != 0 && _fx.BufferView == 0 && _pingRtv != null && _pingSrv != null;
        var cb = UpscaleCB.From(inputW, inputH, _outW, _outH, mode, _blendT, _fx);
        DrawPass(cb, refine ? _pingRtv! : _rtv, srv, _srvPrev);
        if (refine)
        {
            cb.PassIndex = 1;
            cb.Mode = 0;
            cb.BlendT = 1;
            cb.FxMask = 0;
            cb.InputW = _outW;
            cb.InputH = _outH;
            DrawPass(cb, _rtv, _pingSrv!, _pingSrv!);
        }
        return true;
    }

    private void DrawPass(UpscaleCB cb, ID3D11RenderTargetView rtv, ID3D11ShaderResourceView srv0, ID3D11ShaderResourceView srv1)
    {
        var mapped = _context!.Map(_cbuf!, 0, MapMode.WriteDiscard);
        Marshal.StructureToPtr(cb, mapped.DataPointer, false);
        _context.Unmap(_cbuf, 0);
        _context.OMSetRenderTargets(rtv);
        _context.RSSetViewport(new Viewport(0, 0, _outW, _outH));
        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _context.VSSetShader(_vs);
        _context.PSSetShader(_ps);
        _context.PSSetShaderResource(0, srv0);
        _context.PSSetShaderResource(1, srv1);
        _context.PSSetSampler(0, _sampler);
        _context.PSSetConstantBuffer(0, _cbuf);
        _context.Draw(3, 0);
        _context.PSSetShaderResource(0, (ID3D11ShaderResourceView?)null!);
        _context.PSSetShaderResource(1, (ID3D11ShaderResourceView?)null!);
        _context.OMSetRenderTargets((ID3D11RenderTargetView?)null!);
    }

    public Bitmap? Snapshot()
    {
        if (!Ok || _device is null || _context is null || _gpu is null || !_hasImage)
            return null;
        using var staging = _device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)_width,
            Height = (uint)_height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CPUAccessFlags = CpuAccessFlags.Read,
        });
        _context.CopyResource(staging, _gpu);
        var mapped = _context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            using var wrap = new Bitmap(_width, _height, (int)mapped.RowPitch, System.Drawing.Imaging.PixelFormat.Format32bppRgb, mapped.DataPointer);
            return (Bitmap)wrap.Clone();
        }
        finally
        {
            _context.Unmap(staging, 0);
        }
    }

    public void Dispose()
    {
        Ok = false;
        ReleaseTargets();
        ReleasePipeline();
        _swap?.Dispose();
        _context?.Dispose();
        _device?.Dispose();
        _swap = null;
        _context = null;
        _device = null;
        if (_hwnd != IntPtr.Zero)
        {
            Native.DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
    }
}
