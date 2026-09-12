using System.Drawing.Imaging;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace CpuScreenViewer;

internal sealed class FrameBuffer : IDisposable
{
    private IntPtr _screenDc;
    private IntPtr _old;
    private int[]? _xOff;
    private int[]? _ySrc;
    private int _mapSrcX, _mapSrcY, _mapSrcW, _mapSrcH, _mapDestW, _mapDestH;
    private IntPtr _cursorHandle;
    private int _hotX, _hotY, _cursorW = 32, _cursorH = 32;
    private FrameBuffer? _edgeScratch;

    public int Width { get; private set; }
    public int Height { get; private set; }
    public int Stride { get; private set; }
    public IntPtr Bits { get; private set; }
    public IntPtr Hdc { get; private set; }
    public IntPtr Hbmp { get; private set; }

    public void EnsureScreenDc()
    {
        if (_screenDc == IntPtr.Zero)
            _screenDc = Native.GetDC(IntPtr.Zero);
    }

    public void EnsureSize(int width, int height)
    {
        width = Math.Max(8, width);
        height = Math.Max(8, height);
        if (width == Width && height == Height && Hbmp != IntPtr.Zero)
            return;

        ReleaseBitmap();
        EnsureScreenDc();
        Hdc = Native.CreateCompatibleDC(_screenDc);
        var info = new Native.BitmapInfo
        {
            Header = new Native.BitmapInfoHeader
            {
                Size = 40,
                Width = width,
                Height = -height,
                Planes = 1,
                BitCount = 32,
                Compression = Native.BiRgb,
            },
        };
        Hbmp = Native.CreateDIBSection(Hdc, ref info, Native.DibRgbColors, out var bits, IntPtr.Zero, 0);
        if (Hbmp == IntPtr.Zero || bits == IntPtr.Zero)
            throw new InvalidOperationException("CreateDIBSection failed");
        Bits = bits;
        _old = Native.SelectObject(Hdc, Hbmp);
        Native.SetStretchBltMode(Hdc, Native.ColorOnColor);
        Width = width;
        Height = height;
        Stride = width * 4;
    }

    public void Capture(Rectangle src, int destW, int destH)
    {
        EnsureSize(destW, destH);
        EnsureScreenDc();
        if (src.Width == destW && src.Height == destH)
            Native.BitBlt(Hdc, 0, 0, destW, destH, _screenDc, src.X, src.Y, Native.SrcCopy);
        else
            Native.StretchBlt(Hdc, 0, 0, destW, destH, _screenDc, src.X, src.Y, src.Width, src.Height, Native.SrcCopy);
    }

    /// <summary>
    /// Soft full-frame at edgeScale%, then a sharp center half resampled from the screen.
    /// </summary>
    public void CaptureFoveated(Rectangle src, int destW, int destH, int edgeScale)
    {
        destW = Math.Max(8, destW);
        destH = Math.Max(8, destH);
        edgeScale = Math.Clamp(edgeScale, 25, 100);
        if (edgeScale >= 100)
        {
            Capture(src, destW, destH);
            return;
        }

        EnsureSize(destW, destH);
        EnsureScreenDc();
        var edgeW = Math.Max(8, (destW * edgeScale / 100) & ~1);
        var edgeH = Math.Max(8, (destH * edgeScale / 100) & ~1);
        _edgeScratch ??= new FrameBuffer();
        _edgeScratch.Capture(src, edgeW, edgeH);
        Native.StretchBlt(Hdc, 0, 0, destW, destH, _edgeScratch.Hdc, 0, 0, edgeW, edgeH, Native.SrcCopy);

        var foveaW = Math.Max(8, (destW * 50 / 100) & ~1);
        var foveaH = Math.Max(8, (destH * 50 / 100) & ~1);
        var dx = (destW - foveaW) / 2;
        var dy = (destH - foveaH) / 2;
        var sx = src.X + dx * src.Width / destW;
        var sy = src.Y + dy * src.Height / destH;
        var sw = Math.Max(1, foveaW * src.Width / destW);
        var sh = Math.Max(1, foveaH * src.Height / destH);
        Native.SetStretchBltMode(Hdc, Native.Halftone);
        Native.StretchBlt(Hdc, dx, dy, foveaW, foveaH, _screenDc, sx, sy, sw, sh, Native.SrcCopy);
        Native.SetStretchBltMode(Hdc, Native.ColorOnColor);
    }

    public unsafe void CopyScaled(IntPtr src, int srcX, int srcY, int srcW, int srcH, int srcStride, int destW, int destH)
    {
        EnsureSize(destW, destH);
        if (src == IntPtr.Zero || srcW < 1 || srcH < 1)
            return;
        var s = (byte*)src;
        var d = (byte*)Bits;
        srcX = Math.Max(0, srcX);
        srcY = Math.Max(0, srcY);
        var copy = Math.Min(destW * 4, Stride);
        copy = Math.Min(copy, srcStride - srcX * 4);
        if (copy < 4)
            return;
        if (srcW == destW && srcH == destH)
        {
            if (srcX == 0 && srcStride == Stride)
            {
                var bytes = (nuint)(destH * Stride);
                Buffer.MemoryCopy(s + srcY * srcStride, d, bytes, bytes);
                return;
            }
            for (var y = 0; y < destH; y++)
                Buffer.MemoryCopy(s + (srcY + y) * srcStride + srcX * 4, d + y * Stride, copy, copy);
            return;
        }

        EnsureScaleMap(srcX, srcY, srcW, srcH, destW, destH);
        var xOff = _xOff!;
        var ySrc = _ySrc!;
        for (var y = 0; y < destH; y++)
        {
            var drow = d + y * Stride;
            var srow = s + ySrc[y] * srcStride;
            for (var x = 0; x < destW; x++)
                *(int*)(drow + x * 4) = *(int*)(srow + xOff[x]);
        }
    }

    private void EnsureScaleMap(int srcX, int srcY, int srcW, int srcH, int destW, int destH)
    {
        if (_xOff != null && _ySrc != null &&
            _mapSrcX == srcX && _mapSrcY == srcY && _mapSrcW == srcW && _mapSrcH == srcH &&
            _mapDestW == destW && _mapDestH == destH)
            return;

        _xOff = new int[destW];
        _ySrc = new int[destH];
        var lastX = srcX + srcW - 1;
        var lastY = srcY + srcH - 1;
        for (var x = 0; x < destW; x++)
        {
            var sx = srcX + x * srcW / destW;
            if (sx > lastX)
                sx = lastX;
            _xOff[x] = sx * 4;
        }
        for (var y = 0; y < destH; y++)
        {
            var sy = srcY + y * srcH / destH;
            _ySrc[y] = sy > lastY ? lastY : sy;
        }
        _mapSrcX = srcX;
        _mapSrcY = srcY;
        _mapSrcW = srcW;
        _mapSrcH = srcH;
        _mapDestW = destW;
        _mapDestH = destH;
    }

    public void BlitFromScreen(Rectangle src, int destX, int destY, int destW, int destH)
    {
        EnsureScreenDc();
        if (src.Width == destW && src.Height == destH)
            Native.BitBlt(Hdc, destX, destY, destW, destH, _screenDc, src.X, src.Y, Native.SrcCopy);
        else
            Native.StretchBlt(Hdc, destX, destY, destW, destH, _screenDc, src.X, src.Y, src.Width, src.Height, Native.SrcCopy);
    }

    public unsafe void Blend(FrameBuffer a, FrameBuffer b, int t256)
    {
        if (a.Bits == IntPtr.Zero || b.Bits == IntPtr.Zero || a.Width != b.Width || a.Height != b.Height)
            return;
        EnsureSize(a.Width, a.Height);
        t256 = Math.Clamp(t256, 0, 256);
        var bytes = (nuint)(Height * Stride);
        if (t256 <= 0)
        {
            Buffer.MemoryCopy((void*)a.Bits, (void*)Bits, bytes, bytes);
            return;
        }
        if (t256 >= 256)
        {
            Buffer.MemoryCopy((void*)b.Bits, (void*)Bits, bytes, bytes);
            return;
        }

        var pixels = Width * Height;
        var pa = (uint*)a.Bits;
        var pb = (uint*)b.Bits;
        var pd = (uint*)Bits;
        var t = (uint)t256;
        var u = 256u - t;
        for (var i = 0; i < pixels; i++)
        {
            var ca = pa[i];
            var cb = pb[i];
            var r = ((ca & 0xFF) * u + (cb & 0xFF) * t) >> 8;
            var g = (((ca >> 8) & 0xFF) * u + ((cb >> 8) & 0xFF) * t) >> 8;
            var bl = (((ca >> 16) & 0xFF) * u + ((cb >> 16) & 0xFF) * t) >> 8;
            pd[i] = r | (g << 8) | (bl << 16);
        }
    }

    public void DrawCursor(Rectangle src, int destW, int destH)
    {
        if (Hdc == IntPtr.Zero || src.Width < 1 || src.Height < 1)
            return;
        var info = new Native.CursorInfo { Size = Marshal.SizeOf<Native.CursorInfo>() };
        if (!Native.GetCursorInfo(ref info) || (info.Flags & Native.CursorShowing) == 0 || info.Cursor == IntPtr.Zero)
            return;
        if (info.Cursor != _cursorHandle)
            CacheCursor(info.Cursor);

        var screenX = info.ScreenPos.X - _hotX;
        var screenY = info.ScreenPos.Y - _hotY;
        var cursor = new Rectangle(screenX, screenY, _cursorW, _cursorH);
        if (!cursor.IntersectsWith(src))
            return;

        var x = (screenX - src.X) * destW / src.Width;
        var y = (screenY - src.Y) * destH / src.Height;
        var w = Math.Max(1, _cursorW * destW / src.Width);
        var h = Math.Max(1, _cursorH * destH / src.Height);
        Native.DrawIconEx(Hdc, x, y, info.Cursor, w, h, 0, IntPtr.Zero, Native.DiNormal);
    }

    public unsafe bool TryGetCursorSprite(Rectangle src, int destW, int destH, out int x, out int y, out int w, out int h)
    {
        x = y = w = h = 0;
        if (src.Width < 1 || src.Height < 1 || destW < 1 || destH < 1)
            return false;
        var info = new Native.CursorInfo { Size = Marshal.SizeOf<Native.CursorInfo>() };
        if (!Native.GetCursorInfo(ref info) || (info.Flags & Native.CursorShowing) == 0 || info.Cursor == IntPtr.Zero)
            return false;
        if (info.Cursor != _cursorHandle)
            CacheCursor(info.Cursor);
        var screenX = info.ScreenPos.X - _hotX;
        var screenY = info.ScreenPos.Y - _hotY;
        var cursor = new Rectangle(screenX, screenY, _cursorW, _cursorH);
        if (!cursor.IntersectsWith(src))
            return false;
        x = (screenX - src.X) * destW / src.Width;
        y = (screenY - src.Y) * destH / src.Height;
        w = Math.Max(1, _cursorW * destW / src.Width);
        h = Math.Max(1, _cursorH * destH / src.Height);
        EnsureSize(w, h);
        Clear(0, 0, 0);
        Native.DrawIconEx(Hdc, 0, 0, info.Cursor, w, h, 0, IntPtr.Zero, Native.DiNormal);
        var p = (uint*)Bits;
        var n = Width * Height;
        for (var i = 0; i < n; i++)
        {
            var c = p[i];
            if ((c & 0xFFFFFFu) == 0)
                p[i] = 0;
            else
                p[i] = c | 0xFF000000u;
        }
        return true;
    }

    private void CacheCursor(IntPtr cursor)
    {
        _cursorHandle = cursor;
        _hotX = 0;
        _hotY = 0;
        _cursorW = 32;
        _cursorH = 32;
        if (!Native.GetIconInfo(cursor, out var icon))
            return;
        _hotX = icon.HotspotX;
        _hotY = icon.HotspotY;
        if (icon.Color != IntPtr.Zero && Native.GetObject(icon.Color, Marshal.SizeOf<Native.Bitmap>(), out var bmp) != 0)
        {
            _cursorW = Math.Max(1, bmp.Width);
            _cursorH = Math.Max(1, bmp.Height);
        }
        else if (icon.Mask != IntPtr.Zero && Native.GetObject(icon.Mask, Marshal.SizeOf<Native.Bitmap>(), out var mask) != 0)
        {
            _cursorW = Math.Max(1, mask.Width);
            _cursorH = Math.Max(1, mask.Height / 2);
        }
        if (icon.Mask != IntPtr.Zero)
            Native.DeleteObject(icon.Mask);
        if (icon.Color != IntPtr.Zero)
            Native.DeleteObject(icon.Color);
    }

    public unsafe void Stabilize(FrameBuffer src, int deadzone, int soft)
    {
        if (src.Bits == IntPtr.Zero || src.Width < 1 || src.Height < 1)
            return;
        var bytes = (nuint)(src.Height * src.Stride);
        if (Bits == IntPtr.Zero || Width != src.Width || Height != src.Height)
        {
            EnsureSize(src.Width, src.Height);
            Buffer.MemoryCopy((void*)src.Bits, (void*)Bits, bytes, bytes);
            return;
        }
        MixStabilize((uint*)Bits, (uint*)src.Bits, (uint*)Bits, Width * Height, deadzone, soft);
    }

    public unsafe void StabilizeInPlace(FrameBuffer hist, int deadzone, int soft)
    {
        if (Bits == IntPtr.Zero || hist.Bits == IntPtr.Zero || hist == this)
            return;
        if (hist.Width != Width || hist.Height != Height)
            return;
        MixStabilize((uint*)Bits, (uint*)Bits, (uint*)hist.Bits, Width * Height, deadzone, soft);
    }

    private static unsafe void MixStabilize(uint* dest, uint* src, uint* hist, int pixels, int deadzone, int soft)
    {
        deadzone = Math.Max(0, deadzone);
        soft = Math.Max(1, soft);
        var range = (uint)soft;
        var lockAt = (uint)deadzone;
        var i = 0;
        if (Avx2.IsSupported)
        {
            var n = pixels & ~7;
            for (; i < n; i += 8)
            {
                var vSrc = Vector256.Load(src + i);
                var vHist = Vector256.Load(hist + i);
                var eq = Avx2.CompareEqual(vSrc, vHist);
                if (Avx2.MoveMask(eq.AsByte()) == -1)
                    continue;
                Mix8(dest + i, src + i, hist + i, lockAt, range);
            }
        }
        for (; i < pixels; i++)
            MixOne(dest + i, src[i], hist[i], lockAt, range);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void Mix8(uint* dest, uint* src, uint* hist, uint lockAt, uint range)
    {
        for (var j = 0; j < 8; j++)
            MixOne(dest + j, src[j], hist[j], lockAt, range);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void MixOne(uint* dest, uint c, uint h, uint lockAt, uint range)
    {
        if (c == h)
            return;
        var dr = AbsDiff(c, h, 0);
        var dg = AbsDiff(c, h, 8);
        var db = AbsDiff(c, h, 16);
        var d = dr > dg ? dr : dg;
        if (db > d)
            d = db;
        if (d <= lockAt)
        {
            *dest = h;
            return;
        }
        if (d >= lockAt + range)
        {
            *dest = c;
            return;
        }
        var t = (d - lockAt) * 256u / range;
        var u = 256u - t;
        var r = ((h & 0xFF) * u + (c & 0xFF) * t) >> 8;
        var g = (((h >> 8) & 0xFF) * u + ((c >> 8) & 0xFF) * t) >> 8;
        var b = (((h >> 16) & 0xFF) * u + ((c >> 16) & 0xFF) * t) >> 8;
        *dest = r | (g << 8) | (b << 16);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint AbsDiff(uint a, uint b, int shift)
    {
        var ca = (a >> shift) & 0xFF;
        var cb = (b >> shift) & 0xFF;
        return ca > cb ? ca - cb : cb - ca;
    }

    public unsafe void Clear(byte r, byte g, byte b)
    {
        if (Bits == IntPtr.Zero)
            return;
        var color = r | (g << 8) | (b << 16);
        var pixels = (int*)Bits;
        var count = (Stride / 4) * Height;
        for (var i = 0; i < count; i++)
            pixels[i] = color;
    }

    public Bitmap ToBitmap()
    {
        using var wrap = new Bitmap(Width, Height, Stride, PixelFormat.Format32bppRgb, Bits);
        return (Bitmap)wrap.Clone();
    }

    public unsafe void CopyFrom(FrameBuffer src)
    {
        if (src.Bits == IntPtr.Zero || src.Width < 1 || src.Height < 1)
            return;
        EnsureSize(src.Width, src.Height);
        var bytes = (nuint)(Height * Stride);
        Buffer.MemoryCopy((void*)src.Bits, (void*)Bits, bytes, bytes);
    }

    public void Reset() => ReleaseBitmap();

    private void ReleaseBitmap()
    {
        if (Hdc != IntPtr.Zero && _old != IntPtr.Zero)
            Native.SelectObject(Hdc, _old);
        if (Hbmp != IntPtr.Zero)
            Native.DeleteObject(Hbmp);
        if (Hdc != IntPtr.Zero)
            Native.DeleteDC(Hdc);
        Hdc = IntPtr.Zero;
        Hbmp = IntPtr.Zero;
        Bits = IntPtr.Zero;
        _old = IntPtr.Zero;
        Width = 0;
        Height = 0;
        Stride = 0;
    }

    public void Dispose()
    {
        _edgeScratch?.Dispose();
        _edgeScratch = null;
        ReleaseBitmap();
        if (_screenDc != IntPtr.Zero)
        {
            Native.ReleaseDC(IntPtr.Zero, _screenDc);
            _screenDc = IntPtr.Zero;
        }
    }
}
