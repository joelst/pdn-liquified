#pragma warning disable CS0612, CS0618 // EffectConfigForm2 base class triggers obsolescence warnings

using PaintDotNet;
using PaintDotNet.Effects;
using PaintDotNet.Imaging;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LiquifyPlugin;

/// <summary>
/// Interactive liquify dialog with brush painting canvas, zoom/pan, and undo/redo.
/// Integrates with Paint.NET's effect host via <see cref="EffectConfigForm2"/>.
/// </summary>
internal sealed class LiquifyConfigForm : EffectConfigForm2
{
  // ── UI Controls ──────────────────────────────────────────
  private ComboBox modeCombo = null!;
  private Label modeDescLabel = null!;
  private TrackBar brushSizeSlider = null!, strengthSlider = null!, densitySlider = null!, qualitySlider = null!;
  private Label brushSizeValue = null!, strengthValue = null!, densityValue = null!, qualityValue = null!;
  private Button undoBtn = null!, redoBtn = null!, resetBtn = null!;
  private Panel canvas = null!;

  // ── Canvas / interaction state ───────────────────────────
  private float zoom = 1f;
  private PointF pan;
  private bool isPanning, isDrawing;
  private Point lastPanPt;
  private PointF lastBrushPt;
  private Point lastCursorPos = Point.Empty;

  // ── Stroke data ──────────────────────────────────────────
  private readonly List<BrushStroke> strokes = new();
  private readonly List<BrushStroke> redoStack = new();
  private BrushStroke? currentStroke;

  // ── CPU preview surfaces ─────────────────────────────────
  private Surface? origSurface;
  private Surface? workSurface;
  private Surface? snapSurface;

  // ── Constants ────────────────────────────────────────────
  private const float MinZoom = 0.1f;
  private const float MaxZoom = 8f;
  private const float ZoomStep = 1.2f;

  // ── Theme colors ─────────────────────────────────────────
  private static readonly Color LabelColor = Color.FromArgb(220, 220, 220);
  private static readonly Color PanelBg = Color.FromArgb(45, 45, 48);
  private static readonly Color ControlBg = Color.FromArgb(60, 60, 65);

  private bool initialFitDone;
  private readonly float _dpi;
  private int S(int v) => (int)(v * _dpi);

  // ═════════════════════════════════════════════════════════
  //  Construction
  // ═════════════════════════════════════════════════════════

  public LiquifyConfigForm()
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
    return new LiquifyConfigToken();
  }

  protected override void OnUpdateDialogFromToken(EffectConfigToken effectToken)
  {
    var token = (LiquifyConfigToken)effectToken;

    strokes.Clear();
    if (token.Strokes != null)
    {
      foreach (var s in token.Strokes)
        strokes.Add(CloneStroke(s));
    }
    redoStack.Clear();

    // Guard: controls may not exist yet if called during base constructor
    if (modeCombo != null)
      SyncUIFromToken(token);

    RegeneratePreview();
  }

  protected override void OnUpdateTokenFromDialog(EffectConfigToken dstToken)
  {
    var token = (LiquifyConfigToken)dstToken;

    token.Mode = (LiquifyMode)modeCombo.SelectedIndex;
    token.BrushSize = brushSizeSlider.Value;
    token.Strength = strengthSlider.Value / 100.0;
    token.Density = densitySlider.Value / 100.0;
    token.Quality = qualitySlider.Value;

    token.Strokes = new List<BrushStroke>(strokes.Count);
    foreach (var s in strokes)
      token.Strokes.Add(CloneStroke(s));
  }

  protected override void OnLoaded()
  {
    base.OnLoaded();

    // Each dialog session starts with a blank slate
    strokes.Clear();
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
        RegeneratePreview();
        // Defer FitToWindow until after layout is complete so canvas has its real size
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

    Text = "Liquify5 \u2013 Left-click: paint, Right-click: pan, Wheel: zoom";
    ClientSize = new Size(S(1000), S(700));
    MinimumSize = new Size(S(600), S(400));
    FormBorderStyle = FormBorderStyle.Sizable;
    MaximizeBox = true;
    StartPosition = FormStartPosition.CenterParent;
    WindowState = FormWindowState.Maximized;

    int panelW = S(270);
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
      BackColor = PanelBg
    };

    leftPanel.Controls.Add(MakeAutoLabel("Mode:"));

    modeCombo = new ComboBox
    {
      Width = ctrlW,
      DropDownStyle = ComboBoxStyle.DropDownList,
      FlatStyle = FlatStyle.Flat,
      BackColor = ControlBg,
      ForeColor = LabelColor,
      Margin = new Padding(0, 0, 0, S(4))
    };
    modeCombo.Items.AddRange(new object[]
    {
            "Forward Warp", "Pucker", "Bloat", "Twist CW",
            "Twist CCW", "Push Left", "Reconstruct", "Turbulence"
    });
    modeCombo.SelectedIndex = 0;
    leftPanel.Controls.Add(modeCombo);

    modeDescLabel = new Label
    {
      Text = GetModeDescription(0),
      AutoSize = true,
      MaximumSize = new Size(ctrlW, 0),
      ForeColor = Color.FromArgb(160, 170, 180),
      Font = new Font(Font.FontFamily, Font.Size * 0.9f, FontStyle.Italic),
      Margin = new Padding(0, 0, 0, S(4))
    };
    leftPanel.Controls.Add(modeDescLabel);

    (brushSizeSlider, brushSizeValue) = AddSlider(leftPanel, "Brush Size:", ctrlW, 5, 1200, 80, v => $"{v} px");
    leftPanel.Controls.Add(MakeHintLabel("Radius of the brush area, in pixels.", ctrlW));
    (strengthSlider, strengthValue) = AddSlider(leftPanel, "Strength:", ctrlW, 5, 100, 50, v => $"{v}%");
    strengthSlider.TickFrequency = 5;
    strengthSlider.SmallChange = 5;
    strengthSlider.LargeChange = 10;
    leftPanel.Controls.Add(MakeHintLabel("How far pixels are displaced per brush application.", ctrlW));
    (densitySlider, densityValue) = AddSlider(leftPanel, "Density:", ctrlW, 0, 100, 50, v => $"{v}%");
    leftPanel.Controls.Add(MakeHintLabel("Falloff sharpness. Low = soft edges, high = uniform.", ctrlW));
    (qualitySlider, qualityValue) = AddSlider(leftPanel, "Quality:", ctrlW, 1, 4, 2, v => $"{v}x");
    leftPanel.Controls.Add(MakeHintLabel("RGSS multisampling level. Higher = smoother but slower.", ctrlW));

    var helpLabel = new Label
    {
      Text = "Left-click: paint effect\nRight-click: pan view\n" +
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
    undoBtn = MakeButton("Undo");
    redoBtn = MakeButton("Redo");
    resetBtn = MakeButton("Reset");
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
    modeCombo.SelectedIndexChanged += (_, _) =>
    {
      modeDescLabel.Text = GetModeDescription(modeCombo.SelectedIndex);
      PushUpdate();
    };

    brushSizeSlider.ValueChanged += (_, _) =>
    {
      brushSizeValue.Text = $"{brushSizeSlider.Value} px";
      canvas?.Invalidate(); // redraw brush cursor at new size
    };
    strengthSlider.ValueChanged += (_, _) => { strengthValue.Text = $"{strengthSlider.Value}%"; };
    densitySlider.ValueChanged += (_, _) => { densityValue.Text = $"{densitySlider.Value}%"; };
    qualitySlider.ValueChanged += (_, _) =>
    {
      qualityValue.Text = $"{qualitySlider.Value}x";
      PushUpdate(); // quality affects GPU render
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

    // Brush cursor
    var mpt = canvas.PointToClient(MousePosition);
    if (canvas.ClientRectangle.Contains(mpt))
    {
      DrawBrushCursor(g, mpt);
    }
  }

  private void DrawBrushCursor(Graphics g, Point mousePos)
  {
    var imgPt = ScreenToImage(mousePos);
    if (origSurface == null) return;
    if (imgPt.X < 0 || imgPt.Y < 0 || imgPt.X >= origSurface.Width || imgPt.Y >= origSurface.Height) return;

    float r = brushSizeSlider.Value * 0.5f * zoom;

    using var pen = new Pen(Color.Lime, 1.5f);
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
      g.FillEllipse(Brushes.Lime, mousePos.X - 1, mousePos.Y - 1, 2, 2);
    }
  }

  // ═════════════════════════════════════════════════════════
  //  Mouse Interaction
  // ═════════════════════════════════════════════════════════

  private void Canvas_MouseDown(object? sender, MouseEventArgs e)
  {
    canvas.Focus();

    if (e.Button == MouseButtons.Left && origSurface != null)
    {
      var ip = ScreenToImage(e.Location);
      if (ip.X >= 0 && ip.Y >= 0 && ip.X < origSurface.Width && ip.Y < origSurface.Height)
      {
        isDrawing = true;
        currentStroke = new BrushStroke
        {
          Mode = (LiquifyMode)modeCombo.SelectedIndex,
          BrushSize = brushSizeSlider.Value,
          Strength = strengthSlider.Value / 100.0,
          Density = densitySlider.Value / 100.0
        };
        snapSurface?.Dispose();
        snapSurface = workSurface!.Clone();
        currentStroke.Points.Add(new[] { ip.X, ip.Y });
        lastBrushPt = ip;
        ApplyBrushAt(ip, currentStroke);
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
    if (isDrawing && e.Button == MouseButtons.Left && currentStroke != null && origSurface != null)
    {
      var ip = ScreenToImage(e.Location);
      if (ip.X >= 0 && ip.Y >= 0 && ip.X < origSurface.Width && ip.Y < origSurface.Height)
      {
        float dx = ip.X - lastBrushPt.X;
        float dy = ip.Y - lastBrushPt.Y;
        if (dx * dx + dy * dy >= 4f)
        {
          currentStroke.Points.Add(new[] { ip.X, ip.Y });
          ApplyBrushAt(ip, currentStroke);
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
    if (isDrawing && e.Button == MouseButtons.Left)
    {
      isDrawing = false;
      if (currentStroke != null && currentStroke.Points.Count > 0)
      {
        strokes.Add(currentStroke);
        redoStack.Clear();
        UpdateButtons();
        PushUpdate();
      }
      currentStroke = null;
      snapSurface?.Dispose();
      snapSurface = null;
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
    if (strokes.Count == 0) return;
    redoStack.Add(strokes[^1]);
    strokes.RemoveAt(strokes.Count - 1);
    RegeneratePreview();
    UpdateButtons();
    PushUpdate();
  }

  private void Redo()
  {
    if (redoStack.Count == 0) return;
    strokes.Add(redoStack[^1]);
    redoStack.RemoveAt(redoStack.Count - 1);
    RegeneratePreview();
    UpdateButtons();
    PushUpdate();
  }

  private void ResetStrokes()
  {
    strokes.Clear();
    redoStack.Clear();
    RegeneratePreview();
    UpdateButtons();
    PushUpdate();
  }

  // ═════════════════════════════════════════════════════════
  //  CPU Preview Rendering
  // ═════════════════════════════════════════════════════════

  private void ApplyBrushAt(PointF center, BrushStroke stroke)
  {
    if (workSurface == null) return;

    float radius = Math.Max(1f, stroke.BrushSize * 0.5f);
    float r2 = radius * radius;
    float invR = 1f / radius;
    float baseStr = (float)stroke.Strength;
    float baseDensity = (float)stroke.Density;
    var src = snapSurface ?? workSurface;

    int left = Math.Max(0, (int)(center.X - radius));
    int top = Math.Max(0, (int)(center.Y - radius));
    int right = Math.Min(workSurface.Width, (int)(center.X + radius) + 1);
    int bottom = Math.Min(workSurface.Height, (int)(center.Y + radius) + 1);

    for (int py = top; py < bottom; py++)
    {
      float dy = py - center.Y;
      for (int px = left; px < right; px++)
      {
        float dx = px - center.X;
        float d2 = dx * dx + dy * dy;
        if (d2 > r2 || d2 < 0.0001f) continue;

        float dist = MathF.Sqrt(d2);
        float t = 1f - dist * invR;
        float falloff = t * t * (3f - 2f * t);
        falloff = MathF.Pow(MathF.Abs(falloff), 2f - baseDensity);
        float eff = baseStr * falloff;

        var srcPos = CalcSourcePos(px, py, center, stroke.Mode, eff, dist, dx, dy);
        var sampled = SampleBilinear(src, srcPos.X, srcPos.Y);
        workSurface[px, py] = sampled;
      }
    }

    // Invalidate the affected screen region
    var screenRect = ImageToScreen(new RectangleF(left, top, right - left, bottom - top));
    screenRect.Inflate(2, 2);
    canvas.Invalidate(screenRect);
  }

  private void RegeneratePreview()
  {
    if (origSurface == null || workSurface == null) return;

    CopySurface(origSurface, workSurface);

    // Flatten all stroke points with precomputed parameters
    var allPoints = new List<(float cx, float cy, float radius, float strength, float density, LiquifyMode mode)>();
    float globalLeft = float.MaxValue, globalTop = float.MaxValue;
    float globalRight = float.MinValue, globalBottom = float.MinValue;

    foreach (var stroke in strokes)
    {
      float radius = Math.Max(1f, stroke.BrushSize * 0.5f);
      foreach (var pt in stroke.Points)
      {
        if (pt.Length < 2) continue;
        allPoints.Add((pt[0], pt[1], radius, (float)stroke.Strength, (float)stroke.Density, stroke.Mode));
        globalLeft = Math.Min(globalLeft, pt[0] - radius);
        globalTop = Math.Min(globalTop, pt[1] - radius);
        globalRight = Math.Max(globalRight, pt[0] + radius);
        globalBottom = Math.Max(globalBottom, pt[1] + radius);
      }
    }

    if (allPoints.Count == 0)
    {
      canvas?.Invalidate();
      return;
    }

    // Per-pixel displacement accumulation (matches GPU SampleMapShader)
    int w = workSurface.Width, h = workSurface.Height;
    int left = Math.Max(0, (int)globalLeft);
    int top = Math.Max(0, (int)globalTop);
    int right = Math.Min(w, (int)globalRight + 1);
    int bottom = Math.Min(h, (int)globalBottom + 1);

    for (int py = top; py < bottom; py++)
    {
      for (int px = left; px < right; px++)
      {
        float posX = px;
        float posY = py;
        float origX = px;
        float origY = py;

        foreach (var sp in allPoints)
        {
          float r2 = sp.radius * sp.radius;
          float dx = posX - sp.cx;
          float dy = posY - sp.cy;
          float d2 = dx * dx + dy * dy;
          if (d2 > r2 || d2 < 1e-6f) continue;

          float dist = MathF.Sqrt(d2);
          float invR = 1f / sp.radius;
          float t = 1f - dist * invR;
          float falloff = t * t * (3f - 2f * t);
          falloff = MathF.Pow(MathF.Abs(falloff), 2f - sp.density);
          float eff = sp.strength * falloff;
          float invDist = 1f / dist;

          switch (sp.mode)
          {
            case LiquifyMode.ForwardWarp:
              posX -= dx * invDist * eff * 20f;
              posY -= dy * invDist * eff * 20f;
              break;
            case LiquifyMode.Pucker:
              posX += dx * invDist * eff * 15f;
              posY += dy * invDist * eff * 15f;
              break;
            case LiquifyMode.Bloat:
              posX -= dx * invDist * eff * 25f;
              posY -= dy * invDist * eff * 25f;
              break;
            case LiquifyMode.TwistCW:
              {
                float a = eff * 0.5f;
                float cos = MathF.Cos(a), sin = MathF.Sin(a);
                posX = sp.cx + dx * cos - dy * sin;
                posY = sp.cy + dx * sin + dy * cos;
                break;
              }
            case LiquifyMode.TwistCCW:
              {
                float a = -eff * 0.5f;
                float cos = MathF.Cos(a), sin = MathF.Sin(a);
                posX = sp.cx + dx * cos - dy * sin;
                posY = sp.cy + dx * sin + dy * cos;
                break;
              }
            case LiquifyMode.PushLeft:
              posX += dy * invDist * eff * 20f;
              posY -= dx * invDist * eff * 20f;
              break;
            case LiquifyMode.Reconstruct:
              posX += (origX - posX) * eff;
              posY += (origY - posY) * eff;
              break;
            case LiquifyMode.Turbulence:
              {
                float hash1 = MathF.Abs(MathF.Sin(posX * 12.9898f + posY * 78.233f) * 43758.5453f) % 1f;
                float hash2 = MathF.Abs(MathF.Sin(posX * 39.346f + posY * 11.135f) * 43758.5453f) % 1f;
                posX += (hash1 - 0.5f) * eff * 15f;
                posY += (hash2 - 0.5f) * eff * 15f;
                break;
              }
          }
        }

        workSurface[px, py] = SampleBilinear(origSurface, posX, posY);
      }
    }

    canvas?.Invalidate();
  }

  private void ApplyBrushStrokePoint(PointF center, BrushStroke stroke)
  {
    if (workSurface == null || origSurface == null) return;

    float radius = Math.Max(1f, stroke.BrushSize * 0.5f);
    float r2 = radius * radius;
    float invR = 1f / radius;
    float baseStr = (float)stroke.Strength;
    float baseDensity = (float)stroke.Density;

    int left = Math.Max(0, (int)(center.X - radius));
    int top = Math.Max(0, (int)(center.Y - radius));
    int right = Math.Min(workSurface.Width, (int)(center.X + radius) + 1);
    int bottom = Math.Min(workSurface.Height, (int)(center.Y + radius) + 1);

    for (int py = top; py < bottom; py++)
    {
      float dy = py - center.Y;
      for (int px = left; px < right; px++)
      {
        float dx = px - center.X;
        float d2 = dx * dx + dy * dy;
        if (d2 > r2 || d2 < 0.0001f) continue;

        float dist = MathF.Sqrt(d2);
        float t = 1f - dist * invR;
        float falloff = t * t * (3f - 2f * t);
        falloff = MathF.Pow(MathF.Abs(falloff), 2f - baseDensity);
        float eff = baseStr * falloff;

        var srcPos = CalcSourcePos(px, py, center, stroke.Mode, eff, dist, dx, dy);
        var sampled = SampleBilinear(origSurface, srcPos.X, srcPos.Y);
        float blend = Math.Min(0.8f, eff);
        workSurface[px, py] = ColorBgra.Lerp(workSurface[px, py], sampled, blend);
      }
    }
  }

  private static PointF CalcSourcePos(float x, float y, PointF center, LiquifyMode mode,
      float eff, float dist, float dx, float dy)
  {
    float invDist = 1f / dist;
    float sx = x, sy = y;

    switch (mode)
    {
      case LiquifyMode.ForwardWarp:
        sx -= dx * invDist * eff * 20f;
        sy -= dy * invDist * eff * 20f;
        break;
      case LiquifyMode.Pucker:
        sx += dx * invDist * eff * 15f;
        sy += dy * invDist * eff * 15f;
        break;
      case LiquifyMode.Bloat:
        sx -= dx * invDist * eff * 25f;
        sy -= dy * invDist * eff * 25f;
        break;
      case LiquifyMode.TwistCW:
        {
          float a = eff * 0.5f;
          float cos = MathF.Cos(a), sin = MathF.Sin(a);
          sx = center.X + dx * cos - dy * sin;
          sy = center.Y + dx * sin + dy * cos;
          break;
        }
      case LiquifyMode.TwistCCW:
        {
          float a = -eff * 0.5f;
          float cos = MathF.Cos(a), sin = MathF.Sin(a);
          sx = center.X + dx * cos - dy * sin;
          sy = center.Y + dx * sin + dy * cos;
          break;
        }
      case LiquifyMode.PushLeft:
        sx += dy * invDist * eff * 20f;
        sy -= dx * invDist * eff * 20f;
        break;
      case LiquifyMode.Reconstruct:
        break;
      case LiquifyMode.Turbulence:
        {
          float hash1 = MathF.Abs(MathF.Sin(dx * 12.9898f + dy * 78.233f) * 43758.5453f) % 1f;
          float hash2 = MathF.Abs(MathF.Sin(dx * 39.346f + dy * 11.135f) * 43758.5453f) % 1f;
          sx += (hash1 - 0.5f) * eff * 15f;
          sy += (hash2 - 0.5f) * eff * 15f;
          break;
        }
    }

    return new PointF(sx, sy);
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

  private static void CopySurface(Surface src, Surface dst)
  {
    for (int y = 0; y < src.Height && y < dst.Height; y++)
    {
      for (int x = 0; x < src.Width && x < dst.Width; x++)
      {
        dst[x, y] = src[x, y];
      }
    }
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

  private void SyncUIFromToken(LiquifyConfigToken token)
  {
    modeCombo.SelectedIndex = Math.Clamp((int)token.Mode, 0, modeCombo.Items.Count - 1);
    brushSizeSlider.Value = Math.Clamp(token.BrushSize, brushSizeSlider.Minimum, brushSizeSlider.Maximum);
    strengthSlider.Value = Math.Clamp((int)(token.Strength * 100), strengthSlider.Minimum, strengthSlider.Maximum);
    densitySlider.Value = Math.Clamp((int)(token.Density * 100), densitySlider.Minimum, densitySlider.Maximum);
    qualitySlider.Value = Math.Clamp(token.Quality, qualitySlider.Minimum, qualitySlider.Maximum);
    brushSizeValue.Text = $"{brushSizeSlider.Value} px";
    strengthValue.Text = $"{strengthSlider.Value}%";
    densityValue.Text = $"{densitySlider.Value}%";
    qualityValue.Text = $"{qualitySlider.Value}x";
    UpdateButtons();
  }

  private void UpdateButtons()
  {
    if (undoBtn != null) undoBtn.Enabled = strokes.Count > 0;
    if (redoBtn != null) redoBtn.Enabled = redoStack.Count > 0;
  }

  // ═════════════════════════════════════════════════════════
  //  Helpers
  // ═════════════════════════════════════════════════════════

  private static BrushStroke CloneStroke(BrushStroke s)
  {
    var clone = new BrushStroke
    {
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
      ForeColor = LabelColor,
      Margin = new Padding(0, S(2), 0, S(2))
    };
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
      SmallChange = Math.Max(1, (max - min) / 100),
      LargeChange = Math.Max(1, (max - min) / 10),
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
      origSurface?.Dispose();
      workSurface?.Dispose();
      snapSurface?.Dispose();
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
