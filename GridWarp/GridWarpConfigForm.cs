#pragma warning disable CS0612, CS0618 // EffectConfigForm2 base class triggers obsolescence warnings

using PaintDotNet;
using PaintDotNet.Effects;
using PaintDotNet.Imaging;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
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
    private ComboBox gridWidthCombo = null!;
    private ComboBox gridHeightCombo = null!;
    private ComboBox qualityCombo = null!;
    private ComboBox gridColorCombo = null!;
    private DistortionPlugins.GlyphButton showGridButton = null!;
    private Button undoBtn = null!, redoBtn = null!, resetBtn = null!;
    private Panel canvas = null!;
    private NumericUpDown pointX = null!, pointY = null!;
    private Label pointCoordinatesLabel = null!;
    private bool syncingPointCoordinates;
    private bool[] frozenPoints = Array.Empty<bool>();
    private int[] linkedGroups = Array.Empty<int>();
    private readonly HashSet<int> selectedPoints = new();
    private enum PointEditMode { Move, Freeze, Unfreeze, Link, Unlink }
    private PointEditMode editMode;
    private int linkAnchor = -1;
    private readonly List<Button> modeButtons = new();
    private Label modeStatus = null!;
    private Button returnToMoveBtn = null!;

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
    private sealed record GridSnapshot(float[] Displacements, bool[] FrozenPoints, int[] LinkedGroups);
    private readonly List<GridSnapshot> undoStack = new();
    private readonly List<GridSnapshot> redoStack = new();
    private const int MaximumUndoSteps = 256;

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
    private bool suppressGridControlEvents;
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
        ApplySuiteTheme();
    }

    protected override void OnThemeConfigChanged()
    {
        base.OnThemeConfigChanged();
        if (canvas is not null) ApplySuiteTheme();
    }

    private void ApplySuiteTheme() => DistortionPlugins.DialogUi.ApplyTheme(this, canvas, IsAppThemeDark);

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

        int pointCount = (currentGridWidth + 1) * (currentGridHeight + 1);
        frozenPoints = token.FrozenPoints?.Length == pointCount ? (bool[])token.FrozenPoints.Clone() : new bool[pointCount];
        linkedGroups = new int[pointCount];
        if (token.LinkedGroups?.Length == pointCount)
            for (int i = 0; i < pointCount; i++)
                linkedGroups[i] = token.LinkedGroups[i] > 0 && token.LinkedGroups[i] <= pointCount ? token.LinkedGroups[i] : 0;
        NormalizeLinkGroups();
        linkAnchor = -1;

        selectedGridPointIndex = Math.Min(selectedGridPointIndex,
            (currentGridWidth + 1) * (currentGridHeight + 1) - 1);
        selectedPoints.Clear();
        selectedPoints.Add(selectedGridPointIndex);
        undoStack.Clear();
        redoStack.Clear();

        // Guard: controls may not exist yet if called during base constructor
        if (gridWidthCombo != null)
            SyncUIFromToken(token);

        SchedulePreview();
    }

    protected override void OnUpdateTokenFromDialog(EffectConfigToken dstToken)
    {
        var token = (GridWarpConfigToken)dstToken;
        token.GridWidth = currentGridWidth;
        token.GridHeight = currentGridHeight;
        token.Quality = qualityCombo.SelectedIndex + 1;
        token.Displacements = (float[])displacements.Clone();
        token.FrozenPoints = (bool[])frozenPoints.Clone();
        token.LinkedGroups = (int[])linkedGroups.Clone();
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

        Text = "Grid Warpified \u2013 Ctrl+Page Up/Down selects points; Tab moves focus";
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
            Text = "Left-click: drag control points\nCtrl+click: add/remove selected points\nRight-click: pan view\n" +
                   "Mouse wheel: zoom\nDouble-click: fit to window\n" +
                   "Ctrl+Page Up/Down: previous / next point\nTab / Shift+Tab: move focus\n" +
                   "Arrows: 1 pixel; Shift+arrows: 10 pixels\nF6: edit X/Y coordinates\n" +
                   "Ctrl+Z / Ctrl+Y: undo / redo\nCtrl+Shift+Page Up/Down: extend selection\n" +
                   "Alt+1..5: Move / Freeze / Unfreeze / Link / Unlink\n" +
                   "In constraint modes click points (or navigate and press Space); no dragging.\n" +
                   "Link: click an anchor, then other points. Esc: return to Move.\n" +
                   "Freeze fixes selected points; linked groups move together except frozen members.",
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
        resetBtn = MakeIconButton("\u21BA", "Reset unfrozen control-point displacements; keep frozen positions and links");
        actionRow.Controls.AddRange(new Control[] { undoBtn, redoBtn, resetBtn });

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
        var topBar = DistortionPlugins.DialogUi.AddChrome(this, leftPanel, canvas, uiToolTip,
            undoBtn, redoBtn, resetBtn, okBtn, cancelBtn);
        var modes = new FlowLayoutPanel { Name = "pointModeBar", AutoSize = true, WrapContents = false, Margin = new Padding(S(4), 0, S(8), 0) };
        foreach (PointEditMode mode in Enum.GetValues<PointEditMode>())
        {
            int shortcut = (int)mode + 1;
            var group = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0, 0, S(4), 0) };
            var button = DistortionPlugins.DialogUi.Icon(this, uiToolTip,
                Enum.Parse<DistortionPlugins.UiGlyph>(mode.ToString()), $"{mode} mode (Alt+{shortcut})",
                $"{mode} mode (Alt+{shortcut}). {GetModeHelp(mode)}");
            button.Name = "pointMode" + mode;
            button.Click += (_, _) => SetEditMode(mode);
            modeButtons.Add(button);
            group.Controls.Add(button);
            group.Controls.Add(new Label { Text = mode.ToString(), AutoSize = true, Margin = Padding.Empty });
            modes.Controls.Add(group);
        }
        topBar.Controls.Add(modes);
        gridWidthCombo = AddTopDropdown(topBar, "Grid Width", S(100),
            "Number of horizontal grid cells, from 2 to 50. Changing dimensions resets all displacements and clears undo/redo; choose the grid size before shaping the image.");
        gridHeightCombo = AddTopDropdown(topBar, "Grid Height", S(100),
            "Number of vertical grid cells, from 2 to 50. Width and height are independent. Changing dimensions resets all displacements and clears undo/redo.");
        for (int value = MinGrid; value <= MaxGridWidth; value++) gridWidthCombo.Items.Add(value);
        for (int value = MinGrid; value <= MaxGridHeight; value++) gridHeightCombo.Items.Add(value);
        gridWidthCombo.SelectedIndex = currentGridWidth - MinGrid;
        gridHeightCombo.SelectedIndex = currentGridHeight - MinGrid;
        qualityCombo = AddTopDropdown(topBar, "Quality", S(90),
            "RGSS multisampling quality for the final GPU render, from 1x to 7x. Higher values smooth edges but take longer. This does not change the CPU preview.");
        for (int value = 1; value <= 7; value++) qualityCombo.Items.Add($"{value}x");
        qualityCombo.SelectedIndex = 3;
        gridColorCombo = AddTopDropdown(topBar, "Grid Color", S(110),
            "Choose the grid-line and control-point overlay colors for contrast against your image. Overlay colors do not change the final image.");
        gridColorCombo.Items.AddRange(GridColorPresets);
        gridColorCombo.SelectedIndex = 0;
        showGridButton = DistortionPlugins.DialogUi.Icon(this, uiToolTip, DistortionPlugins.UiGlyph.Grid,
            "Hide grid", "Toggle grid lines and ordinary point handles. Frozen-point locks remain visible until unfrozen. This affects only the preview overlay, not the final output.");
        showGridButton.Name = "showGridToggle";
        showGridButton.Margin = new Padding(S(6), S(8), S(6), S(4));
        topBar.Controls.Add(showGridButton);
        SetGridVisible(true);
        modeStatus = new Label { Name = "pointModeStatus", AutoSize = true, MaximumSize = new Size(S(310), 0), Margin = new Padding(S(6), S(8), S(8), S(4)) };
        topBar.Controls.Add(modeStatus);
        returnToMoveBtn = MakeButton("Move (Esc)");
        returnToMoveBtn.Name = "returnToMove";
        returnToMoveBtn.AccessibleName = "Return to Move mode";
        returnToMoveBtn.Margin = new Padding(0, S(8), S(4), S(4));
        uiToolTip.SetToolTip(returnToMoveBtn, "End the current constraint mode and return to moving points (Esc).");
        returnToMoveBtn.Click += (_, _) => SetEditMode(PointEditMode.Move);
        topBar.Controls.Add(returnToMoveBtn);
        SetEditMode(PointEditMode.Move);
        actionRow.Dispose();
        okCancelRow.Dispose();

        ResumeLayout(true);
    }

    // ═════════════════════════════════════════════════════════
    //  Event Wiring
    // ═════════════════════════════════════════════════════════

    private void WireEvents()
    {
        uiToolTip.SetToolTip(canvas, "Left-click or Ctrl+Page Up/Down selects a control point. Tab/Shift+Tab moves focus out of the canvas; F6 edits coordinates. Arrow keys move the point; Shift+arrows move 10 image pixels. Right-click drag to pan; wheel to zoom.");

        gridWidthCombo.SelectedIndexChanged += (_, _) =>
        {
            if (suppressGridControlEvents)
                return;

            ApplyGridDimensions();
        };

        gridHeightCombo.SelectedIndexChanged += (_, _) =>
        {
            if (suppressGridControlEvents)
                return;

            ApplyGridDimensions();
        };

        qualityCombo.SelectedIndexChanged += (_, _) =>
        {
            if (!suppressGridControlEvents) PushUpdate();
        };
        gridColorCombo.SelectedIndexChanged += (_, _) => SelectGridColor(gridColorCombo.SelectedIndex);
        showGridButton.Click += (_, _) => SetGridVisible(!isGridVisible);

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
        DrawFrozenPoints(g);
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

        DrawLinkedConnections(g, ptsX, cellW, cellH);

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
                if (linkedGroups[idx] > 0)
                {
                    g.DrawRectangle(outlinePen, sp.X - r - 2, sp.Y - r - 2, r * 2 + 4, r * 2 + 4);
                    g.DrawRectangle(contrastOutlinePen, sp.X - r - 2, sp.Y - r - 2, r * 2 + 4, r * 2 + 4);
                }
                if (selectedPoints.Contains(idx))
                {
                    using var focusPen = new Pen(Color.White, 2f) { DashStyle = DashStyle.Dash };
                    g.DrawEllipse(focusPen, sp.X - r - 4, sp.Y - r - 4, r * 2 + 8, r * 2 + 8);
                }
            }
        }
    }

    private void DrawFrozenPoints(Graphics g)
    {
        if (origSurface == null) return;
        int pointsPerRow = currentGridWidth + 1;
        float cellW = (float)origSurface.Width / currentGridWidth;
        float cellH = (float)origSurface.Height / currentGridHeight;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var fill = new SolidBrush(DistortionPlugins.DialogUi.FreezeColor);
        using var key = new Pen(SystemInformation.HighContrast ? SystemColors.HighlightText : Color.Black, S(2));
        using var halo = new Pen(Color.Black, S(5)) { LineJoin = LineJoin.Round };
        using var contrast = new Pen(Color.White, S(3)) { LineJoin = LineJoin.Round };
        using var accent = new Pen(fill.Color, S(1)) { LineJoin = LineJoin.Round };
        for (int index = 0; index < frozenPoints.Length; index++)
        {
            if (!frozenPoints[index]) continue;
            PointF point = GetDeformedScreenPos(index / pointsPerRow, index % pointsPerRow, pointsPerRow, cellW, cellH);
            var body = new RectangleF(point.X - S(6), point.Y - S(2), S(12), S(10));
            foreach (Pen pen in new[] { halo, contrast, accent })
            {
                g.DrawArc(pen, point.X - S(4), point.Y - S(10), S(8), S(14), 180, 180);
                g.DrawRectangle(pen, body.X, body.Y, body.Width, body.Height);
            }
            g.FillRectangle(fill, body);
            g.DrawLine(key, point.X, point.Y + S(1), point.X, point.Y + S(5));
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

    private IEnumerable<(int From, int To)> GetLinkedConnections()
    {
        int pointsPerRow = currentGridWidth + 1;
        int[] parents = Enumerable.Range(0, linkedGroups.Length).ToArray();
        int Root(int index)
        {
            while (parents[index] != index)
            {
                parents[index] = parents[parents[index]];
                index = parents[index];
            }
            return index;
        }
        for (int index = 0; index < linkedGroups.Length; index++)
        {
            if (linkedGroups[index] <= 0) continue;
            foreach (int neighbor in new[] { index % pointsPerRow < pointsPerRow - 1 ? index + 1 : -1, index + pointsPerRow })
            {
                if (neighbor < 0 || neighbor >= linkedGroups.Length || linkedGroups[index] != linkedGroups[neighbor]) continue;
                parents[Root(neighbor)] = Root(index);
                yield return (index, neighbor);
            }
        }
        var previous = new Dictionary<int, int>();
        for (int index = 0; index < linkedGroups.Length; index++)
        {
            int group = linkedGroups[index];
            if (group <= 0) continue;
            if (previous.TryGetValue(group, out int from) && Root(from) != Root(index))
            {
                parents[Root(index)] = Root(from);
                yield return (from, index);
            }
            previous[group] = index;
        }
    }

    private void DrawLinkedConnections(Graphics g, int pointsPerRow, float cellW, float cellH)
    {
        // Solid black/white outlines and a dashed accent remain distinct from every grid preset.
        using var halo = new Pen(Color.Black, S(7));
        using var contrast = new Pen(Color.White, S(5));
        using var accent = new Pen(SystemInformation.HighContrast ? SystemColors.Highlight : Color.FromArgb(205, 0, 130), S(3))
        { DashStyle = DashStyle.Dash };
        foreach (var (from, to) in GetLinkedConnections())
        {
            PointF start = GetDeformedScreenPos(from / pointsPerRow, from % pointsPerRow, pointsPerRow, cellW, cellH);
            PointF end = GetDeformedScreenPos(to / pointsPerRow, to % pointsPerRow, pointsPerRow, cellW, cellH);
            g.DrawLine(halo, start, end);
            g.DrawLine(contrast, start, end);
            g.DrawLine(accent, start, end);
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
                if (editMode != PointEditMode.Move)
                {
                    ApplyModeToPoint(hitIdx);
                    return;
                }
                if ((ModifierKeys & Keys.Control) != 0)
                {
                    if (!selectedPoints.Add(hitIdx)) selectedPoints.Remove(hitIdx);
                    if (selectedPoints.Count == 0) selectedPoints.Add(hitIdx);
                    SelectGridPoint(selectedPoints.Contains(hitIdx) ? hitIdx : selectedPoints.First());
                    return;
                }
                if (!selectedPoints.Contains(hitIdx))
                {
                    selectedPoints.Clear();
                    selectedPoints.Add(hitIdx);
                }
                SelectGridPoint(hitIdx);
                if (frozenPoints[hitIdx]) return;
                dragPointIndex = hitIdx;
                // Save undo snapshot before starting drag
                RecordUndo();
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
        if (editMode == PointEditMode.Move && dragPointIndex >= 0 && e.Button == MouseButtons.Left && origSurface != null)
        {
            var imgPt = ScreenToImage(e.Location);
            int ptsX = currentGridWidth + 1;
            int row = dragPointIndex / ptsX;
            int col = dragPointIndex % ptsX;
            float cellW = (float)origSurface.Width / currentGridWidth;
            float cellH = (float)origSurface.Height / currentGridHeight;
            float defaultX = col * cellW;
            float defaultY = row * cellH;

            MovePointGroup(dragPointIndex,
                imgPt.X - defaultX - displacements[dragPointIndex * 2],
                imgPt.Y - defaultY - displacements[dragPointIndex * 2 + 1]);

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
            canvas.Cursor = editMode == PointEditMode.Move ? Cursors.Default : Cursors.Cross;
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
        if (editMode != PointEditMode.Move && e.KeyCode is Keys.Space or Keys.Enter)
        {
            ApplyModeToPoint(selectedGridPointIndex);
            e.SuppressKeyPress = true;
            return;
        }
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
            int index = (selectedGridPointIndex + direction + pointCount) % pointCount;
            if (!e.Shift) selectedPoints.Clear();
            selectedPoints.Add(index);
            SelectGridPoint(index);
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }

        if (e.KeyCode is Keys.Left or Keys.Right or Keys.Up or Keys.Down)
        {
            if (editMode == PointEditMode.Move) NudgeSelectedGridPoint(e.KeyCode, e.Shift ? 10f : 1f);
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
        canvas.AccessibleDescription = $"Selected control point, row {row + 1}, column {col + 1}, image coordinates {x:0.##}, {y:0.##}. {(frozenPoints[selectedGridPointIndex] ? "Frozen." : "Unfrozen.")} Link group {linkedGroups[selectedGridPointIndex]}; {selectedPoints.Count} selected points. Ctrl+Page Up/Down selects another point; add Shift to extend selection. Ctrl+click adds/removes points. Use arrow keys to move; hold Shift for 10-pixel steps. Tab or Shift+Tab leaves the canvas; F6 focuses the X coordinate editor.";
        UpdateButtons();
        canvas.Invalidate();
    }

    private void NudgeSelectedGridPoint(Keys key, float amount)
    {
        if (origSurface == null || frozenPoints[selectedGridPointIndex]) return;
        RecordUndo();
        switch (key)
        {
            case Keys.Left: MovePointGroup(selectedGridPointIndex, -amount, 0); break;
            case Keys.Right: MovePointGroup(selectedGridPointIndex, amount, 0); break;
            case Keys.Up: MovePointGroup(selectedGridPointIndex, 0, -amount); break;
            case Keys.Down: MovePointGroup(selectedGridPointIndex, 0, amount); break;
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

    private void MovePointGroup(int index, float dx, float dy)
    {
        if (frozenPoints[index]) return;
        int group = linkedGroups[index];
        for (int i = 0; i < frozenPoints.Length; i++)
        {
            if (frozenPoints[i] || (i != index && (group == 0 || linkedGroups[i] != group))) continue;
            displacements[i * 2] += dx;
            displacements[i * 2 + 1] += dy;
        }
    }

    private static string GetModeHelp(PointEditMode mode) => mode switch
    {
        PointEditMode.Move => "Drag or nudge points. Ctrl+click selects multiple points.",
        PointEditMode.Freeze => "Click points to lock their positions without moving them.",
        PointEditMode.Unfreeze => "Click points to unlock their positions without moving them.",
        PointEditMode.Link => "Click the first point to choose an anchor. Then click points to join its group. Click Link again for a new group.",
        _ => "Click points to detach them from their groups without moving them."
    };

    private void SetEditMode(PointEditMode mode)
    {
        if (dragPointIndex >= 0)
        {
            dragPointIndex = -1;
            PushUpdate();
        }
        editMode = mode;
        linkAnchor = -1;
        foreach (Button button in modeButtons)
        {
            bool selected = button == modeButtons[(int)mode];
            button.FlatAppearance.BorderSize = selected ? 3 : 1;
            ((DistortionPlugins.GlyphButton)button).IsSelected = selected;
            button.AccessibleDescription = button.AccessibleName + (selected ? ". Selected. " : ". ") + GetModeHelp((PointEditMode)modeButtons.IndexOf(button));
            button.Invalidate();
        }
        canvas.Cursor = mode == PointEditMode.Move ? Cursors.Default : Cursors.Cross;
        UpdateModeStatus();
        returnToMoveBtn.Enabled = mode != PointEditMode.Move;
        ApplySuiteTheme();
        if (mode != PointEditMode.Move) SetGridVisible(true);
        if (displacements.Length > 0) SelectGridPoint(selectedGridPointIndex);
    }

    private void ApplyModeToPoint(int index)
    {
        selectedPoints.Clear();
        selectedPoints.Add(index);
        SelectGridPoint(index);
        switch (editMode)
        {
            case PointEditMode.Freeze: SetSelectedFrozen(true); break;
            case PointEditMode.Unfreeze: SetSelectedFrozen(false); break;
            case PointEditMode.Unlink: UnlinkSelectedPoints(); break;
            case PointEditMode.Link:
                if (linkAnchor < 0)
                {
                    linkAnchor = index;
                    UpdateModeStatus();
                }
                else
                {
                    selectedPoints.Add(linkAnchor);
                    LinkSelectedPoints();
                    SelectGridPoint(index);
                    UpdateModeStatus();
                }
                break;
        }
    }

    private void SetGridVisible(bool visible)
    {
        isGridVisible = visible;
        showGridButton.IsSelected = visible;
        showGridButton.AccessibleName = visible ? "Hide grid" : "Show grid";
        showGridButton.AccessibleDescription = $"Grid {(visible ? "visible" : "hidden")}. Toggle grid lines and ordinary handles; frozen-point locks remain visible. Preview only.";
        uiToolTip.SetToolTip(showGridButton, showGridButton.AccessibleName + ". " + showGridButton.AccessibleDescription);
        ApplySuiteTheme();
        canvas.Invalidate();
    }

    private string GetLinkStatus() =>
        $"Link: anchor row {linkAnchor / (currentGridWidth + 1) + 1}, column {linkAnchor % (currentGridWidth + 1) + 1}. Click points to join; click Link again for a new group.";

    private void UpdateModeStatus()
    {
        bool active = editMode != PointEditMode.Move;
        modeStatus.Tag = active;
        modeStatus.Padding = new Padding(S(6));
        modeStatus.Text = active
            ? $"{editMode.ToString().ToUpperInvariant()} MODE - Esc returns to Move. " +
                (editMode == PointEditMode.Link && linkAnchor >= 0 ? GetLinkStatus() : GetModeHelp(editMode))
            : $"Move: {GetModeHelp(editMode)}";
        modeStatus.AccessibleDescription = modeStatus.Text;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        int keyCode = (int)(keyData & Keys.KeyCode);
        if ((keyData & Keys.Modifiers) == Keys.Alt &&
            keyCode >= (int)Keys.D1 && keyCode <= (int)Keys.D5)
        {
            SetEditMode((PointEditMode)(keyCode - (int)Keys.D1));
            return true;
        }
        if (keyData == Keys.Escape && editMode != PointEditMode.Move)
        {
            SetEditMode(PointEditMode.Move);
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void SetSelectedFrozen(bool frozen)
    {
        if (!selectedPoints.Any(index => frozenPoints[index] != frozen)) return;
        RecordUndo();
        foreach (int index in selectedPoints) frozenPoints[index] = frozen;
        SelectGridPoint(selectedGridPointIndex);
        PushUpdate();
    }

    private void LinkSelectedPoints()
    {
        if (selectedPoints.Count < 2) return;
        var existingGroups = selectedPoints.Select(index => linkedGroups[index]).Where(group => group > 0).ToHashSet();
        var members = Enumerable.Range(0, linkedGroups.Length)
            .Where(index => selectedPoints.Contains(index) || existingGroups.Contains(linkedGroups[index])).ToArray();
        int group = members.Min() + 1;
        if (members.All(index => linkedGroups[index] == group)) return;
        RecordUndo();
        foreach (int index in members) linkedGroups[index] = group;
        SelectGridPoint(selectedGridPointIndex);
        PushUpdate();
    }

    private void UnlinkSelectedPoints()
    {
        if (!selectedPoints.Any(index => linkedGroups[index] > 0)) return;
        RecordUndo();
        foreach (int index in selectedPoints) linkedGroups[index] = 0;
        NormalizeLinkGroups();
        SelectGridPoint(selectedGridPointIndex);
        PushUpdate();
    }

    private void NormalizeLinkGroups()
    {
        // Reassign remaining groups by their first member so new groups cannot reuse an existing ID.
        foreach (var group in Enumerable.Range(0, linkedGroups.Length)
            .Where(index => linkedGroups[index] > 0).GroupBy(index => linkedGroups[index]).Select(group => group.ToArray()).ToArray())
        {
            int id = group.Length > 1 ? group.Min() + 1 : 0;
            foreach (int index in group) linkedGroups[index] = id;
        }
    }

    private void RestoreSnapshot(GridSnapshot snapshot)
    {
        displacements = snapshot.Displacements;
        frozenPoints = snapshot.FrozenPoints;
        linkedGroups = snapshot.LinkedGroups;
    }

    private void AddHistorySnapshot(List<GridSnapshot> history)
    {
        if (history.Count >= MaximumUndoSteps)
            history.RemoveAt(0);
        history.Add(new GridSnapshot((float[])displacements.Clone(), (bool[])frozenPoints.Clone(), (int[])linkedGroups.Clone()));
    }

    private void RecordUndo()
    {
        AddHistorySnapshot(undoStack);
        redoStack.Clear();
    }

    private void Undo()
    {
        if (undoStack.Count == 0) return;
        AddHistorySnapshot(redoStack);
        RestoreSnapshot(undoStack[^1]);
        undoStack.RemoveAt(undoStack.Count - 1);
        SelectGridPoint(selectedGridPointIndex);
        SchedulePreview();
        UpdateButtons();
        PushUpdate();
    }

    private void Redo()
    {
        if (redoStack.Count == 0) return;
        AddHistorySnapshot(undoStack);
        RestoreSnapshot(redoStack[^1]);
        redoStack.RemoveAt(redoStack.Count - 1);
        SelectGridPoint(selectedGridPointIndex);
        SchedulePreview();
        UpdateButtons();
        PushUpdate();
    }

    private void ResetGrid()
    {
        RecordUndo();
        for (int i = 0; i < frozenPoints.Length; i++)
            if (!frozenPoints[i]) displacements[i * 2] = displacements[i * 2 + 1] = 0;
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
        frozenPoints = new bool[count / 2];
        linkedGroups = new int[count / 2];
        selectedPoints.Clear();
        selectedPoints.Add(Math.Clamp(selectedGridPointIndex, 0, count / 2 - 1));
        linkAnchor = -1;
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

    private void ApplyGridDimensions()
    {
        int requestedWidth = gridWidthCombo.SelectedIndex + MinGrid;
        int requestedHeight = gridHeightCombo.SelectedIndex + MinGrid;

        if (requestedWidth == currentGridWidth && requestedHeight == currentGridHeight)
            return;

        currentGridWidth = requestedWidth;
        currentGridHeight = requestedHeight;

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
        suppressGridControlEvents = true;
        try
        {
            gridWidthCombo.SelectedIndex = currentGridWidth - MinGrid;
            gridHeightCombo.SelectedIndex = currentGridHeight - MinGrid;
            qualityCombo.SelectedIndex = Math.Clamp(token.Quality, 1, 7) - 1;
        }
        finally
        {
            suppressGridControlEvents = false;
        }
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
            pointX.Enabled = pointY.Enabled = editMode == PointEditMode.Move && origSurface != null && !frozenPoints[selectedGridPointIndex];
            pointCoordinatesLabel.Text = $"Point row {row + 1}, column {col + 1} (pixels); " +
                $"{selectedPoints.Count} selected; {(frozenPoints[selectedGridPointIndex] ? "frozen" : "unfrozen")}; link {linkedGroups[selectedGridPointIndex]}";
        }
        finally
        {
            syncingPointCoordinates = false;
        }
    }

    private void ApplyPointCoordinates(bool editX)
    {
        if (editMode != PointEditMode.Move || syncingPointCoordinates || origSurface == null || frozenPoints[selectedGridPointIndex]) return;
        int pointsPerRow = currentGridWidth + 1;
        int row = selectedGridPointIndex / pointsPerRow;
        int col = selectedGridPointIndex % pointsPerRow;
        int offset = selectedGridPointIndex * 2;
        float dx = editX ? (float)pointX.Value - col * ((float)origSurface.Width / currentGridWidth) : displacements[offset];
        float dy = editX ? displacements[offset + 1] : (float)pointY.Value - row * ((float)origSurface.Height / currentGridHeight);
        if (dx == displacements[offset] && dy == displacements[offset + 1]) return;
        RecordUndo();
        MovePointGroup(selectedGridPointIndex, dx - displacements[offset], dy - displacements[offset + 1]);
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
        var glyph = icon switch
        {
            "\u21B6" => DistortionPlugins.UiGlyph.Undo,
            "\u21B7" => DistortionPlugins.UiGlyph.Redo,
            "\u21BA" => DistortionPlugins.UiGlyph.Reset,
            _ => DistortionPlugins.UiGlyph.Grid
        };
        return DistortionPlugins.DialogUi.Icon(this, uiToolTip, glyph, tooltip, tooltip);
    }

    private ComboBox AddTopDropdown(FlowLayoutPanel topBar, string caption, int width, string description)
    {
        var group = new FlowLayoutPanel
        {
            AutoSize = true, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, Margin = new Padding(S(4), 0, S(4), 0)
        };
        var nameLabel = new Label
        {
            Text = caption, AutoSize = true, Margin = Padding.Empty
        };
        var combo = new ComboBox
        {
            Width = width, DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = caption, AccessibleDescription = description,
            Margin = Padding.Empty, IntegralHeight = true, MaxDropDownItems = 12
        };
        group.Controls.Add(nameLabel);
        group.Controls.Add(combo);
        topBar.Controls.Add(group);
        DistortionPlugins.DialogUi.AddControlInfo(this, combo, uiToolTip, description);
        return combo;
    }

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
