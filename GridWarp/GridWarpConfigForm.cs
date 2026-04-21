#pragma warning disable CS0612, CS0618 // EffectConfigForm2 base class triggers obsolescence warnings

using PaintDotNet;
using PaintDotNet.Effects;
using PaintDotNet.Imaging;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GridWarpPlugin;

/// <summary>
/// Interactive grid warp dialog with draggable control points, zoom/pan, and undo/redo.
/// Integrates with Paint.NET's effect host via <see cref="EffectConfigForm2"/>.
/// </summary>
internal sealed class GridWarpConfigForm : EffectConfigForm2
{
    // ── UI Controls ──────────────────────────────────────────
    private TrackBar gridWidthSlider = null!;
    private Label gridWidthValue = null!;
    private TrackBar gridHeightSlider = null!;
    private Label gridHeightValue = null!;
    private TrackBar qualitySlider = null!;
    private Label qualityValue = null!;
    private Button toggleGridBtn = null!;
    private FlowLayoutPanel gridColorSwatches = null!;
    private Button undoBtn = null!, redoBtn = null!, resetBtn = null!;
    private Panel canvas = null!;

    // ── Canvas / interaction state ───────────────────────────
    private float zoom = 1f;
    private PointF pan;
    private bool isPanning;
    private Point lastPanPt;

    // ── Grid state ───────────────────────────────────────────
    private int currentGridWidth = 4;
    private int currentGridHeight = 4;
    private bool isGridVisible = true;
    private int selectedGridColorIndex;
    private float[] displacements = Array.Empty<float>();
    private int dragPointIndex = -1;

    // ── Undo / redo ──────────────────────────────────────────
    private readonly List<float[]> undoStack = new();
    private readonly List<float[]> redoStack = new();

    // ── CPU preview surfaces ─────────────────────────────────
    private Surface? origSurface;
    private Surface? workSurface;

    // ── Constants ────────────────────────────────────────────
    private const float MinZoom = 0.1f;
    private const float MaxZoom = 8f;
    private const float ZoomStep = 1.2f;
    private const int MinGrid = 2;
    private const int MaxGridWidth = 50;
    private const int MaxGridHeight = 50;
    private const float PointHitRadius = 12f; // screen pixels

    // ── Theme colors ─────────────────────────────────────────
    private static readonly Color LabelColor = Color.FromArgb(220, 220, 220);
    private static readonly Color PanelBg = Color.FromArgb(45, 45, 48);
    private static readonly Color ControlBg = Color.FromArgb(60, 60, 65);
    private readonly List<Button> gridColorButtons = new();
    private readonly ToolTip gridColorToolTip = new();
    private readonly ToolTip uiToolTip = new();

    private sealed class GridColorPreset
    {
        public string Name { get; }
        public Color GridLineColor { get; }
        public Color PointColor { get; }
        public Color DragPointColor { get; }

        public GridColorPreset(string name, Color gridLineColor, Color pointColor, Color dragPointColor)
        {
            Name = name;
            GridLineColor = gridLineColor;
            PointColor = pointColor;
            DragPointColor = dragPointColor;
        }

        public override string ToString() => Name;
    }

    private static readonly GridColorPreset[] GridColorPresets =
    {
        new("White", Color.FromArgb(220, 255, 255, 255), Color.White, Color.Cyan),
        new("Yellow", Color.FromArgb(220, 255, 235, 59), Color.FromArgb(255, 253, 216), Color.FromArgb(255, 87, 34)),
        new("Blue", Color.FromArgb(220, 86, 180, 233), Color.FromArgb(209, 233, 255), Color.FromArgb(0, 114, 178)),
        new("Orange", Color.FromArgb(220, 230, 159, 0), Color.FromArgb(255, 240, 214), Color.FromArgb(213, 94, 0)),
        new("Neon Lime", Color.FromArgb(220, 0, 255, 120), Color.FromArgb(207, 255, 225), Color.FromArgb(255, 0, 128))
    };

    private bool initialFitDone;
    private bool suppressGridSliderEvents;
    private readonly float _dpi;
    private int S(int v) => (int)(v * _dpi);

    // ═════════════════════════════════════════════════════════
    //  Construction
    // ═════════════════════════════════════════════════════════

    public GridWarpConfigForm()
    {
        AutoScaleMode = AutoScaleMode.None;
        _dpi = Math.Max(1f, DeviceDpi / 96f);
        BuildUI();
        WireEvents();
    }

    // ═════════════════════════════════════════════════════════
    //  Token integration
    // ═════════════════════════════════════════════════════════

    protected override EffectConfigToken OnCreateInitialToken()
    {
        return new GridWarpConfigToken();
    }

    protected override void OnUpdateDialogFromToken(EffectConfigToken effectToken)
    {
        var token = (GridWarpConfigToken)effectToken;
        int tokenWidth = token.GridWidth > 0 ? token.GridWidth : token.GridSize;
        int tokenHeight = token.GridHeight > 0 ? token.GridHeight : token.GridSize;
        currentGridWidth = Math.Clamp(tokenWidth, MinGrid, MaxGridWidth);
        currentGridHeight = Math.Clamp(tokenHeight, MinGrid, MaxGridHeight);

        int expectedLen = (currentGridWidth + 1) * (currentGridHeight + 1) * 2;
        if (token.Displacements != null && token.Displacements.Length == expectedLen)
            displacements = (float[])token.Displacements.Clone();
        else
            InitDisplacements(currentGridWidth, currentGridHeight);

        undoStack.Clear();
        redoStack.Clear();

        // Guard: controls may not exist yet if called during base constructor
        if (gridWidthSlider != null)
            SyncUIFromToken(token);

        RegeneratePreview();
    }

    protected override void OnUpdateTokenFromDialog(EffectConfigToken dstToken)
    {
        var token = (GridWarpConfigToken)dstToken;
        token.GridWidth = currentGridWidth;
        token.GridHeight = currentGridHeight;
        token.Quality = qualitySlider.Value;
        token.Displacements = (float[])displacements.Clone();
    }

    protected override void OnLoaded()
    {
        base.OnLoaded();

        undoStack.Clear();
        redoStack.Clear();

        try
        {
            using var srcBmp = Environment.GetSourceBitmapBgra32();
            var size = srcBmp.Size;
            if (size.Width > 0 && size.Height > 0)
            {
                origSurface?.Dispose();
                workSurface?.Dispose();
                origSurface = new Surface(size.Width, size.Height);
                unsafe
                {
                    srcBmp.CopyPixels(origSurface.Scan0.VoidStar, origSurface.Stride,
                        (uint)origSurface.Scan0.Length, null);
                }
                workSurface = origSurface.Clone();
                InitDisplacements(currentGridWidth, currentGridHeight);
                RegeneratePreview();
                initialFitDone = false;
                BeginInvoke(new Action(FitToWindow));
            }
        }
        catch
        {
            // Source surface not available — canvas stays blank
        }
    }

    // ═════════════════════════════════════════════════════════
    //  UI Construction
    // ═════════════════════════════════════════════════════════

    private void BuildUI()
    {
        SuspendLayout();

        Text = "Grid Warp \u2013 Left-click: drag points, Right-click: pan, Wheel: zoom";
        ClientSize = new Size(S(1000), S(700));
        MinimumSize = new Size(S(600), S(400));
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        StartPosition = FormStartPosition.CenterParent;
        WindowState = FormWindowState.Maximized;

        int panelW = S(270);
        int ctrlW = panelW - S(30);

        // ── Left tool panel ──
        var leftPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Left,
            Width = panelW,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(S(8), S(6), S(4), S(6)),
            BackColor = PanelBg
        };

        (gridWidthSlider, gridWidthValue) = AddSlider(leftPanel, "Grid Width:", ctrlW, MinGrid, MaxGridWidth, 4, v => v.ToString());
        (gridHeightSlider, gridHeightValue) = AddSlider(leftPanel, "Grid Height:", ctrlW, MinGrid, MaxGridHeight, 4, v => v.ToString());
        leftPanel.Controls.Add(MakeHintLabel("Independent X/Y cells (e.g. 10x20), up to 50x50.", ctrlW));

        (qualitySlider, qualityValue) = AddSlider(leftPanel, "Quality:", ctrlW, 1, 7, 4, v => $"{v}x");
        leftPanel.Controls.Add(MakeHintLabel("RGSS multisampling level. Higher = smoother but slower.", ctrlW));

        toggleGridBtn = MakeIconButton("\u25A6", "Show or hide the grid overlay");
        toggleGridBtn.Margin = new Padding(0, S(6), 0, S(4));
        leftPanel.Controls.Add(toggleGridBtn);

        var gridColorLabel = new Label
        {
            Text = "Grid Color:",
            AutoSize = true,
            ForeColor = LabelColor,
            Margin = new Padding(0, S(4), 0, 0)
        };
        leftPanel.Controls.Add(gridColorLabel);

        gridColorSwatches = new FlowLayoutPanel
        {
            Width = ctrlW,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = new Padding(0, S(2), 0, S(2)),
            BackColor = Color.Transparent
        };
        for (int i = 0; i < GridColorPresets.Length; i++)
        {
            int index = i;
            GridColorPreset preset = GridColorPresets[index];
            var swatch = new Button
            {
                Size = new Size(S(28), S(28)),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(255, preset.GridLineColor.R, preset.GridLineColor.G, preset.GridLineColor.B),
                Margin = new Padding(0, 0, S(6), 0),
                Tag = index
            };
            swatch.FlatAppearance.BorderSize = 1;
            swatch.FlatAppearance.BorderColor = Color.FromArgb(30, 30, 30);
            swatch.Click += (_, _) => SelectGridColor(index);
            gridColorToolTip.SetToolTip(swatch, preset.Name);
            gridColorButtons.Add(swatch);
            gridColorSwatches.Controls.Add(swatch);
        }
        leftPanel.Controls.Add(gridColorSwatches);
        leftPanel.Controls.Add(MakeHintLabel("Hover color boxes for names.", ctrlW));
        SelectGridColor(0);

        var helpLabel = new Label
        {
            Text = "Left-click: drag control points\nRight-click: pan view\n" +
                   "Mouse wheel: zoom\nDouble-click: fit to window\n" +
                   "Ctrl+Z / Ctrl+Y: undo / redo",
            AutoSize = true,
            MaximumSize = new Size(ctrlW, 0),
            ForeColor = LabelColor,
            Margin = new Padding(0, S(8), 0, S(8))
        };
        leftPanel.Controls.Add(helpLabel);

        // Undo / Redo / Reset row
        var actionRow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = new Padding(0, S(2), 0, S(2)),
            BackColor = Color.Transparent
        };
        undoBtn = MakeIconButton("\u21B6", "Undo last grid edit");
        redoBtn = MakeIconButton("\u21B7", "Redo last undone edit");
        resetBtn = MakeIconButton("\u21BA", "Reset all control-point displacements");
        actionRow.Controls.AddRange(new Control[] { undoBtn, redoBtn, resetBtn });
        leftPanel.Controls.Add(actionRow);

        // OK / Cancel row
        var okCancelRow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = new Padding(0, S(2), 0, S(2)),
            BackColor = Color.Transparent
        };
        var okBtn = MakeButton("OK");
        okBtn.DialogResult = DialogResult.OK;
        okBtn.MinimumSize = new Size(S(90), okBtn.MinimumSize.Height);
        var cancelBtn = MakeButton("Cancel");
        cancelBtn.DialogResult = DialogResult.Cancel;
        cancelBtn.MinimumSize = new Size(S(90), cancelBtn.MinimumSize.Height);
        okCancelRow.Controls.AddRange(new Control[] { okBtn, cancelBtn });
        leftPanel.Controls.Add(okCancelRow);

        AcceptButton = okBtn;
        CancelButton = cancelBtn;

        // ── Canvas (double-buffered) ──
        canvas = new DoubleBufferedPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(48, 48, 48)
        };

        Controls.Add(canvas);
        Controls.Add(leftPanel);

        ResumeLayout(true);
    }

    // ═════════════════════════════════════════════════════════
    //  Event Wiring
    // ═════════════════════════════════════════════════════════

    private void WireEvents()
    {
        uiToolTip.SetToolTip(gridWidthSlider, "Number of horizontal grid cells");
        uiToolTip.SetToolTip(gridHeightSlider, "Number of vertical grid cells");
        uiToolTip.SetToolTip(qualitySlider, "RGSS sample quality for final render");
        uiToolTip.SetToolTip(canvas, "Left-click drag points, right-click pan, wheel zoom");

        gridWidthSlider.ValueChanged += (_, _) =>
        {
            if (suppressGridSliderEvents)
                return;

            ApplyGridSliderChange(preferWidth: true);
        };

        gridHeightSlider.ValueChanged += (_, _) =>
        {
            if (suppressGridSliderEvents)
                return;

            ApplyGridSliderChange(preferWidth: false);
        };

        qualitySlider.ValueChanged += (_, _) =>
        {
            qualityValue.Text = $"{qualitySlider.Value}x";
            PushUpdate();
        };

        toggleGridBtn.Click += (_, _) =>
        {
            isGridVisible = !isGridVisible;
            toggleGridBtn.Text = isGridVisible ? "\u25A6" : "\u25A1";
            uiToolTip.SetToolTip(toggleGridBtn, isGridVisible ? "Hide grid overlay" : "Show grid overlay");
            canvas?.Invalidate();
        };

        undoBtn.Click += (_, _) => Undo();
        redoBtn.Click += (_, _) => Redo();
        resetBtn.Click += (_, _) => ResetGrid();

        canvas.Paint += Canvas_Paint;
        canvas.MouseDown += Canvas_MouseDown;
        canvas.MouseMove += Canvas_MouseMove;
        canvas.MouseUp += Canvas_MouseUp;
        canvas.MouseWheel += Canvas_MouseWheel;
        canvas.DoubleClick += (_, _) => FitToWindow();
        canvas.Resize += (_, _) =>
        {
            if (!initialFitDone && origSurface != null && canvas.ClientSize.Width > 0)
            {
                initialFitDone = true;
                FitToWindow();
            }
        };

        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.Z) { Undo(); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.Y) { Redo(); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.D0)
            {
                zoom = 1f;
                pan = PointF.Empty;
                canvas.Invalidate();
                e.Handled = true;
            }
        };
    }

    // ═════════════════════════════════════════════════════════
    //  Canvas Rendering
    // ═════════════════════════════════════════════════════════

    private void Canvas_Paint(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(canvas.BackColor);

        if (workSurface == null) return;

        g.InterpolationMode = zoom > 2f
            ? InterpolationMode.NearestNeighbor
            : InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.Half;

        var imgRect = GetImageRect();
        using (var bmp = workSurface.CreateAliasedBitmap())
        {
            g.DrawImage(bmp, imgRect);
        }

        // Image border
        using (var pen = new Pen(Color.FromArgb(128, 255, 255, 255), 1) { DashStyle = DashStyle.Dash })
        {
            g.DrawRectangle(pen, imgRect.X, imgRect.Y, imgRect.Width, imgRect.Height);
        }

        // Grid overlay
        if (isGridVisible)
        {
            DrawGridOverlay(g);
        }
    }

    private void DrawGridOverlay(Graphics g)
    {
        if (origSurface == null) return;

        int ptsX = currentGridWidth + 1;
        int ptsY = currentGridHeight + 1;
        float cellW = (float)origSurface.Width / currentGridWidth;
        float cellH = (float)origSurface.Height / currentGridHeight;

        g.SmoothingMode = SmoothingMode.AntiAlias;

        GridColorPreset gridColorPreset = GetSelectedGridColorPreset();

        // Draw grid lines
        using var gridPen = new Pen(gridColorPreset.GridLineColor, 1f);

        // Horizontal lines
        for (int row = 0; row < ptsY; row++)
        {
            for (int col = 0; col < currentGridWidth; col++)
            {
                var p1 = GetDeformedScreenPos(row, col, ptsX, cellW, cellH);
                var p2 = GetDeformedScreenPos(row, col + 1, ptsX, cellW, cellH);
                g.DrawLine(gridPen, p1, p2);
            }
        }

        // Vertical lines
        for (int col = 0; col < ptsX; col++)
        {
            for (int row = 0; row < currentGridHeight; row++)
            {
                var p1 = GetDeformedScreenPos(row, col, ptsX, cellW, cellH);
                var p2 = GetDeformedScreenPos(row + 1, col, ptsX, cellW, cellH);
                g.DrawLine(gridPen, p1, p2);
            }
        }

        // Draw control points
        float ptRadius = 4f;
        using var fillBrush = new SolidBrush(gridColorPreset.PointColor);
        using var dragBrush = new SolidBrush(gridColorPreset.DragPointColor);
        using var outlinePen = new Pen(Color.FromArgb(60, 60, 60), 1.5f);

        for (int row = 0; row < ptsY; row++)
        {
            for (int col = 0; col < ptsX; col++)
            {
                var sp = GetDeformedScreenPos(row, col, ptsX, cellW, cellH);
                int idx = row * ptsX + col;
                bool isDragging = idx == dragPointIndex;
                float r = isDragging ? ptRadius + 1.5f : ptRadius;
                var brush = isDragging ? dragBrush : fillBrush;
                g.FillEllipse(brush, sp.X - r, sp.Y - r, r * 2, r * 2);
                g.DrawEllipse(outlinePen, sp.X - r, sp.Y - r, r * 2, r * 2);
            }
        }
    }

    private PointF GetDeformedScreenPos(int row, int col, int pointsPerRow, float cellW, float cellH)
    {
        int idx = row * pointsPerRow + col;
        float imgX = col * cellW + (idx * 2 < displacements.Length ? displacements[idx * 2] : 0);
        float imgY = row * cellH + (idx * 2 + 1 < displacements.Length ? displacements[idx * 2 + 1] : 0);
        return new PointF(imgX * zoom + pan.X, imgY * zoom + pan.Y);
    }

    // ═════════════════════════════════════════════════════════
    //  Mouse Interaction
    // ═════════════════════════════════════════════════════════

    private void Canvas_MouseDown(object? sender, MouseEventArgs e)
    {
        canvas.Focus();

        if (e.Button == MouseButtons.Left && origSurface != null)
        {
            int hitIdx = HitTestControlPoint(e.Location);
            if (hitIdx >= 0)
            {
                dragPointIndex = hitIdx;
                // Save undo snapshot before starting drag
                undoStack.Add((float[])displacements.Clone());
                redoStack.Clear();
                canvas.Invalidate();
            }
        }
        else if (e.Button == MouseButtons.Right)
        {
            isPanning = true;
            lastPanPt = e.Location;
            canvas.Cursor = Cursors.SizeAll;
        }
    }

    private void Canvas_MouseMove(object? sender, MouseEventArgs e)
    {
        if (dragPointIndex >= 0 && e.Button == MouseButtons.Left && origSurface != null)
        {
            var imgPt = ScreenToImage(e.Location);
            int ptsX = currentGridWidth + 1;
            int row = dragPointIndex / ptsX;
            int col = dragPointIndex % ptsX;
            float cellW = (float)origSurface.Width / currentGridWidth;
            float cellH = (float)origSurface.Height / currentGridHeight;
            float defaultX = col * cellW;
            float defaultY = row * cellH;

            displacements[dragPointIndex * 2] = imgPt.X - defaultX;
            displacements[dragPointIndex * 2 + 1] = imgPt.Y - defaultY;

            RegeneratePreview();
        }
        else if (isPanning && e.Button == MouseButtons.Right)
        {
            pan = new PointF(
                pan.X + e.X - lastPanPt.X,
                pan.Y + e.Y - lastPanPt.Y);
            lastPanPt = e.Location;
            canvas.Invalidate();
        }
    }

    private void Canvas_MouseUp(object? sender, MouseEventArgs e)
    {
        if (dragPointIndex >= 0 && e.Button == MouseButtons.Left)
        {
            dragPointIndex = -1;
            UpdateButtons();
            PushUpdate();
            canvas.Invalidate();
        }
        else if (isPanning && e.Button == MouseButtons.Right)
        {
            isPanning = false;
            canvas.Cursor = Cursors.Default;
        }
    }

    private void Canvas_MouseWheel(object? sender, MouseEventArgs e)
    {
        var before = ScreenToImage(e.Location);
        zoom = e.Delta > 0
            ? Math.Min(zoom * ZoomStep, MaxZoom)
            : Math.Max(zoom / ZoomStep, MinZoom);
        var after = ScreenToImage(e.Location);
        pan = new PointF(
            pan.X + (after.X - before.X) * zoom,
            pan.Y + (after.Y - before.Y) * zoom);
        canvas.Invalidate();
    }

    private int HitTestControlPoint(Point screenPos)
    {
        if (origSurface == null) return -1;

        int ptsX = currentGridWidth + 1;
        int ptsY = currentGridHeight + 1;
        float cellW = (float)origSurface.Width / currentGridWidth;
        float cellH = (float)origSurface.Height / currentGridHeight;
        float bestDist = PointHitRadius;
        int bestIdx = -1;

        for (int row = 0; row < ptsY; row++)
        {
            for (int col = 0; col < ptsX; col++)
            {
                var sp = GetDeformedScreenPos(row, col, ptsX, cellW, cellH);
                float dx = screenPos.X - sp.X;
                float dy = screenPos.Y - sp.Y;
                float dist = MathF.Sqrt(dx * dx + dy * dy);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestIdx = row * ptsX + col;
                }
            }
        }

        return bestIdx;
    }

    // ═════════════════════════════════════════════════════════
    //  Undo / Redo / Reset
    // ═════════════════════════════════════════════════════════

    private void Undo()
    {
        if (undoStack.Count == 0) return;
        redoStack.Add((float[])displacements.Clone());
        displacements = undoStack[^1];
        undoStack.RemoveAt(undoStack.Count - 1);
        RegeneratePreview();
        UpdateButtons();
        PushUpdate();
    }

    private void Redo()
    {
        if (redoStack.Count == 0) return;
        undoStack.Add((float[])displacements.Clone());
        displacements = redoStack[^1];
        redoStack.RemoveAt(redoStack.Count - 1);
        RegeneratePreview();
        UpdateButtons();
        PushUpdate();
    }

    private void ResetGrid()
    {
        undoStack.Add((float[])displacements.Clone());
        redoStack.Clear();
        InitDisplacements(currentGridWidth, currentGridHeight);
        RegeneratePreview();
        UpdateButtons();
        PushUpdate();
    }

    // ═════════════════════════════════════════════════════════
    //  CPU Preview Rendering
    // ═════════════════════════════════════════════════════════

    private void RegeneratePreview()
    {
        if (origSurface == null || workSurface == null) return;

        int gridWidth = currentGridWidth;
        int gridHeight = currentGridHeight;
        int ptsX = gridWidth + 1;
        int w = origSurface.Width, h = origSurface.Height;
        float cellW = (float)w / gridWidth;
        float cellH = (float)h / gridHeight;

        // Fast path: if all displacements are zero, just copy the original
        bool allZero = true;
        for (int i = 0; i < displacements.Length && allZero; i++)
        {
            if (displacements[i] != 0) allZero = false;
        }

        if (allZero)
        {
            CopySurface(origSurface, workSurface);
            canvas?.Invalidate();
            return;
        }

        // Local copy for thread safety in Parallel.For
        float[] disp = displacements;
        var orig = origSurface;
        var work = workSurface;

        Parallel.For(0, h, py =>
        {
            float gy = py / cellH;
            int cj = Math.Clamp((int)MathF.Floor(gy), 0, gridHeight - 1);
            float v = Math.Clamp(gy - cj, 0f, 1f);

            for (int px = 0; px < w; px++)
            {
                float gx = px / cellW;
                int ci = Math.Clamp((int)MathF.Floor(gx), 0, gridWidth - 1);
                float u = Math.Clamp(gx - ci, 0f, 1f);

                int i00 = cj * ptsX + ci;
                int i10 = i00 + 1;
                int i01 = i00 + ptsX;
                int i11 = i01 + 1;

                float dx00 = disp[i00 * 2], dy00 = disp[i00 * 2 + 1];
                float dx10 = disp[i10 * 2], dy10 = disp[i10 * 2 + 1];
                float dx01 = disp[i01 * 2], dy01 = disp[i01 * 2 + 1];
                float dx11 = disp[i11 * 2], dy11 = disp[i11 * 2 + 1];

                float tdx = Lerp(Lerp(dx00, dx10, u), Lerp(dx01, dx11, u), v);
                float tdy = Lerp(Lerp(dy00, dy10, u), Lerp(dy01, dy11, u), v);

                float srcX = px - tdx;
                float srcY = py - tdy;

                work[px, py] = SampleBilinear(orig, srcX, srcY);
            }
        });

        canvas?.Invalidate();
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private static ColorBgra SampleBilinear(Surface sfc, float x, float y)
    {
        x = Math.Clamp(x, 0, sfc.Width - 1);
        y = Math.Clamp(y, 0, sfc.Height - 1);
        int x1 = (int)x, y1 = (int)y;
        int x2 = Math.Min(x1 + 1, sfc.Width - 1);
        int y2 = Math.Min(y1 + 1, sfc.Height - 1);
        float fx = x - x1, fy = y - y1;
        return ColorBgra.Lerp(
            ColorBgra.Lerp(sfc[x1, y1], sfc[x2, y1], fx),
            ColorBgra.Lerp(sfc[x1, y2], sfc[x2, y2], fx),
            fy);
    }

    private static void CopySurface(Surface src, Surface dst)
    {
        for (int y = 0; y < src.Height && y < dst.Height; y++)
            for (int x = 0; x < src.Width && x < dst.Width; x++)
                dst[x, y] = src[x, y];
    }

    // ═════════════════════════════════════════════════════════
    //  Grid Displacement Management
    // ═════════════════════════════════════════════════════════

    private void InitDisplacements(int gridWidth, int gridHeight)
    {
        int count = (gridWidth + 1) * (gridHeight + 1) * 2;
        displacements = new float[count];
    }

    // ═════════════════════════════════════════════════════════
    //  Coordinate Helpers
    // ═════════════════════════════════════════════════════════

    private RectangleF GetImageRect()
    {
        if (origSurface == null) return RectangleF.Empty;
        return new RectangleF(pan.X, pan.Y, origSurface.Width * zoom, origSurface.Height * zoom);
    }

    private PointF ScreenToImage(Point screenPt)
    {
        return new PointF(
            (screenPt.X - pan.X) / zoom,
            (screenPt.Y - pan.Y) / zoom);
    }

    private void FitToWindow()
    {
        if (origSurface == null || canvas == null) return;
        if (canvas.ClientSize.Width <= 0 || canvas.ClientSize.Height <= 0) return;

        float sx = (float)canvas.ClientSize.Width / origSurface.Width;
        float sy = (float)canvas.ClientSize.Height / origSurface.Height;
        zoom = Math.Min(sx, sy) * 0.9f;
        pan = new PointF(
            (canvas.ClientSize.Width - origSurface.Width * zoom) / 2f,
            (canvas.ClientSize.Height - origSurface.Height * zoom) / 2f);
        initialFitDone = true;
        canvas.Invalidate();
    }

    // ═════════════════════════════════════════════════════════
    //  Token Push & UI Sync
    // ═════════════════════════════════════════════════════════

    private void PushUpdate()
    {
        UpdateTokenFromDialog();
    }

    private void ApplyGridSliderChange(bool preferWidth)
    {
        int requestedWidth = gridWidthSlider.Value;
        int requestedHeight = gridHeightSlider.Value;

        if (requestedWidth == currentGridWidth && requestedHeight == currentGridHeight)
        {
            gridWidthValue.Text = requestedWidth.ToString();
            gridHeightValue.Text = requestedHeight.ToString();
            return;
        }

        currentGridWidth = requestedWidth;
        currentGridHeight = requestedHeight;

        suppressGridSliderEvents = true;
        gridWidthSlider.Value = currentGridWidth;
        gridHeightSlider.Value = currentGridHeight;
        suppressGridSliderEvents = false;

        gridWidthValue.Text = currentGridWidth.ToString();
        gridHeightValue.Text = currentGridHeight.ToString();

        InitDisplacements(currentGridWidth, currentGridHeight);
        undoStack.Clear();
        redoStack.Clear();
        UpdateButtons();
        RegeneratePreview();
        PushUpdate();
    }

    private GridColorPreset GetSelectedGridColorPreset()
    {
        if (selectedGridColorIndex < 0 || selectedGridColorIndex >= GridColorPresets.Length)
            return GridColorPresets[0];

        return GridColorPresets[selectedGridColorIndex];
    }

    private void SelectGridColor(int index)
    {
        selectedGridColorIndex = Math.Clamp(index, 0, GridColorPresets.Length - 1);

        for (int i = 0; i < gridColorButtons.Count; i++)
        {
            Button swatch = gridColorButtons[i];
            bool isSelected = i == selectedGridColorIndex;
            swatch.FlatAppearance.BorderSize = isSelected ? 3 : 1;
            swatch.FlatAppearance.BorderColor = isSelected ? Color.White : Color.FromArgb(30, 30, 30);
        }

        canvas?.Invalidate();
    }

    private void SyncUIFromToken(GridWarpConfigToken token)
    {
        suppressGridSliderEvents = true;
        gridWidthSlider.Value = currentGridWidth;
        gridHeightSlider.Value = currentGridHeight;
        suppressGridSliderEvents = false;
        qualitySlider.Value = Math.Clamp(token.Quality, qualitySlider.Minimum, qualitySlider.Maximum);
        gridWidthValue.Text = currentGridWidth.ToString();
        gridHeightValue.Text = currentGridHeight.ToString();
        qualityValue.Text = $"{qualitySlider.Value}x";
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        if (undoBtn != null) undoBtn.Enabled = undoStack.Count > 0;
        if (redoBtn != null) redoBtn.Enabled = redoStack.Count > 0;
    }

    // ═════════════════════════════════════════════════════════
    //  Helpers
    // ═════════════════════════════════════════════════════════

    private Button MakeButton(string text)
    {
        return new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(S(60), S(28)),
            FlatStyle = FlatStyle.Flat,
            BackColor = ControlBg,
            ForeColor = LabelColor,
            Padding = new Padding(S(8), S(2), S(8), S(2)),
            Margin = new Padding(0, 0, S(4), 0)
        };
    }

    private Button MakeIconButton(string icon, string tooltip)
    {
        Button btn = MakeButton(icon);
        btn.MinimumSize = new Size(S(34), S(30));
        btn.Padding = new Padding(S(6), S(2), S(6), S(2));
        uiToolTip.SetToolTip(btn, tooltip);
        return btn;
    }

    private (TrackBar slider, Label valueLabel) AddSlider(
        FlowLayoutPanel parent, string label, int ctrlW, int min, int max, int val, Func<int, string> fmt)
    {
        int rowH = S(22);
        int valW = S(80);
        var row = new Panel
        {
            Width = ctrlW,
            Height = rowH,
            Margin = new Padding(0, S(4), 0, 0),
            BackColor = Color.Transparent
        };
        var nameLabel = new Label
        {
            Text = label,
            AutoSize = true,
            ForeColor = LabelColor,
            Location = Point.Empty
        };
        row.Controls.Add(nameLabel);
        var valLabel = new Label
        {
            Text = fmt(val),
            AutoSize = false,
            Size = new Size(valW, rowH),
            ForeColor = LabelColor,
            TextAlign = ContentAlignment.TopRight,
            Location = new Point(ctrlW - valW, 0)
        };
        row.Controls.Add(valLabel);
        parent.Controls.Add(row);

        var tb = new TrackBar
        {
            Width = ctrlW,
            Minimum = min,
            Maximum = max,
            Value = val,
            TickFrequency = Math.Max(1, (max - min) / 10),
            SmallChange = 1,
            LargeChange = Math.Max(1, (max - min) / 4),
            BackColor = PanelBg,
            Margin = new Padding(0, 0, 0, S(2))
        };
        parent.Controls.Add(tb);

        return (tb, valLabel);
    }

    private Label MakeHintLabel(string text, int maxW) => new Label
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(maxW, 0),
        ForeColor = Color.FromArgb(160, 170, 180),
        Font = new Font(Font.FontFamily, Font.Size * 0.9f, FontStyle.Italic),
        Margin = new Padding(0, 0, 0, S(2))
    };

    // ═════════════════════════════════════════════════════════
    //  Cleanup
    // ═════════════════════════════════════════════════════════

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing
            && DialogResult != DialogResult.OK
            && DialogResult != DialogResult.Cancel)
        {
            DialogResult = DialogResult.Cancel;
        }
        base.OnFormClosing(e);
    }

    protected override void OnDispose(bool disposing)
    {
        if (disposing)
        {
            origSurface?.Dispose();
            workSurface?.Dispose();
        }
        base.OnDispose(disposing);
    }

    // ═════════════════════════════════════════════════════════
    //  Nested Types
    // ═════════════════════════════════════════════════════════

    private sealed class DoubleBufferedPanel : Panel
    {
        public DoubleBufferedPanel()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            SetStyle(ControlStyles.Selectable, true);
        }
    }
}

#pragma warning restore CS0612, CS0618
