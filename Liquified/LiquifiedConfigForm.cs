#pragma warning disable CS0612, CS0618 // EffectConfigForm2 base class triggers obsolescence warnings

using PaintDotNet;
using PaintDotNet.Effects;
using PaintDotNet.Imaging;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LiquifiedPlugin;

/// <summary>
/// Interactive liquified dialog with brush painting canvas, zoom/pan, and undo/redo.
/// Integrates with Paint.NET's effect host via <see cref="EffectConfigForm2"/>.
/// </summary>
internal sealed class LiquifiedConfigForm : EffectConfigForm2
{
  // ── UI Controls ──────────────────────────────────────────
  private FlowLayoutPanel modeButtonsPanel = null!;
  private readonly List<Button> modeButtons = new();
  private Label modeDescLabel = null!;
  private TrackBar brushSizeSlider = null!, strengthSlider = null!, densitySlider = null!, rateSlider = null!, reconstructSlider = null!, qualitySlider = null!, meshSizeSlider = null!;
  private Label brushSizeValue = null!, strengthValue = null!, densityValue = null!, rateValue = null!, reconstructValue = null!, qualityValue = null!, meshSizeValue = null!, statusLabel = null!;
  private Button undoBtn = null!, redoBtn = null!, resetBtn = null!;
  private CheckBox showMeshCheck = null!, showMaskCheck = null!, showBackdropCheck = null!;
  private CheckBox penPressureCheck = null!;
  private Panel canvas = null!;
  private LiquifiedMode selectedMode = LiquifiedMode.ForwardWarp;

  // ── Canvas / interaction state ───────────────────────────
  private float zoom = 1f;
  private PointF pan;
  private bool isPanning, isDrawing;
  private Point lastPanPt;
  private PointF lastBrushPt;
  private Point lastCursorPos = Point.Empty;

  // ── Stroke data ──────────────────────────────────────────
  private readonly List<BrushStroke> strokes = new();
  private readonly List<HistoryState> undoStack = new();
  private readonly List<HistoryState> redoStack = new();
  private const int MaximumUndoSteps = 256;
  private sealed record HistoryState(int StrokeCount, bool[] InitialMask, int Reconstruction);
  private readonly List<BrushStroke> historyStrokeArchive = new();
  private bool meshHasUnresolvedNodes;
  private int previousReconstruction;
  private BrushStroke? currentStroke;
  private bool[] freezeMask = Array.Empty<bool>();
  private bool[] freezeMaskBaseline = Array.Empty<bool>();
  private bool[] previewMaskWorkspace = Array.Empty<bool>();
  private int frozenPixelCount;

  // ── CPU preview surfaces ─────────────────────────────────
  private Surface? origSurface;
  private Surface? workSurface;
  private Surface? backdropSurface;
  private byte[] selectionCoverage = Array.Empty<byte>();
  private readonly System.Windows.Forms.Timer previewTimer = new() { Interval = 40 };
  private readonly System.Windows.Forms.Timer rateTimer = new() { Interval = 50 };
  private bool previewPending;
  private bool previewRunning;
  private float[]? displayedMap;
  private string? previewError;
  private float[]? cachedSampleMap;
  private bool[]? cachedInitialMask;
  private List<BrushStroke> cachedHistory = new();
  private List<int> cachedPointCounts = new();
  private CancellationTokenSource? previewCancellation;
  private readonly object previewSurfaceLock = new();
  private int previewGeneration;
  private bool suppressUiEvents;
  private bool pointerCaptureActive;
  private bool penPressureAvailable;
  private float lastPointerPressure = 1f;


  // ── Constants ────────────────────────────────────────────
  private const float MinZoom = 0.1f;
  private const float MaxZoom = 8f;
  private const float ZoomStep = 1.2f;

  // ── Theme colors ─────────────────────────────────────────
  private static readonly Color LabelColor = Color.FromArgb(220, 220, 220);
  private static readonly Color PanelBg = Color.FromArgb(45, 45, 48);
  private static readonly Color ControlBg = Color.FromArgb(60, 60, 65);
  private static readonly Color SelectedModeButtonBg = Color.FromArgb(84, 122, 190);
  private readonly ToolTip uiToolTip = new();

  private readonly struct ModeUiEntry
  {
    public ModeUiEntry(LiquifiedMode mode, string name, string icon)
    {
      Mode = mode;
      Name = name;
      Icon = icon;
    }

    public LiquifiedMode Mode { get; }
    public string Name { get; }
    public string Icon { get; }
  }

  private static readonly ModeUiEntry[] ModeEntries =
  {
    new(LiquifiedMode.ForwardWarp, "Forward Warp", "FW"),
    new(LiquifiedMode.Pucker, "Pucker", "PK"),
    new(LiquifiedMode.Bloat, "Bloat", "BL"),
    new(LiquifiedMode.TwistCW, "Twist CW", "CW"),
    new(LiquifiedMode.TwistCCW, "Twist CCW", "CC"),
    new(LiquifiedMode.PushLeft, "Push Left", "PL"),
    new(LiquifiedMode.Reconstruct, "Reconstruct", "RC"),
    new(LiquifiedMode.Turbulence, "Turbulence", "TB"),
    new(LiquifiedMode.Freeze, "Freeze", "FZ"),
    new(LiquifiedMode.Unfreeze, "Unfreeze", "UF")
  };

  private bool initialFitDone;
  private float _dpi;
  private int S(int v) => (int)(v * _dpi);

  // ═════════════════════════════════════════════════════════
  //  Construction
  // ═════════════════════════════════════════════════════════

  public LiquifiedConfigForm()
  {
    AutoScaleMode = AutoScaleMode.None;
    _dpi = Math.Max(1f, DeviceDpi / 96f);
    BuildUI();
    WireEvents();
  }

  protected override void OnDpiChanged(DpiChangedEventArgs e)
  {
    float oldDpi = _dpi;
    base.OnDpiChanged(e);
    float newDpi = Math.Max(1f, e.DeviceDpiNew / 96f);
    if (Math.Abs(newDpi - oldDpi) > 0.001f)
    {
      Scale(new SizeF(newDpi / oldDpi, newDpi / oldDpi));
      _dpi = newDpi;
      canvas?.Invalidate();
    }
  }

  // ═════════════════════════════════════════════════════════
  //  Token integration
  // ═════════════════════════════════════════════════════════

  protected override EffectConfigToken OnCreateInitialToken()
  {
    return new LiquifiedConfigToken();
  }

  protected override void OnUpdateDialogFromToken(EffectConfigToken effectToken)
  {
    var token = (LiquifiedConfigToken)effectToken;
    LiquifiedDeformation.ValidateBrushSettings(token.Mode, token.BrushSize, token.Strength, token.Density);
    if (token.Quality is < 1 or > 4)
      throw new ArgumentOutOfRangeException(nameof(token.Quality));
    if (token.Rate is < 1 or > 120)
      throw new ArgumentOutOfRangeException(nameof(token.Rate));
    if (!double.IsFinite(token.ReconstructAmount) || token.ReconstructAmount is < 0 or > 1)
      throw new ArgumentOutOfRangeException(nameof(token.ReconstructAmount));

    strokes.Clear();
    if (token.Strokes != null)
    {
      foreach (var s in token.Strokes)
      {
        if (token.FrozenMask is { Length: > 0 } && !token.FrozenMaskIsInitial &&
            s.Mode is LiquifiedMode.Freeze or LiquifiedMode.Unfreeze)
          continue;
        strokes.Add(CloneStroke(s));
      }
    }
    if (token.FrozenMask is { Length: > 0 })
    {
      if (origSurface != null && token.FrozenMask.Length != checked(origSurface.Width * origSurface.Height))
        throw new ArgumentException("Freeze mask dimensions do not match the source image.", nameof(effectToken));
      freezeMaskBaseline = (bool[])token.FrozenMask.Clone();
      if (origSurface != null)
      {
        freezeMask = (bool[])freezeMaskBaseline.Clone();
        frozenPixelCount = CountFrozenPixels(freezeMask);
      }
    }
    else if (origSurface != null)
    {
      int pixelCount = checked(origSurface.Width * origSurface.Height);
      freezeMaskBaseline = new bool[pixelCount];
      freezeMask = new bool[pixelCount];
      frozenPixelCount = 0;
    }
    redoStack.Clear();
    undoStack.Clear();
    historyStrokeArchive.Clear();

    // Guard: controls may not exist yet if called during base constructor
    if (modeButtonsPanel != null)
      SyncUIFromToken(token);

    RegeneratePreview();
  }

  protected override void OnUpdateTokenFromDialog(EffectConfigToken dstToken)
  {
    var token = (LiquifiedConfigToken)dstToken;

    token.Mode = selectedMode;
    token.BrushSize = brushSizeSlider.Value;
    token.Strength = strengthSlider.Value / 100.0;
    token.Density = densitySlider.Value / 100.0;
    token.Rate = rateSlider.Value;
    token.ReconstructAmount = reconstructSlider.Value / 100.0;
    token.Quality = qualitySlider.Value;
    token.FrozenMask = (bool[])freezeMaskBaseline.Clone();
    token.FrozenMaskIsInitial = true;

    token.Strokes = new List<BrushStroke>(strokes.Count);
    foreach (var s in strokes)
      token.Strokes.Add(CloneStroke(s));
  }

  protected override void OnLoaded()
  {
    base.OnLoaded();
    LiquifiedDeformation.ValidateDocumentSize(Environment.Document.Size.Width, Environment.Document.Size.Height);

    using var srcBmp = Environment.GetSourceBitmapBgra32();
    using var compositeBmp = Environment.Document.GetBitmapBgra32();
    using var selectionBmp = Environment.Selection.MaskBitmap;
    var size = srcBmp.Size;
    if (size.Width > 0 && size.Height > 0)
    {
      LiquifiedDeformation.ValidateDocumentSize(size.Width, size.Height);
      origSurface?.Dispose();
      workSurface?.Dispose();
      backdropSurface?.Dispose();
      int pixelCount = checked(size.Width * size.Height);
      origSurface = new Surface(size.Width, size.Height);
      unsafe
      {
        srcBmp.CopyPixels(origSurface.Scan0.VoidStar, origSurface.Stride,
            (uint)origSurface.Scan0.Length, null);
      }
      workSurface = origSurface.Clone();
      backdropSurface = new Surface(size.Width, size.Height);
      unsafe
      {
        compositeBmp.CopyPixels(backdropSurface.Scan0.VoidStar, backdropSurface.Stride,
            (uint)backdropSurface.Scan0.Length, null);
      }
      selectionCoverage = new byte[pixelCount];
      unsafe
      {
        fixed (byte* selection = selectionCoverage)
          selectionBmp.CopyPixels(selection, size.Width, (uint)selectionCoverage.Length, null);
      }
      freezeMask = new bool[pixelCount];
      if (freezeMaskBaseline.Length > 0 && freezeMaskBaseline.Length != pixelCount)
        throw new ArgumentException("Freeze mask dimensions do not match the source image.");
      if (freezeMaskBaseline.Length == pixelCount)
        Array.Copy(freezeMaskBaseline, freezeMask, pixelCount);
      else
        freezeMaskBaseline = new bool[pixelCount];
      previewMaskWorkspace = new bool[pixelCount];
      frozenPixelCount = CountFrozenPixels(freezeMask);
      RegeneratePreview();
      initialFitDone = false;
      BeginInvoke(new Action(FitToWindow));
    }
  }

  // ═════════════════════════════════════════════════════════
  //  UI Construction
  // ═════════════════════════════════════════════════════════

  private void BuildUI()
  {
    SuspendLayout();

    Text = "Liquified \u2013 Left-click: paint, Right-click: pan, Wheel: zoom";
    ClientSize = new Size(S(1000), S(700));
    MinimumSize = new Size(S(600), S(400));
    FormBorderStyle = FormBorderStyle.Sizable;
    MaximizeBox = true;
    StartPosition = FormStartPosition.CenterParent;
    WindowState = FormWindowState.Maximized;

    int panelW = S(300);
    int ctrlW = panelW - S(30);

    // ── Left tool panel (FlowLayout for DPI-safe stacking) ──
    var leftPanel = new FlowLayoutPanel
    {
      Dock = DockStyle.Left,
      Width = panelW,
      FlowDirection = FlowDirection.TopDown,
      WrapContents = false,
      AutoScroll = true,
      Padding = new Padding(S(8), S(6), S(4), S(6)),
      BackColor = SystemInformation.HighContrast ? SystemColors.Control : PanelBg
    };

    leftPanel.Controls.Add(MakeAutoLabel("Mode:"));

    modeButtonsPanel = new FlowLayoutPanel
    {
      Width = ctrlW,
      AutoSize = true,
      AutoSizeMode = AutoSizeMode.GrowAndShrink,
      FlowDirection = FlowDirection.LeftToRight,
      WrapContents = true,
      BackColor = Color.Transparent,
      Margin = new Padding(0, 0, 0, S(4))
    };
    BuildModeButtons();
    leftPanel.Controls.Add(modeButtonsPanel);

    modeDescLabel = new Label
    {
      Text = GetModeDescription((int)selectedMode),
      AutoSize = true,
      MaximumSize = new Size(ctrlW, 0),
      ForeColor = SystemInformation.HighContrast ? SystemColors.ControlText : Color.FromArgb(160, 170, 180),
      Font = new Font(Font.FontFamily, Font.Size * 0.9f, FontStyle.Italic),
      Margin = new Padding(0, 0, 0, S(4))
    };
    leftPanel.Controls.Add(modeDescLabel);

    (brushSizeSlider, brushSizeValue) = AddSlider(leftPanel, "Brush Size:", ctrlW, 5, 1200, 80, v => $"{v} px");
    leftPanel.Controls.Add(MakeHintLabel("Radius of the brush area, in pixels.", ctrlW));
    (strengthSlider, strengthValue) = AddSlider(leftPanel, "Pressure / Strength:", ctrlW, 5, 100, 50, v => $"{v}%");
    strengthSlider.TickFrequency = 5;
    strengthSlider.SmallChange = 5;
    strengthSlider.LargeChange = 10;
    leftPanel.Controls.Add(MakeHintLabel("How far pixels are displaced per brush application.", ctrlW));
    (densitySlider, densityValue) = AddSlider(leftPanel, "Density:", ctrlW, 0, 100, 50, v => $"{v}%");
    leftPanel.Controls.Add(MakeHintLabel("Falloff sharpness. Low = soft edges, high = uniform.", ctrlW));
    (rateSlider, rateValue) = AddSlider(leftPanel, "Rate:", ctrlW, 1, 120, 20, v => $"{v}/s");
    leftPanel.Controls.Add(MakeHintLabel("Repeated applications per second while the pen is held still.", ctrlW));
    (reconstructSlider, reconstructValue) = AddSlider(leftPanel, "Reconstruct Amount:", ctrlW, 0, 100, 0, v => $"{v}%");
    leftPanel.Controls.Add(MakeHintLabel("Blend the complete deformation field back toward the original image.", ctrlW));
    (qualitySlider, qualityValue) = AddSlider(leftPanel, "Quality:", ctrlW, 1, 4, 2, v => $"{v}x");
    leftPanel.Controls.Add(MakeHintLabel("RGSS multisampling level. Higher = smoother but slower.", ctrlW));
    (meshSizeSlider, meshSizeValue) = AddSlider(leftPanel, "Mesh Size:", ctrlW, 8, 256, 32, v => $"{v} px");

    showMeshCheck = MakeCheckBox("Show Mesh");
    showMaskCheck = MakeCheckBox("Show Mask", true);
    showBackdropCheck = MakeCheckBox("Show Backdrop");
    penPressureCheck = MakeCheckBox("Use Pen Pressure", true);
    leftPanel.Controls.Add(showMeshCheck);
    leftPanel.Controls.Add(showMaskCheck);
    leftPanel.Controls.Add(showBackdropCheck);
    leftPanel.Controls.Add(penPressureCheck);
    leftPanel.Controls.Add(MakeHintLabel("Pen pressure requires a Windows pen driver that reports pressure. Mouse/unsupported drivers use full intensity.", ctrlW));
    leftPanel.Controls.Add(MakeHintLabel("Mesh follows the image deformation. Folded or unsolved mesh segments are omitted and reported in status. Undo retains 256 steps.", ctrlW));

    var maskRow = new FlowLayoutPanel
    {
      FlowDirection = FlowDirection.LeftToRight,
      AutoSize = true,
      WrapContents = false,
      BackColor = Color.Transparent,
      Margin = new Padding(0, S(2), 0, S(2))
    };
    var maskNoneBtn = MakeButton("Mask None");
    var maskAllBtn = MakeButton("Mask All");
    var maskInvertBtn = MakeButton("Invert");
    maskNoneBtn.Click += (_, _) => SetMask(LiquifiedMaskOperation.None);
    maskAllBtn.Click += (_, _) => SetMask(LiquifiedMaskOperation.All);
    maskInvertBtn.Click += (_, _) => SetMask(LiquifiedMaskOperation.Invert);
    maskRow.Controls.AddRange(new Control[] { maskNoneBtn, maskAllBtn, maskInvertBtn });
    leftPanel.Controls.Add(maskRow);

    var reconstructionRow = new FlowLayoutPanel
    {
      FlowDirection = FlowDirection.LeftToRight,
      AutoSize = true,
      WrapContents = false,
      BackColor = Color.Transparent,
      Margin = new Padding(0, S(2), 0, S(2))
    };
    var reconstructAllBtn = MakeButton("Reconstruct All");
    var restoreAllBtn = MakeButton("Restore All");
    reconstructAllBtn.Click += (_, _) => { reconstructSlider.Value = reconstructSlider.Maximum; PushUpdate(); RequestPreview(); };
    restoreAllBtn.Click += (_, _) => ResetStrokes();
    reconstructionRow.Controls.AddRange(new Control[] { reconstructAllBtn, restoreAllBtn });
    leftPanel.Controls.Add(reconstructionRow);

    statusLabel = MakeAutoLabel("Ready");
    statusLabel.MaximumSize = new Size(ctrlW, 0);
    leftPanel.Controls.Add(statusLabel);

    var helpLabel = new Label
    {
      Text = "Left-click: paint effect\nRight-click: pan view\n" +
             "Mouse wheel: zoom\nDouble-click: fit to window\n" +
             "Ctrl+Z / Ctrl+Y: undo / redo\n" +
             "[ / ]: brush size\nArrow keys: move brush; Shift+Arrow: faster",
      AutoSize = true,
      MaximumSize = new Size(ctrlW, 0),
      ForeColor = SystemInformation.HighContrast ? SystemColors.ControlText : LabelColor,
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
    undoBtn = MakeIconButton("\u21B6", "Undo last edit (up to 256 retained steps)");
    redoBtn = MakeIconButton("\u21B7", "Redo last undone edit");
    resetBtn = MakeIconButton("\u21BA", "Restore deformation while preserving the freeze mask");
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
    var canvasPanel = new DoubleBufferedPanel
    {
      Dock = DockStyle.Fill,
      BackColor = SystemInformation.HighContrast ? SystemColors.Window : Color.FromArgb(48, 48, 48)
    };
    canvasPanel.SetPenInput(HandlePenPointer);
    canvas = canvasPanel;

    Controls.Add(canvas);
    Controls.Add(leftPanel);

    ResumeLayout(true);
  }

  // ═════════════════════════════════════════════════════════
  //  Event Wiring
  // ═════════════════════════════════════════════════════════

  private void WireEvents()
  {
    uiToolTip.SetToolTip(brushSizeSlider, "Brush radius in pixels");
    uiToolTip.SetToolTip(strengthSlider, "Displacement strength per stroke");
    uiToolTip.SetToolTip(densitySlider, "Brush falloff density");
    uiToolTip.SetToolTip(rateSlider, "Applications per second while the pointer is stationary");
    uiToolTip.SetToolTip(reconstructSlider, "Blend the complete deformation map back toward identity");
    uiToolTip.SetToolTip(qualitySlider, "RGSS quality for final render");
    uiToolTip.SetToolTip(meshSizeSlider, "Spacing of the visual mesh overlay in image pixels");
    uiToolTip.SetToolTip(canvas, "Left-click paint, right-click pan, wheel zoom");

    brushSizeSlider.ValueChanged += (_, _) =>
    {
      brushSizeValue.Text = $"{brushSizeSlider.Value} px";
      canvas?.Invalidate(); // redraw brush cursor at new size
    };
    strengthSlider.ValueChanged += (_, _) => { strengthValue.Text = $"{strengthSlider.Value}%"; };
    densitySlider.ValueChanged += (_, _) => { densityValue.Text = $"{densitySlider.Value}%"; };
    rateSlider.ValueChanged += (_, _) =>
    {
      rateValue.Text = $"{rateSlider.Value}/s";
      rateTimer.Interval = Math.Max(8, 1000 / rateSlider.Value);
      if (!suppressUiEvents) PushUpdate();
    };
    reconstructSlider.ValueChanged += (_, _) =>
    {
      reconstructValue.Text = $"{reconstructSlider.Value}%";
      if (!suppressUiEvents)
      {
        FinishCurrentStroke();
        AddUndoState(CaptureHistory() with { Reconstruction = previousReconstruction });
        redoStack.Clear();
        if (historyStrokeArchive.Count > strokes.Count)
          historyStrokeArchive.RemoveRange(strokes.Count, historyStrokeArchive.Count - strokes.Count);
        previousReconstruction = reconstructSlider.Value;
        UpdateButtons();
        PushUpdate();
        RequestPreview();
      }
    };
    qualitySlider.ValueChanged += (_, _) =>
    {
      qualityValue.Text = $"{qualitySlider.Value}x";
      if (!suppressUiEvents) PushUpdate(); // quality affects GPU render
    };
    meshSizeSlider.ValueChanged += (_, _) =>
    {
      meshSizeValue.Text = $"{meshSizeSlider.Value} px";
      canvas?.Invalidate();
    };
    showMeshCheck.CheckedChanged += (_, _) => canvas?.Invalidate();
    showMaskCheck.CheckedChanged += (_, _) => canvas?.Invalidate();
    showBackdropCheck.CheckedChanged += (_, _) =>
    {
      if (showBackdropCheck.Checked)
      {
        try
        {
          RefreshCompositedBackdrop();
        }
        catch (Exception ex)
        {
          statusLabel.Text = $"Backdrop preview error: {ex.Message}";
        }
      }
      canvas?.Invalidate();
    };

    undoBtn.Click += (_, _) => Undo();
    redoBtn.Click += (_, _) => Redo();
    resetBtn.Click += (_, _) => ResetStrokes();

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

    previewTimer.Tick += (_, _) =>
    {
      previewTimer.Stop();
      if (previewPending && !previewRunning)
      {
        previewPending = false;
        RegeneratePreview();
      }
    };
    rateTimer.Tick += (_, _) => ApplyRateTick();

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
      else if (e.KeyCode is Keys.OemOpenBrackets or Keys.Subtract)
      {
        brushSizeSlider.Value = Math.Max(brushSizeSlider.Minimum, brushSizeSlider.Value - 5);
        e.Handled = true;
      }
      else if (e.KeyCode is Keys.OemCloseBrackets or Keys.Add)
      {
        brushSizeSlider.Value = Math.Min(brushSizeSlider.Maximum, brushSizeSlider.Value + 5);
        e.Handled = true;
      }
      else if (e.KeyCode is Keys.Left or Keys.Right or Keys.Up or Keys.Down)
      {
        NudgeBrush(e.KeyCode, e.Shift ? 10f : 1f);
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

    if (workSurface == null || origSurface == null) return;

    g.InterpolationMode = zoom > 2f
        ? InterpolationMode.NearestNeighbor
        : InterpolationMode.HighQualityBicubic;
    g.PixelOffsetMode = PixelOffsetMode.Half;

    var imgRect = GetImageRect();
    Surface displayedSurface = showBackdropCheck?.Checked == true && backdropSurface != null
      ? backdropSurface
      : workSurface;
    using (var bmp = displayedSurface.CreateAliasedBitmap())
    {
      g.DrawImage(bmp, imgRect);
    }

    // Image border
    using (var pen = new Pen(Color.FromArgb(128, 255, 255, 255), 1) { DashStyle = DashStyle.Dash })
    {
      g.DrawRectangle(pen, imgRect.X, imgRect.Y, imgRect.Width, imgRect.Height);
    }

    if (showMaskCheck?.Checked == true && frozenPixelCount > 0)
      DrawFrozenOverlay(g, imgRect);

    if (showMeshCheck?.Checked == true)
      DrawMesh(g, imgRect);

    // Brush cursor
    var mpt = canvas.PointToClient(MousePosition);
    if (canvas.ClientRectangle.Contains(mpt))
    {
      DrawBrushCursor(g, mpt);
    }
    UpdateStatus(mpt);
  }

  private void DrawFrozenOverlay(Graphics g, RectangleF imageRect)
  {
    if (origSurface == null || freezeMask.Length == 0)
      return;
    Rectangle visible = Rectangle.Intersect(
      Rectangle.Round(imageRect), canvas.ClientRectangle);
    if (visible.IsEmpty)
      return;

    PointF topLeft = ScreenToImage(visible.Location);
    PointF bottomRight = ScreenToImage(new Point(visible.Right, visible.Bottom));
    int left = Math.Clamp((int)MathF.Floor(topLeft.X), 0, origSurface.Width);
    int top = Math.Clamp((int)MathF.Floor(topLeft.Y), 0, origSurface.Height);
    int right = Math.Clamp((int)MathF.Ceiling(bottomRight.X), 0, origSurface.Width);
    int bottom = Math.Clamp((int)MathF.Ceiling(bottomRight.Y), 0, origSurface.Height);
    Color overlay = SystemInformation.HighContrast
      ? SystemColors.Highlight
      : Color.FromArgb(100, 80, 170, 255);
    using var brush = new SolidBrush(overlay);
    for (int y = top; y < bottom; y++)
    {
      int row = y * origSurface.Width;
      for (int x = left; x < right; x++)
      {
        if (!freezeMask[row + x])
          continue;
        g.FillRectangle(brush,
          pan.X + x * zoom, pan.Y + y * zoom,
          Math.Max(1f, zoom), Math.Max(1f, zoom));
      }
    }
  }

  private void DrawMesh(Graphics g, RectangleF imageRect)
  {
    if (origSurface == null || displayedMap == null)
      return;
    int step = meshSizeSlider?.Value ?? 32;
    Color color = SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(90, 230, 230, 230);
    using var pen = new Pen(color, 1f);
    GraphicsState state = g.Save();
    g.SetClip(imageRect, CombineMode.Intersect);
    meshHasUnresolvedNodes = false;
    PointF? Node(int x, int y)
    {
      if (!LiquifiedDeformation.TryGetForwardMeshPoint(displayedMap, origSurface.Width, origSurface.Height,
          x, y, out float outputX, out float outputY))
      {
        meshHasUnresolvedNodes = true;
        return null;
      }
      return new PointF(pan.X + outputX * zoom, pan.Y + outputY * zoom);
    }
    void Segment(PointF? a, PointF? b)
    {
      if (a.HasValue && b.HasValue)
        g.DrawLine(pen, a.Value, b.Value);
    }
    for (int y = 0; y < origSurface.Height; y += step)
    {
      for (int x = 0; x < origSurface.Width - 1; x += step)
        Segment(Node(x, y), Node(Math.Min(x + step, origSurface.Width - 1), y));
    }
    for (int x = 0; x < origSurface.Width; x += step)
      for (int y = 0; y < origSurface.Height - 1; y += step)
        Segment(Node(x, y), Node(x, Math.Min(y + step, origSurface.Height - 1)));
    g.Restore(state);
  }

  private void UpdateStatus(Point cursor)
  {
    if (statusLabel == null || origSurface == null)
      return;
    if (previewError is not null)
    {
      statusLabel.Text = previewError;
      return;
    }
    PointF image = ScreenToImage(cursor);
    statusLabel.Text = $"Mode: {ModeEntries[(int)selectedMode].Name} | " +
      $"Brush: {brushSizeSlider.Value}px | Rate: {rateSlider.Value}/s | " +
      $"Zoom: {zoom:P0} | Frozen: {frozenPixelCount:N0} | " +
      $"Position: {(int)image.X}, {(int)image.Y} | " +
      (pointerCaptureActive && penPressureCheck.Checked && penPressureAvailable ? $"Pen: {lastPointerPressure:P0}" : "Pressure: full (mouse/unsupported driver)") +
      (showMeshCheck.Checked ? (meshHasUnresolvedNodes ? " | Mesh: ambiguous/folded or unsolved segments omitted" : " | Mesh: forward deformation") : string.Empty) +
      $" | Undo: {undoStack.Count}/{MaximumUndoSteps}";
  }

  private void DrawBrushCursor(Graphics g, Point mousePos)
  {
    var imgPt = ScreenToImage(mousePos);
    if (origSurface == null) return;
    if (imgPt.X < 0 || imgPt.Y < 0 || imgPt.X >= origSurface.Width || imgPt.Y >= origSurface.Height) return;

    float r = brushSizeSlider.Value * 0.5f * zoom;

    Color cursorColor = SystemInformation.HighContrast
      ? SystemColors.Highlight
      : selectedMode switch
    {
      LiquifiedMode.Freeze => Color.Orange,
      LiquifiedMode.Unfreeze => Color.Cyan,
      _ => Color.Lime
    };

    using var pen = new Pen(cursorColor, 1.5f);
    if (r > 2f)
    {
      g.DrawEllipse(pen, mousePos.X - r, mousePos.Y - r, r * 2, r * 2);
    }
    if (r > 5f)
    {
      float c = Math.Min(5f, r / 3f);
      g.DrawLine(pen, mousePos.X - c, mousePos.Y, mousePos.X + c, mousePos.Y);
      g.DrawLine(pen, mousePos.X, mousePos.Y - c, mousePos.X, mousePos.Y + c);
    }
    else
    {
      using var dotBrush = new SolidBrush(cursorColor);
      g.FillEllipse(dotBrush, mousePos.X - 1, mousePos.Y - 1, 2, 2);
    }
  }

  // ═════════════════════════════════════════════════════════
  //  Mouse Interaction
  // ═════════════════════════════════════════════════════════

  private void Canvas_MouseDown(object? sender, MouseEventArgs e)
  {
    if (pointerCaptureActive && ReferenceEquals(sender, canvas))
      return;
    if (ReferenceEquals(sender, canvas))
      lastPointerPressure = 1f;
    canvas.Focus();

    if (e.Button == MouseButtons.Left && origSurface != null)
    {
      var ip = ScreenToImage(e.Location);
      if (ip.X >= 0 && ip.Y >= 0 && ip.X < origSurface.Width && ip.Y < origSurface.Height)
      {
        LiquifiedMode mode = selectedMode;
        FinishCurrentStroke();
        RecordUndo();
        isDrawing = true;
        currentStroke = new BrushStroke
        {
          Mode = mode,
          BrushSize = brushSizeSlider.Value,
          Strength = strengthSlider.Value / 100.0,
          Density = densitySlider.Value / 100.0
        };
        currentStroke.Points.Add(new[] { ip.X, ip.Y, 1f, 0f, 0f });
        lastBrushPt = ip;
        if (mode is LiquifiedMode.Freeze or LiquifiedMode.Unfreeze)
          PaintFreezeMaskAtPoint(ip, currentStroke.BrushSize, mode == LiquifiedMode.Freeze);
        rateTimer.Interval = Math.Max(8, 1000 / rateSlider.Value);
        rateTimer.Start();
        RequestPreview();
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
    if (pointerCaptureActive && ReferenceEquals(sender, canvas))
      return;
    if (isDrawing && e.Button == MouseButtons.Left && currentStroke != null && origSurface != null)
    {
      var ip = ScreenToImage(e.Location);
      if (ip.X >= 0 && ip.Y >= 0 && ip.X < origSurface.Width && ip.Y < origSurface.Height)
      {
        float dx = ip.X - lastBrushPt.X;
        float dy = ip.Y - lastBrushPt.Y;
        if (dx * dx + dy * dy >= 4f)
        {
          currentStroke.Points.Add(new[] { ip.X, ip.Y, penPressureCheck.Checked ? lastPointerPressure : 1f, dx, dy });
          if (currentStroke.Mode is LiquifiedMode.Freeze or LiquifiedMode.Unfreeze)
            PaintFreezeMaskAtPoint(ip, currentStroke.BrushSize, currentStroke.Mode == LiquifiedMode.Freeze);
          RequestPreview();

          lastBrushPt = ip;
        }
      }
    }
    else if (isPanning && e.Button == MouseButtons.Right)
    {
      pan = new PointF(
          pan.X + e.X - lastPanPt.X,
          pan.Y + e.Y - lastPanPt.Y);
      lastPanPt = e.Location;
      canvas.Invalidate();
    }
    else
    {
      InvalidateCursorArea(e.Location);
    }
  }

  private void Canvas_MouseUp(object? sender, MouseEventArgs e)
  {
    if (pointerCaptureActive && ReferenceEquals(sender, canvas))
      return;
    if (isDrawing && e.Button == MouseButtons.Left)
    {
      isDrawing = false;
      rateTimer.Stop();
      if (currentStroke != null && currentStroke.Points.Count > 0)
      {
        strokes.Add(currentStroke);
        redoStack.Clear();
        UpdateButtons();
        PushUpdate();
      }
      currentStroke = null;
      RegeneratePreview();
      UpdateStatus(e.Location);
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
    UpdateStatus(e.Location);
  }

  private void InvalidateCursorArea(Point newPos)
  {
    int r = (int)(brushSizeSlider.Value * 0.5f * zoom) + 6;
    if (lastCursorPos != Point.Empty)
    {
      canvas.Invalidate(new Rectangle(lastCursorPos.X - r, lastCursorPos.Y - r, r * 2, r * 2));
    }
    canvas.Invalidate(new Rectangle(newPos.X - r, newPos.Y - r, r * 2, r * 2));
    lastCursorPos = newPos;
  }

  // ═════════════════════════════════════════════════════════
  //  Undo / Redo / Reset
  // ═════════════════════════════════════════════════════════

  private void Undo()
  {
    FinishCurrentStroke();
    if (undoStack.Count == 0) return;
    redoStack.Add(CaptureHistory());
    RestoreHistory(undoStack[^1]);
    undoStack.RemoveAt(undoStack.Count - 1);
    RegeneratePreview();
    UpdateButtons();
    PushUpdate();
  }

  private void Redo()
  {
    FinishCurrentStroke();
    if (redoStack.Count == 0) return;
    AddUndoState(CaptureHistory());
    RestoreHistory(redoStack[^1]);
    redoStack.RemoveAt(redoStack.Count - 1);
    RegeneratePreview();
    UpdateButtons();
    PushUpdate();
  }

  private void ResetStrokes()
  {
    FinishCurrentStroke();
    RecordUndo();
    strokes.Add(new BrushStroke { Action = LiquifiedHistoryAction.RestoreAll });
    suppressUiEvents = true;
    reconstructSlider.Value = reconstructSlider.Minimum;
    previousReconstruction = reconstructSlider.Value;
    suppressUiEvents = false;
    RegeneratePreview();
    UpdateButtons();
    PushUpdate();
  }

  private HistoryState CaptureHistory()
  {
    if (redoStack.Count == 0)
    {
      if (historyStrokeArchive.Count > strokes.Count)
        historyStrokeArchive.RemoveRange(strokes.Count, historyStrokeArchive.Count - strokes.Count);
      for (int i = historyStrokeArchive.Count; i < strokes.Count; i++)
        historyStrokeArchive.Add(strokes[i]);
    }
    return new(strokes.Count, freezeMaskBaseline, reconstructSlider.Value);
  }

  private void AddUndoState(HistoryState state)
  {
    if (undoStack.Count == MaximumUndoSteps)
      undoStack.RemoveAt(0);
    undoStack.Add(state);
  }

  private void RecordUndo()
  {
    AddUndoState(CaptureHistory());
    redoStack.Clear();
    if (historyStrokeArchive.Count > strokes.Count)
      historyStrokeArchive.RemoveRange(strokes.Count, historyStrokeArchive.Count - strokes.Count);
  }

  private void RestoreHistory(HistoryState state)
  {
    strokes.Clear();
    for (int i = 0; i < state.StrokeCount; i++)
      strokes.Add(historyStrokeArchive[i]);
    freezeMaskBaseline = state.InitialMask;
    suppressUiEvents = true;
    reconstructSlider.Value = state.Reconstruction;
    previousReconstruction = state.Reconstruction;
    suppressUiEvents = false;
  }

  private void FinishCurrentStroke()
  {
    if (currentStroke is not null)
    {
      strokes.Add(currentStroke);
      currentStroke = null;
    }
    isDrawing = false;
    rateTimer.Stop();
  }

  // ═════════════════════════════════════════════════════════
  //  CPU Preview Rendering
  // ═════════════════════════════════════════════════════════

  private async void RegeneratePreview()
  {
    if (origSurface == null || workSurface == null) return;
    if (previewRunning)
    {
      previewPending = true;
      return;
    }
    previewRunning = true;
    previewPending = false;

    RebuildFreezeMaskFromStrokes();
    List<BrushStroke> previewStrokes = new(strokes);
    List<BrushStroke> historyIdentities = new(strokes);
    if (currentStroke is not null)
    {
      previewStrokes.Add(CloneStroke(currentStroke));
      historyIdentities.Add(currentStroke);
    }

    bool[] maskSnapshot = freezeMaskBaseline;
    bool[] maskWorkspace = previewMaskWorkspace;
    byte[] selectionSnapshot = selectionCoverage;
    int generation = Interlocked.Increment(ref previewGeneration);
    CancellationTokenSource cancellation = new();
    CancellationTokenSource? previous = Interlocked.Exchange(ref previewCancellation, cancellation);
    previous?.Cancel();

    try
    {
      int width = origSurface.Width;
      int height = origSurface.Height;
      double reconstructAmount = reconstructSlider.Value / 100.0;
      int rate = rateSlider.Value;
      int quality = qualitySlider.Value;
      Surface source = origSurface;
      (ColorBgra[] Pixels, float[] Map) result = await Task.Run(() =>
      {
        lock (previewSurfaceLock)
        {
          CancellationToken token = cancellation.Token;
          bool cacheValid = cachedSampleMap != null &&
            ReferenceEquals(cachedInitialMask, maskSnapshot) &&
            cachedHistory.Count <= historyIdentities.Count;
          if (cacheValid)
          {
            for (int i = 0; i < cachedHistory.Count; i++)
            {
              if (!ReferenceEquals(cachedHistory[i], historyIdentities[i]) ||
                  cachedPointCounts[i] > previewStrokes[i].Points.Count)
              {
                cacheValid = false;
                break;
              }
            }
          }

          List<BrushStroke> pending = new();
          if (cacheValid)
          {
            for (int i = 0; i < previewStrokes.Count; i++)
            {
              BrushStroke stroke = previewStrokes[i];
              if (i >= cachedHistory.Count)
                pending.Add(stroke);
              else if (cachedPointCounts[i] < stroke.Points.Count)
              {
                BrushStroke tail = new()
                {
                  Action = stroke.Action,
                  Mode = stroke.Mode,
                  BrushSize = stroke.BrushSize,
                  Strength = stroke.Strength,
                  Density = stroke.Density,
                  Points = stroke.Points.GetRange(cachedPointCounts[i], stroke.Points.Count - cachedPointCounts[i])
                };
                pending.Add(tail);
              }
            }
          }
          float[] map;
          if (cacheValid && pending.Count == 0)
            map = cachedSampleMap!;
          else
            map = LiquifiedDeformation.BuildSampleMap(
              width, height, cacheValid ? pending : previewStrokes, 0, rate, quality,
              cacheValid ? maskWorkspace : maskSnapshot, token,
              initialSampleMap: cacheValid ? cachedSampleMap : null,
              maskWorkspace: maskWorkspace);
          cachedSampleMap = map;
          cachedInitialMask = maskSnapshot;
          cachedHistory = historyIdentities;
          cachedPointCounts = new List<int>(previewStrokes.Count);
          foreach (BrushStroke stroke in previewStrokes)
            cachedPointCounts.Add(stroke.Points.Count);
          if (reconstructAmount > 0)
          {
            map = (float[])map.Clone();
            LiquifiedDeformation.ApplyGlobalReconstruction(map, maskWorkspace, width, height, (float)reconstructAmount, token);
          }
          ColorBgra[] result = new ColorBgra[checked(width * height)];
          for (int y = 0; y < height; y++)
          {
            token.ThrowIfCancellationRequested();
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
              int mapOffset = (row + x) * 2;
              ColorBgra sampled = SampleBilinear(source, map[mapOffset], map[mapOffset + 1]);
              byte coverage = selectionSnapshot.Length == maskSnapshot.Length
                ? selectionSnapshot[row + x]
                : byte.MaxValue;
              result[row + x] = ColorBgra.Lerp(source[x, y], sampled, coverage / 255f);
            }
          }
          return (result, map);
        }
      }, cancellation.Token);

      if (IsDisposed || Disposing || generation != Volatile.Read(ref previewGeneration))
        return;
      unsafe
      {
        fixed (ColorBgra* sourcePixels = result.Pixels)
        {
          byte* destination = (byte*)workSurface.Scan0.VoidStar;
          int rowBytes = checked(width * sizeof(ColorBgra));
          for (int y = 0; y < height; y++)
            Buffer.MemoryCopy(sourcePixels + y * width, destination + y * workSurface.Stride,
              workSurface.Stride, rowBytes);
        }
        displayedMap = result.Map;
        previewError = null;
      }
      if (showBackdropCheck.Checked)
        RefreshCompositedBackdrop();
    }
    catch (OperationCanceledException)
    {
    }
    catch (Exception ex)
    {
      cachedSampleMap = null;
      cachedHistory.Clear();
      cachedPointCounts.Clear();
      if (!IsDisposed && generation == Volatile.Read(ref previewGeneration))
        previewError = $"Preview error: {ex.Message}";
    }
    finally
    {
      Interlocked.CompareExchange(ref previewCancellation, null, cancellation);
      cancellation.Dispose();
      previewRunning = false;
      if (previewPending && !IsDisposed && !Disposing)
        previewTimer.Start();
    }

    canvas?.Invalidate();
    UpdateStatus(lastCursorPos);
  }

  private void RefreshCompositedBackdrop()
  {
    if (workSurface == null || backdropSurface == null)
      return;
    using var previewSource = workSurface.CreateSharedBitmap();
    using var previewEnvironment = Environment.CloneWithNewSource(previewSource);
    using var compositeBitmap = previewEnvironment.Document.GetBitmapBgra32();
    unsafe
    {
      compositeBitmap.CopyPixels(backdropSurface.Scan0.VoidStar, backdropSurface.Stride,
          (uint)backdropSurface.Scan0.Length, null);
    }
  }

  private void RequestPreview()
  {
    previewPending = true;
    if (!previewTimer.Enabled && !previewRunning)
      previewTimer.Start();
  }

  private void ApplyRateTick()
  {
    if (!isDrawing || currentStroke == null || origSurface == null)
      return;
    if (currentStroke.Mode == LiquifiedMode.ForwardWarp)
      return;
    currentStroke.Points.Add(new[] { lastBrushPt.X, lastBrushPt.Y, penPressureCheck.Checked ? lastPointerPressure : 1f, 0f, 0f });
    if (currentStroke.Mode is LiquifiedMode.Freeze or LiquifiedMode.Unfreeze)
      PaintFreezeMaskAtPoint(lastBrushPt, currentStroke.BrushSize, currentStroke.Mode == LiquifiedMode.Freeze);
    RequestPreview();
  }

  private void NudgeBrush(Keys key, float distance)
  {
    if (ActiveControl is TrackBar)
      return;
    if (key == Keys.Left) distance = -distance;
    else if (key == Keys.Up) distance = -distance;
    else distance = Math.Abs(distance);

    if (Control.ModifierKeys.HasFlag(Keys.Control))
    {
      float dx = key == Keys.Left ? distance : key == Keys.Right ? distance : 0;
      float dy = key == Keys.Up ? distance : key == Keys.Down ? distance : 0;
      pan = new PointF(pan.X - dx * 8, pan.Y - dy * 8);
      canvas.Invalidate();
      return;
    }

    if (lastCursorPos == Point.Empty)
      lastCursorPos = new Point(canvas.ClientSize.Width / 2, canvas.ClientSize.Height / 2);
    Point offset = key switch
    {
      Keys.Left => new Point(-(int)Math.Abs(distance), 0),
      Keys.Right => new Point((int)Math.Abs(distance), 0),
      Keys.Up => new Point(0, -(int)Math.Abs(distance)),
      _ => new Point(0, (int)Math.Abs(distance))
    };
    lastCursorPos = new Point(lastCursorPos.X + offset.X, lastCursorPos.Y + offset.Y);
    canvas.Invalidate();

    if (origSurface == null || !canvas.ClientRectangle.Contains(lastCursorPos))
      return;
    PointF imagePoint = ScreenToImage(lastCursorPos);
    if (imagePoint.X < 0 || imagePoint.Y < 0 ||
        imagePoint.X >= origSurface.Width || imagePoint.Y >= origSurface.Height)
      return;

    var stroke = new BrushStroke
    {
      Mode = selectedMode,
      BrushSize = brushSizeSlider.Value,
      Strength = strengthSlider.Value / 100.0,
      Density = densitySlider.Value / 100.0
    };
    FinishCurrentStroke();
    RecordUndo();
    stroke.Points.Add(new[] { imagePoint.X, imagePoint.Y, 1f, offset.X / zoom, offset.Y / zoom });
    strokes.Add(stroke);
    if (selectedMode is LiquifiedMode.Freeze or LiquifiedMode.Unfreeze)
    {
      PaintFreezeMaskAtPoint(imagePoint, stroke.BrushSize, selectedMode == LiquifiedMode.Freeze);
    }
    redoStack.Clear();
    RegeneratePreview();
    UpdateButtons();
    PushUpdate();
  }

  private void HandlePenPointer(Point location, float pressure, int phase, bool hasPressure)
  {
    penPressureAvailable = hasPressure;
    if (phase == 0)
    {
      lastPointerPressure = pressure;
      Canvas_MouseDown(null, new MouseEventArgs(MouseButtons.Left, 1, location.X, location.Y, 0));
      pointerCaptureActive = true;
      if (currentStroke is { Points.Count: > 0 })
        currentStroke.Points[^1][2] = penPressureCheck.Checked ? pressure : 1f;
      return;
    }

    if (phase == 1)
    {
      lastPointerPressure = pressure;
      if (isDrawing && currentStroke != null)
      {
        int oldCount = currentStroke.Points.Count;
        Canvas_MouseMove(null, new MouseEventArgs(MouseButtons.Left, 0, location.X, location.Y, 0));
        if (currentStroke.Points.Count > oldCount)
        {
          float scale = penPressureCheck.Checked ? pressure : 1f;
          currentStroke.Points[^1][2] = scale;
        }
      }
      return;
    }

    pointerCaptureActive = false;
    if (isDrawing)
      Canvas_MouseUp(null, new MouseEventArgs(MouseButtons.Left, 1, location.X, location.Y, 0));
  }

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

  private void ClearFreezeMask()
  {
    if (origSurface == null)
      return;

    int pixelCount = checked(origSurface.Width * origSurface.Height);
    if (freezeMask.Length != pixelCount)
      freezeMask = new bool[pixelCount];
    else
      Array.Clear(freezeMask, 0, freezeMask.Length);
    freezeMaskBaseline = new bool[pixelCount];

    frozenPixelCount = 0;
    canvas?.Invalidate();
  }

  private void PaintFreezeMaskAtPoint(PointF center, int brushSize, bool freeze)
  {
    if (origSurface == null || freezeMask.Length == 0)
      return;

    float radius = Math.Max(1f, brushSize * 0.5f);
    float r2 = radius * radius;
    int left = Math.Max(0, (int)(center.X - radius));
    int top = Math.Max(0, (int)(center.Y - radius));
    int right = Math.Min(origSurface.Width, (int)(center.X + radius) + 1);
    int bottom = Math.Min(origSurface.Height, (int)(center.Y + radius) + 1);

    for (int py = top; py < bottom; py++)
    {
      float dy = py - center.Y;
      for (int px = left; px < right; px++)
      {
        float dx = px - center.X;
        if ((dx * dx + dy * dy) > r2)
          continue;

        int idx = py * origSurface.Width + px;
        bool old = freezeMask[idx];
        if (old == freeze)
          continue;

        freezeMask[idx] = freeze;
        frozenPixelCount += freeze ? 1 : -1;
      }
    }

    var screenRect = ImageToScreen(new RectangleF(left, top, right - left, bottom - top));
    screenRect.Inflate(2, 2);
    canvas.Invalidate(screenRect);
  }

  private void RebuildFreezeMaskFromStrokes()
  {
    if (origSurface == null)
      return;

    int pixelCount = checked(origSurface.Width * origSurface.Height);
    if (freezeMask.Length != pixelCount)
      freezeMask = new bool[pixelCount];
    if (freezeMaskBaseline.Length != pixelCount)
      freezeMaskBaseline = new bool[pixelCount];
    Array.Copy(freezeMaskBaseline, freezeMask, pixelCount);
    frozenPixelCount = CountFrozenPixels(freezeMask);

    foreach (var stroke in strokes)
    {
      if (stroke.Action != LiquifiedHistoryAction.Brush)
      {
        switch (stroke.Action)
        {
          case LiquifiedHistoryAction.MaskNone:
            Array.Clear(freezeMask);
            break;
          case LiquifiedHistoryAction.MaskAll:
            Array.Fill(freezeMask, true);
            break;
          case LiquifiedHistoryAction.MaskInvert:
            for (int i = 0; i < freezeMask.Length; i++)
              freezeMask[i] = !freezeMask[i];
            break;
        }
        frozenPixelCount = CountFrozenPixels(freezeMask);
        continue;
      }
      if (stroke.Mode != LiquifiedMode.Freeze && stroke.Mode != LiquifiedMode.Unfreeze)
        continue;

      bool freeze = stroke.Mode == LiquifiedMode.Freeze;
      foreach (var pt in stroke.Points)
      {
        if (pt.Length < 2)
          continue;

        PaintFreezeMaskAtPoint(new PointF(pt[0], pt[1]), stroke.BrushSize, freeze);
      }
    }
    if (currentStroke is { Mode: LiquifiedMode.Freeze or LiquifiedMode.Unfreeze })
    {
      bool freeze = currentStroke.Mode == LiquifiedMode.Freeze;
      foreach (float[] point in currentStroke.Points)
        if (point.Length >= 2)
          PaintFreezeMaskAtPoint(new PointF(point[0], point[1]), currentStroke.BrushSize, freeze);
    }
  }

  private static int CountFrozenPixels(bool[] mask)
  {
    int count = 0;
    foreach (bool frozen in mask)
      if (frozen) count++;
    return count;
  }

  private void SetMask(LiquifiedMaskOperation operation)
  {
    if (origSurface == null)
      return;
    FinishCurrentStroke();
    RecordUndo();
    strokes.Add(new BrushStroke
    {
      Action = operation switch
      {
        LiquifiedMaskOperation.None => LiquifiedHistoryAction.MaskNone,
        LiquifiedMaskOperation.All => LiquifiedHistoryAction.MaskAll,
        LiquifiedMaskOperation.Invert => LiquifiedHistoryAction.MaskInvert,
        _ => throw new ArgumentOutOfRangeException(nameof(operation))
      }
    });
    RegeneratePreview();
    UpdateButtons();
    PushUpdate();
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

  private Rectangle ImageToScreen(RectangleF imgRect)
  {
    return new Rectangle(
        (int)(imgRect.X * zoom + pan.X),
        (int)(imgRect.Y * zoom + pan.Y),
        (int)(imgRect.Width * zoom) + 1,
        (int)(imgRect.Height * zoom) + 1);
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

  private void SyncUIFromToken(LiquifiedConfigToken token)
  {
    suppressUiEvents = true;
    try
    {
      SetSelectedMode(Enum.IsDefined(token.Mode) ? token.Mode : LiquifiedMode.ForwardWarp, pushUpdate: false);
      brushSizeSlider.Value = Math.Clamp(token.BrushSize, brushSizeSlider.Minimum, brushSizeSlider.Maximum);
      strengthSlider.Value = Math.Clamp((int)(token.Strength * 100), strengthSlider.Minimum, strengthSlider.Maximum);
      densitySlider.Value = Math.Clamp((int)(token.Density * 100), densitySlider.Minimum, densitySlider.Maximum);
      rateSlider.Value = Math.Clamp(token.Rate, rateSlider.Minimum, rateSlider.Maximum);
      reconstructSlider.Value = Math.Clamp((int)(token.ReconstructAmount * 100), reconstructSlider.Minimum, reconstructSlider.Maximum);
      qualitySlider.Value = Math.Clamp(token.Quality, qualitySlider.Minimum, qualitySlider.Maximum);
      brushSizeValue.Text = $"{brushSizeSlider.Value} px";
      strengthValue.Text = $"{strengthSlider.Value}%";
      densityValue.Text = $"{densitySlider.Value}%";
      rateValue.Text = $"{rateSlider.Value}/s";
      reconstructValue.Text = $"{reconstructSlider.Value}%";
      previousReconstruction = reconstructSlider.Value;
      qualityValue.Text = $"{qualitySlider.Value}x";
      UpdateButtons();
    }
    finally
    {
      suppressUiEvents = false;
    }
  }

  private void UpdateButtons()
  {
    if (undoBtn != null) undoBtn.Enabled = undoStack.Count > 0;
    if (redoBtn != null) redoBtn.Enabled = redoStack.Count > 0;
  }

  // ═════════════════════════════════════════════════════════
  //  Helpers
  // ═════════════════════════════════════════════════════════

  private static BrushStroke CloneStroke(BrushStroke s)
  {
    var clone = new BrushStroke
    {
      Action = s.Action,
      Mode = s.Mode,
      BrushSize = s.BrushSize,
      Strength = s.Strength,
      Density = s.Density,
      Points = new List<float[]>(s.Points.Count)
    };
    foreach (var pt in s.Points)
      clone.Points.Add((float[])pt.Clone());
    return clone;
  }

  private Label MakeAutoLabel(string text)
  {
    return new Label
    {
      Text = text,
      AutoSize = true,
      ForeColor = SystemInformation.HighContrast ? SystemColors.ControlText : LabelColor,
      Margin = new Padding(0, S(2), 0, S(2))
    };
  }

  private Button MakeButton(string text)
  {
    bool highContrast = SystemInformation.HighContrast;
    return new Button
    {
      Text = text,
      AutoSize = true,
      AutoSizeMode = AutoSizeMode.GrowAndShrink,
      MinimumSize = new Size(S(60), S(28)),
      FlatStyle = FlatStyle.Flat,
      BackColor = highContrast ? SystemColors.Control : ControlBg,
      ForeColor = highContrast ? SystemColors.ControlText : LabelColor,
      Padding = new Padding(S(8), S(2), S(8), S(2)),
      Margin = new Padding(0, 0, S(4), 0)
    };
  }

  private CheckBox MakeCheckBox(string text, bool isChecked = false)
  {
    bool highContrast = SystemInformation.HighContrast;
    return new CheckBox
    {
      Text = text,
      Checked = isChecked,
      AutoSize = true,
      ForeColor = highContrast ? SystemColors.ControlText : LabelColor,
      BackColor = highContrast ? SystemColors.Control : Color.Transparent,
      Margin = new Padding(0, S(2), 0, S(2)),
      AccessibleName = text,
      AccessibleRole = AccessibleRole.CheckButton
    };
  }

  private Button MakeIconButton(string icon, string tooltip)
  {
    Button btn = MakeButton(icon);
    btn.MinimumSize = new Size(S(34), S(30));
    btn.Padding = new Padding(S(6), S(2), S(6), S(2));
    btn.AccessibleName = tooltip;
    uiToolTip.SetToolTip(btn, tooltip);
    return btn;
  }

  private void BuildModeButtons()
  {
    modeButtons.Clear();
    modeButtonsPanel.Controls.Clear();

    int btnW = S(38);
    int btnH = S(32);
    int iconSize = S(20);

    foreach (var entry in ModeEntries)
    {
      var button = new Button
      {
        Text = string.Empty,
        Tag = entry.Mode,
        Width = btnW,
        Height = btnH,
        FlatStyle = FlatStyle.Flat,
        BackColor = SystemInformation.HighContrast ? SystemColors.Control : ControlBg,
        ForeColor = SystemInformation.HighContrast ? SystemColors.ControlText : LabelColor,
        Margin = new Padding(0, 0, S(4), S(4)),
        Image = CreateModeIcon(entry.Mode, iconSize,
          SystemInformation.HighContrast ? SystemColors.ControlText : LabelColor),
        ImageAlign = ContentAlignment.MiddleCenter
      };
      button.AccessibleRole = AccessibleRole.PushButton;
      button.AccessibleName = entry.Name;
      button.AccessibleDescription = GetModeDescription((int)entry.Mode);
      button.TabStop = true;
      button.FlatAppearance.BorderColor = Color.FromArgb(90, 90, 95);
      button.FlatAppearance.BorderSize = 1;
      button.Click += ModeButton_Click;
      modeButtons.Add(button);
      modeButtonsPanel.Controls.Add(button);

      string tooltip = entry.Name + System.Environment.NewLine + GetModeDescription((int)entry.Mode);
      uiToolTip.SetToolTip(button, tooltip);
    }

    UpdateModeButtonStyles();
  }

  /// <summary>
  /// Procedurally render a glyph icon for a liquified mode. Avoids shipping image assets.
  /// </summary>
  private static Bitmap CreateModeIcon(LiquifiedMode mode, int size, Color color)
  {
    var bmp = new Bitmap(size, size);
    using var g = Graphics.FromImage(bmp);
    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;

    float pad = size * 0.15f;
    float cx = size / 2f;
    float cy = size / 2f;
    float r = size / 2f - pad;
    float strokeW = Math.Max(1.4f, size / 14f);

    using var pen = new Pen(color, strokeW)
    {
      StartCap = System.Drawing.Drawing2D.LineCap.Round,
      EndCap = System.Drawing.Drawing2D.LineCap.Round,
      LineJoin = System.Drawing.Drawing2D.LineJoin.Round
    };
    using var brush = new SolidBrush(color);

    switch (mode)
    {
      case LiquifiedMode.ForwardWarp:
        {
          // Curved arrow sweeping right.
          float y = cy;
          var path = new System.Drawing.Drawing2D.GraphicsPath();
          path.AddBezier(pad, y + r * 0.4f, cx - r * 0.2f, y - r * 0.6f, cx + r * 0.2f, y + r * 0.4f, size - pad - strokeW, y - r * 0.2f);
          g.DrawPath(pen, path);
          // Arrowhead.
          float hx = size - pad - strokeW;
          float hy = y - r * 0.2f;
          DrawArrowHead(g, brush, hx, hy, 1f, -0.3f, size * 0.18f);
          break;
        }
      case LiquifiedMode.Pucker:
        {
          // 4 arrows pointing inward.
          DrawDirArrow(g, pen, brush, cx, cy, 1, 0, r, size, inward: true);
          DrawDirArrow(g, pen, brush, cx, cy, -1, 0, r, size, inward: true);
          DrawDirArrow(g, pen, brush, cx, cy, 0, 1, r, size, inward: true);
          DrawDirArrow(g, pen, brush, cx, cy, 0, -1, r, size, inward: true);
          // Center dot.
          float d = strokeW * 1.2f;
          g.FillEllipse(brush, cx - d, cy - d, d * 2, d * 2);
          break;
        }
      case LiquifiedMode.Bloat:
        {
          // 4 arrows pointing outward.
          DrawDirArrow(g, pen, brush, cx, cy, 1, 0, r, size, inward: false);
          DrawDirArrow(g, pen, brush, cx, cy, -1, 0, r, size, inward: false);
          DrawDirArrow(g, pen, brush, cx, cy, 0, 1, r, size, inward: false);
          DrawDirArrow(g, pen, brush, cx, cy, 0, -1, r, size, inward: false);
          float d = strokeW * 1.2f;
          g.FillEllipse(brush, cx - d, cy - d, d * 2, d * 2);
          break;
        }
      case LiquifiedMode.TwistCW:
        {
          DrawCircularArrow(g, pen, brush, cx, cy, r * 0.85f, clockwise: true, size);
          break;
        }
      case LiquifiedMode.TwistCCW:
        {
          DrawCircularArrow(g, pen, brush, cx, cy, r * 0.85f, clockwise: false, size);
          break;
        }
      case LiquifiedMode.PushLeft:
        {
          // Two parallel right-pointing arrows offset vertically (perpendicular push hint).
          float ah = size * 0.16f;
          float yTop = cy - r * 0.45f;
          float yBot = cy + r * 0.45f;
          g.DrawLine(pen, pad, yTop, size - pad - ah, yTop);
          DrawArrowHead(g, brush, size - pad - ah, yTop, 1, 0, ah);
          g.DrawLine(pen, pad, yBot, size - pad - ah, yBot);
          DrawArrowHead(g, brush, size - pad - ah, yBot, 1, 0, ah);
          break;
        }
      case LiquifiedMode.Reconstruct:
        {
          // Counter-clockwise restore arrow (3/4 circle with arrowhead pointing down-left).
          var rect = new RectangleF(cx - r * 0.7f, cy - r * 0.7f, r * 1.4f, r * 1.4f);
          g.DrawArc(pen, rect, -45f, -270f);
          // Arrowhead at end of arc (top-right area, pointing tangentially).
          double endAngle = (-45 - 270) * Math.PI / 180.0;
          float ex = cx + (r * 0.7f) * (float)Math.Cos(endAngle);
          float ey = cy + (r * 0.7f) * (float)Math.Sin(endAngle);
          DrawArrowHead(g, brush, ex, ey, (float)-Math.Sin(endAngle), (float)Math.Cos(endAngle), size * 0.18f);
          break;
        }
      case LiquifiedMode.Turbulence:
        {
          // Sine wave.
          var pts = new PointF[24];
          for (int i = 0; i < pts.Length; i++)
          {
            float t = i / (float)(pts.Length - 1);
            float x = pad + t * (size - 2 * pad);
            float y = cy + (float)Math.Sin(t * Math.PI * 2.5) * r * 0.5f;
            pts[i] = new PointF(x, y);
          }
          g.DrawCurve(pen, pts);
          break;
        }
      case LiquifiedMode.Freeze:
        {
          // Snowflake: 3 lines through center + tiny tick marks.
          DrawSnowflake(g, pen, cx, cy, r, strokeW);
          break;
        }
      case LiquifiedMode.Unfreeze:
        {
          DrawSnowflake(g, pen, cx, cy, r * 0.85f, strokeW);
          // Diagonal slash to indicate "remove freeze".
          using var slashPen = new Pen(color, strokeW * 1.2f)
          {
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap = System.Drawing.Drawing2D.LineCap.Round
          };
          g.DrawLine(slashPen, pad, size - pad, size - pad, pad);
          break;
        }
    }
    return bmp;
  }

  private static void DrawArrowHead(Graphics g, Brush brush, float tipX, float tipY, float dx, float dy, float len)
  {
    // dx,dy is the direction the arrow points.
    float mag = MathF.Sqrt(dx * dx + dy * dy);
    if (mag < 1e-4f) return;
    dx /= mag; dy /= mag;
    float px = -dy, py = dx; // perpendicular
    var p1 = new PointF(tipX, tipY);
    var p2 = new PointF(tipX - dx * len + px * len * 0.5f, tipY - dy * len + py * len * 0.5f);
    var p3 = new PointF(tipX - dx * len - px * len * 0.5f, tipY - dy * len - py * len * 0.5f);
    g.FillPolygon(brush, new[] { p1, p2, p3 });
  }

  private static void DrawDirArrow(Graphics g, Pen pen, Brush brush, float cx, float cy, float dx, float dy, float r, int size, bool inward)
  {
    float ah = size * 0.16f;
    float gap = size * 0.18f;
    float startMag = inward ? r : gap;
    float endMag = inward ? gap : r;
    float sx = cx + dx * startMag;
    float sy = cy + dy * startMag;
    float ex = cx + dx * endMag;
    float ey = cy + dy * endMag;
    g.DrawLine(pen, sx, sy, ex, ey);
    DrawArrowHead(g, brush, ex, ey, ex - sx, ey - sy, ah);
  }

  private static void DrawCircularArrow(Graphics g, Pen pen, Brush brush, float cx, float cy, float r, bool clockwise, int size)
  {
    var rect = new RectangleF(cx - r, cy - r, r * 2, r * 2);
    float startAngle = clockwise ? 200f : -20f;
    float sweep = clockwise ? 250f : -250f;
    g.DrawArc(pen, rect, startAngle, sweep);
    double endDeg = (startAngle + sweep) * Math.PI / 180.0;
    float ex = cx + r * (float)Math.Cos(endDeg);
    float ey = cy + r * (float)Math.Sin(endDeg);
    // Tangent direction.
    float tx = (float)-Math.Sin(endDeg) * (clockwise ? 1 : -1);
    float ty = (float)Math.Cos(endDeg) * (clockwise ? 1 : -1);
    DrawArrowHead(g, brush, ex, ey, tx, ty, size * 0.2f);
  }

  private static void DrawSnowflake(Graphics g, Pen pen, float cx, float cy, float r, float strokeW)
  {
    for (int i = 0; i < 3; i++)
    {
      double ang = i * Math.PI / 3.0;
      float dx = (float)Math.Cos(ang) * r;
      float dy = (float)Math.Sin(ang) * r;
      g.DrawLine(pen, cx - dx, cy - dy, cx + dx, cy + dy);
      // Small V tips on each end.
      float tipLen = r * 0.3f;
      float perpX = -dy / r;
      float perpY = dx / r;
      g.DrawLine(pen, cx + dx, cy + dy, cx + dx - dx * 0.3f + perpX * tipLen * 0.6f, cy + dy - dy * 0.3f + perpY * tipLen * 0.6f);
      g.DrawLine(pen, cx + dx, cy + dy, cx + dx - dx * 0.3f - perpX * tipLen * 0.6f, cy + dy - dy * 0.3f - perpY * tipLen * 0.6f);
      g.DrawLine(pen, cx - dx, cy - dy, cx - dx + dx * 0.3f + perpX * tipLen * 0.6f, cy - dy + dy * 0.3f + perpY * tipLen * 0.6f);
      g.DrawLine(pen, cx - dx, cy - dy, cx - dx + dx * 0.3f - perpX * tipLen * 0.6f, cy - dy + dy * 0.3f - perpY * tipLen * 0.6f);
    }
  }

  private void ModeButton_Click(object? sender, EventArgs e)
  {
    if (sender is not Button button || button.Tag is not LiquifiedMode mode)
      return;

    SetSelectedMode(mode, pushUpdate: true);
  }

  private void SetSelectedMode(LiquifiedMode mode, bool pushUpdate)
  {
    selectedMode = mode;
    modeDescLabel.Text = GetModeDescription((int)mode);
    UpdateModeButtonStyles();
    canvas?.Invalidate();

    if (pushUpdate)
      PushUpdate();
  }

  private void UpdateModeButtonStyles()
  {
    for (int i = 0; i < modeButtons.Count; i++)
    {
      Button button = modeButtons[i];
      bool isSelected = button.Tag is LiquifiedMode mode && mode == selectedMode;
      button.BackColor = SystemInformation.HighContrast
        ? (isSelected ? SystemColors.Highlight : SystemColors.Control)
        : (isSelected ? SelectedModeButtonBg : ControlBg);
      button.ForeColor = SystemInformation.HighContrast
        ? (isSelected ? SystemColors.HighlightText : SystemColors.ControlText)
        : LabelColor;
      button.FlatAppearance.BorderColor = isSelected
        ? (SystemInformation.HighContrast ? SystemColors.HighlightText : Color.FromArgb(170, 205, 255))
        : (SystemInformation.HighContrast ? SystemColors.ControlText : Color.FromArgb(90, 90, 95));
      LiquifiedMode buttonMode = button.Tag is LiquifiedMode taggedMode ? taggedMode : selectedMode;
      button.AccessibleDescription = GetModeDescription((int)buttonMode) +
        (isSelected ? " Selected." : string.Empty);
    }
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
      ForeColor = SystemInformation.HighContrast ? SystemColors.ControlText : LabelColor,
      Location = Point.Empty
    };
    row.Controls.Add(nameLabel);
    var valLabel = new Label
    {
      Text = fmt(val),
      AutoSize = false,
      Size = new Size(valW, rowH),
      ForeColor = SystemInformation.HighContrast ? SystemColors.ControlText : LabelColor,
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
      SmallChange = Math.Max(1, (max - min) / 100),
      LargeChange = Math.Max(1, (max - min) / 10),
      BackColor = SystemInformation.HighContrast ? SystemColors.Control : PanelBg,
      Margin = new Padding(0, 0, 0, S(2))
    };
    tb.AccessibleName = label;
    tb.AccessibleDescription = fmt(val);
    parent.Controls.Add(tb);

    return (tb, valLabel);
  }

  private Label MakeHintLabel(string text, int maxW) => new Label
  {
    Text = text,
    AutoSize = true,
    MaximumSize = new Size(maxW, 0),
    ForeColor = SystemInformation.HighContrast ? SystemColors.ControlText : Color.FromArgb(160, 170, 180),
    Font = new Font(Font.FontFamily, Font.Size * 0.9f, FontStyle.Italic),
    Margin = new Padding(0, 0, 0, S(2))
  };

  private static string GetModeDescription(int modeIndex) => modeIndex switch
  {
    0 => "Drag pixels in the direction of the brush stroke.",
    1 => "Pull pixels inward toward the brush center.",
    2 => "Push pixels outward from the brush center.",
    3 => "Rotate pixels clockwise around the brush center.",
    4 => "Rotate pixels counter-clockwise around the brush center.",
    5 => "Shift pixels perpendicular to the drag direction.",
    6 => "Restore displaced pixels back toward their original position.",
    7 => "Add random noise to pixel positions within the brush.",
    8 => "Protect areas from future liquify strokes. Frozen regions show an overlay.",
    9 => "Erase frozen protection so later strokes can affect those pixels again.",
    _ => ""
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
      Interlocked.Increment(ref previewGeneration);
      previewCancellation?.Cancel();
      previewTimer.Stop();
      previewTimer.Dispose();
      rateTimer.Stop();
      rateTimer.Dispose();
      uiToolTip.Dispose();
      foreach (Button button in modeButtons)
      {
        button.Image?.Dispose();
        button.Image = null;
      }
      lock (previewSurfaceLock)
      {
        origSurface?.Dispose();
        workSurface?.Dispose();
        backdropSurface?.Dispose();
      }
    }
    base.OnDispose(disposing);
  }

  // ═════════════════════════════════════════════════════════
  //  Nested Types
  // ═════════════════════════════════════════════════════════

  private sealed class DoubleBufferedPanel : Panel
  {
    private const int WmPointerUpdate = 0x0245;
    private const int WmPointerDown = 0x0246;
    private const int WmPointerUp = 0x0247;
    private const uint PenPointerType = 3;
    private const uint PenMaskPressure = 1;

    private Action<Point, float, int, bool>? penInput;

    public void SetPenInput(Action<Point, float, int, bool> callback) => penInput = callback;

    public DoubleBufferedPanel()
    {
      DoubleBuffered = true;
      ResizeRedraw = true;
      SetStyle(ControlStyles.Selectable, true);
    }

    protected override void WndProc(ref Message m)
    {
      if (m.Msg is WmPointerDown or WmPointerUpdate or WmPointerUp)
      {
        uint pointerId = (uint)(m.WParam.ToInt64() & 0xFFFF);
        if (GetPointerPenInfo(pointerId, out PointerPenInfo info) &&
            info.PointerInfo.PointerType == PenPointerType)
        {
          Point screenPoint = new(info.PointerInfo.PixelLocation.X, info.PointerInfo.PixelLocation.Y);
          Point location = PointToClient(screenPoint);
          float pressure = (info.PenMask & PenMaskPressure) != 0
            ? Math.Clamp(info.Pressure / 1024f, 0f, 1f)
            : 1f;
          int phase = m.Msg == WmPointerDown ? 0 : m.Msg == WmPointerUpdate ? 1 : 2;
          penInput?.Invoke(location, pressure, phase, (info.PenMask & PenMaskPressure) != 0);
          m.Result = IntPtr.Zero;
          return;
        }
      }
      base.WndProc(ref m);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPointerPenInfo(uint pointerId, out PointerPenInfo penInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
      public int X;
      public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PointerInfo
    {
      public uint PointerType;
      public uint PointerId;
      public uint FrameId;
      public uint PointerFlags;
      public IntPtr SourceDevice;
      public IntPtr TargetWindow;
      public NativePoint PixelLocation;
      public NativePoint HimetricLocation;
      public NativePoint PixelLocationRaw;
      public NativePoint HimetricLocationRaw;
      public uint Time;
      public uint HistoryCount;
      public int InputData;
      public uint KeyStates;
      public ulong PerformanceCount;
      public int ButtonChangeType;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PointerPenInfo
    {
      public PointerInfo PointerInfo;
      public uint PenFlags;
      public uint PenMask;
      public uint Pressure;
      public uint Rotation;
      public int TiltX;
      public int TiltY;
    }
  }
}

#pragma warning restore CS0612, CS0618
