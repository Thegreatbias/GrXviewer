using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace CpuScreenViewer;

internal sealed class GpuCapturer : IDisposable
{
    private const int DxgiAccessLost = unchecked((int)0x887A0026);
    private const int DxgiInvalidCall = unchecked((int)0x887A0001);
    private const int DxgiDeviceRemoved = unchecked((int)0x887A0005);
    private const int DxgiDeviceReset = unchecked((int)0x887A0007);
    private const int DxgiSessionDisconnected = unchecked((int)0x887A0028);

    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;
    private IDXGIOutputDuplication? _dup;
    private ID3D11Texture2D? _gpuCrop;
    private ID3D11Texture2D? _gpuDest;
    private ID3D11Texture2D? _gpuEdge;
    private ID3D11Texture2D? _staging;
    private ID3D11ShaderResourceView? _cropSrv;
    private ID3D11RenderTargetView? _destRtv;
    private ID3D11RenderTargetView? _edgeRtv;
    private ID3D11ShaderResourceView? _edgeSrv;
    private ID3D11VertexShader? _vs;
    private ID3D11PixelShader? _ps;
    private ID3D11PixelShader? _foveaPs;
    private ID3D11PixelShader? _checkerPs;
    private ID3D11PixelShader? _stabPs;
    private ID3D11PixelShader? _cursorPs;
    private ID3D11SamplerState? _sampler;
    private ID3D11BlendState? _blend;
    private ID3D11Buffer? _capCb;
    private ID3D11Texture2D? _gpuStab;
    private ID3D11Texture2D? _gpuMixed;
    private ID3D11Texture2D? _gpuShare;
    private ID3D11Texture2D? _cursorTex;
    private ID3D11ShaderResourceView? _destSrv;
    private ID3D11ShaderResourceView? _stabSrv;
    private ID3D11ShaderResourceView? _cursorSrv;
    private ID3D11RenderTargetView? _mixedRtv;
    private IDXGIKeyedMutex? _shareMutex;
    private IntPtr _shareHandle;
    private int _shareW;
    private int _shareH;
    private int _cursorTw;
    private int _cursorTh;
    private readonly FrameBuffer _cursorSprite = new();
    private bool _stabReady;
    private Format _format;
    private int _cropW;
    private int _cropH;
    private int _destW;
    private int _destH;
    private int _edgeW;
    private int _edgeH;
    private ID3D11Texture2D? _gpuCheckHist;
    private ID3D11ShaderResourceView? _checkHistSrv;
    private int _checkW;
    private int _checkH;
    private bool _checkReady;
    private uint _checkPhase;
    private int _stageW;
    private int _stageH;
    private int _adapterIndex = -1;
    private Rectangle _outputBounds;
    private bool _lost;
    private long _retryAt;
    private bool _blitFailed;

    public bool Ready => _dup != null && !_lost;
    public IntPtr SharedHandle { get; private set; }
    public int SharedWidth { get; private set; }
    public int SharedHeight { get; private set; }
    public bool Foveated { get; set; }
    public int FoveaEdgeScale { get; set; } = 50;
    public bool Checkerboard { get; set; }

    public void DiscardQueued()
    {
        if (_dup is null || _lost)
            return;
        for (var i = 0; i < 8; i++)
        {
            var result = _dup.AcquireNextFrame(0, out _, out var extra);
            extra?.Dispose();
            if (result.Failure)
            {
                if (IsLost(result.Code))
                    MarkLost();
                return;
            }
            try { _dup.ReleaseFrame(); }
            catch
            {
                MarkLost();
                return;
            }
        }
    }

    public bool TryEnsure(int adapterIndex, Rectangle target)
    {
        if (_dup != null && !_lost && _adapterIndex == adapterIndex && _outputBounds.Contains(target.X + target.Width / 2, target.Y + target.Height / 2))
            return true;
        if (Environment.TickCount64 < _retryAt)
            return false;
        DisposeDevice();
        _adapterIndex = adapterIndex;
        _lost = false;
        try
        {
            using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
            if (factory.EnumAdapters1((uint)adapterIndex, out var adapter).Failure)
                return false;
            using (adapter)
            {
                var levels = new[] { FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0 };
                D3D11.D3D11CreateDevice(
                    adapter,
                    DriverType.Unknown,
                    DeviceCreationFlags.BgraSupport,
                    levels,
                    out _device,
                    out _,
                    out _context).CheckError();
                IDXGIOutput? best = null;
                var bestArea = -1;
                for (uint i = 0; adapter.EnumOutputs(i, out var output).Success; i++)
                {
                    var desk = ToRect(output.Description.DesktopCoordinates);
                    var area = Rectangle.Intersect(desk, target);
                    var score = area.Width * area.Height;
                    if (score > bestArea)
                    {
                        best?.Dispose();
                        best = output;
                        bestArea = score;
                        _outputBounds = desk;
                    }
                    else
                        output.Dispose();
                }
                if (best is null || _device is null)
                    return false;
                using var output1 = best.QueryInterface<IDXGIOutput1>();
                best.Dispose();
                _dup = output1.DuplicateOutput(_device);
                EnsureBlitPipeline();
                return _dup != null;
            }
        }
        catch
        {
            DisposeDevice();
            _lost = true;
            _retryAt = Environment.TickCount64 + 50;
            return false;
        }
    }

    public bool TryGrab(Rectangle src, FrameBuffer dest, int destW, int destH, int timeoutMs = 16, bool readback = true, int stabDead = -1, int stabSoft = 0)
    {
        if (_dup is null || _context is null || _device is null || _lost)
            return false;
        // Allow a longer wait after glass transitions; empty frames are worse than a short stall.
        timeoutMs = Math.Clamp(timeoutMs, 2, 50);
        destW = Math.Max(8, destW);
        destH = Math.Max(8, destH);

        IDXGIResource? resource = null;
        var held = false;
        try
        {
            var result = _dup.AcquireNextFrame((uint)timeoutMs, out _, out resource);
            if (result.Failure)
            {
                resource?.Dispose();
                if (IsLost(result.Code))
                    MarkLost();
                return false;
            }
            held = true;
            if (!CopyHeldToCrop(resource, src))
                return false;
        }
        catch
        {
            MarkLost();
            return false;
        }
        finally
        {
            if (held && !_lost && _dup != null)
            {
                try { _dup.ReleaseFrame(); }
                catch { MarkLost(); }
            }
            resource?.Dispose();
        }

        try
        {
            return Compose(dest, src, destW, destH, readback, stabDead, stabSoft);
        }
        catch
        {
            SharedHandle = IntPtr.Zero;
            return false;
        }
    }

    private bool CopyHeldToCrop(IDXGIResource? resource, Rectangle src)
    {
        if (resource is null || _context is null)
            return false;
        using var tex = resource.QueryInterface<ID3D11Texture2D>();
        var desc = tex.Description;
        var local = src;
        local.X -= _outputBounds.X;
        local.Y -= _outputBounds.Y;
        local.Intersect(new Rectangle(0, 0, (int)desc.Width, (int)desc.Height));
        if (local.Width < 2 || local.Height < 2)
            return false;
        EnsureGpuCrop(local.Width, local.Height, desc.Format);
        if (_gpuCrop is null)
            return false;
        var box = new Box(local.X, local.Y, 0, local.X + local.Width, local.Y + local.Height, 1);
        _context.CopySubresourceRegion(_gpuCrop, 0, 0, 0, 0, tex, 0, box);
        return true;
    }

    private bool Compose(FrameBuffer dest, Rectangle src, int destW, int destH, bool readback, int stabDead, int stabSoft)
    {
        if (_context is null || _gpuCrop is null)
            return false;

        if (!FillDest(destW, destH))
        {
            EnsureStaging(_cropW, _cropH, _format);
            if (_staging is null)
                return false;
            _context.CopyResource(_staging, _gpuCrop);
            if (!MapCopy(dest, _cropW, _cropH, destW, destH))
                return false;
            try { dest.DrawCursor(src, destW, destH); } catch { }
            SharedHandle = IntPtr.Zero;
            return true;
        }

        try { DrawCursorGpu(src, destW, destH); } catch { }
        var gpuSrc = StabilizeGpu(destW, destH, stabDead, stabSoft) ?? _gpuDest!;
        var shared = PublishShared(gpuSrc, destW, destH);
        if (!readback && shared)
        {
            // Keep CPU slot size in sync even when skipping MapCopy.
            dest.EnsureSize(destW, destH);
            return true;
        }

        EnsureStaging(destW, destH, Format.B8G8R8A8_UNorm);
        if (_staging is null)
            return false;
        _context.CopyResource(_staging, gpuSrc);
        if (!MapCopy(dest, destW, destH, destW, destH))
            return false;
        if (!shared)
            try { dest.DrawCursor(src, destW, destH); } catch { }
        return true;
    }

    private bool FillDest(int destW, int destH)
    {
        if (_gpuCrop is null || _context is null)
            return false;
        EnsureGpuDest(destW, destH, Format.B8G8R8A8_UNorm);
        if (_gpuDest is null)
            return false;

        if (Checkerboard)
        {
            if (TryFillCheckerboard(destW, destH))
                return true;
        }

        var edgeScale = Math.Clamp(FoveaEdgeScale, 25, 100);
        if (Foveated && edgeScale < 100)
        {
            if (TryFillFoveated(destW, destH, edgeScale))
                return true;
        }

        if (_cropW == destW && _cropH == destH && _format == Format.B8G8R8A8_UNorm)
        {
            _context.CopyResource(_gpuDest, _gpuCrop);
            return true;
        }
        return TryBlit(_cropSrv, _destRtv, destW, destH);
    }

    private bool TryFillCheckerboard(int destW, int destH)
    {
        if (_blitFailed || _context is null || _device is null || _gpuCrop is null || _cropSrv is null)
            return false;
        if (_vs is null || _ps is null || _checkerPs is null || _sampler is null || _capCb is null)
            EnsureBlitPipeline();
        if (_vs is null || _ps is null || _checkerPs is null || _sampler is null || _capCb is null || _destRtv is null)
            return false;

        EnsureCheckerHist(destW, destH);
        if (_gpuCheckHist is null || _checkHistSrv is null)
            return false;

        // Seed history with a full frame, then alternate checker cells each grab.
        if (!_checkReady)
        {
            if (_cropW == destW && _cropH == destH && _format == Format.B8G8R8A8_UNorm)
                _context.CopyResource(_gpuDest, _gpuCrop);
            else if (!TryBlit(_cropSrv, _destRtv, destW, destH))
                return false;
            _context.CopyResource(_gpuCheckHist, _gpuDest);
            _checkReady = true;
            _checkPhase = 0;
            return true;
        }

        _checkPhase ^= 1u;
        WriteCapCb(_checkPhase, 0, destW, destH, 0, 0, 1, 1);
        _context.OMSetRenderTargets(_destRtv);
        _context.RSSetViewport(new Viewport(0, 0, destW, destH));
        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _context.VSSetShader(_vs);
        _context.PSSetShader(_checkerPs);
        _context.PSSetShaderResource(0, _cropSrv);
        _context.PSSetShaderResource(1, _checkHistSrv);
        _context.PSSetSampler(0, _sampler);
        _context.PSSetConstantBuffer(1, _capCb);
        _context.Draw(3, 0);
        _context.PSSetShaderResource(0, (ID3D11ShaderResourceView?)null!);
        _context.PSSetShaderResource(1, (ID3D11ShaderResourceView?)null!);
        _context.OMSetRenderTargets((ID3D11RenderTargetView?)null!);
        _context.CopyResource(_gpuCheckHist, _gpuDest);
        return true;
    }

    private bool TryFillFoveated(int destW, int destH, int edgeScale)
    {
        if (_blitFailed || _context is null || _device is null || _gpuCrop is null || _cropSrv is null)
            return false;
        if (_vs is null || _ps is null || _foveaPs is null || _sampler is null || _capCb is null)
            EnsureBlitPipeline();
        if (_vs is null || _ps is null || _foveaPs is null || _sampler is null || _capCb is null || _destRtv is null)
            return false;

        var edgeW = Math.Max(8, (destW * edgeScale / 100) & ~1);
        var edgeH = Math.Max(8, (destH * edgeScale / 100) & ~1);
        if (edgeW >= destW && edgeH >= destH)
            return TryBlit(_cropSrv, _destRtv, destW, destH);

        EnsureGpuEdge(edgeW, edgeH);
        if (_gpuEdge is null || _edgeRtv is null || _edgeSrv is null)
            return false;

        // Soft full frame: crop → low-res → dest
        if (!TryBlit(_cropSrv, _edgeRtv, edgeW, edgeH))
            return false;
        if (!TryBlit(_edgeSrv, _destRtv, destW, destH))
            return false;

        // Sharp center: resample crop at dest pixel density into the middle half
        var foveaW = Math.Max(8, (destW * 50 / 100) & ~1);
        var foveaH = Math.Max(8, (destH * 50 / 100) & ~1);
        var x = (destW - foveaW) / 2;
        var y = (destH - foveaH) / 2;
        WriteCapCb(0, 0, destW, destH, 0, 0, 1, 1);
        _context.OMSetRenderTargets(_destRtv);
        _context.RSSetViewport(new Viewport(x, y, foveaW, foveaH));
        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _context.VSSetShader(_vs);
        _context.PSSetShader(_foveaPs);
        _context.PSSetShaderResource(0, _cropSrv);
        _context.PSSetSampler(0, _sampler);
        _context.PSSetConstantBuffer(1, _capCb);
        _context.Draw(3, 0);
        _context.PSSetShaderResource(0, (ID3D11ShaderResourceView?)null!);
        _context.OMSetRenderTargets((ID3D11RenderTargetView?)null!);
        return true;
    }

    private bool MapCopy(FrameBuffer dest, int srcW, int srcH, int destW, int destH)
    {
        if (_context is null || _staging is null)
            return false;
        _context.Map(_staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None, out var mapped);
        try
        {
            dest.CopyScaled(mapped.DataPointer, 0, 0, srcW, srcH, (int)mapped.RowPitch, destW, destH);
            return true;
        }
        finally
        {
            _context.Unmap(_staging, 0);
        }
    }

    private bool TryBlit(ID3D11ShaderResourceView? srv, ID3D11RenderTargetView? rtv, int destW, int destH)
    {
        if (_blitFailed || _context is null || _device is null || srv is null || rtv is null)
            return false;
        if (_vs is null || _ps is null || _sampler is null)
            EnsureBlitPipeline();
        if (_vs is null || _ps is null || _sampler is null)
        {
            _blitFailed = true;
            return false;
        }
        _context.OMSetRenderTargets(rtv);
        _context.RSSetViewport(new Viewport(0, 0, destW, destH));
        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _context.VSSetShader(_vs);
        _context.PSSetShader(_ps);
        _context.PSSetShaderResource(0, srv);
        _context.PSSetSampler(0, _sampler);
        _context.Draw(3, 0);
        _context.PSSetShaderResource(0, (ID3D11ShaderResourceView?)null!);
        _context.OMSetRenderTargets((ID3D11RenderTargetView?)null!);
        return true;
    }

    private void DrawCursorGpu(Rectangle src, int destW, int destH)
    {
        if (_context is null || _device is null || _gpuDest is null || _destRtv is null || _vs is null || _cursorPs is null || _sampler is null || _blend is null || _capCb is null)
            return;
        if (!_cursorSprite.TryGetCursorSprite(src, destW, destH, out var x, out var y, out var w, out var h))
            return;
        EnsureCursorTex(w, h);
        if (_cursorTex is null || _cursorSrv is null)
            return;
        _context.UpdateSubresource(_cursorTex, 0, null, _cursorSprite.Bits, (uint)_cursorSprite.Stride, 0);
        WriteCapCb(0, 1, destW, destH, x, y, w, h);
        _context.OMSetRenderTargets(_destRtv);
        _context.OMSetBlendState(_blend);
        _context.RSSetViewport(new Viewport(0, 0, destW, destH));
        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _context.VSSetShader(_vs);
        _context.PSSetShader(_cursorPs);
        _context.PSSetShaderResource(0, _cursorSrv);
        _context.PSSetSampler(0, _sampler);
        _context.PSSetConstantBuffer(1, _capCb);
        _context.Draw(3, 0);
        _context.PSSetShaderResource(0, (ID3D11ShaderResourceView?)null!);
        _context.OMSetBlendState(null);
        _context.OMSetRenderTargets((ID3D11RenderTargetView?)null!);
    }

    private ID3D11Texture2D? StabilizeGpu(int destW, int destH, int dead, int soft)
    {
        if (dead < 0 || _context is null || _gpuDest is null)
            return _gpuDest;
        EnsureStab(destW, destH);
        if (_gpuStab is null)
            return _gpuDest;
        if (soft <= 0 || !_stabReady || _stabPs is null || _destSrv is null || _stabSrv is null || _gpuMixed is null || _mixedRtv is null || _vs is null || _sampler is null || _capCb is null)
        {
            _context.CopyResource(_gpuStab, _gpuDest);
            _stabReady = true;
            return _gpuDest;
        }
        WriteCapCb((uint)dead, (uint)soft, destW, destH, 0, 0, 1, 1);
        _context.OMSetRenderTargets(_mixedRtv);
        _context.RSSetViewport(new Viewport(0, 0, destW, destH));
        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _context.VSSetShader(_vs);
        _context.PSSetShader(_stabPs);
        _context.PSSetShaderResource(0, _destSrv);
        _context.PSSetShaderResource(1, _stabSrv);
        _context.PSSetSampler(0, _sampler);
        _context.PSSetConstantBuffer(1, _capCb);
        _context.Draw(3, 0);
        _context.PSSetShaderResource(0, (ID3D11ShaderResourceView?)null!);
        _context.PSSetShaderResource(1, (ID3D11ShaderResourceView?)null!);
        _context.OMSetRenderTargets((ID3D11RenderTargetView?)null!);
        _context.CopyResource(_gpuStab, _gpuMixed);
        _stabReady = true;
        return _gpuMixed;
    }

    private bool PublishShared(ID3D11Texture2D src, int width, int height)
    {
        if (_context is null || !EnsureShare(width, height) || _gpuShare is null || _shareMutex is null)
        {
            SharedHandle = IntPtr.Zero;
            return false;
        }
        if (!AcquireMutex(_shareMutex, 0, 4) && !AcquireMutex(_shareMutex, 1, 4))
        {
            SharedHandle = IntPtr.Zero;
            return false;
        }
        var released = false;
        try
        {
            _context.CopyResource(_gpuShare, src);
            _shareMutex.ReleaseSync(1);
            released = true;
        }
        finally
        {
            if (!released)
            {
                try { _shareMutex.ReleaseSync(1); } catch { }
                SharedHandle = IntPtr.Zero;
            }
        }
        if (!released)
            return false;
        SharedHandle = _shareHandle;
        SharedWidth = width;
        SharedHeight = height;
        return _shareHandle != IntPtr.Zero;
    }

    private void WriteCapCb(uint dead, uint soft, int outW, int outH, int cx, int cy, int cw, int ch)
    {
        if (_context is null || _capCb is null)
            return;
        var cb = new CapCB
        {
            StabDead = dead,
            StabSoft = soft,
            OutW = outW,
            OutH = outH,
            CursorX = cx,
            CursorY = cy,
            CursorW = cw,
            CursorH = ch,
        };
        var mapped = _context.Map(_capCb, 0, MapMode.WriteDiscard);
        Marshal.StructureToPtr(cb, mapped.DataPointer, false);
        _context.Unmap(_capCb, 0);
    }

    private bool EnsureShare(int width, int height)
    {
        if (_device is null)
            return false;
        if (_gpuShare != null && _shareW == width && _shareH == height && _shareHandle != IntPtr.Zero)
            return true;
        ReleaseShare();
        try
        {
            _gpuShare = _device.CreateTexture2D(new Texture2DDescription
            {
                Width = (uint)width,
                Height = (uint)height,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.ShaderResource,
                CPUAccessFlags = CpuAccessFlags.None,
                MiscFlags = ResourceOptionFlags.SharedKeyedMutex | ResourceOptionFlags.SharedNTHandle,
            });
            _shareMutex = _gpuShare.QueryInterface<IDXGIKeyedMutex>();
            using var res = _gpuShare.QueryInterface<IDXGIResource1>();
            _shareHandle = res.CreateSharedHandle(null, Vortice.DXGI.SharedResourceFlags.Read | Vortice.DXGI.SharedResourceFlags.Write, null);
            _shareW = width;
            _shareH = height;
            return _shareHandle != IntPtr.Zero;
        }
        catch
        {
            ReleaseShare();
            return false;
        }
    }

    private void EnsureCursorTex(int width, int height)
    {
        if (_device is null)
            return;
        if (_cursorTex != null && _cursorTw == width && _cursorTh == height)
            return;
        _cursorSrv?.Dispose();
        _cursorTex?.Dispose();
        _cursorSrv = null;
        _cursorTex = _device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ShaderResource,
            CPUAccessFlags = CpuAccessFlags.None,
        });
        _cursorSrv = _device.CreateShaderResourceView(_cursorTex);
        _cursorTw = width;
        _cursorTh = height;
    }

    private void EnsureStab(int width, int height)
    {
        if (_device is null)
            return;
        if (_gpuStab != null && _gpuMixed != null && _destW == width && _destH == height)
            return;
        ReleaseStab();
        ID3D11Texture2D Make() => _device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
            CPUAccessFlags = CpuAccessFlags.None,
        });
        _gpuStab = Make();
        _gpuMixed = Make();
        _stabSrv = _device.CreateShaderResourceView(_gpuStab);
        _mixedRtv = _device.CreateRenderTargetView(_gpuMixed);
        _stabReady = false;
    }

    private void EnsureBlitPipeline()
    {
        if (_device is null || _vs != null)
            return;
        try
        {
            var vs = UpscaleShaders.CompileBytes("VSMain", "vs_5_0");
            var ps = UpscaleShaders.CompileBytes("BlitPS", "ps_5_0");
            _vs = _device.CreateVertexShader(vs);
            _ps = _device.CreatePixelShader(ps);
            _foveaPs = _device.CreatePixelShader(UpscaleShaders.CompileBytes("FoveaCenterPS", "ps_5_0"));
            _checkerPs = _device.CreatePixelShader(UpscaleShaders.CompileBytes("CheckerPS", "ps_5_0"));
            _stabPs = _device.CreatePixelShader(UpscaleShaders.CompileBytes("StabPS", "ps_5_0"));
            _cursorPs = _device.CreatePixelShader(UpscaleShaders.CompileBytes("CursorPS", "ps_5_0"));
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
            _blend = _device.CreateBlendState(BlendDescription.NonPremultiplied);
            _capCb = _device.CreateBuffer((uint)Marshal.SizeOf<CapCB>(), BindFlags.ConstantBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write);
            _blitFailed = false;
        }
        catch
        {
            _vs?.Dispose();
            _ps?.Dispose();
            _foveaPs?.Dispose();
            _checkerPs?.Dispose();
            _stabPs?.Dispose();
            _cursorPs?.Dispose();
            _sampler?.Dispose();
            _blend?.Dispose();
            _capCb?.Dispose();
            _vs = null;
            _ps = null;
            _foveaPs = null;
            _checkerPs = null;
            _stabPs = null;
            _cursorPs = null;
            _sampler = null;
            _blend = null;
            _capCb = null;
            _blitFailed = true;
        }
    }

    private void EnsureCheckerHist(int width, int height)
    {
        if (_device is null)
            return;
        if (_gpuCheckHist != null && _checkW == width && _checkH == height)
            return;
        ReleaseCheckerHist();
        _checkW = width;
        _checkH = height;
        _gpuCheckHist = _device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ShaderResource,
            CPUAccessFlags = CpuAccessFlags.None,
        });
        _checkHistSrv = _device.CreateShaderResourceView(_gpuCheckHist);
        _checkReady = false;
        _checkPhase = 0;
    }

    private void EnsureGpuEdge(int width, int height)
    {
        if (_device is null)
            return;
        if (_gpuEdge != null && _edgeW == width && _edgeH == height)
            return;
        ReleaseEdge();
        _edgeW = width;
        _edgeH = height;
        _gpuEdge = _device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
            CPUAccessFlags = CpuAccessFlags.None,
        });
        _edgeRtv = _device.CreateRenderTargetView(_gpuEdge);
        _edgeSrv = _device.CreateShaderResourceView(_gpuEdge);
    }

    private void EnsureGpuCrop(int width, int height, Format format)
    {
        if (_device is null)
            return;
        if (_gpuCrop != null && _cropW == width && _cropH == height && _format == format)
            return;
        ReleaseCrop();
        if (_format != format)
        {
            ReleaseDest();
            _staging?.Dispose();
            _staging = null;
            _stageW = 0;
            _stageH = 0;
        }
        _cropW = width;
        _cropH = height;
        _format = format;
        _gpuCrop = _device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = format,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ShaderResource,
            CPUAccessFlags = CpuAccessFlags.None,
        });
        try
        {
            _cropSrv = _device.CreateShaderResourceView(_gpuCrop);
        }
        catch
        {
            _cropSrv = null;
        }
    }

    private void EnsureGpuDest(int width, int height, Format format)
    {
        if (_device is null)
            return;
        if (_gpuDest != null && _destW == width && _destH == height && _format == format)
            return;
        ReleaseDest();
        _destW = width;
        _destH = height;
        _gpuDest = _device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = format,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
            CPUAccessFlags = CpuAccessFlags.None,
        });
        _destRtv = _device.CreateRenderTargetView(_gpuDest);
        try { _destSrv = _device.CreateShaderResourceView(_gpuDest); }
        catch { _destSrv = null; }
        ReleaseStab();
    }

    private void EnsureStaging(int width, int height, Format format)
    {
        if (_device is null)
            return;
        if (_staging != null && _stageW == width && _stageH == height && _format == format)
            return;
        _staging?.Dispose();
        _stageW = width;
        _stageH = height;
        _staging = _device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = format,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CPUAccessFlags = CpuAccessFlags.Read,
        });
    }

    private void MarkLost()
    {
        _lost = true;
        _retryAt = Environment.TickCount64 + 50;
    }

    private static bool IsLost(int code) =>
        code is DxgiAccessLost or DxgiInvalidCall or DxgiDeviceRemoved or DxgiDeviceReset or DxgiSessionDisconnected;

    private static bool AcquireMutex(IDXGIKeyedMutex mutex, ulong key, int ms)
    {
        try
        {
            mutex.AcquireSync(key, ms);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static Rectangle ToRect(RectI r) => Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);

    private void ReleaseCrop()
    {
        _cropSrv?.Dispose();
        _gpuCrop?.Dispose();
        _cropSrv = null;
        _gpuCrop = null;
        _cropW = 0;
        _cropH = 0;
    }

    private void ReleaseDest()
    {
        if (_context != null)
            _context.OMSetRenderTargets((ID3D11RenderTargetView?)null!);
        _destSrv?.Dispose();
        _destRtv?.Dispose();
        _gpuDest?.Dispose();
        _destSrv = null;
        _destRtv = null;
        _gpuDest = null;
        _destW = 0;
        _destH = 0;
        ReleaseEdge();
        ReleaseCheckerHist();
        ReleaseStab();
    }

    private void ReleaseCheckerHist()
    {
        _checkHistSrv?.Dispose();
        _gpuCheckHist?.Dispose();
        _checkHistSrv = null;
        _gpuCheckHist = null;
        _checkW = 0;
        _checkH = 0;
        _checkReady = false;
        _checkPhase = 0;
    }

    private void ReleaseEdge()
    {
        _edgeSrv?.Dispose();
        _edgeRtv?.Dispose();
        _gpuEdge?.Dispose();
        _edgeSrv = null;
        _edgeRtv = null;
        _gpuEdge = null;
        _edgeW = 0;
        _edgeH = 0;
    }

    private void ReleaseStab()
    {
        _mixedRtv?.Dispose();
        _stabSrv?.Dispose();
        _gpuMixed?.Dispose();
        _gpuStab?.Dispose();
        _mixedRtv = null;
        _stabSrv = null;
        _gpuMixed = null;
        _gpuStab = null;
        _stabReady = false;
    }

    private void ReleaseShare()
    {
        SharedHandle = IntPtr.Zero;
        SharedWidth = 0;
        SharedHeight = 0;
        _shareMutex?.Dispose();
        _gpuShare?.Dispose();
        if (_shareHandle != IntPtr.Zero)
        {
            Native.CloseHandle(_shareHandle);
            _shareHandle = IntPtr.Zero;
        }
        _shareMutex = null;
        _gpuShare = null;
        _shareW = 0;
        _shareH = 0;
    }

    private void DisposeDevice()
    {
        _dup?.Dispose();
        ReleaseShare();
        ReleaseDest();
        ReleaseCrop();
        _cursorSrv?.Dispose();
        _cursorTex?.Dispose();
        _staging?.Dispose();
        _vs?.Dispose();
        _ps?.Dispose();
        _foveaPs?.Dispose();
        _checkerPs?.Dispose();
        _stabPs?.Dispose();
        _cursorPs?.Dispose();
        _sampler?.Dispose();
        _blend?.Dispose();
        _capCb?.Dispose();
        _context?.Dispose();
        _device?.Dispose();
        _dup = null;
        _cursorSrv = null;
        _cursorTex = null;
        _staging = null;
        _vs = null;
        _ps = null;
        _foveaPs = null;
        _checkerPs = null;
        _stabPs = null;
        _cursorPs = null;
        _sampler = null;
        _blend = null;
        _capCb = null;
        _context = null;
        _device = null;
        _stageW = 0;
        _stageH = 0;
        _cursorTw = 0;
        _cursorTh = 0;
        _blitFailed = false;
        _cursorSprite.Reset();
    }

    public void Dispose()
    {
        DisposeDevice();
        _cursorSprite.Dispose();
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct CapCB
{
    public uint StabDead;
    public uint StabSoft;
    public float OutW;
    public float OutH;
    public float CursorX;
    public float CursorY;
    public float CursorW;
    public float CursorH;
}
