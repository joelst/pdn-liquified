#pragma warning disable CS0612, CS0618 // EffectConfigForm2 base class triggers obsolescence warnings

using PaintDotNet;
using PaintDotNet.Effects;
using PaintDotNet.Imaging;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
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
    private NumericUpDown pointX = null!, pointY = null!;
    private Label pointCoordinatesLabel = null!;
    private bool syncingPointCoordinates;

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
    private int selectedGridPointIndex;

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
    private float _dpi;
    private int S(int v) => (int)(v * _dpi);

    private readonly object previewSync = new();
    private PreviewRequest? pendingPreview;
    private Task? previewWorker;
    private int previewVersion;
    private bool previewWorkerRunning;
    private bool previewDisposed;

    private const int MaxPreviewDimension = 1400;

    private sealed class PreviewRequest
    {
        public required Surface Source { get; init; }
        public required float[] Displacements { get; init; }
        public required int GridWidth { get; init; }
        public required int GridHeight { get; init; }
        public required int Version { get; init; }
    }

    // ═════════════════════════════════════════════════════════
    //  Construction
    // ═════════════════════════════════════════════════════════

    public GridWarpConfigForm()
    {
        AutoScaleMode = AutoScaleMode.None;
        _dpi = Math.Max(1f, DeviceDpi / 96f);
        BuildUI();
        WireEvents();
        DpiChanged += (_, e) => RescaleForDpi(e.DeviceDpiOld, e.DeviceDpiNew);
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
        {
            displacements = (float[])token.Displacements.Clone();
            for (int i = 0; i < displacements.Length; i++)
            {
                if (!float.IsFinite(displacements[i])) displacements[i] = 0f;
            }
        }
        else
            InitDisplacements(currentGridWidth, currentGridHeight);

        selectedGridPointIndex = Math.Min(selectedGridPointIndex,
            (currentGridWidth + 1) * (currentGridHeight + 1) - 1);
        undoStack.Clear();
        redoStack.Clear();

        // Guard: controls may not exist yet if called during base constructor
        if (gridWidthSlider != null)
            SyncUIFromToken(token);

        SchedulePreview();
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
                DrainPreviewWorker();
                origSurface?.Dispose();
                workSurface?.Dispose();
                workSurface = null;
                origSurface = new Surface(size.Width, size.Height);
                unsafe
                {
                    srcBmp.CopyPixels(origSurface.Scan0.VoidStar, origSurface.Stride,
                        (uint)origSurface.Scan0.Length, null);
                }
                SelectGridPoint(selectedGridPointIndex);
                SchedulePreview();
                initialFitDone = false;
                BeginInvoke(new Action(FitToWindow));
            }
        }
        catch (Exception ex)
        {
            origSurface?.Dispose();
            origSurface = null;
            workSurface?.Dispose();
            workSurface = null;
            canvas.AccessibleDescription = $"Grid preview is unavailable because the source image could not be loaded: {ex.Message}";
        }
    }

    // ═════════════════════════════════════════════════════════
    //  UI Construction
    // ═════════════════════════════════════════════════════════

    private void BuildUI()
    {
        SuspendLayout();

        Text = "Grid Warp \u2013 Ctrl+Page Up/Down selects points; Tab moves focus";
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
                Tag = index,
                AccessibleName = $"{preset.Name} grid color",
                AccessibleDescription = $"Use the {preset.Name.ToLowerInvariant()} color for the grid overlay."
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

        pointCoordinatesLabel = new Label
        {
            Text = "Selected point (image pixels):",
            AutoSize = true,
            MaximumSize = new Size(ctrlW, 0),
            ForeColor = LabelColor
        };
        leftPanel.Controls.Add(pointCoordinatesLabel);
        pointX = AddCoordinateEditor(leftPanel, "&X:", ctrlW, "Selected grid point X");
        pointY = AddCoordinateEditor(leftPanel, "&Y:", ctrlW, "Selected grid point Y");
        pointX.ValueChanged += (_, _) => ApplyPointCoordinates(editX: true);
        pointY.ValueChanged += (_, _) => ApplyPointCoordinates(editX: false);

        var helpLabel = new Label
        {
            Text = "Left-click: drag control points\nRight-click: pan view\n" +
                   "Mouse wheel: zoom\nDouble-click: fit to window\n" +
                   "Ctrl+Page Up/Down: previous / next point\nTab / Shift+Tab: move focus\n" +
                   "Arrows: 1 pixel; Shift+arrows: 10 pixels\nF6: edit X/Y coordinates\n" +
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
            BackColor = Color.FromArgb(48, 48, 48),
            TabStop = true,
            AccessibleName = "Grid warp canvas",
            AccessibleDescription = "Use Ctrl+Page Down or Ctrl+Page Up to select a control point, then arrow keys to move it. Hold Shift to move ten image pixels. Tab and Shift+Tab move focus out of the canvas; F6 focuses the X coordinate editor. Right-click and drag to pan; use the mouse wheel to zoom."
        };
        canvas.KeyDown += Canvas_KeyDown;

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
        uiToolTip.SetToolTip(canvas, "Left-click or Ctrl+Page Up/Down selects a control point. Tab/Shift+Tab moves focus out of the canvas; F6 edits coordinates. Arrow keys move the point; Shift+arrows move 10 image pixels. Right-click drag to pan; wheel to zoom.");

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
        if (isGridVisible) DrawGridOverlay(g);
        else DrawSelectedPointFocus(g);
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

        // Paired dark/light strokes keep the grid legible against both light and dark images.
        using var gridHaloPen = new Pen(Color.Black, 3.5f);
        using var gridContrastPen = new Pen(Color.White, 1.75f);
        using var gridPen = new Pen(gridColorPreset.GridLineColor, 1f);

        // Horizontal lines
        for (int row = 0; row < ptsY; row++)
        {
            for (int col = 0; col < currentGridWidth; col++)
            {
                var p1 = GetDeformedScreenPos(row, col, ptsX, cellW, cellH);
                var p2 = GetDeformedScreenPos(row, col + 1, ptsX, cellW, cellH);
                g.DrawLine(gridHaloPen, p1, p2);
                g.DrawLine(gridContrastPen, p1, p2);
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
                g.DrawLine(gridHaloPen, p1, p2);
                g.DrawLine(gridContrastPen, p1, p2);
                g.DrawLine(gridPen, p1, p2);
            }
        }

        // Draw control points
        float ptRadius = 4f;
        using var fillBrush = new SolidBrush(gridColorPreset.PointColor);
        using var dragBrush = new SolidBrush(gridColorPreset.DragPointColor);
        using var outlinePen = new Pen(Color.Black, 2.5f);
        using var contrastOutlinePen = new Pen(Color.White, 1f);

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
                g.DrawEllipse(contrastOutlinePen, sp.X - r + 1, sp.Y - r + 1, (r * 2) - 2, (r * 2) - 2);
                if (idx == selectedGridPointIndex)
                {
                    using var focusPen = new Pen(Color.White, 2f) { DashStyle = DashStyle.Dash };
                    g.DrawEllipse(focusPen, sp.X - r - 4, sp.Y - r - 4, r * 2 + 8, r * 2 + 8);
                }
            }
        }
    }

    private void DrawSelectedPointFocus(Graphics g)
    {
        if (origSurface == null) return;
        int pointsPerRow = currentGridWidth + 1;
        int row = selectedGridPointIndex / pointsPerRow;
        int col = selectedGridPointIndex % pointsPerRow;
        PointF point = GetDeformedScreenPos(row, col, pointsPerRow,
            (float)origSurface.Width / currentGridWidth,
            (float)origSurface.Height / currentGridHeight);
        using var halo = new Pen(Color.Black, 4f);
        using var focus = new Pen(Color.White, 2f) { DashStyle = DashStyle.Dash };
        g.DrawEllipse(halo, point.X - 10, point.Y - 10, 20, 20);
        g.DrawEllipse(focus, point.X - 10, point.Y - 10, 20, 20);
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
                SelectGridPoint(hitIdx);
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

            SelectGridPoint(dragPointIndex);
            SchedulePreview();
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

    private void Canvas_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.F6)
        {
            pointX.Focus();
            e.SuppressKeyPress = true;
            return;
        }
        int pointCount = (currentGridWidth + 1) * (currentGridHeight + 1);
        if (e.Control && e.KeyCode is Keys.PageDown or Keys.PageUp)
        {
            int direction = e.KeyCode == Keys.PageUp ? -1 : 1;
            SelectGridPoint((selectedGridPointIndex + direction + pointCount) % pointCount);
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }

        if (e.KeyCode is Keys.Left or Keys.Right or Keys.Up or Keys.Down)
        {
            NudgeSelectedGridPoint(e.KeyCode, e.Shift ? 10f : 1f);
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
    }

    private void SelectGridPoint(int index)
    {
        int pointCount = (currentGridWidth + 1) * (currentGridHeight + 1);
        selectedGridPointIndex = Math.Clamp(index, 0, pointCount - 1);
        int pointsPerRow = currentGridWidth + 1;
        int row = selectedGridPointIndex / pointsPerRow;
        int col = selectedGridPointIndex % pointsPerRow;
        float cellW = origSurface == null ? 0 : (float)origSurface.Width / currentGridWidth;
        float cellH = origSurface == null ? 0 : (float)origSurface.Height / currentGridHeight;
        float x = col * cellW + displacements[selectedGridPointIndex * 2];
        float y = row * cellH + displacements[selectedGridPointIndex * 2 + 1];
        SyncPointCoordinates(x, y, row, col);
        canvas.AccessibleDescription = $"Selected control point, row {row + 1}, column {col + 1}, image coordinates {x:0.##}, {y:0.##}. Ctrl+Page Up/Down selects another point. Use arrow keys to move; hold Shift for 10-pixel steps. Tab or Shift+Tab leaves the canvas; F6 focuses the X coordinate editor.";
        canvas.Invalidate();
    }

    private void NudgeSelectedGridPoint(Keys key, float amount)
    {
        if (origSurface == null) return;
        undoStack.Add((float[])displacements.Clone());
        redoStack.Clear();
        int offset = selectedGridPointIndex * 2;
        switch (key)
        {
            case Keys.Left: displacements[offset] -= amount; break;
            case Keys.Right: displacements[offset] += amount; break;
            case Keys.Up: displacements[offset + 1] -= amount; break;
            case Keys.Down: displacements[offset + 1] += amount; break;
        }

        SelectGridPoint(selectedGridPointIndex);
        UpdateButtons();
        SchedulePreview();
        PushUpdate();
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
        SelectGridPoint(selectedGridPointIndex);
        SchedulePreview();
        UpdateButtons();
        PushUpdate();
    }

    private void Redo()
    {
        if (redoStack.Count == 0) return;
        undoStack.Add((float[])displacements.Clone());
        displacements = redoStack[^1];
        redoStack.RemoveAt(redoStack.Count - 1);
        SelectGridPoint(selectedGridPointIndex);
        SchedulePreview();
        UpdateButtons();
        PushUpdate();
    }

    private void ResetGrid()
    {
        undoStack.Add((float[])displacements.Clone());
        redoStack.Clear();
        InitDisplacements(currentGridWidth, currentGridHeight);
        SelectGridPoint(selectedGridPointIndex);
        SchedulePreview();
        UpdateButtons();
        PushUpdate();
    }

    // ═════════════════════════════════════════════════════════
    //  CPU Preview Rendering
    // ═════════════════════════════════════════════════════════

    private void SchedulePreview()
    {
        if (origSurface == null) return;

        lock (previewSync)
        {
            if (previewDisposed) return;

            pendingPreview = new PreviewRequest
            {
                Source = origSurface,
                Displacements = (float[])displacements.Clone(),
                GridWidth = currentGridWidth,
                GridHeight = currentGridHeight,
                Version = Interlocked.Increment(ref previewVersion)
            };

            if (!previewWorkerRunning)
            {
                previewWorkerRunning = true;
                previewWorker = Task.Run(ProcessPreviewQueue);
            }
        }
    }

    private void ProcessPreviewQueue()
    {
        while (true)
        {
            PreviewRequest? request;
            lock (previewSync)
            {
                if (previewDisposed || pendingPreview == null)
                {
                    previewWorkerRunning = false;
                    return;
                }

                request = pendingPreview;
                pendingPreview = null;
            }

            Surface? preview = null;
            try
            {
                preview = RenderPreview(request);
            }
            catch (Exception ex)
            {
                preview?.Dispose();
                preview = null;
                PostPreviewFailure(request.Version, ex.Message);
            }

            if (preview == null)
            {
                continue;
            }

            bool isCurrent;
            lock (previewSync)
            {
                isCurrent = !previewDisposed
                    && request.Version == Volatile.Read(ref previewVersion)
                    && pendingPreview == null;
            }

            if (!isCurrent || !IsHandleCreated || IsDisposed)
            {
                preview.Dispose();
                continue;
            }

            Surface result = preview;
            try
            {
                BeginInvoke(new Action(() =>
                {
                    lock (previewSync)
                    {
                        if (previewDisposed || result == null || request.Version != Volatile.Read(ref previewVersion))
                        {
                            result?.Dispose();
                            result = null!;
                            return;
                        }
                    }

                    Surface? old = workSurface;
                    workSurface = result;
                    result = null!;
                    old?.Dispose();
                    canvas.Invalidate();
                }));
            }
            catch (ObjectDisposedException)
            {
                result.Dispose();
            }
            catch (InvalidOperationException)
            {
                result.Dispose();
            }
        }
    }

    private void DrainPreviewWorker()
    {
        Task? worker;
        lock (previewSync)
        {
            pendingPreview = null;
            Interlocked.Increment(ref previewVersion);
            worker = previewWorker;
        }

        try
        {
            worker?.Wait();
        }
        catch (AggregateException)
        {
            _ = worker?.Exception;
        }
    }

    private Surface RenderPreview(PreviewRequest request)
    {
        int gridWidth = request.GridWidth;
        int gridHeight = request.GridHeight;
        int ptsX = gridWidth + 1;
        int w = request.Source.Width, h = request.Source.Height;
        float scale = Math.Min(1f, (float)MaxPreviewDimension / Math.Max(w, h));
        int previewWidth = Math.Max(1, (int)MathF.Round(w * scale));
        int previewHeight = Math.Max(1, (int)MathF.Round(h * scale));
        float cellW = (float)w / gridWidth;
        float cellH = (float)h / gridHeight;
        Surface work = new(previewWidth, previewHeight);
        try
        {
            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, System.Environment.ProcessorCount - 1)
            };

            Parallel.For(0, previewHeight, options, py =>
            {
                if (request.Version != Volatile.Read(ref previewVersion)) return;
                float imageY = (py + 0.5f) * h / previewHeight - 0.5f;
                float gy = imageY / cellH;
                int cj = Math.Clamp((int)MathF.Floor(gy), 0, gridHeight - 1);
                float v = Math.Clamp(gy - cj, 0f, 1f);

                for (int px = 0; px < previewWidth; px++)
                {
                    if ((px & 127) == 0 && request.Version != Volatile.Read(ref previewVersion)) return;
                    float imageX = (px + 0.5f) * w / previewWidth - 0.5f;
                    float gx = imageX / cellW;
                    int ci = Math.Clamp((int)MathF.Floor(gx), 0, gridWidth - 1);
                    float u = Math.Clamp(gx - ci, 0f, 1f);

                    int i00 = cj * ptsX + ci;
                    int i10 = i00 + 1;
                    int i01 = i00 + ptsX;
                    int i11 = i01 + 1;
                    float[] disp = request.Displacements;
                    float dx00 = disp[i00 * 2], dy00 = disp[i00 * 2 + 1];
                    float dx10 = disp[i10 * 2], dy10 = disp[i10 * 2 + 1];
                    float dx01 = disp[i01 * 2], dy01 = disp[i01 * 2 + 1];
                    float dx11 = disp[i11 * 2], dy11 = disp[i11 * 2 + 1];
                    float tdx = Lerp(Lerp(dx00, dx10, u), Lerp(dx01, dx11, u), v);
                    float tdy = Lerp(Lerp(dy00, dy10, u), Lerp(dy01, dy11, u), v);

                    work[px, py] = SampleBilinear(request.Source, imageX - tdx, imageY - tdy);
                }
            });

            if (request.Version != Volatile.Read(ref previewVersion))
            {
                work.Dispose();
                return null!;
            }

            return work;
        }
        catch
        {
            work.Dispose();
            throw;
        }
    }

    private void PostPreviewFailure(int version, string message)
    {
        if (!IsHandleCreated || IsDisposed) return;
        try
        {
            BeginInvoke(new Action(() =>
            {
                if (version == Volatile.Read(ref previewVersion))
                {
                    canvas.AccessibleDescription = $"Grid preview could not be refreshed: {message}";
                }
            }));
        }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
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
        SelectGridPoint(Math.Min(selectedGridPointIndex,
            (currentGridWidth + 1) * (currentGridHeight + 1) - 1));
        undoStack.Clear();
        redoStack.Clear();
        UpdateButtons();
        SchedulePreview();
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

    private void RescaleForDpi(int oldDpi, int newDpi)
    {
        if (oldDpi <= 0 || newDpi <= 0 || oldDpi == newDpi) return;
        float ratio = (float)newDpi / oldDpi;
        _dpi = newDpi / 96f;
        ScaleControlTree(this, ratio);
        MinimumSize = new Size((int)MathF.Round(MinimumSize.Width * ratio), (int)MathF.Round(MinimumSize.Height * ratio));
        PerformLayout();
        canvas?.Invalidate();
    }

    private static void ScaleControlTree(Control parent, float ratio)
    {
        foreach (Control control in parent.Controls)
        {
            control.Bounds = new Rectangle(
                (int)MathF.Round(control.Left * ratio),
                (int)MathF.Round(control.Top * ratio),
                Math.Max(1, (int)MathF.Round(control.Width * ratio)),
                Math.Max(1, (int)MathF.Round(control.Height * ratio)));
            control.Margin = new Padding(
                (int)MathF.Round(control.Margin.Left * ratio),
                (int)MathF.Round(control.Margin.Top * ratio),
                (int)MathF.Round(control.Margin.Right * ratio),
                (int)MathF.Round(control.Margin.Bottom * ratio));
            control.Padding = new Padding(
                (int)MathF.Round(control.Padding.Left * ratio),
                (int)MathF.Round(control.Padding.Top * ratio),
                (int)MathF.Round(control.Padding.Right * ratio),
                (int)MathF.Round(control.Padding.Bottom * ratio));
            control.MinimumSize = ScaleSize(control.MinimumSize, ratio);
            control.MaximumSize = ScaleSize(control.MaximumSize, ratio);
            ScaleControlTree(control, ratio);
        }
    }

    private static Size ScaleSize(Size size, float ratio) =>
        new(size.Width <= 0 ? 0 : Math.Max(1, (int)MathF.Round(size.Width * ratio)),
            size.Height <= 0 ? 0 : Math.Max(1, (int)MathF.Round(size.Height * ratio)));

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
        SelectGridPoint(selectedGridPointIndex);
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

    private NumericUpDown AddCoordinateEditor(FlowLayoutPanel parent, string label, int width, string name)
    {
        var row = new FlowLayoutPanel
        {
            Width = width,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, S(2), 0, S(2))
        };
        row.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            ForeColor = LabelColor,
            Margin = new Padding(0, S(5), S(6), 0)
        });
        var editor = new NumericUpDown
        {
            Minimum = -1000000000m,
            Maximum = 1000000000m,
            DecimalPlaces = 2,
            Increment = 1m,
            Width = width - S(36),
            Enabled = false,
            AccessibleName = name,
            AccessibleDescription = "Absolute image pixel coordinate of the selected control point; negative coordinates are allowed.",
            BackColor = SystemInformation.HighContrast ? SystemColors.Window : ControlBg,
            ForeColor = SystemInformation.HighContrast ? SystemColors.WindowText : LabelColor
        };
        row.Controls.Add(editor);
        parent.Controls.Add(row);
        uiToolTip.SetToolTip(editor, editor.AccessibleDescription);
        return editor;
    }

    private void SyncPointCoordinates(float x, float y, int row, int col)
    {
        if (pointX == null || pointY == null) return;
        syncingPointCoordinates = true;
        try
        {
            pointX.Value = (decimal)Math.Clamp(x, (float)pointX.Minimum, (float)pointX.Maximum);
            pointY.Value = (decimal)Math.Clamp(y, (float)pointY.Minimum, (float)pointY.Maximum);
            pointX.Enabled = pointY.Enabled = origSurface != null;
            pointCoordinatesLabel.Text = $"Point row {row + 1}, column {col + 1} (pixels):";
        }
        finally
        {
            syncingPointCoordinates = false;
        }
    }

    private void ApplyPointCoordinates(bool editX)
    {
        if (syncingPointCoordinates || origSurface == null) return;
        int pointsPerRow = currentGridWidth + 1;
        int row = selectedGridPointIndex / pointsPerRow;
        int col = selectedGridPointIndex % pointsPerRow;
        int offset = selectedGridPointIndex * 2;
        float dx = editX ? (float)pointX.Value - col * ((float)origSurface.Width / currentGridWidth) : displacements[offset];
        float dy = editX ? displacements[offset + 1] : (float)pointY.Value - row * ((float)origSurface.Height / currentGridHeight);
        if (dx == displacements[offset] && dy == displacements[offset + 1]) return;
        undoStack.Add((float[])displacements.Clone());
        redoStack.Clear();
        displacements[offset] = dx;
        displacements[offset + 1] = dy;
        SelectGridPoint(selectedGridPointIndex);
        UpdateButtons();
        SchedulePreview();
        PushUpdate();
    }

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
        btn.AccessibleName = tooltip;
        btn.AccessibleDescription = tooltip;
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
            Margin = new Padding(0, 0, 0, S(2)),
            AccessibleName = label.TrimEnd(':'),
            AccessibleDescription = $"{label.TrimEnd(':')}. Use the arrow keys to change the value."
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
            lock (previewSync)
            {
                previewDisposed = true;
            }
            DrainPreviewWorker();

            origSurface?.Dispose();
            workSurface?.Dispose();
            uiToolTip.Dispose();
            gridColorToolTip.Dispose();
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
            TabStop = true;
        }

        protected override bool IsInputKey(Keys keyData) =>
            (keyData & Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Up or Keys.Down
            || ((keyData & Keys.Control) != 0 && (keyData & Keys.KeyCode) is Keys.PageUp or Keys.PageDown)
            || base.IsInputKey(keyData);
    }
}

#pragma warning restore CS0612, CS0618
