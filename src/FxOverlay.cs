namespace CpuScreenViewer;

internal sealed class FxOverlay : Panel
{
    private static readonly Color Bg = Color.FromArgb(232, 18, 18, 20);
    private static readonly Color PanelBg = Color.FromArgb(22, 22, 24);
    private static readonly Color Accent = Color.FromArgb(90, 178, 214);
    private static readonly Color RowHot = Color.FromArgb(38, 52, 64);
    private static readonly Color Ink = Color.FromArgb(230, 230, 232);
    private static readonly Color Dim = Color.FromArgb(150, 150, 156);

    private readonly Panel _list = new() { BackColor = PanelBg };
    private readonly Panel _params = new() { BackColor = PanelBg };
    private readonly Label _paramTitle = new();
    private readonly FlowLayoutPanel _paramBody = new();
    private readonly CheckBox _fsr = TechBox("FSR 3");
    private readonly CheckBox _lanczos = TechBox("Lanczos 3");
    private readonly CheckBox _sharpen = TechBox("Sharpen");
    private readonly CheckBox _color = TechBox("Color");
    private readonly CheckBox _vignette = TechBox("Vignette");
    private readonly CheckBox _fog = TechBox("Fog");
    private readonly CheckBox _dof = TechBox("Depth of field");
    private readonly CheckBox _mblur = TechBox("Motion blur");
    private readonly CheckBox _stab = TechBox("Motion stabilizer");
    private readonly CheckBox _fg = TechBox("Frame generation");
    private readonly CheckBox _buffers = TechBox("Buffers");
    private readonly CheckBox[] _builtins;
    private readonly List<CheckBox> _shaderBoxes = [];
    private int _selected;
    private bool _syncing;
    private PostFxState _fx = PostFxState.Default;
    private int _stabLevel = 1;
    private int _fgMul = 1;

    public event Action<UpscaleMode>? UpscaleChanged;
    public event Action<int>? FrameGenChanged;
    public event Action<int>? StabilizerChanged;
    public event Action<PostFxState>? PostFxChanged;
    public event Action? ShadersChanged;

    public FxOverlay()
    {
        _builtins = [_fsr, _lanczos, _sharpen, _color, _vignette, _fog, _dof, _mblur, _stab, _fg, _buffers];
        Visible = false;
        Width = 460;
        Height = 540;
        BackColor = Bg;
        ForeColor = Ink;
        Font = new Font("Segoe UI", 9f);
        Padding = new Padding(1);
        BorderStyle = BorderStyle.None;

        var header = new Label
        {
            Dock = DockStyle.Top,
            Height = 36,
            Text = "   ¬ overlay",
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Accent,
            BackColor = Color.FromArgb(16, 16, 18),
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
        };
        var hint = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 28,
            Text = "  ¬ toggle  ·  Esc close  ·  click outside to pass through",
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Dim,
            BackColor = Color.FromArgb(16, 16, 18),
        };

        _list.Dock = DockStyle.Left;
        _list.Width = 200;
        _list.Padding = new Padding(8, 8, 4, 8);
        _list.AutoScroll = true;
        _params.Dock = DockStyle.Fill;
        _params.Padding = new Padding(8);
        _paramTitle.Dock = DockStyle.Top;
        _paramTitle.Height = 28;
        _paramTitle.ForeColor = Accent;
        _paramTitle.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        _paramBody.Dock = DockStyle.Fill;
        _paramBody.FlowDirection = FlowDirection.TopDown;
        _paramBody.WrapContents = false;
        _paramBody.AutoScroll = true;
        _params.Controls.Add(_paramBody);
        _params.Controls.Add(_paramTitle);

        var y = 4;
        for (var i = 0; i < _builtins.Length; i++)
        {
            var index = i;
            var box = _builtins[i];
            box.Location = new Point(8, y);
            box.Width = 180;
            box.Height = 26;
            box.CheckedChanged += (_, _) => OnTechChecked(index);
            box.Click += (_, _) => SelectTech(index);
            _list.Controls.Add(box);
            y += 28;
        }

        Controls.Add(_params);
        Controls.Add(_list);
        Controls.Add(hint);
        Controls.Add(header);

        RebuildShaderRows();
        SelectTech(0);
    }

    public PostFxState PostFx => _fx;

    public void Sync(UpscaleMode upscale, int frameGen, int stabilizer, in PostFxState fx)
    {
        _syncing = true;
        _fx = fx;
        _fgMul = Math.Clamp(frameGen, 1, 4);
        _stabLevel = Math.Clamp(stabilizer, 0, 4);
        _fsr.Checked = upscale == UpscaleMode.Fsr3;
        _lanczos.Checked = upscale == UpscaleMode.Lanczos;
        _sharpen.Checked = fx.Sharpen;
        _color.Checked = fx.Color;
        _vignette.Checked = fx.Vignette;
        _fog.Checked = fx.Fog;
        _dof.Checked = fx.Dof;
        _mblur.Checked = fx.MotionBlur;
        _stab.Checked = _stabLevel > 0;
        _fg.Checked = _fgMul > 1;
        _buffers.Checked = fx.BufferView != 0;
        _syncing = false;
        RefreshFolderShaders();
    }

    public void RefreshFolderShaders()
    {
        var keep = SelectedShaderName();
        if (ShaderRowsMatch())
            SyncShaderBoxes();
        else
            RebuildShaderRows();
        if (keep != null)
        {
            var index = ShaderIndex(keep);
            if (index >= 0)
                _selected = _builtins.Length + index;
        }
        if (_selected >= TechCount)
            _selected = 0;
        HighlightSelected();
        RebuildParams();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0084 && D3DPresenter.PassClicks)
        {
            m.Result = (IntPtr)(-1);
            return;
        }
        base.WndProc(ref m);
    }

    public void Attach(IntPtr host)
    {
        if (host == IntPtr.Zero)
            return;
        if (!IsHandleCreated)
            CreateControl();
        Native.SetParent(Handle, host);
        Native.SetWindowDisplayAffinity(Handle, Native.WdaExcludeFromCapture);
        Native.SetWindowPos(Handle, Native.HwndTop, 0, 0, 0, 0, Native.SwpNoMove | Native.SwpNoSize);
    }

    public void Place(int hostW, int hostH)
    {
        var w = Math.Clamp(hostW * 42 / 100, 380, 520);
        var h = Math.Clamp(hostH - 24, 320, 560);
        Bounds = new Rectangle(12, 12, w, h);
    }

    public bool HitTestScreen(int x, int y)
    {
        if (!Visible || !IsHandleCreated)
            return false;
        var client = PointToClient(new Point(x, y));
        return ClientRectangle.Contains(client);
    }

    private void SelectTech(int index)
    {
        if (index < 0 || index >= TechCount)
            return;
        _selected = index;
        HighlightSelected();
        RebuildParams();
    }

    private void HighlightSelected()
    {
        for (var i = 0; i < TechCount; i++)
            TechAt(i).BackColor = i == _selected ? RowHot : PanelBg;
    }

    private void OnTechChecked(int index)
    {
        if (_syncing)
            return;
        switch (index)
        {
            case 0:
                _syncing = true;
                if (_fsr.Checked)
                    _lanczos.Checked = false;
                _syncing = false;
                UpscaleChanged?.Invoke(_fsr.Checked
                    ? UpscaleMode.Fsr3
                    : _lanczos.Checked ? UpscaleMode.Lanczos : UpscaleMode.Bilinear);
                break;
            case 1:
                _syncing = true;
                if (_lanczos.Checked)
                    _fsr.Checked = false;
                _syncing = false;
                UpscaleChanged?.Invoke(_lanczos.Checked
                    ? UpscaleMode.Lanczos
                    : _fsr.Checked ? UpscaleMode.Fsr3 : UpscaleMode.Bilinear);
                break;
            case 2:
                _fx.Sharpen = _sharpen.Checked;
                PostFxChanged?.Invoke(_fx);
                break;
            case 3:
                _fx.Color = _color.Checked;
                PostFxChanged?.Invoke(_fx);
                break;
            case 4:
                _fx.Vignette = _vignette.Checked;
                PostFxChanged?.Invoke(_fx);
                break;
            case 5:
                _fx.Fog = _fog.Checked;
                PostFxChanged?.Invoke(_fx);
                break;
            case 6:
                _fx.Dof = _dof.Checked;
                PostFxChanged?.Invoke(_fx);
                break;
            case 7:
                _fx.MotionBlur = _mblur.Checked;
                PostFxChanged?.Invoke(_fx);
                break;
            case 8:
                if (_stab.Checked && _stabLevel == 0)
                    _stabLevel = 1;
                if (!_stab.Checked)
                    _stabLevel = 0;
                StabilizerChanged?.Invoke(_stabLevel);
                break;
            case 9:
                if (_fg.Checked && _fgMul < 2)
                    _fgMul = 2;
                if (!_fg.Checked)
                    _fgMul = 1;
                FrameGenChanged?.Invoke(_fgMul);
                break;
            case 10:
                _fx.BufferView = _buffers.Checked ? Math.Max(_fx.BufferView, 2u) : 0;
                PostFxChanged?.Invoke(_fx);
                break;
        }
        if (index == _selected)
            RebuildParams();
    }

    private void OnShaderChecked(int fileIndex)
    {
        if (_syncing || fileIndex < 0 || fileIndex >= UserShaders.Files.Count)
            return;
        var file = UserShaders.Files[fileIndex];
        var on = fileIndex < _shaderBoxes.Count && _shaderBoxes[fileIndex].Checked;
        UserShaders.SetEnabled(file.FileName, on);
        ShadersChanged?.Invoke();
        if (_selected == _builtins.Length + fileIndex)
            RebuildParams();
    }

    private void RebuildParams()
    {
        _paramBody.SuspendLayout();
        _paramBody.Controls.Clear();
        _paramTitle.Text = _selected < TechCount ? TechAt(_selected).Text : "";
        switch (_selected)
        {
            case 0:
                AddSlider("Sharpness", (int)(_fx.FsrSharpness * 100), 0, 100, v =>
                {
                    _fx.FsrSharpness = v / 100f;
                    PostFxChanged?.Invoke(_fx);
                });
                AddNote("EASU upscale + RCAS. Uncheck for bilinear.");
                break;
            case 1:
                AddNote("3-lobe Lanczos. Uncheck for bilinear.");
                break;
            case 2:
                AddSlider("Amount", (int)(_fx.CasAmount * 100), 0, 100, v =>
                {
                    _fx.CasAmount = v / 100f;
                    PostFxChanged?.Invoke(_fx);
                });
                break;
            case 3:
                AddSlider("Brightness", (int)(_fx.Brightness * 100), -50, 50, v =>
                {
                    _fx.Brightness = v / 100f;
                    PostFxChanged?.Invoke(_fx);
                });
                AddSlider("Contrast", (int)(_fx.Contrast * 100), 50, 150, v =>
                {
                    _fx.Contrast = v / 100f;
                    PostFxChanged?.Invoke(_fx);
                });
                AddSlider("Saturation", (int)(_fx.Saturation * 100), 0, 200, v =>
                {
                    _fx.Saturation = v / 100f;
                    PostFxChanged?.Invoke(_fx);
                });
                break;
            case 4:
                AddSlider("Amount", (int)(_fx.VignetteAmount * 100), 0, 100, v =>
                {
                    _fx.VignetteAmount = v / 100f;
                    PostFxChanged?.Invoke(_fx);
                });
                AddSlider("Radius", (int)(_fx.VignetteRadius * 100), 20, 100, v =>
                {
                    _fx.VignetteRadius = v / 100f;
                    PostFxChanged?.Invoke(_fx);
                });
                break;
            case 5:
                AddSlider("Amount", (int)(_fx.FogAmount * 100), 0, 100, v =>
                {
                    _fx.FogAmount = v / 100f;
                    PostFxChanged?.Invoke(_fx);
                });
                AddSlider("Start", (int)(_fx.FogStart * 100), 0, 90, v =>
                {
                    _fx.FogStart = v / 100f;
                    PostFxChanged?.Invoke(_fx);
                });
                AddNote("Uses reconstructed depth. Far pixels fade to haze.");
                break;
            case 6:
                AddSlider("Amount", (int)(_fx.DofAmount * 100), 0, 100, v =>
                {
                    _fx.DofAmount = v / 100f;
                    PostFxChanged?.Invoke(_fx);
                });
                AddSlider("Focus", (int)(_fx.DofFocus * 100), 0, 100, v =>
                {
                    _fx.DofFocus = v / 100f;
                    PostFxChanged?.Invoke(_fx);
                });
                AddNote("0 focus = near, 100 = far. Uses reconstructed depth.");
                break;
            case 7:
                AddSlider("Amount", (int)(_fx.MotionBlurAmount * 100), 0, 100, v =>
                {
                    _fx.MotionBlurAmount = v / 100f;
                    PostFxChanged?.Invoke(_fx);
                });
                AddNote("Blurs along reconstructed motion vectors.");
                break;
            case 8:
                AddChoice("Strength", ["Off", "Very low", "Low", "Medium", "High"], _stabLevel, v =>
                {
                    _stabLevel = v;
                    _syncing = true;
                    _stab.Checked = v > 0;
                    _syncing = false;
                    StabilizerChanged?.Invoke(v);
                });
                break;
            case 9:
                var fgIndex = _fgMul <= 1 ? 0 : _fgMul - 1;
                AddChoice("Multiplier", ["Off", "2x", "3x", "4x"], fgIndex, v =>
                {
                    _fgMul = v == 0 ? 1 : v + 1;
                    _syncing = true;
                    _fg.Checked = _fgMul > 1;
                    _syncing = false;
                    FrameGenChanged?.Invoke(_fgMul);
                });
                break;
            case 10:
                AddChoice("View", ["Off", "Color", "Depth", "Motion", "History", "All"], (int)_fx.BufferView, v =>
                {
                    _fx.BufferView = (uint)v;
                    _syncing = true;
                    _buffers.Checked = v != 0;
                    _syncing = false;
                    PostFxChanged?.Invoke(_fx);
                });
                AddSlider("Depth mix", (int)(_fx.DepthMix * 100), 0, 100, v =>
                {
                    _fx.DepthMix = v / 100f;
                    PostFxChanged?.Invoke(_fx);
                });
                AddNote("No game depth. 0 = motion (moving is near), 100 = luma (bright is far). All = color / motion / depth / history.");
                break;
            default:
                RebuildShaderParams(_selected - _builtins.Length);
                break;
        }
        _paramBody.ResumeLayout();
    }

    private void RebuildShaderParams(int fileIndex)
    {
        if (fileIndex < 0 || fileIndex >= UserShaders.Files.Count)
            return;
        var file = UserShaders.Files[fileIndex];
        AddNote(file.FileName);
        if (file.Error != null)
            AddNote(file.Error);
        else if (!string.IsNullOrEmpty(UserShaders.CompileError) && file.Enabled)
            AddNote(UserShaders.CompileError);
    }

    private int TechCount => _builtins.Length + _shaderBoxes.Count;

    private CheckBox TechAt(int index)
        => index < _builtins.Length ? _builtins[index] : _shaderBoxes[index - _builtins.Length];

    private string? SelectedShaderName()
    {
        var i = _selected - _builtins.Length;
        if (i < 0 || i >= UserShaders.Files.Count)
            return null;
        return UserShaders.Files[i].FileName;
    }

    private static int ShaderIndex(string fileName)
    {
        for (var i = 0; i < UserShaders.Files.Count; i++)
        {
            if (UserShaders.Files[i].FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    private bool ShaderRowsMatch()
    {
        if (_shaderBoxes.Count != UserShaders.Files.Count)
            return false;
        for (var i = 0; i < _shaderBoxes.Count; i++)
        {
            if (_shaderBoxes[i].Tag is not string name ||
                !name.Equals(UserShaders.Files[i].FileName, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    private void SyncShaderBoxes()
    {
        _syncing = true;
        for (var i = 0; i < _shaderBoxes.Count; i++)
        {
            var file = UserShaders.Files[i];
            var box = _shaderBoxes[i];
            box.Text = file.DisplayName;
            box.Checked = file.Enabled;
            box.Enabled = file.Error is null;
        }
        _syncing = false;
    }

    private void RebuildShaderRows()
    {
        foreach (var box in _shaderBoxes)
        {
            _list.Controls.Remove(box);
            box.Dispose();
        }
        _shaderBoxes.Clear();

        var y = 4 + _builtins.Length * 28;
        _syncing = true;
        for (var i = 0; i < UserShaders.Files.Count; i++)
        {
            var file = UserShaders.Files[i];
            var fileIndex = i;
            var box = TechBox(file.DisplayName);
            box.Tag = file.FileName;
            box.Checked = file.Enabled;
            box.Enabled = file.Error is null;
            box.Location = new Point(8, y);
            box.Width = 180;
            box.Height = 26;
            box.CheckedChanged += (_, _) => OnShaderChecked(fileIndex);
            box.Click += (_, _) => SelectTech(_builtins.Length + fileIndex);
            _list.Controls.Add(box);
            _shaderBoxes.Add(box);
            y += 28;
        }
        _syncing = false;
        _list.AutoScrollMinSize = new Size(0, y + 8);
    }

    private void AddNote(string text)
    {
        _paramBody.Controls.Add(new Label
        {
            Text = text,
            AutoSize = true,
            MaximumSize = new Size(240, 0),
            ForeColor = Dim,
            Margin = new Padding(0, 4, 0, 8),
        });
    }

    private void AddSlider(string title, int value, int min, int max, Action<int> changed)
    {
        var label = new Label
        {
            AutoSize = true,
            ForeColor = Ink,
            Margin = new Padding(0, 6, 0, 0),
        };
        void SetLabel(int v) => label.Text = $"{title}  {v}";
        SetLabel(value);
        var bar = new TrackBar
        {
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(value, min, max),
            TickStyle = TickStyle.None,
            Width = 220,
            Height = 28,
            BackColor = PanelBg,
            Margin = new Padding(0, 0, 0, 4),
        };
        bar.ValueChanged += (_, _) =>
        {
            SetLabel(bar.Value);
            if (!_syncing)
                changed(bar.Value);
        };
        _paramBody.Controls.Add(label);
        _paramBody.Controls.Add(bar);
    }

    private void AddChoice(string title, string[] options, int selected, Action<int> changed)
    {
        _paramBody.Controls.Add(new Label
        {
            Text = title,
            AutoSize = true,
            ForeColor = Ink,
            Margin = new Padding(0, 6, 0, 4),
        });
        for (var i = 0; i < options.Length; i++)
        {
            var index = i;
            var radio = new RadioButton
            {
                Text = options[i],
                AutoSize = true,
                ForeColor = Ink,
                BackColor = PanelBg,
                Checked = i == selected,
                Margin = new Padding(0, 0, 0, 2),
            };
            radio.CheckedChanged += (_, _) =>
            {
                if (radio.Checked && !_syncing)
                    changed(index);
            };
            _paramBody.Controls.Add(radio);
        }
    }

    private static CheckBox TechBox(string text)
    {
        var box = new CheckBox
        {
            Text = text,
            ForeColor = Ink,
            BackColor = PanelBg,
            AutoSize = false,
            FlatStyle = FlatStyle.Flat,
        };
        box.FlatAppearance.BorderColor = Dim;
        box.FlatAppearance.CheckedBackColor = Color.FromArgb(50, 90, 120);
        box.FlatAppearance.MouseOverBackColor = RowHot;
        return box;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Accent, 1);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }
}
