namespace CpuScreenViewer;

/// <summary>
/// Preview host that participates in click-through via WM_NCHITTEST (no WS_EX style flips).
/// </summary>
internal sealed class ClickThroughPanel : Panel
{
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0084 && D3DPresenter.PassClicks) // WM_NCHITTEST
        {
            m.Result = (IntPtr)(-1); // HTTRANSPARENT
            return;
        }
        base.WndProc(ref m);
    }
}

internal sealed class ClickThroughLabel : Label
{
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0084 && D3DPresenter.PassClicks)
        {
            m.Result = (IntPtr)(-1);
            return;
        }
        base.WndProc(ref m);
    }
}
