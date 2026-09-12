using System.Diagnostics;

namespace CpuScreenViewer;

internal sealed class MainForm : Form
{
    private readonly MenuStrip _menu = new();
    private readonly ClickThroughPanel _preview = new();
    private readonly TextBox _filter = new() { Width = 248, BorderStyle = BorderStyle.FixedSingle };
    private readonly TreeView _tree = new() { Width = 248, Height = 240, BorderStyle = BorderStyle.None };
    private readonly TrackBar _fpsBar = new() { Minimum = 1, Maximum = 240, Value = 60, TickFrequency = 30, Width = 180 };
    private readonly Label _fpsLabel = new() { Text = "60", AutoSize = true };
    private readonly TrackBar _resBar = new() { Minimum = 25, Maximum = 100, Value = 100, TickFrequency = 25, Width = 180 };
    private readonly Label _resLabel = new() { Text = "100%", AutoSize = true };
    private readonly ToolStripMenuItem _pauseItem = new("Pause");
    private readonly ToolStripMenuItem _fullItem = new("Full screen") { Checked = true, CheckOnClick = true };
    private readonly ToolStripMenuItem _progItem = new("Selected programs") { CheckOnClick = true };
    private readonly ToolStripMenuItem _displayMenu = new("Display");
    private readonly ToolStripMenuItem _topMost = new("Always on top") { Checked = true, CheckOnClick = true };
    private readonly ToolStripMenuItem _windowClickThrough = new("Click-through in window") { CheckOnClick = true };
    private bool _windowClickThroughSaved;
    private bool _syncWindowClick;
    private bool _clickThroughOn = true;
    private readonly ToolStripMenuItem _captureCpu = new("Capture on CPU") { Checked = true, CheckOnClick = true };
    private readonly ToolStripMenuItem _captureGpu = new("Capture on GPU") { CheckOnClick = true };
    private readonly ToolStripMenuItem _captureGpuMenu = new("Capture GPU");
    private readonly ToolStripMenuItem _hwDetect = new("Detect hardware and apply");
    private readonly ToolStripMenuItem _laptopPreset = new("Laptop preset (faster capture)");
    private readonly ToolStripMenuItem _hwSummary = new("Detected: …") { Enabled = false };
    private readonly ToolStripMenuItem _gpuRender = new("Present on GPU") { Checked = true, CheckOnClick = true };
    private readonly ToolStripMenuItem _gpuMenu = new("Present GPU");
    private readonly ToolStripMenuItem _fgOff = new("Off") { Checked = true };
    private readonly ToolStripMenuItem _fg2 = new("2x");
    private readonly ToolStripMenuItem _fg3 = new("3x");
    private readonly ToolStripMenuItem _fg4 = new("4x");
    private readonly ToolStripMenuItem _upOff = new("Off (Bilinear)") { Checked = true };
    private readonly ToolStripMenuItem _upLanczos = new("Lanczos 3");
    private readonly ToolStripMenuItem _upFsr = new("FSR 3");
    private UpscaleMode _upscale = UpscaleMode.Bilinear;
    private readonly ToolStripMenuItem _stabOff = new("Off");
    private readonly ToolStripMenuItem _stabVeryLow = new("Very low") { Checked = true };
    private readonly ToolStripMenuItem _stabLow = new("Low");
    private readonly ToolStripMenuItem _stabMed = new("Medium");
    private readonly ToolStripMenuItem _stabHigh = new("High");
    private readonly ToolStripMenuItem _foveated = new("Foveated capture (soft edges)") { CheckOnClick = true };
    private readonly ToolStripMenuItem _checkerboard = new("Checkerboard (test)") { CheckOnClick = true };
    private volatile int _stab = 1;
    private long _stabRefresh;
    private readonly ToolStripMenuItem _showFps = new("Show framerate") { CheckOnClick = true };
    private readonly ClickThroughLabel _fpsHud = new()
    {
        AutoSize = true,
        Visible = false,
        ForeColor = Color.White,
        BackColor = Color.FromArgb(170, 12, 12, 16),
        Font = new Font("Segoe UI", 11f, FontStyle.Bold),
        Padding = new Padding(8, 4, 8, 4),
        Location = new Point(12, 12),
    };
    private int _monitorIndex = 1;
    private int _gpuIndex;
    private int _captureGpuIndex;
    private int _stickyCapAdapter = -1;
    private long _capFailoverAt;
    private int _gpuMisses;
    private int _glassCapMisses;
    private volatile bool _forceCpuReadback;
    private List<GpuAdapter> _gpus = [];
    private bool _autoGpu = true;
    private bool _autoFps = true;
    private bool _applyingHw;
    private readonly object _capLock = new();
    private GpuCapturer? _gpuCap;

    private IPresenter? _d3d;
    private volatile bool _presenterReady;
    private volatile bool _suspendPresent;
    private readonly System.Windows.Forms.Timer _hudTimer = new() { Interval = 250 };
    private readonly System.Windows.Forms.Timer _printShow = new() { Interval = 400 };
    private volatile bool _printHidden;
    private readonly System.Windows.Forms.Timer _resizeTimer = new() { Interval = 80 };
    private readonly Thread _captureThread;
    private readonly Thread _presentThread;
    private readonly object _frameLock = new();
    private readonly object _d3dLock = new();
    private readonly object _stabLock = new();
    private readonly FrameBuffer[] _slots = [new(), new(), new(), new()];
    private readonly FrameBuffer _blend = new();
    private int _write;
    private int _ready = 1;
    private int _display = 2;
    private int _prev = 3;
    private int _readyGen;
    private int _shownGen = -1;
    private IntPtr _shareHandle;
    private int _shareW;
    private int _shareH;
    private bool _havePrev;
    private long _captureStamp;
    private long _captureInterval;
    private volatile bool _running = true;
    private volatile bool _paused;
    private volatile bool _shotFreeze;
    private volatile bool _fullMode = true;
    private volatile int _fps = 60;
    private volatile int _fgMul = 1;
    private volatile int _scale = 100;
    private volatile bool _foveatedMode;
    private volatile bool _checkerboardMode;
    private int _presents;
    private int _captures;
    private int _hudPresentFps;
    private int _hudCaptureFps;
    private long _statsStart = Stopwatch.GetTimestamp();
    private volatile int _hostW = 8;
    private volatile int _hostH = 8;
    private volatile int _destW = 8;
    private volatile int _destH = 8;
    private readonly List<IntPtr> _hwnds = [];
    private readonly List<IntPtr> _captureHwnds = [];
    private readonly List<Rectangle> _captureRects = [];
    private Rectangle _monitorBounds = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
    private bool _menuVisible = true;
    private bool _glass;
    private bool _glassBusy;
    private long _glassStamp;
    private Rectangle _savedBounds;
    private FormBorderStyle _savedBorder;
    private bool _savedControlBox = true;
    private bool _savedMaximizeBox = true;
    private bool _savedMinimizeBox = true;
    private string _savedTitle = "GrXviewer";
    private bool _borderlessForClickThrough;
    private int _pendHostW = 8;
    private int _pendHostH = 8;
    private int _pendDestW = 8;
    private int _pendDestH = 8;
    private int _appliedHostW;
    private int _appliedHostH;
    private int _appliedDestW;
    private int _appliedDestH;
    private bool _needResize;
    private long _homeStamp;
    private HomeHook? _homeHook;
    private readonly FxOverlay _overlay = new();
    private readonly System.Windows.Forms.Timer _shaderWatchTimer = new() { Interval = 400 };
    private FileSystemWatcher? _shaderWatch;
    private PostFxState _fx = PostFxState.Default;
    private bool _fromOverlay;
    private volatile IntPtr _uiForm;
    private volatile IntPtr _uiMenu;
    private volatile IntPtr _uiHud;
    private volatile IntPtr _uiD3d;
    private volatile IntPtr _uiOverlay;

    public MainForm()
    {
        Text = "GrXviewer";
        BackColor = Color.FromArgb(18, 18, 22);
        ForeColor = Color.WhiteSmoke;
        MinimumSize = new Size(960, 600);
        Size = new Size(1280, 780);
        KeyPreview = true;
        TopMost = true;
        BuildUi();
        LoadGpus();
        LoadMonitors();
        RefreshWindows();
        ApplySavedSettings();
        UserShaders.Scan();
        SyncOverlay();
        SyncDestSize();

        LocationChanged += (_, _) => PlacePresenterHost();
        Move += (_, _) => PlacePresenterHost();
        Shown += (_, _) =>
        {
            try
            {
                Native.SetWindowDisplayAffinity(Handle, Native.WdaExcludeFromCapture);
                ExcludeAppFromCapture();
                ApplyClickThrough();
                StartHomeHook();
                SyncDestSize();
                CreatePresenter();
                StartShaderWatch();
                // Layout can finish after Shown — sync again so the first upload isn't size-rejected.
                BeginInvoke(() =>
                {
                    if (IsDisposed)
                        return;
                    SyncDestSize();
                    _appliedHostW = -1;
                    _appliedHostH = -1;
                    _appliedDestW = -1;
                    _appliedDestH = -1;
                    ForcePresenterResize();
                    PlacePresenterHost();
                    PlaceOverlay();
                    lock (_d3dLock)
                        ApplyPresenterResize();
                });
            }
            catch
            {
                _presenterReady = false;
                _d3d?.Dispose();
                _d3d = null;
            }
        };
        _preview.Resize += (_, _) =>
        {
            SyncDestSize();
            _resizeTimer.Stop();
            _resizeTimer.Start();
            PlaceOverlay();
            if (_showFps.Checked)
                UpdateHud();
        };
        _resizeTimer.Tick += (_, _) =>
        {
            _resizeTimer.Stop();
            QueuePresenterResize();
        };
        _preview.MouseDown += (_, _) => OnContentClicked();
        D3DPresenter.ContentClicked = () => Ui(OnContentClicked);
        _hudTimer.Tick += (_, _) => UpdateHud();
        _printShow.Tick += (_, _) => ShowAfterPrint();
        Native.timeBeginPeriod(1);
        _captureThread = new Thread(CaptureLoop)
        {
            IsBackground = true,
            Name = "capture",
            Priority = ThreadPriority.AboveNormal,
        };
        _presentThread = new Thread(PresentLoop)
        {
            IsBackground = true,
            Name = "present",
            Priority = ThreadPriority.AboveNormal,
        };
        _captureThread.Start();
        _presentThread.Start();
        FormClosing += (_, _) =>
        {
            try { AppSettings.Save(SnapshotSettings()); }
            catch { /* ignore */ }
        };
        FormClosed += (_, _) =>
        {
            _running = false;
            _resizeTimer.Stop();
            _hudTimer.Stop();
            _printShow.Stop();
            _shaderWatchTimer.Stop();
            _shaderWatch?.Dispose();
            _shaderWatch = null;
_captureThread.Join(500);
            _presentThread.Join(500);
            _homeHook?.Dispose();
            _homeHook = null;
            Native.timeEndPeriod(1);
            lock (_d3dLock)
            {
                DetachHud();
                _d3d?.Dispose();
                _d3d = null;
            }
            lock (_capLock)
            {
                _gpuCap?.Dispose();
                _gpuCap = null;
            }
            foreach (var slot in _slots)
                slot.Dispose();
            _blend.Dispose();
};
        KeyDown += OnKeyDown;
    }

    private void BuildUi()
    {
        _preview.Dock = DockStyle.Fill;
        _preview.BackColor = Color.Black;

        StyleMenu(_menu);
        _menu.Items.AddRange([FileMenu(), CaptureMenu(), PreviewMenu(), GraphicsMenu(), ViewMenu(), HelpMenu()]);
        MainMenuStrip = _menu;

        _preview.Controls.Add(_overlay);
        _preview.Controls.Add(_fpsHud);

        Controls.Add(_preview);
        Controls.Add(_menu);
        WireMenuPopups(_menu);

        _fullItem.CheckedChanged += (_, _) =>
        {
            if (!_fullItem.Checked)
                return;
            _progItem.Checked = false;
            _fullMode = true;
        };
        _progItem.CheckedChanged += (_, _) =>
        {
            if (!_progItem.Checked)
                return;
            _fullItem.Checked = false;
            _fullMode = false;
            RefreshWindows();
        };
        _filter.TextChanged += (_, _) => RefreshWindows();
        _tree.HideSelection = false;
        _tree.BackColor = Color.FromArgb(28, 28, 34);
        _tree.ForeColor = Color.White;
        _tree.AfterSelect += (_, _) => ApplySelection();
        _fpsBar.ValueChanged += (_, _) =>
        {
            _fps = _fpsBar.Value;
            if (!_applyingHw)
                _autoFps = false;
            UpdateFpsLabel();
        };
        _fgOff.Click += (_, _) => SetFrameGen(1);
        _fg2.Click += (_, _) => SetFrameGen(2);
        _fg3.Click += (_, _) => SetFrameGen(3);
        _fg4.Click += (_, _) => SetFrameGen(4);
        _upOff.Click += (_, _) => SetUpscale(UpscaleMode.Bilinear);
        _upLanczos.Click += (_, _) => SetUpscale(UpscaleMode.Lanczos);
        _upFsr.Click += (_, _) => SetUpscale(UpscaleMode.Fsr3);
        _stabOff.Click += (_, _) => SetStabilizer(0);
        _stabVeryLow.Click += (_, _) => SetStabilizer(1);
        _stabLow.Click += (_, _) => SetStabilizer(2);
        _stabMed.Click += (_, _) => SetStabilizer(3);
        _stabHigh.Click += (_, _) => SetStabilizer(4);
        _foveated.CheckedChanged += (_, _) =>
        {
            if (_foveated.Checked && _checkerboard.Checked)
                _checkerboard.Checked = false;
            _foveatedMode = _foveated.Checked;
            SyncDestSize();
            _resizeTimer.Stop();
            _resizeTimer.Start();
        };
        _checkerboard.CheckedChanged += (_, _) =>
        {
            if (_checkerboard.Checked && _foveated.Checked)
                _foveated.Checked = false;
            _checkerboardMode = _checkerboard.Checked;
            SyncDestSize();
        };
        _overlay.UpscaleChanged += mode => FromOverlay(() => SetUpscale(mode));
        _overlay.FrameGenChanged += mul => FromOverlay(() => SetFrameGen(mul));
        _overlay.StabilizerChanged += level => FromOverlay(() => SetStabilizer(level));
        _overlay.PostFxChanged += fx =>
        {
            _fx = fx;
            PushFx();
        };
        _overlay.ShadersChanged += () => ReloadUserPipeline();
        _showFps.CheckedChanged += (_, _) =>
        {
            _fpsHud.Visible = _showFps.Checked;
            if (_showFps.Checked)
            {
                _hudTimer.Start();
                UpdateHud();
                RaiseHud();
            }
            else
                _hudTimer.Stop();
        };
        _resBar.ValueChanged += (_, _) =>
        {
            if (_applyingHw)
                return;
            _scale = _resBar.Value;
            SyncDestSize();
            _resizeTimer.Stop();
            _resizeTimer.Start();
        };
        _topMost.CheckedChanged += (_, _) => TopMost = _topMost.Checked || _glass;
        _windowClickThrough.CheckedChanged += (_, _) =>
        {
            if (_syncWindowClick)
                return;
            if (_windowClickThrough.Checked && _clickThroughOn && !_glass)
                SetMenuVisible(false);
            else
                ApplyClickThrough();
        };
        _captureCpu.CheckedChanged += (_, _) =>
        {
            if (!_captureCpu.Checked)
                return;
            _captureGpu.Checked = false;
            _captureGpuMenu.Enabled = false;
        };
        _captureGpu.CheckedChanged += (_, _) =>
        {
            if (!_captureGpu.Checked)
                return;
            _captureCpu.Checked = false;
            _captureGpuMenu.Enabled = true;
        };
        _gpuRender.CheckedChanged += (_, _) =>
        {
            _gpuMenu.Enabled = _gpuRender.Checked;
            RecreateGpu();
        };
        _hwDetect.Click += (_, _) =>
        {
            _autoGpu = true;
            _autoFps = true;
            ApplyHardwareProfile(includeFps: true);
        };
        _laptopPreset.Click += (_, _) => ApplyLaptopPreset();
        _captureGpuMenu.Enabled = false;
        _pauseItem.Click += (_, _) =>
        {
            _paused = !_paused;
            _pauseItem.Text = _paused ? "Resume" : "Pause";
        };
    }

    private static void StyleMenu(MenuStrip menu)
    {
        menu.Dock = DockStyle.Top;
        menu.GripStyle = ToolStripGripStyle.Hidden;
        menu.Padding = new Padding(6, 2, 0, 2);
        menu.BackColor = Color.FromArgb(36, 36, 42);
        menu.ForeColor = Color.White;
        menu.Renderer = new DarkMenuRenderer();
    }

    private ToolStripMenuItem FileMenu()
    {
        var file = new ToolStripMenuItem("&File");
        var shot = new ToolStripMenuItem("Save screenshot    Ctrl+S", null, (_, _) => SaveShot());
        var reset = new ToolStripMenuItem("Reset to defaults", null, (_, _) => ResetToDefaults());
        var hide = new ToolStripMenuItem("Hide menu    F8", null, (_, _) => SetMenuVisible(false));
        var overlay = new ToolStripMenuItem("Effect overlay    ¬", null, (_, _) => ToggleOverlay());
        var exit = new ToolStripMenuItem("Exit", null, (_, _) => Close());
        file.DropDownItems.AddRange([_pauseItem, shot, new ToolStripSeparator(), reset, new ToolStripSeparator(), overlay, hide, exit]);
        return file;
    }

    private ToolStripMenuItem CaptureMenu()
    {
        var capture = new ToolStripMenuItem("&Capture");
        var refresh = new ToolStripMenuItem("Refresh programs    F5", null, (_, _) => RefreshWindows());
        capture.DropDownItems.AddRange([_fullItem, _progItem, new ToolStripSeparator(), _displayMenu, ProgramsMenu(), refresh]);
        return capture;
    }

    private ToolStripMenuItem ProgramsMenu()
    {
        var programs = new ToolStripMenuItem("Programs");
        var panel = new Panel
        {
            Width = 260,
            Height = 310,
            BackColor = Color.FromArgb(28, 28, 34),
            Padding = new Padding(6),
        };
        _filter.Location = new Point(6, 6);
        _filter.BackColor = Color.FromArgb(22, 22, 26);
        _filter.ForeColor = Color.White;
        _tree.Location = new Point(6, 34);
        var refresh = new Button
        {
            Text = "Refresh",
            Width = 248,
            Height = 26,
            Location = new Point(6, 278),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(50, 50, 60),
            ForeColor = Color.White,
        };
        refresh.Click += (_, _) => RefreshWindows();
        panel.Controls.Add(_filter);
        panel.Controls.Add(_tree);
        panel.Controls.Add(refresh);
        programs.DropDown.Closing += (_, e) =>
        {
            if (e.CloseReason == ToolStripDropDownCloseReason.ItemClicked)
                e.Cancel = true;
        };
        programs.DropDownItems.Add(new ToolStripControlHost(panel) { AutoSize = false, Margin = Padding.Empty });
        return programs;
    }

    private ToolStripMenuItem PreviewMenu()
    {
        var preview = new ToolStripMenuItem("&Preview");
        var fg = new ToolStripMenuItem("Frame generation");
        fg.DropDownItems.AddRange([_fgOff, _fg2, _fg3, _fg4]);
        var up = new ToolStripMenuItem("Upscaling");
        up.DropDownItems.AddRange([_upOff, _upLanczos, _upFsr]);
        var stab = new ToolStripMenuItem("Motion stabilizer");
        stab.DropDownItems.AddRange([_stabOff, _stabVeryLow, _stabLow, _stabMed, _stabHigh]);
        preview.DropDownItems.Add(SliderHost("FPS", _fpsBar, _fpsLabel));
        preview.DropDownItems.Add(SliderHost("Resolution", _resBar, _resLabel));
        preview.DropDownItems.Add(_foveated);
        preview.DropDownItems.Add(_checkerboard);
        preview.DropDownItems.Add(new ToolStripSeparator());
        preview.DropDownItems.Add(fg);
        preview.DropDownItems.Add(up);
        preview.DropDownItems.Add(stab);
        return preview;
    }

    private static ToolStripControlHost SliderHost(string title, TrackBar bar, Label value)
    {
        var panel = new Panel
        {
            Width = 260,
            Height = 58,
            BackColor = Color.FromArgb(28, 28, 34),
        };
        var label = new Label
        {
            Text = title,
            AutoSize = true,
            Location = new Point(8, 6),
            ForeColor = Color.White,
        };
        value.Location = new Point(200, 6);
        value.ForeColor = Color.White;
        bar.Location = new Point(4, 24);
        bar.BackColor = Color.FromArgb(28, 28, 34);
        panel.Controls.Add(label);
        panel.Controls.Add(value);
        panel.Controls.Add(bar);
        return new ToolStripControlHost(panel) { AutoSize = false, Margin = Padding.Empty };
    }

    private ToolStripMenuItem GraphicsMenu()
    {
        var graphics = new ToolStripMenuItem("&Graphics");
        graphics.DropDownItems.AddRange([
            _hwDetect, _laptopPreset, _hwSummary,
            new ToolStripSeparator(),
            new ToolStripMenuItem("Reset to defaults", null, (_, _) => ResetToDefaults()),
            new ToolStripSeparator(),
            _captureCpu, _captureGpu, _captureGpuMenu,
            new ToolStripSeparator(),
            _gpuRender, _gpuMenu]);
        return graphics;
    }

    private ToolStripMenuItem ViewMenu()
    {
        var view = new ToolStripMenuItem("&View");
        var overlay = new ToolStripMenuItem("Effect overlay    ¬", null, (_, _) => ToggleOverlay());
        var glass = new ToolStripMenuItem("Borderless fullscreen    F11", null, (_, _) => ToggleGlass());
        view.DropDownItems.AddRange([_topMost, _windowClickThrough, _showFps, overlay, new ToolStripSeparator(), glass]);
        return view;
    }

    private static ToolStripMenuItem HelpMenu()
    {
        return new ToolStripMenuItem("&Help", null, (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://discord.com/channels/1408098019194310818/1546622071159656678",
                    UseShellExecute = true,
                });
            }
            catch
            {
                // ignore
            }
        });
    }

    private void SelectMonitor(int index)
    {
        _monitorIndex = index;
        for (var i = 0; i < _displayMenu.DropDownItems.Count; i++)
        {
            if (_displayMenu.DropDownItems[i] is ToolStripMenuItem item)
                item.Checked = i == index;
        }
        ApplyMonitor();
        if (_autoGpu)
            ApplyHardwareProfile(includeFps: _autoFps);
    }

    private void LoadMonitors()
    {
        _displayMenu.DropDownItems.Clear();
        AddMonitorItem($"All displays ({SystemInformation.VirtualScreen.Width}×{SystemInformation.VirtualScreen.Height})", 0);
        var i = 1;
        foreach (var s in Screen.AllScreens)
        {
            AddMonitorItem($"Display {i} ({s.Bounds.Width}×{s.Bounds.Height})", i);
            i++;
        }
        SelectMonitor(Math.Min(1, _displayMenu.DropDownItems.Count - 1));
    }

    private void AddMonitorItem(string text, int index)
    {
        var item = new ToolStripMenuItem(text);
        item.Click += (_, _) => SelectMonitor(index);
        _displayMenu.DropDownItems.Add(item);
    }

    private void LoadGpus()
    {
        _gpus = GpuAdapters.List();
        ApplyHardwareProfile(includeFps: true);
        FillGpuMenu(_gpuMenu, _gpuIndex, SelectGpu);
        FillGpuMenu(_captureGpuMenu, _captureGpuIndex, SelectCaptureGpu);
    }

    private void ApplyHardwareProfile(bool includeFps)
    {
        if (_gpus.Count == 0)
            _gpus = GpuAdapters.List();
        var profile = HardwareProfile.Detect(_gpus, _monitorBounds);
        _hwSummary.Text = profile.Summary;
        if (_autoGpu)
        {
            var gpuChanged = _gpuIndex != profile.PresentGpuIndex;
            var capChanged = _captureGpuIndex != profile.CaptureGpuIndex;
            _gpuIndex = profile.PresentGpuIndex;
            _captureGpuIndex = profile.CaptureGpuIndex;
            if (profile.CaptureOnGpu && !_captureGpu.Checked)
                _captureGpu.Checked = true;
            MarkGpu(_gpuMenu, _gpuIndex);
            MarkGpu(_captureGpuMenu, _captureGpuIndex);
            if (capChanged)
            {
                _stickyCapAdapter = _captureGpuIndex;
                lock (_capLock)
                {
                    _gpuCap?.Dispose();
                    _gpuCap = null;
                }
            }
            if (gpuChanged)
                RecreateGpu();
        }
        if (includeFps && _autoFps)
        {
            var fps = Math.Clamp(profile.Fps, _fpsBar.Minimum, _fpsBar.Maximum);
            _applyingHw = true;
            try
            {
                if (_fpsBar.Value != fps)
                    _fpsBar.Value = fps;
                else
                    _fps = fps;
            }
            finally
            {
                _applyingHw = false;
            }
            UpdateFpsLabel();
        }
    }

    private void ApplyLaptopPreset()
    {
        _autoGpu = true;
        ApplyHardwareProfile(includeFps: false);

        if (!_captureGpu.Checked)
            _captureGpu.Checked = true;
        if (!_gpuRender.Checked)
            _gpuRender.Checked = true;

        SetStabilizer(0);
        SetUpscale(UpscaleMode.Bilinear);

        const int laptopScale = 70;
        _applyingHw = true;
        try
        {
            if (_resBar.Value != laptopScale)
                _resBar.Value = laptopScale;
            else
            {
                _scale = laptopScale;
                SyncDestSize();
            }
        }
        finally
        {
            _applyingHw = false;
        }
        _scale = laptopScale;
        SyncDestSize();
        QueuePresenterResize();

        _hwSummary.Text = "Laptop preset: GPU capture · stab off · 70% res";
        if (!_showFps.Checked)
            _showFps.Checked = true;
        UpdateHud();
    }

    private void FillGpuMenu(ToolStripMenuItem menu, int selected, Action<int> onPick)
    {
        menu.DropDownItems.Clear();
        foreach (var gpu in _gpus.Where(g => !g.Software))
        {
            var item = new ToolStripMenuItem(GpuAdapters.Label(gpu)) { Tag = gpu.Index, ForeColor = Color.White };
            item.Click += (_, _) => onPick(gpu.Index);
            menu.DropDownItems.Add(item);
        }
        if (menu.DropDownItems.Count == 0)
        {
            menu.DropDownItems.Add(new ToolStripMenuItem("No GPU found") { Enabled = false, ForeColor = Color.White });
            return;
        }
        MarkGpu(menu, selected);
    }

    private static void MarkGpu(ToolStripMenuItem menu, int index)
    {
        foreach (ToolStripItem raw in menu.DropDownItems)
        {
            if (raw is ToolStripMenuItem item)
                item.Checked = item.Tag is int tag && tag == index;
        }
    }

    private void SelectGpu(int index)
    {
        _autoGpu = false;
        _gpuIndex = index;
        MarkGpu(_gpuMenu, index);
        if (_d3d != null)
            RecreateGpu();
    }

    private void SelectCaptureGpu(int index)
    {
        _autoGpu = false;
        _captureGpuIndex = index;
        _stickyCapAdapter = index;
        MarkGpu(_captureGpuMenu, index);
        lock (_capLock)
        {
            _gpuCap?.Dispose();
            _gpuCap = null;
        }
    }

    private bool _clickThroughApplied;

    private void CreatePresenter()
    {
        _presenterReady = false;
        _suspendPresent = true;
        SyncDestSize();
        // Only strip click-through styles when click-through is off.
        if (!D3DPresenter.PassClicks)
        {
            ClearLegacyClickStyles(Handle);
            if (_preview.IsHandleCreated)
                ClearLegacyClickStyles(_preview.Handle);
        }
        lock (_d3dLock)
        {
            DetachHud();
            DetachOverlay();
            _d3d?.Dispose();
            _d3d = null;
            _d3d = new D3DPresenter(_preview.Handle, _gpuRender.Checked, _gpuIndex);
            Native.SetWindowDisplayAffinity(_preview.Handle, Native.WdaExcludeFromCapture);
            if (_d3d.Hwnd != IntPtr.Zero)
                Native.SetWindowDisplayAffinity(_d3d.Hwnd, Native.WdaExcludeFromCapture);
            _d3d.Resize(_hostW, _hostH, _destW, _destH);
            _d3d.SetUpscale(_upscale);
            PushFxUnlocked();
            NoteAppliedSize();
            AttachHud();
            AttachOverlay();
            D3DPresenter.PassClicks = ClickThroughActive();
        }
        if (D3DPresenter.PassClicks)
            ApplyFormClickThrough(true);
        else
            ApplyFormClickThrough(false);
        _clickThroughApplied = D3DPresenter.PassClicks;
        lock (_frameLock)
        {
            _shownGen = -1;
            _readyGen = 0;
            _havePrev = false;
            _shareHandle = IntPtr.Zero;
        }
        ExcludeAppFromCapture();
        ResetInterp();
        UpdateInputHook();
        RaiseHud();
        FocusD3dHost();
        _suspendPresent = false;
        _presenterReady = _d3d is { Ok: true };
    }

    private static void ClearLegacyClickStyles(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !Native.IsWindow(hwnd))
            return;
        try
        {
            var style = Native.GetWindowLongPtr(hwnd, Native.GwlExStyle).ToInt64();
            var next = style & ~Native.WsExTransparent & ~Native.WsExLayered;
            if (next != style)
                Native.SetWindowLongPtr(hwnd, Native.GwlExStyle, (IntPtr)next);
        }
        catch
        {
            // ignore
        }
    }

    private void SetUpscale(UpscaleMode mode)
    {
        _upscale = mode;
        _upOff.Checked = mode == UpscaleMode.Bilinear;
        _upLanczos.Checked = mode == UpscaleMode.Lanczos;
        _upFsr.Checked = mode == UpscaleMode.Fsr3;
        lock (_d3dLock)
            _d3d?.SetUpscale(_upscale);
        PushFx();
        SyncOverlay();
        SyncDestSize();
        QueuePresenterResize();
        if (mode != UpscaleMode.Bilinear && !_showFps.Checked)
            _showFps.Checked = true;
        UpdateHud();
    }

    private void RecreateGpu()
    {
        if (_d3d is null)
            return;
        _presenterReady = false;
        _suspendPresent = true;
        SyncDestSize();
        lock (_d3dLock)
        {
            try { _d3d.Idle(); } catch { /* ignore */ }
            _d3d.RecreateDevice(_gpuRender.Checked, _gpuIndex, _hostW, _hostH, _destW, _destH);
            _d3d.SetUpscale(_upscale);
            PushFxUnlocked();
            NoteAppliedSize();
            Native.SetWindowDisplayAffinity(_preview.Handle, Native.WdaExcludeFromCapture);
            if (_d3d.Hwnd != IntPtr.Zero)
                Native.SetWindowDisplayAffinity(_d3d.Hwnd, Native.WdaExcludeFromCapture);
            _d3d.PlaceHost();
            AttachHud();
            AttachOverlay();
            D3DPresenter.PassClicks = ClickThroughActive();
        }
        ExcludeAppFromCapture();
        ResetInterp();
        RaiseHud();
        if (!_menuVisible)
            FocusD3dHost();
        _suspendPresent = false;
        _presenterReady = _d3d is { Ok: true };
    }

    private void SetStabilizer(int level)
    {
        lock (_stabLock)
        {
            _stab = Math.Clamp(level, 0, 4);
            _stabRefresh = 0;
        }
        _stabOff.Checked = _stab == 0;
        _stabVeryLow.Checked = _stab == 1;
        _stabLow.Checked = _stab == 2;
        _stabMed.Checked = _stab == 3;
        _stabHigh.Checked = _stab == 4;
        SyncOverlay();
        UpdateHud();
    }

    private void SetFrameGen(int mul)
    {
        _fgMul = Math.Clamp(mul, 1, 4);
        _fgOff.Checked = _fgMul == 1;
        _fg2.Checked = _fgMul == 2;
        _fg3.Checked = _fgMul == 3;
        _fg4.Checked = _fgMul == 4;
        lock (_frameLock)
        {
            _captureStamp = Stopwatch.GetTimestamp();
            _captureInterval = 0;
        }
        UpdateFpsLabel();
        if (_fgMul > 1 && !_showFps.Checked)
            _showFps.Checked = true;
        SyncOverlay();
        UpdateHud();
    }

    private void UpdateFpsLabel()
    {
        _fpsLabel.Text = _fgMul > 1 ? $"{_fps} → {PresentFps}" : _fps.ToString();
    }

    private int CaptureFps => Math.Max(1, _fps);
    private int PresentFps => Math.Min(240, Math.Max(1, _fps) * Math.Max(1, _fgMul));

    private void ApplyMonitor()
    {
        if (_monitorIndex <= 0)
            _monitorBounds = SystemInformation.VirtualScreen;
        else
        {
            var screens = Screen.AllScreens;
            var idx = Math.Clamp(_monitorIndex - 1, 0, screens.Length - 1);
            _monitorBounds = screens[idx].Bounds;
        }
    }

    private void RefreshWindows()
    {
        var selected = _tree.SelectedNode?.Tag;
        _tree.BeginUpdate();
        _tree.Nodes.Clear();
        var q = _filter.Text.Trim();
        var windows = CaptureService.ListWindows(Environment.ProcessId)
            .Where(w => string.IsNullOrEmpty(q) ||
                        w.App.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        w.Title.Contains(q, StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var group in windows.GroupBy(w => (w.Pid, w.App)))
        {
            var parent = _tree.Nodes.Add(group.Key.App);
            parent.Tag = group.Select(w => w.Hwnd).ToList();
            foreach (var win in group)
            {
                var title = win.Minimized ? win.Title + "  (minimized)" : win.Title;
                var child = parent.Nodes.Add(title);
                child.Tag = win.Hwnd;
            }
        }
        _tree.EndUpdate();
        ApplySelection();
        _ = selected;
    }

    private void ApplySelection()
    {
        lock (_hwnds)
        {
            _hwnds.Clear();
            var node = _tree.SelectedNode;
            if (node?.Tag is List<IntPtr> many)
                _hwnds.AddRange(many);
            else if (node?.Tag is IntPtr one && one != IntPtr.Zero)
                _hwnds.Add(one);
        }
    }

    private void SyncDestSize()
    {
        _hostW = Math.Max(8, _preview.ClientSize.Width);
        _hostH = Math.Max(8, _preview.ClientSize.Height);
        var scale = Math.Clamp(_scale, 25, 100);
        // Foveated: full preview buffer so the center can stay sharp; Resolution = edge quality.
        if (_foveatedMode)
        {
            _destW = Math.Max(8, _hostW & ~1);
            _destH = Math.Max(8, _hostH & ~1);
            _resLabel.Text = $"fovea · edges {scale}%  {_destW}×{_destH}";
        }
        else if (_checkerboardMode)
        {
            _destW = Math.Max(8, (_hostW * scale / 100) & ~1);
            _destH = Math.Max(8, (_hostH * scale / 100) & ~1);
            _resLabel.Text = $"checker · {scale}%  {_destW}×{_destH}";
        }
        else
        {
            _destW = Math.Max(8, (_hostW * scale / 100) & ~1);
            _destH = Math.Max(8, (_hostH * scale / 100) & ~1);
            _resLabel.Text = $"{scale}%  {_destW}×{_destH}";
        }
    }

    private void CaptureLoop()
    {
        CpuTune.ApplyToCurrentThread();
        var clock = Stopwatch.StartNew();
        while (_running)
        {
            if (!_presenterReady)
            {
                Thread.Sleep(8);
                continue;
            }

            var started = clock.Elapsed;
            var got = false;
            if (!_paused && !_shotFreeze)
            {
                try
                {
                    var dest = _slots[_write];
                    var dw = Math.Max(8, _destW);
                    var dh = Math.Max(8, _destH);
                    var stab = _stab;
                    var dead = -1;
                    var soft = 0;
                    if (stab > 0)
                    {
                        lock (_stabLock)
                        {
                            if (_stab > 0)
                            {
                                var now = Stopwatch.GetTimestamp();
                                if (now - _stabRefresh >= Stopwatch.Frequency / 2)
                                {
                                    _stabRefresh = now;
                                    dead = 0;
                                    soft = 0;
                                }
                                else
                                {
                                    dead = _stab switch { 1 => 3, 2 => 6, 4 => 22, _ => 12 };
                                    soft = _stab switch { 1 => 5, 2 => 10, 4 => 28, _ => 16 };
                                }
                            }
                        }
                    }
                    if (GrabFrame(dest, dw, dh, dead, soft))
                    {
                        _glassCapMisses = 0;
                        var gpuShared = false;
                        lock (_capLock)
                            gpuShared = !_forceCpuReadback && _gpuCap != null && _gpuCap.SharedHandle != IntPtr.Zero;
                        if (!gpuShared && dead > 0 && dest.Bits != IntPtr.Zero)
                        {
                            lock (_stabLock)
                            {
                                FrameBuffer hist;
                                lock (_frameLock)
                                    hist = _slots[_ready];
                                if (hist != dest && hist.Bits != IntPtr.Zero &&
                                    hist.Width == dest.Width && hist.Height == dest.Height)
                                    dest.StabilizeInPlace(hist, dead, soft);
                            }
                        }
                        lock (_frameLock)
                        {
                            if (gpuShared)
                            {
                                lock (_capLock)
                                {
                                    _shareHandle = _gpuCap?.SharedHandle ?? IntPtr.Zero;
                                    _shareW = _gpuCap?.SharedWidth ?? 0;
                                    _shareH = _gpuCap?.SharedHeight ?? 0;
                                }
                            }
                            else
                                _shareHandle = IntPtr.Zero;
                            _ready = _write;
                            _write = NextWriteSlot();
                            _readyGen++;
                            Interlocked.Increment(ref _captures);
                        }
                        got = true;
                    }
                }
                catch
                {
                    // keep last good frame
                }
            }

            if (!got)
            {
                if (_glass)
                {
                    _glassCapMisses++;
                    // DDA often dies across the borderless transition — rebuild duplication.
                    if (_glassCapMisses >= 12)
                    {
                        _glassCapMisses = 0;
                        ResetCapturePipeline();
                    }
                }
                Thread.Sleep(_glass ? 0 : 1);
                continue;
            }

            var frameMs = 1000.0 / CaptureFps;
            var elapsed = (clock.Elapsed - started).TotalMilliseconds;
            var remain = frameMs - elapsed;
            if (remain > 2)
                Thread.Sleep((int)remain - 1);
            while ((clock.Elapsed - started).TotalMilliseconds < frameMs && _running)
                Thread.SpinWait(64);
        }
    }

    private bool TryGrabGpu(int adapterIndex, FrameBuffer dest, int dw, int dh, int timeoutMs, int stabDead, int stabSoft, bool readback)
    {
        var src = _monitorBounds;
        if (!_fullMode)
        {
            _captureHwnds.Clear();
            lock (_hwnds)
                _captureHwnds.AddRange(_hwnds);
            if (_captureHwnds.Count != 1 || !CaptureService.TryGetWindowBounds(_captureHwnds[0], out src))
                return false;
        }

        lock (_capLock)
        {
            _gpuCap ??= new GpuCapturer();
            if (!_gpuCap.TryEnsure(adapterIndex, src))
                return false;
            _gpuCap.Foveated = _foveatedMode;
            _gpuCap.FoveaEdgeScale = Math.Clamp(_scale, 25, 100);
            _gpuCap.Checkerboard = _checkerboardMode;
            return _gpuCap.TryGrab(src, dest, dw, dh, timeoutMs, readback, stabDead, stabSoft);
        }
    }

    private bool GrabFrame(FrameBuffer dest, int dw, int dh, int stabDead, int stabSoft)
    {
        var timeoutMs = Math.Clamp(1000 / Math.Max(1, CaptureFps), _glass ? 8 : 2, _glass ? 33 : 16);
        var preferred = _captureGpu.Checked ? _captureGpuIndex : _gpuIndex;
        var adapter = _stickyCapAdapter >= 0 ? _stickyCapAdapter : preferred;
        // Shared handles often fail across Intel / hybrid GPUs — fall back to CPU readback.
        var readback = _forceCpuReadback;

        if (TryGrabGpu(adapter, dest, dw, dh, timeoutMs, stabDead, stabSoft, readback))
        {
            _stickyCapAdapter = adapter;
            _gpuMisses = 0;
            return true;
        }

        _gpuMisses++;
        if (_gpuMisses >= 8)
        {
            _stickyCapAdapter = -1;
            _gpuMisses = 0;
        }

        var now = Environment.TickCount64;
        if (now >= _capFailoverAt)
        {
            _capFailoverAt = now + 250;
            var display = GpuAdapters.CaptureIndexFor(_gpus, _monitorBounds);
            if (display != adapter && TryGrabGpu(display, dest, dw, dh, timeoutMs, stabDead, stabSoft, readback))
            {
                _stickyCapAdapter = display;
                return true;
            }
            if (preferred != adapter && preferred != display
                && TryGrabGpu(preferred, dest, dw, dh, timeoutMs, stabDead, stabSoft, readback))
            {
                _stickyCapAdapter = preferred;
                return true;
            }
            foreach (var gpu in _gpus)
            {
                if (gpu.Software || gpu.Index == adapter || gpu.Index == display || gpu.Index == preferred)
                    continue;
                if (gpu.OverlapArea(_monitorBounds) <= 0)
                    continue;
                if (TryGrabGpu(gpu.Index, dest, dw, dh, timeoutMs, stabDead, stabSoft, readback))
                {
                    _stickyCapAdapter = gpu.Index;
                    return true;
                }
            }
        }

        // Always allow GDI fallback so Intel/hybrid never sits on a black/frozen frame.
        // In glass, WDA_EXCLUDE_FROM_CAPTURE should omit our overlay from the blit.
        if (_fullMode)
        {
            if (_foveatedMode)
                dest.CaptureFoveated(_monitorBounds, dw, dh, Math.Clamp(_scale, 25, 100));
            else
                dest.Capture(_monitorBounds, dw, dh);
            return true;
        }

        _captureHwnds.Clear();
        lock (_hwnds)
            _captureHwnds.AddRange(_hwnds);
        _captureRects.Clear();
        foreach (var hwnd in _captureHwnds)
        {
            if (CaptureService.TryGetWindowBounds(hwnd, out var rect))
                _captureRects.Add(rect);
        }
        if (_captureRects.Count == 0)
            return false;

        dest.EnsureSize(dw, dh);
        if (_captureRects.Count == 1)
        {
            if (_foveatedMode)
                dest.CaptureFoveated(_captureRects[0], dw, dh, Math.Clamp(_scale, 25, 100));
            else
                dest.Capture(_captureRects[0], dw, dh);
            return true;
        }

        dest.Clear(18, 18, 22);
        var cols = (int)Math.Ceiling(Math.Sqrt(_captureRects.Count));
        var rows = (int)Math.Ceiling(_captureRects.Count / (double)cols);
        var gap = 12;
        var cellW = Math.Max(8, (dw - (cols + 1) * gap) / cols);
        var cellH = Math.Max(8, (dh - (rows + 1) * gap) / rows);
        for (var i = 0; i < _captureRects.Count; i++)
        {
            var row = i / cols;
            var col = i % cols;
            var x = gap + col * (cellW + gap);
            var y = gap + row * (cellH + gap);
            dest.BlitFromScreen(_captureRects[i], x, y, cellW, cellH);
        }
        return true;
    }

    private int NextWriteSlot()
    {
        for (var i = 0; i < _slots.Length; i++)
        {
            if (i != _ready && i != _display && i != _prev)
                return i;
        }
        return _write;
    }

    private void PresentLoop()
    {
        CpuTune.ApplyToCurrentThread();
        var clock = Stopwatch.StartNew();
        while (_running)
        {
            var started = clock.Elapsed;
            try
            {
                PresentOnce();
            }
            catch
            {
                // keep last good frame
            }

            var frameMs = 1000.0 / PresentFps;
            var elapsed = (clock.Elapsed - started).TotalMilliseconds;
            var remain = frameMs - elapsed;
            if (remain > 2)
                Thread.Sleep((int)remain - 1);
            while ((clock.Elapsed - started).TotalMilliseconds < frameMs && _running)
                Thread.SpinWait(64);
        }
    }

    private void PresentOnce()
    {
        if (_suspendPresent || _glassBusy)
            return;

        FrameBuffer? frame = null;
        var t = 1f;
        var newCapture = false;
        var share = IntPtr.Zero;
        var shareW = 0;
        var shareH = 0;
        lock (_frameLock)
        {
            if (_readyGen != _shownGen)
            {
                var hadFrame = _shownGen >= 0;
                _prev = _display;
                _display = _ready;
                _shownGen = _readyGen;
                var now = Stopwatch.GetTimestamp();
                if (_captureStamp != 0)
                    _captureInterval = now - _captureStamp;
                _captureStamp = now;
                if (hadFrame)
                    _havePrev = true;
                newCapture = true;
            }

            frame = _slots[_display];
            if (_fgMul > 1 && _havePrev)
                t = BlendT();
            share = _shareHandle;
            shareW = _shareW;
            shareH = _shareH;
        }

        lock (_d3dLock)
        {
            if (_suspendPresent || _glassBusy || _d3d is not { Ok: true })
                return;
            // Match swap-chain buffer to the latest capture so uploads aren't rejected.
            var capW = share != IntPtr.Zero ? shareW : frame?.Width ?? 0;
            var capH = share != IntPtr.Zero ? shareH : frame?.Height ?? 0;
            if (capW >= 8 && capH >= 8 && (capW != _appliedDestW || capH != _appliedDestH))
            {
                SyncDestSize();
                _pendHostW = Math.Max(8, _hostW);
                _pendHostH = Math.Max(8, _hostH);
                _pendDestW = capW;
                _pendDestH = capH;
                _needResize = true;
            }
            if (ApplyPresenterResize())
                t = 1f;
            _d3d.SetFrameBlend(t);
            var uploaded = false;
            var present = frame;
            if ((newCapture || !_d3d.HasImage) && share != IntPtr.Zero)
            {
                uploaded = _d3d.UploadShared(share, shareW, shareH);
                if (!uploaded && shareW >= 8 && shareH >= 8)
                {
                    // One retry after forcing buffer size to the shared texture.
                    _pendHostW = Math.Max(8, _hostW);
                    _pendHostH = Math.Max(8, _hostH);
                    _pendDestW = shareW;
                    _pendDestH = shareH;
                    _needResize = true;
                    ApplyPresenterResize();
                    uploaded = _d3d.UploadShared(share, shareW, shareH);
                }
                if (!uploaded)
                    _forceCpuReadback = true; // Intel/hybrid: shared NT handles often fail
            }
            if (!uploaded && present != null && present.Bits != IntPtr.Zero &&
                (newCapture || !_d3d.HasImage))
            {
                if (present.Width != _appliedDestW || present.Height != _appliedDestH)
                {
                    _pendHostW = Math.Max(8, _hostW);
                    _pendHostH = Math.Max(8, _hostH);
                    _pendDestW = present.Width;
                    _pendDestH = present.Height;
                    _needResize = true;
                    ApplyPresenterResize();
                }
                uploaded = _d3d.Upload(present.Bits, present.Width, present.Height, present.Stride);
                if (uploaded)
                    _forceCpuReadback = true;
            }
            if (_d3d.Present())
                Interlocked.Increment(ref _presents);
        }

        NoteFps();
    }

    private float BlendT()
    {
        var interval = _captureInterval > 0
            ? _captureInterval
            : Stopwatch.Frequency / Math.Max(1, CaptureFps);
        var elapsed = Stopwatch.GetTimestamp() - _captureStamp;
        return (float)Math.Clamp(elapsed / (double)Math.Max(1, interval), 0, 1);
    }

    private void NoteFps()
    {
        var now = Stopwatch.GetTimestamp();
        if (now - _statsStart < Stopwatch.Frequency)
            return;
        _hudPresentFps = Interlocked.Exchange(ref _presents, 0);
        _hudCaptureFps = Interlocked.Exchange(ref _captures, 0);
        _statsStart = now;
    }

    private void UpdateHud()
    {
        if (!_showFps.Checked)
            return;
        var up = _upscale switch
        {
            UpscaleMode.Lanczos => "   Lanczos",
            UpscaleMode.Fsr3 => "   FSR3",
            _ => "",
        };
        up += _fx.BufferView switch
        {
            1 => "   Color",
            2 => "   Depth",
            3 => "   Motion",
            4 => "   History",
            5 => "   Buffers",
            _ => "",
        };
        if (_fx.Fog)
            up += "   Fog";
        if (_fx.Dof)
            up += "   DoF";
        if (_fx.MotionBlur)
            up += "   MBlur";
        var stab = _stab switch
        {
            1 => "   Stab VL",
            2 => "   Stab L",
            3 => "   Stab",
            4 => "   Stab H",
            _ => "",
        };
        _fpsHud.Text = _fgMul > 1
            ? $"{_hudPresentFps} out   {_hudCaptureFps} cap   {_fgMul}x FG{up}{stab}"
            : $"{_hudPresentFps} fps{up}{stab}";
        _fpsHud.Location = new Point(Math.Max(12, _preview.ClientSize.Width - _fpsHud.Width - 12), 12);
        RaiseHud();
    }

    private void RaiseHud()
    {
        if (!_showFps.Checked || !_fpsHud.IsHandleCreated)
            return;
        AttachHud();
        _fpsHud.BringToFront();
        Native.SetWindowPos(_fpsHud.Handle, Native.HwndTop, 0, 0, 0, 0, Native.SwpNoMove | Native.SwpNoSize);
        if (_overlay.Visible)
            _overlay.BringToFront();
    }

    private void SetMenuVisible(bool visible)
    {
        if (_menuVisible == visible && _menu.Visible == visible)
        {
            ApplyClickThroughCore();
            return;
        }

        var nested = _suspendPresent || _glassBusy;
        if (!nested)
        {
            _suspendPresent = true;
            PausePresenterBriefly();
        }

        try
        {
            _menuVisible = visible;
            _menu.Visible = visible;
            // Flag only while suspended — rebuild after Present can run again.
            D3DPresenter.PassClicks = ClickThroughActive();
            try { UpdateInputHook(); } catch { /* ignore */ }

            if (!nested && !_glassBusy)
            {
                SyncDestSize();
                lock (_d3dLock)
                {
                    try { _d3d?.Idle(); } catch { /* ignore */ }
                    _pendHostW = _hostW;
                    _pendHostH = _hostH;
                    _pendDestW = _destW;
                    _pendDestH = _destH;
                    _needResize = true;
                    ApplyPresenterResize();
                    _d3d?.PlaceHost();
                }
            }
            if (visible)
            {
                _menu.BringToFront();
                ExcludeAppFromCapture();
            }
            else
                FocusD3dHost();
        }
        finally
        {
            if (!nested)
            {
                _suspendPresent = false;
                _presenterReady = _d3d is { Ok: true };
            }
        }

        if (!nested)
            ApplyClickThroughCore();
    }

    private void WireMenuPopups(ToolStrip strip)
    {
        foreach (ToolStripItem item in strip.Items)
            WireMenuItem(item);
    }

    private void WireMenuItem(ToolStripItem item)
    {
        if (item is not ToolStripDropDownItem drop)
            return;
        drop.DropDownOpening += (_, _) => ProtectMenuPopup(drop.DropDown);
        drop.DropDownOpened += (_, _) => ProtectMenuPopup(drop.DropDown);
        WireMenuPopups(drop.DropDown);
    }

    private void ProtectMenuPopup(ToolStripDropDown drop)
    {
        if (!drop.IsHandleCreated)
            _ = drop.Handle;
        Native.RaiseOwnedPopup(drop.Handle);
        ExcludeAppFromCapture();
    }

    private void ToggleGlass()
    {
        if (_glassBusy)
            return;
        var now = Stopwatch.GetTimestamp();
        if (now - _glassStamp < Stopwatch.Frequency / 4)
            return;
        _glassStamp = now;
        if (_glass) ExitGlass();
        else EnterGlass();
    }

    private void EnterGlass()
    {
        if (_glass || _glassBusy || IsDisposed)
            return;
        _glassBusy = true;
        _suspendPresent = true;
        _presenterReady = false;
        try
        {
            PausePresenterBriefly();
            _savedBounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            // Keep original window chrome if click-through already went borderless.
            if (!_borderlessForClickThrough)
            {
                _savedBorder = FormBorderStyle;
                _savedControlBox = ControlBox;
                _savedMaximizeBox = MaximizeBox;
                _savedMinimizeBox = MinimizeBox;
                _savedTitle = string.IsNullOrEmpty(Text) ? "GrXviewer" : Text;
            }
            _borderlessForClickThrough = false;
            _glass = true;
            SetWindowClickThroughLocked(true);
            SetMenuVisible(false);
            // Maximized + borderless Bounds changes can AV DXGI; normalize first.
            if (WindowState != FormWindowState.Normal)
                WindowState = FormWindowState.Normal;
            // True borderless — no Windows title bar / caption / system buttons.
            FormBorderStyle = FormBorderStyle.None;
            ControlBox = false;
            MaximizeBox = false;
            MinimizeBox = false;
            Text = "";
            TopMost = true;
            Bounds = _monitorBounds;
            D3DPresenter.PassClicks = true;
            ExcludeAppFromCapture();
            FinishGlassLayout();
        }
        catch
        {
            _glass = false;
            _suspendPresent = false;
            _glassBusy = false;
            _presenterReady = _d3d is { Ok: true };
        }
    }

    private void ExitGlass()
    {
        if (!_glass || _glassBusy || IsDisposed)
            return;
        _glassBusy = true;
        _suspendPresent = true;
        _presenterReady = false;
        try
        {
            PausePresenterBriefly();
            _glass = false;
            SetWindowClickThroughLocked(false);
            if (WindowState != FormWindowState.Normal)
                WindowState = FormWindowState.Normal;
            FormBorderStyle = _savedBorder == FormBorderStyle.None ? FormBorderStyle.Sizable : _savedBorder;
            ControlBox = _savedControlBox;
            MaximizeBox = _savedMaximizeBox;
            MinimizeBox = _savedMinimizeBox;
            Text = string.IsNullOrEmpty(_savedTitle) ? "GrXviewer" : _savedTitle;
            var restore = _savedBounds;
            if (restore.Width < 200 || restore.Height < 150)
                restore = new Rectangle(restore.Location, new Size(1280, 780));
            Bounds = restore;
            SetMenuVisible(true);
            TopMost = _topMost.Checked;
            D3DPresenter.PassClicks = ClickThroughActive();
            FinishGlassLayout();
        }
        catch
        {
            _suspendPresent = false;
            _glassBusy = false;
            _presenterReady = _d3d is { Ok: true };
        }
    }

    private void FinishGlassLayout()
    {
        void Apply()
        {
            try
            {
                if (IsDisposed || !IsHandleCreated)
                    return;
                SyncDestSize();
                // Avoid 0-size swap chain if layout hasn't settled yet.
                if (_hostW < 32 || _hostH < 32)
                {
                    _hostW = Math.Max(8, _monitorBounds.Width);
                    _hostH = Math.Max(8, _monitorBounds.Height);
                    var scale = Math.Clamp(_scale, 25, 100);
                    if (_foveatedMode)
                    {
                        _destW = Math.Max(8, _hostW & ~1);
                        _destH = Math.Max(8, _hostH & ~1);
                    }
                    else
                    {
                        _destW = Math.Max(8, (_hostW * scale / 100) & ~1);
                        _destH = Math.Max(8, (_hostH * scale / 100) & ~1);
                    }
                }

                // Glass: rebuild with click-through (transparent, no layered) + live capture.
                var want = ClickThroughActive();
                D3DPresenter.PassClicks = want;
                RebuildGlassPresenter(want);
                ExcludeAppFromCapture();
                ResetCapturePipeline();
                _glassCapMisses = 0;
                if (!_captureGpu.Checked)
                    _captureGpu.Checked = true;
                _forceCpuReadback = false;
                RaiseHud();
                // Don't steal focus when click-through is on — that blocks pass-through.
                if (!want && !_menuVisible)
                    FocusD3dHost();

                // Re-assert click-through styles after DWM settles (swap chain already alive —
                // only refresh; do not recreate).
                BeginInvoke(new Action(() =>
                {
                    if (!_glass || IsDisposed)
                        return;
                    D3DPresenter.PassClicks = ClickThroughActive();
                    ApplyFormClickThrough(D3DPresenter.PassClicks);
                    ExcludeAppFromCapture();
                    ResetCapturePipeline();
                }));
            }
            catch
            {
                // resize mid-present can fail; next frame retries
            }
            finally
            {
                ExcludeAppFromCapture();
                _suspendPresent = false;
                _glassBusy = false;
                _presenterReady = _d3d is { Ok: true };
            }
        }

        if (IsHandleCreated)
            BeginInvoke(Apply);
        else
            Apply();
    }

    private void ResetCapturePipeline()
    {
        lock (_capLock)
        {
            try { _gpuCap?.Dispose(); } catch { /* ignore */ }
            _gpuCap = null;
        }
        _forceCpuReadback = false;
        _stickyCapAdapter = -1;
        _gpuMisses = 0;
        lock (_frameLock)
        {
            _shareHandle = IntPtr.Zero;
            _shareW = 0;
            _shareH = 0;
            _havePrev = false;
            _shownGen = -1;
        }
    }

    private void PausePresenterBriefly()
    {
        for (var i = 0; i < 25; i++)
        {
            if (Monitor.TryEnter(_d3dLock))
            {
                try { _d3d?.Idle(); }
                catch { /* ignore */ }
                finally { Monitor.Exit(_d3dLock); }
                return;
            }
            Thread.Sleep(2);
        }
    }

    private void SetWindowClickThroughLocked(bool fullscreen)
    {
        _syncWindowClick = true;
        if (fullscreen)
        {
            _windowClickThroughSaved = _windowClickThrough.Checked;
            _windowClickThrough.Checked = false;
            _windowClickThrough.Enabled = false;
        }
        else
        {
            _windowClickThrough.Enabled = true;
            _windowClickThrough.Checked = _windowClickThroughSaved;
        }
        _syncWindowClick = false;
    }

    private void QueuePresenterResize()
    {
        SyncDestSize();
        var sizeChanged = _hostW != _appliedHostW || _hostH != _appliedHostH
            || _destW != _appliedDestW || _destH != _appliedDestH;
        if (!sizeChanged)
            return;
        ForcePresenterResize();
    }

    private void ForcePresenterResize()
    {
        SyncDestSize();
        lock (_d3dLock)
        {
            _pendHostW = _hostW;
            _pendHostH = _hostH;
            _pendDestW = _destW;
            _pendDestH = _destH;
            _needResize = true;
        }
        ResetInterp();
    }

    private bool ApplyPresenterResize()
    {
        if (!_needResize || _d3d is null)
            return false;
        _needResize = false;
        try
        {
            // Always apply — Resize no-ops cheaply when sizes already match.
            var changed = _d3d.Resize(
                Math.Max(8, _pendHostW),
                Math.Max(8, _pendHostH),
                Math.Max(8, _pendDestW),
                Math.Max(8, _pendDestH));
            _appliedHostW = _pendHostW;
            _appliedHostH = _pendHostH;
            _appliedDestW = _pendDestW;
            _appliedDestH = _pendDestH;
            return changed;
        }
        catch
        {
            return false;
        }
    }

    private void NoteAppliedSize()
    {
        _appliedHostW = _hostW;
        _appliedHostH = _hostH;
        _appliedDestW = _destW;
        _appliedDestH = _destH;
    }

    private void ResetInterp()
    {
        lock (_frameLock)
            _havePrev = false;
    }

    private void ApplyClickThrough() => ApplyClickThroughCore();

    private void ApplyClickThroughCore(bool recreateIfLayeredChanged = false)
    {
        _ = recreateIfLayeredChanged;
        var want = ClickThroughActive();
        D3DPresenter.PassClicks = want;
        try { UpdateInputHook(); }
        catch { /* ignore */ }

        // Glass layout owns the rebuild while _glassBusy; don't fight it.
        if (_glassBusy || !IsHandleCreated)
            return;

        if (_clickThroughApplied == want && _d3d is { Ok: true })
            return;

        RebuildClickThroughPresenter(want);
    }

    /// <summary>
    /// Dispose swap chain → style top-level form → create presenter.
    /// Same path for windowed click-through and fullscreen glass.
    /// </summary>
    private void RebuildClickThroughPresenter(bool want)
    {
        _suspendPresent = true;
        _presenterReady = false;
        try
        {
            PausePresenterBriefly();
            lock (_d3dLock)
            {
                try { _d3d?.Idle(); } catch { /* ignore */ }
                DetachHud();
                DetachOverlay();
                _d3d?.Dispose();
                _d3d = null;
            }

            // No live swap chain — safe to style the WinForms top-level window.
            D3DPresenter.PassClicks = want;
            ApplyBorderlessForClickThrough(want);
            ApplyFormClickThrough(want);

            SyncDestSize();
            lock (_d3dLock)
            {
                _d3d = new D3DPresenter(_preview.Handle, _gpuRender.Checked, _gpuIndex);
                Native.SetWindowDisplayAffinity(_preview.Handle, Native.WdaExcludeFromCapture);
                if (_d3d.Hwnd != IntPtr.Zero)
                    Native.SetWindowDisplayAffinity(_d3d.Hwnd, Native.WdaExcludeFromCapture);
                _d3d.Resize(_hostW, _hostH, _destW, _destH);
                _d3d.SetUpscale(_upscale);
                PushFxUnlocked();
                NoteAppliedSize();
                AttachHud();
                AttachOverlay();
            }
            ExcludeAppFromCapture();
            ResetInterp();
            lock (_frameLock)
            {
                _shownGen = -1;
                _havePrev = false;
                _shareHandle = IntPtr.Zero;
            }
            _clickThroughApplied = want;
            RaiseHud();
        }
        catch
        {
            // keep going — resume present below
        }
        finally
        {
            if (!_glassBusy)
            {
                _suspendPresent = false;
                _presenterReady = _d3d is { Ok: true };
            }
        }
    }

    private void ApplyBorderlessForClickThrough(bool on)
    {
        if (_glass || !IsHandleCreated)
            return;
        try
        {
            if (on)
            {
                if (!_borderlessForClickThrough)
                {
                    _savedBorder = FormBorderStyle;
                    _savedControlBox = ControlBox;
                    _savedMaximizeBox = MaximizeBox;
                    _savedMinimizeBox = MinimizeBox;
                    _savedTitle = string.IsNullOrEmpty(Text) ? "GrXviewer" : Text;
                    _borderlessForClickThrough = true;
                }
                if (WindowState != FormWindowState.Normal)
                    WindowState = FormWindowState.Normal;
                FormBorderStyle = FormBorderStyle.None;
                ControlBox = false;
                MaximizeBox = false;
                MinimizeBox = false;
                Text = "";
            }
            else if (_borderlessForClickThrough)
            {
                FormBorderStyle = _savedBorder == FormBorderStyle.None
                    ? FormBorderStyle.Sizable
                    : _savedBorder;
                ControlBox = _savedControlBox;
                MaximizeBox = _savedMaximizeBox;
                MinimizeBox = _savedMinimizeBox;
                Text = string.IsNullOrEmpty(_savedTitle) ? "GrXviewer" : _savedTitle;
                _borderlessForClickThrough = false;
            }
        }
        catch
        {
            // ignore
        }
    }

    private void ApplyFormClickThrough(bool on)
    {
        if (!IsHandleCreated)
            return;
        try
        {
            var style = Native.GetWindowLongPtr(Handle, Native.GwlExStyle).ToInt64();
            var next = style;
            if (on)
            {
                // LAYERED + TRANSPARENT is what Windows needs for clicks to reach apps below
                // (same for windowed and borderless fullscreen).
                next |= Native.WsExTransparent;
                next |= Native.WsExLayered;
                next |= Native.WsExNoActivate;
            }
            else
            {
                next &= ~Native.WsExTransparent;
                next &= ~Native.WsExLayered;
                next &= ~Native.WsExNoActivate;
            }
            if (next != style)
                Native.SetWindowLongPtr(Handle, Native.GwlExStyle, (IntPtr)next);
            if ((next & Native.WsExLayered) != 0)
                Native.SetLayeredWindowAttributes(Handle, 0, 255, Native.LwaAlpha);
            Native.SetWindowPos(
                Handle,
                IntPtr.Zero,
                0, 0, 0, 0,
                Native.SwpNoMove | Native.SwpNoSize | Native.SwpNoZOrder | Native.SwpNoActivate | Native.SwpFrameChanged);
            Native.ExcludeFromCapture(Handle);
        }
        catch
        {
            // ignore
        }
    }

    /// <summary>
    /// Glass presenter rebuild with click-through (transparent form, no layered).
    /// </summary>
    private void RebuildGlassPresenter(bool wantPassClicks)
    {
        _suspendPresent = true;
        _presenterReady = false;
        try
        {
            PausePresenterBriefly();
            lock (_d3dLock)
            {
                try { _d3d?.Idle(); } catch { /* ignore */ }
                DetachHud();
                DetachOverlay();
                _d3d?.Dispose();
                _d3d = null;
            }

            // Strip leftover styles, then apply LAYERED+TRANSPARENT (required for real click-through).
            ClearLegacyClickStyles(Handle);
            if (_preview.IsHandleCreated)
                ClearLegacyClickStyles(_preview.Handle);

            D3DPresenter.PassClicks = wantPassClicks;
            ApplyFormClickThrough(wantPassClicks);

            SyncDestSize();
            if (_hostW < 32 || _hostH < 32)
            {
                _hostW = Math.Max(8, _monitorBounds.Width);
                _hostH = Math.Max(8, _monitorBounds.Height);
            }

            lock (_d3dLock)
            {
                // PassClicks true → CreateWindowEx includes WS_EX_TRANSPARENT on the D3D child.
                _d3d = new D3DPresenter(_preview.Handle, _gpuRender.Checked, _gpuIndex);
                Native.SetWindowDisplayAffinity(_preview.Handle, Native.WdaExcludeFromCapture);
                if (_d3d.Hwnd != IntPtr.Zero)
                    Native.SetWindowDisplayAffinity(_d3d.Hwnd, Native.WdaExcludeFromCapture);
                _d3d.Resize(_hostW, _hostH, _destW, _destH);
                _d3d.SetUpscale(_upscale);
                PushFxUnlocked();
                NoteAppliedSize();
                AttachHud();
                AttachOverlay();
            }
            // Styles can be disturbed by child creation — re-apply while still suspended.
            ApplyFormClickThrough(wantPassClicks);
            ExcludeAppFromCapture();
            ResetInterp();
            lock (_frameLock)
            {
                _shownGen = -1;
                _havePrev = false;
                _shareHandle = IntPtr.Zero;
            }
            _clickThroughApplied = wantPassClicks;
            RaiseHud();
        }
        catch
        {
            // resume below
        }
    }

    private void OnContentClicked()
    {
        // Menu open → treat click-through as off; click on content (not menu) turns it on.
        if (_menuVisible)
        {
            EnableClickThroughFromContentClick();
            return;
        }
        FocusD3dHost();
    }

    private void EnableClickThroughFromContentClick()
    {
        _clickThroughOn = true;
        if (!_glass)
        {
            _syncWindowClick = true;
            _windowClickThrough.Checked = true;
            _syncWindowClick = false;
        }
        SetMenuVisible(false);
    }

    private bool ClickThroughActive() =>
        _clickThroughOn && !_menuVisible && (_glass || _windowClickThrough.Checked);

    protected override void WndProc(ref Message m)
    {
        // WM_NCHITTEST backup when styles are mid-transition.
        if (m.Msg == 0x0084 && (D3DPresenter.PassClicks || ClickThroughActive()))
        {
            m.Result = (IntPtr)(-1); // HTTRANSPARENT
            return;
        }
        base.WndProc(ref m);
    }

    private void ExcludeAppFromCapture()
    {
        if (_printHidden)
            return;
        Native.SetProcessDisplayAffinity(Native.WdaExcludeFromCapture);
        if (IsHandleCreated)
            Native.ExcludeFromCapture(Handle);
        if (_preview.IsHandleCreated)
            Native.ExcludeFromCapture(_preview.Handle);
        if (_d3d is { Hwnd: var d3d } && d3d != IntPtr.Zero)
            Native.ExcludeFromCapture(d3d);
        if (_fpsHud.IsHandleCreated)
            Native.ExcludeFromCapture(_fpsHud.Handle);
        if (_overlay.IsHandleCreated)
            Native.ExcludeFromCapture(_overlay.Handle);
    }

    private void PlacePresenterHost()
    {
        lock (_d3dLock)
            _d3d?.PlaceHost();
    }

    private void AttachHud()
    {
        if (!_fpsHud.IsHandleCreated)
            return;
        var host = _preview.Handle;
        if (host != IntPtr.Zero)
            Native.SetParent(_fpsHud.Handle, host);
    }

    private void DetachHud()
    {
        if (!_fpsHud.IsHandleCreated)
            return;
        Native.SetParent(_fpsHud.Handle, _preview.Handle);
    }

    private IntPtr OverlayHost() =>
        _preview.IsHandleCreated ? _preview.Handle : IntPtr.Zero;

    private void AttachOverlay()
    {
        var host = OverlayHost();
        if (host == IntPtr.Zero)
            return;
        _overlay.Attach(host);
        PlaceOverlay();
        UpdateInputHook();
    }

    private void DetachOverlay()
    {
        if (!_overlay.IsHandleCreated)
            return;
        Native.SetParent(_overlay.Handle, _preview.Handle);
    }

    private void PlaceOverlay()
    {
        _overlay.Place(Math.Max(8, _preview.ClientSize.Width), Math.Max(8, _preview.ClientSize.Height));
    }
    private void PushFx()
    {
        lock (_d3dLock)
            PushFxUnlocked();
    }

    private void PushFxUnlocked()
    {
        _d3d?.SetPostFx(_fx);
    }

    private void ReloadUserPipeline()
    {
        lock (_d3dLock)
        {
            try { _d3d?.ReloadPipeline(); }
            catch { /* keep last-good pixel shader */ }
            PushFxUnlocked();
        }
        _overlay.RefreshFolderShaders();
    }

    private void StartShaderWatch()
    {
        if (_shaderWatch != null)
            return;
        UserShaders.EnsureFolder();
        _shaderWatchTimer.Tick += (_, _) =>
        {
            _shaderWatchTimer.Stop();
            UserShaders.Scan();
            ReloadUserPipeline();
        };
        try
        {
            _shaderWatch = new FileSystemWatcher(UserShaders.Folder, "*.hlsl")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            void Bounce(object? sender, FileSystemEventArgs e)
            {
                if (!IsHandleCreated || IsDisposed)
                    return;
                BeginInvoke(() =>
                {
                    _shaderWatchTimer.Stop();
                    _shaderWatchTimer.Start();
                });
            }
            _shaderWatch.Changed += Bounce;
            _shaderWatch.Created += Bounce;
            _shaderWatch.Deleted += Bounce;
            _shaderWatch.Renamed += Bounce;
        }
        catch
        {
            // folder watch is optional
        }
    }

    private void SyncOverlay()
    {
        if (_fromOverlay)
            return;
        _overlay.Sync(_upscale, _fgMul, _stab, _fx);
    }

    private void FromOverlay(Action action)
    {
        _fromOverlay = true;
        try { action(); }
        finally { _fromOverlay = false; }
    }

    private void ToggleOverlay()
    {
        var now = Stopwatch.GetTimestamp();
        if (now - _homeStamp < Stopwatch.Frequency / 8)
            return;
        _homeStamp = now;
        if (_overlay.Visible)
            CloseOverlay();
        else
            OpenOverlay();
    }

    private void OpenOverlay()
    {
        _clickThroughOn = false;
        ApplyClickThrough();
        StealFocus();
        AttachOverlay();
        _overlay.Visible = true;
        _overlay.BringToFront();
        UpdateInputHook();
    }

    private void CloseOverlay()
    {
        if (_overlay.Visible)
            _overlay.Visible = false;
        EnableClickThrough();
        UpdateInputHook();
    }

    private void SaveShot()
    {
        Bitmap? copy = null;
        lock (_frameLock)
        {
            var frame = _slots[_display];
            if (frame.Bits != IntPtr.Zero)
                copy = frame.ToBitmap();
        }
        if (copy is null)
        {
            lock (_d3dLock)
            {
                if (_d3d is D3DPresenter presenter)
                    copy = presenter.Snapshot();
            }
        }
        if (copy is null)
            return;
        var folder = Path.Combine(AppContext.BaseDirectory, "captures");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"capture_{DateTime.Now:yyyyMMdd_HHmmss}.png");
        copy.Save(path);
        copy.Dispose();
    }

    private void FocusD3dHost()
    {
        try
        {
            if (_d3d is not { Ok: true } || _d3d.Hwnd == IntPtr.Zero || !Native.IsWindow(_d3d.Hwnd))
                return;
            if (IsHandleCreated)
                Native.SetForegroundWindow(Handle);
            _d3d.FocusHost();
        }
        catch
        {
            // focus during fullscreen transitions is best-effort
        }
    }

    private void StartHomeHook()
    {
        if (_homeHook != null)
            return;
        _homeHook = new HomeHook
        {
            IsEmptyOverlayClick = IsEmptyOverlayClick,
        };
        _homeHook.HomePassed += () => Ui(() => ToggleOverlay());
        _homeHook.OverlayKey += vk => Ui(() => HandleOverlayKey(vk));
        _homeHook.EmptyClick += () => Ui(() => CloseOverlay());
        _homeHook.FreezeCapture = FreezeForScreenshot;
        _homeHook.AllowInScreenshot += () => Ui(() => ArmPrintShow(10000));
        _homeHook.Start();
        UpdateInputHook();
    }

    private void Ui(Action action)
    {
        try
        {
            if (IsHandleCreated && !IsDisposed)
                BeginInvoke(action);
        }
        catch
        {
            // closing
        }
    }

    private void UpdateInputHook()
    {
        if (_homeHook is null)
            return;
        _uiForm = IsHandleCreated ? Handle : IntPtr.Zero;
        _uiMenu = _menu.IsHandleCreated ? _menu.Handle : IntPtr.Zero;
        _uiHud = _fpsHud.IsHandleCreated ? _fpsHud.Handle : IntPtr.Zero;
        _uiD3d = _d3d is { Ok: true } ? _d3d.Hwnd : IntPtr.Zero;
        _uiOverlay = _overlay.IsHandleCreated ? _overlay.Handle : IntPtr.Zero;
        _homeHook.FormHwnd = _uiForm;
        _homeHook.PreviewHwnd = _preview.IsHandleCreated ? _preview.Handle : IntPtr.Zero;
        _homeHook.D3dHwnd = _uiD3d;
        _homeHook.EatHome = _clickThroughOn;
        _homeHook.WatchEmptyClicks = !_clickThroughOn;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Oem8 || keyData == (Keys.Shift | Keys.Oem8))
        {
            ToggleOverlay();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void FreezeForScreenshot()
    {
        _shotFreeze = true;
        lock (_capLock) { }
    }

    private void ArmPrintShow(int ms)
    {
        _printHidden = true;
        _printShow.Stop();
        _printShow.Interval = Math.Max(100, ms);
        _printShow.Start();
    }

    private void ShowAfterPrint()
    {
        _printShow.Stop();
        if (!_printHidden || !IsHandleCreated || IsDisposed)
            return;
        _printHidden = false;
        Native.SetProcessDisplayAffinity(Native.WdaExcludeFromCapture);
        lock (_capLock)
        {
            try { _gpuCap?.DiscardQueued(); } catch { }
        }
        _shotFreeze = false;
    }

    private void DisableClickThrough()
    {
        var now = Stopwatch.GetTimestamp();
        if (now - _homeStamp < Stopwatch.Frequency / 5)
            return;
        _homeStamp = now;
        if (!_clickThroughOn)
            return;
        _clickThroughOn = false;
        ApplyClickThrough();
        StealFocus();
    }

    private void EnableClickThrough()
    {
        _clickThroughOn = true;
        if (_glass || _windowClickThrough.Checked)
            SetMenuVisible(false);
        else
            ApplyClickThrough();
    }

    private bool IsEmptyOverlayClick(int x, int y)
    {
        var hwnd = Native.WindowFromPoint(new Native.Point { X = x, Y = y });
        if (hwnd == IntPtr.Zero || !IsOurWindow(hwnd) || IsChromeWindow(hwnd))
            return false;
        return !_overlay.Visible || !_overlay.HitTestScreen(x, y);
    }

    private bool IsOurWindow(IntPtr hwnd)
    {
        var form = _uiForm;
        if (form == IntPtr.Zero)
            return false;
        if (hwnd == form || Native.IsChild(form, hwnd))
            return true;
        return _uiD3d != IntPtr.Zero && hwnd == _uiD3d;
    }

    private bool IsChromeWindow(IntPtr hwnd)
    {
        if (hwnd == _uiMenu || hwnd == _uiHud || hwnd == _uiOverlay)
            return true;
        if (_uiMenu != IntPtr.Zero && Native.IsChild(_uiMenu, hwnd))
            return true;
        if (_uiOverlay != IntPtr.Zero && Native.IsChild(_uiOverlay, hwnd))
            return true;
        var name = new System.Text.StringBuilder(64);
        Native.GetClassName(hwnd, name, name.Capacity);
        var cls = name.ToString();
        return cls.Contains("DropDown", StringComparison.OrdinalIgnoreCase)
            || cls.Contains("tooltips", StringComparison.OrdinalIgnoreCase)
            || cls.Contains("Menu", StringComparison.OrdinalIgnoreCase);
    }

    private void StealFocus()
    {
        var hwnd = Handle;
        Native.ShowWindow(hwnd, Native.SwRestore);
        Native.ShowWindow(hwnd, Native.SwShow);
        var fg = Native.GetForegroundWindow();
        var fgTid = Native.GetWindowThreadProcessId(fg, out _);
        var ourTid = Native.GetCurrentThreadId();
        var attached = fg != IntPtr.Zero && fg != hwnd && fgTid != 0 && fgTid != ourTid
            && Native.AttachThreadInput(fgTid, ourTid, true);
        Native.BringWindowToTop(hwnd);
        Native.SetWindowPos(hwnd, Native.HwndTopMost, 0, 0, 0, 0, Native.SwpNoMove | Native.SwpNoSize);
        Native.SetForegroundWindow(hwnd);
        if (attached)
            Native.AttachThreadInput(fgTid, ourTid, false);
        if (!_topMost.Checked && !_glass)
            Native.SetWindowPos(hwnd, Native.HwndTop, 0, 0, 0, 0, Native.SwpNoMove | Native.SwpNoSize);
        FocusD3dHost();
    }

    private void ResetToDefaults()
    {
        if (MessageBox.Show(
                this,
                "Reset all settings to defaults?\n\nWindow size and position are kept.",
                "GrXviewer",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;

        ApplySettings(new AppSettings(), restoreWindow: false, forceGpuUi: true);
        _forceCpuReadback = false;
        _stickyCapAdapter = -1;
        if (_paused)
        {
            _paused = false;
            _pauseItem.Text = "Pause";
        }
        if (!string.IsNullOrEmpty(_filter.Text))
            _filter.Text = "";
        else
            RefreshWindows();

        try { AppSettings.Save(SnapshotSettings()); }
        catch { /* ignore */ }
    }

    private void ApplySavedSettings()
    {
        var s = AppSettings.Load();
        if (s is null)
            return;
        ApplySettings(s, restoreWindow: true, forceGpuUi: false);
    }

    private void ApplySettings(AppSettings s, bool restoreWindow, bool forceGpuUi)
    {
        _autoGpu = s.AutoGpu;
        _autoFps = s.AutoFps;
        SelectMonitor(Math.Clamp(s.MonitorIndex, 0, Math.Max(0, _displayMenu.DropDownItems.Count - 1)));

        if (!s.AutoGpu || forceGpuUi)
        {
            if (!s.AutoGpu)
            {
                _gpuIndex = FindGpuIndex(s.GpuName, s.GpuIndex);
                _captureGpuIndex = FindGpuIndex(s.CaptureGpuName, s.CaptureGpuIndex);
                MarkGpu(_gpuMenu, _gpuIndex);
                MarkGpu(_captureGpuMenu, _captureGpuIndex);
            }
            if (s.CaptureOnGpu)
                _captureGpu.Checked = true;
            else
                _captureCpu.Checked = true;
            if (_gpuRender.Checked != s.GpuRender)
                _gpuRender.Checked = s.GpuRender;
            else
                _gpuMenu.Enabled = _gpuRender.Checked;
        }

        if (s.AutoGpu)
            ApplyHardwareProfile(includeFps: s.AutoFps);

        if (!s.AutoFps)
        {
            var fps = Math.Clamp(s.Fps, _fpsBar.Minimum, _fpsBar.Maximum);
            _applyingHw = true;
            try
            {
                if (_fpsBar.Value != fps)
                    _fpsBar.Value = fps;
                else
                    _fps = fps;
            }
            finally
            {
                _applyingHw = false;
            }
            UpdateFpsLabel();
        }

        var scale = Math.Clamp(s.Scale, _resBar.Minimum, _resBar.Maximum);
        _applyingHw = true;
        try
        {
            if (_resBar.Value != scale)
                _resBar.Value = scale;
            else
            {
                _scale = scale;
                SyncDestSize();
            }
        }
        finally
        {
            _applyingHw = false;
        }
        if (_scale != scale)
        {
            _scale = scale;
            SyncDestSize();
        }

        SetFrameGen(Math.Clamp(s.FrameGen, 1, 4));
        _fx = new PostFxState
        {
            Sharpen = s.FxSharpen,
            Color = s.FxColor,
            Vignette = s.FxVignette,
            Fog = s.FxFog,
            Dof = s.FxDof,
            MotionBlur = s.FxMotionBlur,
            BufferView = (uint)Math.Clamp(s.BufferView, 0, 5),
            FsrSharpness = Math.Clamp(s.FsrSharpness, 0, 1),
            CasAmount = Math.Clamp(s.CasAmount, 0, 1),
            Brightness = Math.Clamp(s.Brightness, -0.5f, 0.5f),
            Contrast = Math.Clamp(s.Contrast <= 0 ? 1f : s.Contrast, 0.5f, 1.5f),
            Saturation = Math.Clamp(s.Saturation, 0, 2),
            VignetteAmount = Math.Clamp(s.VignetteAmount, 0, 1),
            VignetteRadius = Math.Clamp(s.VignetteRadius <= 0 ? 0.75f : s.VignetteRadius, 0.2f, 1f),
            DepthMix = Math.Clamp(s.DepthMix, 0, 1),
            FogAmount = Math.Clamp(s.FogAmount, 0, 1),
            FogStart = Math.Clamp(s.FogStart, 0, 1),
            DofAmount = Math.Clamp(s.DofAmount, 0, 1),
            DofFocus = Math.Clamp(s.DofFocus, 0, 1),
            MotionBlurAmount = Math.Clamp(s.MotionBlurAmount, 0, 1),
        };
        PushFx();
        UserShaders.ApplySaved(s.EnabledShaders);
        UserShaders.Scan();
        ReloadUserPipeline();
        SetUpscale(s.Upscale is >= 0 and <= 2 ? (UpscaleMode)s.Upscale : UpscaleMode.Bilinear);
        SetStabilizer(s.Stabilizer);
        _foveatedMode = s.Foveated && !s.Checkerboard;
        _checkerboardMode = s.Checkerboard;
        _foveated.Checked = _foveatedMode;
        _checkerboard.Checked = _checkerboardMode;
        SyncDestSize();

        if (s.FullScreenCapture)
        {
            _fullItem.Checked = true;
            _progItem.Checked = false;
            _fullMode = true;
        }
        else
        {
            _progItem.Checked = true;
            _fullItem.Checked = false;
            _fullMode = false;
        }

        _topMost.Checked = s.TopMost;
        TopMost = _topMost.Checked || _glass;
        _windowClickThroughSaved = s.WindowClickThrough;
        _syncWindowClick = true;
        _windowClickThrough.Checked = _glass ? _windowClickThrough.Checked : s.WindowClickThrough;
        _syncWindowClick = false;
        _showFps.Checked = s.ShowFps;
        ApplyClickThrough();
        SyncOverlay();
        UpdateHud();
        if (restoreWindow)
            RestoreWindow(s);
    }

    private AppSettings SnapshotSettings()
    {
        var bounds = _glass ? _savedBounds : WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        var present = GpuAdapters.Find(_gpus, _gpuIndex);
        var capture = GpuAdapters.Find(_gpus, _captureGpuIndex);
        return new AppSettings
        {
            Fps = _fps,
            Scale = _scale,
            FrameGen = _fgMul,
            Upscale = (int)_upscale,
            Stabilizer = _stab,
            Foveated = _foveatedMode,
            Checkerboard = _checkerboardMode,
            FullScreenCapture = _fullMode,
            MonitorIndex = _monitorIndex,
            CaptureOnGpu = _captureGpu.Checked,
            GpuRender = _gpuRender.Checked,
            AutoGpu = _autoGpu,
            AutoFps = _autoFps,
            GpuIndex = _gpuIndex,
            CaptureGpuIndex = _captureGpuIndex,
            GpuName = present?.Name ?? "",
            CaptureGpuName = capture?.Name ?? "",
            TopMost = _topMost.Checked,
            WindowClickThrough = _glass ? _windowClickThroughSaved : _windowClickThrough.Checked,
            ShowFps = _showFps.Checked,
            WindowX = bounds.X,
            WindowY = bounds.Y,
            WindowW = bounds.Width,
            WindowH = bounds.Height,
            WindowState = _glass ? (int)FormWindowState.Normal : (int)WindowState,
            FxSharpen = _fx.Sharpen,
            FxColor = _fx.Color,
            FxVignette = _fx.Vignette,
            FsrSharpness = _fx.FsrSharpness,
            CasAmount = _fx.CasAmount,
            Brightness = _fx.Brightness,
            Contrast = _fx.Contrast,
            Saturation = _fx.Saturation,
            VignetteAmount = _fx.VignetteAmount,
            VignetteRadius = _fx.VignetteRadius,
            FxFog = _fx.Fog,
            FxDof = _fx.Dof,
            FxMotionBlur = _fx.MotionBlur,
            BufferView = (int)_fx.BufferView,
            DepthMix = _fx.DepthMix,
            FogAmount = _fx.FogAmount,
            FogStart = _fx.FogStart,
            DofAmount = _fx.DofAmount,
            DofFocus = _fx.DofFocus,
            MotionBlurAmount = _fx.MotionBlurAmount,
            EnabledShaders = UserShaders.SavedNames(),
        };
    }

    private int FindGpuIndex(string name, int fallback)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            foreach (var gpu in _gpus)
            {
                if (!gpu.Software && gpu.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return gpu.Index;
            }
            foreach (var gpu in _gpus)
            {
                if (!gpu.Software && gpu.Name.Contains(name, StringComparison.OrdinalIgnoreCase))
                    return gpu.Index;
            }
        }
        foreach (var gpu in _gpus)
        {
            if (!gpu.Software && gpu.Index == fallback)
                return gpu.Index;
        }
        return GpuAdapters.DefaultHardwareIndex(_gpus);
    }

    private void RestoreWindow(AppSettings s)
    {
        if (s.WindowX == int.MinValue || s.WindowW < MinimumSize.Width || s.WindowH < MinimumSize.Height)
            return;
        var bounds = new Rectangle(s.WindowX, s.WindowY, s.WindowW, s.WindowH);
        var visible = false;
        foreach (var screen in Screen.AllScreens)
        {
            if (screen.WorkingArea.IntersectsWith(bounds))
            {
                visible = true;
                break;
            }
        }
        if (!visible)
            return;
        StartPosition = FormStartPosition.Manual;
        Bounds = bounds;
        if (s.WindowState == (int)FormWindowState.Maximized)
            WindowState = FormWindowState.Maximized;
    }

    private void HandleOverlayKey(int vk)
    {
        if (vk == Native.VkF8)
        {
            if (_glass)
            {
                ExitGlass();
                StealFocus();
            }
            else
            {
                var show = !_menuVisible;
                SetMenuVisible(show);
                if (show)
                    StealFocus();
            }
        }
        else if (vk == Native.VkF11)
        {
            ToggleGlass();
            StealFocus();
        }
        else if (vk == Native.VkEscape)
        {
            if (_overlay.Visible) CloseOverlay();
            else if (_glass) ExitGlass();
            else if (!_menuVisible) SetMenuVisible(true);
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Oem8)
        {
            ToggleOverlay();
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.F8)
        {
            HandleOverlayKey(Native.VkF8);
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.F11)
        {
            HandleOverlayKey(Native.VkF11);
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Escape)
        {
            HandleOverlayKey(Native.VkEscape);
            e.Handled = true;
        }
        else if (e.Control && e.KeyCode == Keys.S)
        {
            SaveShot();
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.F5)
        {
            RefreshWindows();
            e.Handled = true;
        }
    }
}

internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    public DarkMenuRenderer() : base(new DarkMenuColors())
    {
        RoundedEdges = false;
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = Color.White;
        base.OnRenderItemText(e);
    }
}

internal sealed class DarkMenuColors : ProfessionalColorTable
{
    private static readonly Color Bar = Color.FromArgb(36, 36, 42);
    private static readonly Color Drop = Color.FromArgb(28, 28, 34);
    private static readonly Color Hot = Color.FromArgb(62, 62, 74);

    public override Color MenuStripGradientBegin => Bar;
    public override Color MenuStripGradientEnd => Bar;
    public override Color MenuBorder => Color.FromArgb(18, 18, 22);
    public override Color MenuItemSelected => Hot;
    public override Color MenuItemSelectedGradientBegin => Hot;
    public override Color MenuItemSelectedGradientEnd => Hot;
    public override Color MenuItemBorder => Hot;
    public override Color MenuItemPressedGradientBegin => Drop;
    public override Color MenuItemPressedGradientEnd => Drop;
    public override Color ImageMarginGradientBegin => Drop;
    public override Color ImageMarginGradientMiddle => Drop;
    public override Color ImageMarginGradientEnd => Drop;
    public override Color ToolStripDropDownBackground => Drop;
    public override Color SeparatorDark => Color.FromArgb(52, 52, 60);
    public override Color SeparatorLight => Color.FromArgb(52, 52, 60);
}
