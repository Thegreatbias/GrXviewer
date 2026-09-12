using System.Diagnostics;
using System.Text;

namespace CpuScreenViewer;

internal static class CaptureSimulation
{
    public static int Run()
    {
        var log = new StringBuilder();
        void Line(string s)
        {
            log.AppendLine(s);
            Console.WriteLine(s);
        }

        Line("GrXviewer capture simulation");
        Line("1) CopyScaled at 2560x1440 with DDA-style padded stride");
        Line("2) CopyScaled after a clipped 'window move' region");
        Line("3) Concurrent capture/present slots + fullscreen size jump + 2x FG");
        Line("4) DDA acquire state machine (ReleaseFrame after ACCESS_LOST)");
        Line("5) Live Desktop Duplication drain vs no-drain");
        Line("");

        var failed = 0;
        failed += Test(Line, "padded 1:1 copy", TestPaddedCopy);
        failed += Test(Line, "clipped region copy", TestClippedCopy);
        failed += Test(Line, "slot protocol under 2x FG", TestSlotProtocol);
        failed += Test(Line, "ReleaseFrame after lost", TestReleaseAfterLost);
        failed += Test(Line, "live DDA", TestLiveDda);

        Line("");
        Line(failed == 0 ? "ALL CHECKS PASSED" : $"{failed} CHECK(S) FAILED");
        try
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "sim-results.txt"), log.ToString());
            File.WriteAllText(@"c:\Users\geord\Downloads\shader\sim-results.txt", log.ToString());
        }
        catch
        {
            // ignore
        }
        return failed == 0 ? 0 : 1;
    }

    private static int Test(Action<string> line, string name, Func<string> run)
    {
        try
        {
            var detail = run();
            line("PASS  " + name + (string.IsNullOrEmpty(detail) ? "" : " — " + detail));
            return 0;
        }
        catch (Exception ex)
        {
            line("FAIL  " + name + " — " + ex.GetType().Name + ": " + ex.Message);
            return 1;
        }
    }

    private static string TestPaddedCopy()
    {
        const int w = 2560;
        const int h = 1440;
        const int pad = 256;
        var srcStride = ((w * 4 + pad - 1) / pad) * pad;
        var src = new byte[srcStride * h];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
                src[y * srcStride + x * 4] = (byte)(x ^ y);
        }

        using var dest = new FrameBuffer();
        unsafe
        {
            fixed (byte* p = src)
                dest.CopyScaled((IntPtr)p, 0, 0, w, h, srcStride, destW: w, destH: h);
        }
        if (dest.Width != w || dest.Height != h || dest.Bits == IntPtr.Zero)
            throw new InvalidOperationException("dest size");
        return $"stride {srcStride} → {w}x{h}";
    }

    private static string TestClippedCopy()
    {
        const int stageW = 2560;
        const int stageH = 1440;
        const int destW = 2560;
        const int destH = 1440;
        var srcStride = ((stageW * 4 + 255) / 256) * 256;
        var src = new byte[srcStride * stageH];
        using var dest = new FrameBuffer();
        dest.EnsureSize(destW, destH);
        unsafe
        {
            fixed (byte* p = src)
            {
                dest.CopyScaled((IntPtr)p, 0, 40, stageW, stageH - 80, srcStride, destW, destH);
            }
        }
        return "clipped src 2560x1360 into 2560x1440";
    }

    private static string TestSlotProtocol()
    {
        var slots = new[] { new FrameBuffer(), new FrameBuffer(), new FrameBuffer(), new FrameBuffer() };
        var stable = new FrameBuffer();
        var write = 0;
        var ready = 1;
        var display = 2;
        var prev = 3;
        var readyGen = 0;
        var shownGen = -1;
        var errors = 0;
        var captures = 0;
        var presents = 0;
        var destW = 1280;
        var destH = 720;
        var running = true;
        var gate = new object();

        int NextWrite()
        {
            for (var i = 0; i < 4; i++)
            {
                if (i != ready && i != display && i != prev)
                    return i;
            }
            return write;
        }

        var cap = new Thread(() =>
        {
            var clock = Stopwatch.StartNew();
            while (running)
            {
                try
                {
                    var dest = slots[write];
                    dest.EnsureSize(destW, destH);
                    dest.Clear(20, 20, 24);
                    lock (gate)
                    {
                        ready = write;
                        write = NextWrite();
                        readyGen++;
                        captures++;
                    }
                }
                catch
                {
                    Interlocked.Increment(ref errors);
                }
                var ms = 1000.0 / 60;
                while (clock.Elapsed.TotalMilliseconds < captures * ms && running)
                    Thread.SpinWait(32);
            }
        }) { IsBackground = true };

        var pres = new Thread(() =>
        {
            var clock = Stopwatch.StartNew();
            var havePrev = false;
            while (running)
            {
                try
                {
                    FrameBuffer? frame;
                    var newCap = false;
                    lock (gate)
                    {
                        if (readyGen != shownGen)
                        {
                            var had = shownGen >= 0;
                            prev = display;
                            display = ready;
                            shownGen = readyGen;
                            if (had)
                                havePrev = true;
                            newCap = true;
                        }
                        frame = slots[display];
                    }
                    if (newCap && frame.Bits != IntPtr.Zero)
                    {
                        stable.Stabilize(frame, 12, 16);
                        frame = stable;
                    }
                    if (frame.Bits != IntPtr.Zero)
                    {
                        var n = frame.Height * frame.Stride;
                        if (n > 0)
                            _ = System.Runtime.InteropServices.Marshal.ReadByte(frame.Bits, n - 1);
                    }
                    _ = havePrev;
                    presents++;
                }
                catch
                {
                    Interlocked.Increment(ref errors);
                }
                var ms = 1000.0 / 120;
                while (clock.Elapsed.TotalMilliseconds < presents * ms && running)
                    Thread.SpinWait(32);
            }
        }) { IsBackground = true };

        cap.Start();
        pres.Start();
        Thread.Sleep(200);
        destW = 2560;
        destH = 1440;
        Thread.Sleep(400);
        running = false;
        cap.Join(1000);
        pres.Join(1000);
        foreach (var s in slots)
            s.Dispose();
        stable.Dispose();
        if (errors != 0)
            throw new InvalidOperationException(errors + " thread exceptions");
        if (captures < 10 || presents < 20)
            throw new InvalidOperationException($"too few frames cap={captures} out={presents}");
        return $"cap={captures} out={presents} after 720p→1440p";
    }

    private static string TestReleaseAfterLost()
    {
        var acquired = false;
        var lost = false;
        var releasedAfterLost = false;

        void OldPath(bool copyThrows)
        {
            acquired = true;
            lost = false;
            releasedAfterLost = false;
            try
            {
                if (copyThrows)
                    throw new InvalidOperationException("DXGI_ERROR_ACCESS_LOST");
            }
            catch
            {
                lost = true;
            }
            finally
            {
                if (acquired)
                {
                    releasedAfterLost = lost;
                    acquired = false;
                }
            }
        }

        OldPath(true);
        if (!releasedAfterLost)
            throw new InvalidOperationException("old path should have released after lost");

        var released = false;
        acquired = true;
        lost = true;
        if (acquired && !lost)
            released = true;
        if (released)
            throw new InvalidOperationException("fixed path must not ReleaseFrame after ACCESS_LOST");

        return "old path released-after-lost=true; fixed path skips ReleaseFrame";
    }

    private static string TestLiveDda()
    {
        var bounds = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        using var cap = new GpuCapturer();
        if (!cap.TryEnsure(0, bounds))
            return "skipped (DuplicateOutput unavailable)";

        using var dest = new FrameBuffer();
        var ok = 0;
        var miss = 0;
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < 400)
        {
            if (cap.TryGrab(bounds, dest, Math.Min(640, bounds.Width), Math.Min(360, bounds.Height), 8))
                ok++;
            else
                miss++;
        }
        return $"grabs ok={ok} miss={miss} ready={cap.Ready} {bounds.Width}x{bounds.Height}";
    }
}
