#pragma warning disable CS0612, CS0618 // EffectConfigForm2 base class triggers obsolescence warnings

using PaintDotNet;
using PaintDotNet.Effects;
using PaintDotNet.Imaging;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DocumentRectifyPlugin;

/// <summary>
/// Interactive document-rectify dialog with draggable corner handles.
/// </summary>
internal sealed class DocumentRectifyConfigForm : EffectConfigForm2
{
  private TrackBar qualitySlider = null!;
  private Label qualityValue = null!;
  private Button resetCornersBtn = null!;
  private Button autoDetectBtn = null!;
  private ComboBox aspectCombo = null!;
  private NumericUpDown customAspectW = null!;
  private NumericUpDown customAspectH = null!;
  private Label customAspectLabel = null!;
  private Panel canvas = null!;
  private NumericUpDown cornerX = null!, cornerY = null!;
  private Label cornerCoordinatesLabel = null!;
  private bool syncingCornerCoordinates;

  private Surface? origSurface;
  private float zoom = 1f;
  private PointF pan;
  private bool isPanning;
  private Point lastPanPt;

  // Corner order: TL, TR, BR, BL (normalized [0..1])
  private readonly PointF[] corners =
  {
        new(0.10f, 0.10f),
        new(0.90f, 0.10f),
        new(0.90f, 0.90f),
        new(0.10f, 0.90f)
    };

  private int dragCornerIndex = -1;
  private int selectedCornerIndex;

  private const float MinZoom = 0.1f;
  private const float MaxZoom = 8f;
  private const float ZoomStep = 1.2f;

  private static readonly Color LabelColor = Color.FromArgb(220, 220, 220);
  private static readonly Color PanelBg = Color.FromArgb(45, 45, 48);
  private static readonly Color ControlBg = Color.FromArgb(60, 60, 65);
  private readonly ToolTip uiToolTip = new();
  private readonly System.Windows.Forms.Timer previewUpdateTimer = new() { Interval = 33 };

  private bool initialFitDone;
  private float _dpi;
  private int S(int v) => (int)(v * _dpi);

  public DocumentRectifyConfigForm()
  {
    AutoScaleMode = AutoScaleMode.None;
    _dpi = Math.Max(1f, DeviceDpi / 96f);
    BuildUI();
    WireEvents();
    previewUpdateTimer.Tick += (_, _) => PushUpdate();
    DpiChanged += (_, e) => RescaleForDpi(e.DeviceDpiOld, e.DeviceDpiNew);
    SystemColorsChanged += (_, _) => ApplySystemTheme(this);
    ApplySystemTheme(this);
  }

  protected override EffectConfigToken OnCreateInitialToken()
  {
    return new DocumentRectifyConfigToken();
  }

  protected override void OnUpdateDialogFromToken(EffectConfigToken effectToken)
  {
    var token = (DocumentRectifyConfigToken)effectToken;
    qualitySlider.Value = Math.Clamp(token.Quality, qualitySlider.Minimum, qualitySlider.Maximum);
    qualityValue.Text = $"{qualitySlider.Value}x";

    if (token.Corners != null && token.Corners.Length >= 8)
    {
      for (int i = 0; i < corners.Length; i++)
      {
        corners[i] = new PointF(NormalizeCoordinate(token.Corners[i * 2], corners[i].X),
            NormalizeCoordinate(token.Corners[i * 2 + 1], corners[i].Y));
      }
    }

    aspectCombo.SelectedIndex = Math.Clamp((int)token.AspectMode,
        (int)DocumentRectifyAspectMode.FillCanvas, (int)DocumentRectifyAspectMode.Custom);

    float ratio = float.IsFinite(token.CustomAspect) && token.CustomAspect > 0f ? token.CustomAspect : 1f;
    // Represent ratio as W/H using width=ratio*100, height=100 if input doesn't have separate values.
    decimal w = (decimal)(Math.Clamp(ratio, 0.01f, 99.99f) * 100f);
    decimal h = 100m;
    customAspectW.Value = w;
    customAspectH.Value = h;

    UpdateCustomAspectVisibility();
    SelectCorner(selectedCornerIndex);
    canvas?.Invalidate();
  }

  protected override void OnUpdateTokenFromDialog(EffectConfigToken dstToken)
  {
    var token = (DocumentRectifyConfigToken)dstToken;

    token.Quality = qualitySlider.Value;
    token.Corners = new[]
    {
            corners[0].X, corners[0].Y,
            corners[1].X, corners[1].Y,
            corners[2].X, corners[2].Y,
            corners[3].X, corners[3].Y
        };
    token.AspectMode = (DocumentRectifyAspectMode)Math.Max(0, aspectCombo.SelectedIndex);

    float h = (float)customAspectH.Value;
    if (h <= 0f) { h = 1f; }
    token.CustomAspect = (float)customAspectW.Value / h;
  }

  protected override void OnLoaded()
  {
    base.OnLoaded();

    try
    {
      using var srcBmp = Environment.GetSourceBitmapBgra32();
      var size = srcBmp.Size;
      if (size.Width > 0 && size.Height > 0)
      {
        origSurface?.Dispose();
        origSurface = new Surface(size.Width, size.Height);

        unsafe
        {
          srcBmp.CopyPixels(origSurface.Scan0.VoidStar, origSurface.Stride,
              (uint)origSurface.Scan0.Length, null);
        }

        initialFitDone = false;
        SelectCorner(selectedCornerIndex);
        BeginInvoke(new Action(FitToWindow));
      }
    }
    catch (Exception ex)
    {
      origSurface?.Dispose();
      origSurface = null;
      canvas.AccessibleDescription = $"Document preview is unavailable because the source image could not be loaded: {ex.Message}";
    }
  }

  private void BuildUI()
  {
    SuspendLayout();

    Text = "Document Rectify - Drag corners, Tab-select; arrows nudge, Shift+arrows move 10 pixels";
    ClientSize = new Size(S(1000), S(700));
    MinimumSize = new Size(S(600), S(400));
    FormBorderStyle = FormBorderStyle.Sizable;
    MaximizeBox = true;
    StartPosition = FormStartPosition.CenterParent;
    WindowState = FormWindowState.Maximized;

    int panelW = S(270);
    int ctrlW = panelW - S(30);

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

    leftPanel.Controls.Add(MakeAutoLabel("Quality:"));
    qualitySlider = new TrackBar
    {
      Width = ctrlW,
      Minimum = 1,
      Maximum = 4,
      Value = 2,
      TickFrequency = 1,
      SmallChange = 1,
      LargeChange = 1,
      BackColor = PanelBg,
      Margin = new Padding(0, 0, 0, S(2)),
      AccessibleName = "Rectification quality",
      AccessibleDescription = "RGSS sample quality for the final image."
    };
    leftPanel.Controls.Add(qualitySlider);

    qualityValue = new Label
    {
      Text = "2x",
      AutoSize = true,
      ForeColor = LabelColor,
      Margin = new Padding(0, 0, 0, S(6))
    };
    leftPanel.Controls.Add(qualityValue);

    resetCornersBtn = MakeButton("Reset Corners");
    resetCornersBtn.Margin = new Padding(0, 0, 0, S(8));
    leftPanel.Controls.Add(resetCornersBtn);

    autoDetectBtn = MakeButton("Auto Detect");
    autoDetectBtn.Margin = new Padding(0, 0, 0, S(10));
    leftPanel.Controls.Add(autoDetectBtn);

    leftPanel.Controls.Add(MakeAutoLabel("Output Aspect:"));
    aspectCombo = new ComboBox
    {
      Width = ctrlW,
      DropDownStyle = ComboBoxStyle.DropDownList,
      BackColor = ControlBg,
      ForeColor = LabelColor,
      FlatStyle = FlatStyle.Flat,
      Margin = new Padding(0, 0, 0, S(6)),
      AccessibleName = "Output aspect ratio",
      AccessibleDescription = "Select how the rectified output is fit into the canvas."
    };
    aspectCombo.Items.AddRange(new object[]
    {
            "Fill Canvas (stretch)",
            "Preserve Detected",
            "Source Canvas",
            "Square (1:1)",
            "Letter (8.5 x 11)",
            "Legal (8.5 x 14)",
            "A4 (1 : sqrt 2)",
            "Custom..."
    });
    aspectCombo.SelectedIndex = (int)DocumentRectifyAspectMode.PreserveDetected;
    leftPanel.Controls.Add(aspectCombo);

    customAspectLabel = MakeAutoLabel("Custom W : H");
    leftPanel.Controls.Add(customAspectLabel);

    var customRow = new FlowLayoutPanel
    {
      FlowDirection = FlowDirection.LeftToRight,
      AutoSize = true,
      AutoSizeMode = AutoSizeMode.GrowAndShrink,
      WrapContents = false,
      Margin = new Padding(0, 0, 0, S(8)),
      BackColor = Color.Transparent
    };
    customAspectW = MakeNumeric(85m, 1m, 9999m, 0.1m, 1);
    customAspectH = MakeNumeric(110m, 1m, 9999m, 0.1m, 1);
    customAspectW.AccessibleName = "Custom aspect width";
    customAspectH.AccessibleName = "Custom aspect height";
    var colon = new Label { Text = ":", AutoSize = true, ForeColor = LabelColor, Margin = new Padding(S(4), S(6), S(4), 0) };
    customRow.Controls.Add(customAspectW);
    customRow.Controls.Add(colon);
    customRow.Controls.Add(customAspectH);
    leftPanel.Controls.Add(customRow);

    cornerCoordinatesLabel = MakeAutoLabel("Selected corner (image pixels):");
    cornerCoordinatesLabel.MaximumSize = new Size(ctrlW, 0);
    leftPanel.Controls.Add(cornerCoordinatesLabel);
    cornerX = AddCoordinateEditor(leftPanel, "&X:", ctrlW, "Selected document corner X");
    cornerY = AddCoordinateEditor(leftPanel, "&Y:", ctrlW, "Selected document corner Y");
    cornerX.ValueChanged += (_, _) => ApplyCornerCoordinates(editX: true);
    cornerY.ValueChanged += (_, _) => ApplyCornerCoordinates(editX: false);

    var helpLabel = new Label
    {
      Text = "Drag TL/TR/BR/BL handles to align\nwith the document corners.\n\n" +
               "Left-click: drag corner\nTab: select corner; arrows: nudge, Shift+arrows: 10 pixels\n" +
               "Shift+Tab: previous corner\nF6: edit X/Y coordinates\n" +
               "Right-click: pan view\n" +
               "Mouse wheel: zoom\nDouble-click: fit to window",
      AutoSize = true,
      MaximumSize = new Size(ctrlW, 0),
      ForeColor = LabelColor,
      Margin = new Padding(0, 0, 0, S(8))
    };
    leftPanel.Controls.Add(helpLabel);

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

    canvas = new DoubleBufferedPanel
    {
      Dock = DockStyle.Fill,
      BackColor = Color.FromArgb(48, 48, 48),
      TabStop = true,
      AccessibleName = "Document rectification canvas",
      AccessibleDescription = "Use Ctrl+Page Down or Ctrl+Page Up to select a document corner, then arrow keys to move it. Hold Shift to move ten image pixels. Tab and Shift+Tab move focus out of the canvas; F6 focuses the X coordinate editor. Right-click and drag to pan; use the mouse wheel to zoom."
    };
    canvas.KeyDown += Canvas_KeyDown;

    Controls.Add(canvas);
    Controls.Add(leftPanel);

    ResumeLayout(true);
  }

  private void WireEvents()
  {
    uiToolTip.SetToolTip(qualitySlider, "RGSS sample quality for final render");
    uiToolTip.SetToolTip(canvas, "Left-click or Tab-select a corner. Arrow keys move it; Shift+arrows move 10 image pixels. Right-click drag to pan; wheel to zoom.");
    uiToolTip.SetToolTip(autoDetectBtn, "Estimate document corners from image content");
    uiToolTip.SetToolTip(aspectCombo, "Aspect ratio of the rectified output");

    qualitySlider.ValueChanged += (_, _) =>
    {
      qualityValue.Text = $"{qualitySlider.Value}x";
      PushUpdate();
    };

    resetCornersBtn.Click += (_, _) =>
    {
      ResetCorners();
      PushUpdate();
      canvas?.Invalidate();
    };

    autoDetectBtn.Click += (_, _) =>
    {
      if (AutoDetectCorners())
      {
        SelectCorner(selectedCornerIndex);
        PushUpdate();
        canvas?.Invalidate();
      }
    };

    aspectCombo.SelectedIndexChanged += (_, _) =>
    {
      UpdateCustomAspectVisibility();
      PushUpdate();
    };

    customAspectW.ValueChanged += (_, _) =>
    {
      if (aspectCombo.SelectedIndex == (int)DocumentRectifyAspectMode.Custom) { PushUpdate(); }
    };
    customAspectH.ValueChanged += (_, _) =>
    {
      if (aspectCombo.SelectedIndex == (int)DocumentRectifyAspectMode.Custom) { PushUpdate(); }
    };

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
      if (e.Control && e.KeyCode == Keys.D0)
      {
        zoom = 1f;
        pan = PointF.Empty;
        canvas.Invalidate();
        e.Handled = true;
      }
    };
  }

  private void Canvas_Paint(object? sender, PaintEventArgs e)
  {
    var g = e.Graphics;
    g.Clear(canvas.BackColor);

    if (origSurface == null)
    {
      return;
    }

    g.InterpolationMode = zoom > 2f
        ? InterpolationMode.NearestNeighbor
        : InterpolationMode.HighQualityBicubic;
    g.PixelOffsetMode = PixelOffsetMode.Half;

    RectangleF imgRect = GetImageRect();
    using (var bmp = origSurface.CreateAliasedBitmap())
    {
      g.DrawImage(bmp, imgRect);
    }

    using (var borderPen = new Pen(Color.FromArgb(128, 255, 255, 255), 1) { DashStyle = DashStyle.Dash })
    {
      g.DrawRectangle(borderPen, imgRect.X, imgRect.Y, imgRect.Width, imgRect.Height);
    }

    DrawQuadOverlay(g);
  }

  private void DrawQuadOverlay(Graphics g)
  {
    if (origSurface == null)
    {
      return;
    }

    PointF[] pts = new PointF[4];
    for (int i = 0; i < 4; i++)
    {
      pts[i] = ImageToScreen(new PointF(
          corners[i].X * (origSurface.Width - 1),
          corners[i].Y * (origSurface.Height - 1)));
    }

    bool highContrast = SystemInformation.HighContrast;
    Color overlayColor = highContrast ? SystemColors.Highlight : Color.FromArgb(220, 0, 220, 255);
    Color overlayBackground = highContrast ? SystemColors.Window : Color.Black;
    Color overlayForeground = highContrast ? SystemColors.WindowText : Color.White;
    using (var haloPen = new Pen(overlayBackground, 5f))
    using (var contrastPen = new Pen(overlayForeground, 3f))
    using (var pen = new Pen(overlayColor, 1.5f))
    {
      g.DrawPolygon(haloPen, pts);
      g.DrawPolygon(contrastPen, pts);
      g.DrawPolygon(pen, pts);
    }

    string[] labels = { "TL", "TR", "BR", "BL" };

    for (int i = 0; i < 4; i++)
    {
      bool dragging = i == dragCornerIndex;
      float r = dragging ? 8f : 6f;
      var color = highContrast
          ? (dragging ? SystemColors.HighlightText : SystemColors.Highlight)
          : (dragging ? Color.FromArgb(255, 255, 140, 0) : Color.FromArgb(255, 0, 220, 255));

      using var b = new SolidBrush(color);
      g.FillEllipse(b, pts[i].X - r, pts[i].Y - r, r * 2f, r * 2f);

      using var outline = new Pen(overlayBackground, 1f);
      using var focusHalo = new Pen(overlayBackground, 4f);
      using var focusPen = new Pen(overlayForeground, 2f) { DashStyle = DashStyle.Dash };
      if (i == selectedCornerIndex)
      {
        g.DrawEllipse(focusHalo, pts[i].X - r - 5, pts[i].Y - r - 5, r * 2f + 10, r * 2f + 10);
        g.DrawEllipse(focusPen, pts[i].X - r - 5, pts[i].Y - r - 5, r * 2f + 10, r * 2f + 10);
      }
      g.DrawEllipse(outline, pts[i].X - r, pts[i].Y - r, r * 2f, r * 2f);

      using var textBrush = new SolidBrush(overlayForeground);
      using var textBackground = new SolidBrush(overlayBackground);
      SizeF labelSize = g.MeasureString(labels[i], Font);
      g.FillRectangle(textBackground, pts[i].X + 6f, pts[i].Y + 4f, labelSize.Width, labelSize.Height);
      g.DrawString(labels[i], Font, textBrush, pts[i].X + 6f, pts[i].Y + 4f);
    }
  }

  private void Canvas_MouseDown(object? sender, MouseEventArgs e)
  {
    canvas.Focus();

    if (e.Button == MouseButtons.Left)
    {
      dragCornerIndex = HitTestCorner(e.Location);
      if (dragCornerIndex >= 0) SelectCorner(dragCornerIndex);
    }
    else if (e.Button == MouseButtons.Right)
    {
      isPanning = true;
      lastPanPt = e.Location;
      canvas.Cursor = Cursors.SizeAll;
    }
  }

  private void Canvas_KeyDown(object? sender, KeyEventArgs e)
  {
    if (e.KeyCode == Keys.F6)
    {
      cornerX.Focus();
      e.SuppressKeyPress = true;
      return;
    }
    if (e.Control && e.KeyCode is Keys.PageDown or Keys.PageUp)
    {
      int direction = e.KeyCode == Keys.PageUp ? -1 : 1;
      SelectCorner((selectedCornerIndex + direction + corners.Length) % corners.Length);
      e.Handled = true;
      e.SuppressKeyPress = true;
      return;
    }

    if (e.KeyCode is Keys.Left or Keys.Right or Keys.Up or Keys.Down)
    {
      NudgeSelectedCorner(e.KeyCode, e.Shift ? 10f : 1f);
      e.Handled = true;
      e.SuppressKeyPress = true;
    }
  }

  private void SelectCorner(int index)
  {
    selectedCornerIndex = Math.Clamp(index, 0, corners.Length - 1);
    float x = corners[selectedCornerIndex].X * Math.Max(0, (origSurface?.Width ?? 1) - 1);
    float y = corners[selectedCornerIndex].Y * Math.Max(0, (origSurface?.Height ?? 1) - 1);
    string[] labels = { "top-left", "top-right", "bottom-right", "bottom-left" };
    SyncCornerCoordinates(x, y, labels[selectedCornerIndex]);
    canvas.AccessibleDescription = $"Selected {labels[selectedCornerIndex]} corner, image coordinates {x:0.##}, {y:0.##}. Ctrl+Page Up/Down selects another corner. Use arrow keys to move; hold Shift for 10-pixel steps. Tab or Shift+Tab leaves the canvas; F6 focuses the X coordinate editor.";
    canvas.Invalidate();
  }

  private void NudgeSelectedCorner(Keys key, float amount)
  {
    if (origSurface == null) return;
    PointF corner = corners[selectedCornerIndex];
    float imageWidth = Math.Max(1, origSurface.Width - 1);
    float imageHeight = Math.Max(1, origSurface.Height - 1);
    float dx = key == Keys.Left ? -amount : key == Keys.Right ? amount : 0f;
    float dy = key == Keys.Up ? -amount : key == Keys.Down ? amount : 0f;
    corners[selectedCornerIndex] = new PointF(
        Math.Clamp(corner.X + dx / imageWidth, 0f, 1f),
        Math.Clamp(corner.Y + dy / imageHeight, 0f, 1f));
    SelectCorner(selectedCornerIndex);
    PushUpdate();
  }

  private void Canvas_MouseMove(object? sender, MouseEventArgs e)
  {
    if (dragCornerIndex >= 0 && e.Button == MouseButtons.Left && origSurface != null)
    {
      PointF imgPt = ScreenToImage(e.Location);
      float nx = imgPt.X / Math.Max(1f, origSurface.Width - 1);
      float ny = imgPt.Y / Math.Max(1f, origSurface.Height - 1);

      corners[dragCornerIndex] = new PointF(
          Math.Clamp(nx, 0f, 1f),
          Math.Clamp(ny, 0f, 1f));

      SelectCorner(dragCornerIndex);
      canvas.Invalidate();
      QueuePreviewUpdate();
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
    if (e.Button == MouseButtons.Left)
    {
      bool wasDragging = dragCornerIndex >= 0;
      dragCornerIndex = -1;
      canvas.Invalidate();
      if (wasDragging) PushUpdate();
    }
    else if (e.Button == MouseButtons.Right)
    {
      isPanning = false;
      canvas.Cursor = Cursors.Default;
    }
  }

  private void Canvas_MouseWheel(object? sender, MouseEventArgs e)
  {
    PointF before = ScreenToImage(e.Location);
    zoom = e.Delta > 0
        ? Math.Min(zoom * ZoomStep, MaxZoom)
        : Math.Max(zoom / ZoomStep, MinZoom);
    PointF after = ScreenToImage(e.Location);

    pan = new PointF(
        pan.X + (after.X - before.X) * zoom,
        pan.Y + (after.Y - before.Y) * zoom);

    canvas.Invalidate();
  }

  private int HitTestCorner(Point mouse)
  {
    if (origSurface == null)
    {
      return -1;
    }

    const float radius = 12f;
    float r2 = radius * radius;

    for (int i = 0; i < 4; i++)
    {
      PointF pt = ImageToScreen(new PointF(
          corners[i].X * (origSurface.Width - 1),
          corners[i].Y * (origSurface.Height - 1)));

      float dx = mouse.X - pt.X;
      float dy = mouse.Y - pt.Y;
      if ((dx * dx + dy * dy) <= r2)
      {
        return i;
      }
    }

    return -1;
  }

  private RectangleF GetImageRect()
  {
    if (origSurface == null)
    {
      return RectangleF.Empty;
    }

    return new RectangleF(pan.X, pan.Y, origSurface.Width * zoom, origSurface.Height * zoom);
  }

  private PointF ScreenToImage(Point screenPt)
  {
    return new PointF(
        (screenPt.X - pan.X) / zoom,
        (screenPt.Y - pan.Y) / zoom);
  }

  private PointF ImageToScreen(PointF imagePt)
  {
    return new PointF(
        imagePt.X * zoom + pan.X,
        imagePt.Y * zoom + pan.Y);
  }

  private void FitToWindow()
  {
    if (origSurface == null || canvas == null)
    {
      return;
    }

    if (canvas.ClientSize.Width <= 0 || canvas.ClientSize.Height <= 0)
    {
      return;
    }

    float sx = (float)canvas.ClientSize.Width / origSurface.Width;
    float sy = (float)canvas.ClientSize.Height / origSurface.Height;
    zoom = Math.Min(sx, sy) * 0.9f;
    pan = new PointF(
        (canvas.ClientSize.Width - origSurface.Width * zoom) / 2f,
        (canvas.ClientSize.Height - origSurface.Height * zoom) / 2f);
    initialFitDone = true;
    canvas.Invalidate();
  }

  private void ResetCorners()
  {
    corners[0] = new PointF(0.10f, 0.10f);
    corners[1] = new PointF(0.90f, 0.10f);
    corners[2] = new PointF(0.90f, 0.90f);
    corners[3] = new PointF(0.10f, 0.90f);
    SelectCorner(selectedCornerIndex);
  }

  private static float NormalizeCoordinate(float value, float fallback) =>
      float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : fallback;

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

  private void PushUpdate()
  {
    previewUpdateTimer.Stop();
    UpdateTokenFromDialog();
  }

  private void QueuePreviewUpdate()
  {
    if (!previewUpdateTimer.Enabled)
    {
      previewUpdateTimer.Start();
    }
  }

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
    var editor = MakeNumeric(0m, 0m, 0m, 1m, 2);
    editor.Width = width - S(36);
    editor.Enabled = false;
    editor.AccessibleName = name;
    editor.AccessibleDescription = "Absolute image pixel coordinate of the selected document corner, bounded to the source image.";
    editor.BackColor = SystemInformation.HighContrast ? SystemColors.Window : ControlBg;
    editor.ForeColor = SystemInformation.HighContrast ? SystemColors.WindowText : LabelColor;
    row.Controls.Add(editor);
    parent.Controls.Add(row);
    uiToolTip.SetToolTip(editor, editor.AccessibleDescription);
    return editor;
  }

  private void SyncCornerCoordinates(float x, float y, string label)
  {
    if (cornerX == null || cornerY == null) return;
    syncingCornerCoordinates = true;
    try
    {
      cornerX.Maximum = Math.Max(0, (origSurface?.Width ?? 1) - 1);
      cornerY.Maximum = Math.Max(0, (origSurface?.Height ?? 1) - 1);
      cornerX.Value = Math.Clamp((decimal)x, cornerX.Minimum, cornerX.Maximum);
      cornerY.Value = Math.Clamp((decimal)y, cornerY.Minimum, cornerY.Maximum);
      cornerX.Enabled = cornerY.Enabled = origSurface != null;
      cornerCoordinatesLabel.Text = $"Selected {label} corner (pixels):";
    }
    finally
    {
      syncingCornerCoordinates = false;
    }
  }

  private void ApplyCornerCoordinates(bool editX)
  {
    if (syncingCornerCoordinates || origSurface == null) return;
    PointF corner = corners[selectedCornerIndex];
    corners[selectedCornerIndex] = new PointF(
        editX ? (float)cornerX.Value / Math.Max(1, origSurface.Width - 1) : corner.X,
        editX ? corner.Y : (float)cornerY.Value / Math.Max(1, origSurface.Height - 1));
    SelectCorner(selectedCornerIndex);
    PushUpdate();
  }

  private void ApplySystemTheme(Control parent)
  {
    bool highContrast = SystemInformation.HighContrast;
    foreach (Control control in parent.Controls)
    {
      control.ForeColor = highContrast ? SystemColors.ControlText : LabelColor;
      if (control == canvas)
      {
        control.BackColor = highContrast ? SystemColors.Window : Color.FromArgb(48, 48, 48);
      }
      else if (control is NumericUpDown or ComboBox)
      {
        control.BackColor = highContrast ? SystemColors.Window : ControlBg;
        control.ForeColor = highContrast ? SystemColors.WindowText : LabelColor;
      }
      else if (control is Button)
      {
        control.BackColor = highContrast ? SystemColors.Control : ControlBg;
      }
      else if (control is FlowLayoutPanel or TrackBar)
      {
        control.BackColor = highContrast ? SystemColors.Control : PanelBg;
      }
      ApplySystemTheme(control);
    }
    canvas?.Invalidate();
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
      MinimumSize = new Size(S(80), S(30)),
      FlatStyle = FlatStyle.Flat,
      BackColor = ControlBg,
      ForeColor = LabelColor,
      Padding = new Padding(S(8), S(2), S(8), S(2)),
      Margin = new Padding(0, 0, S(4), 0)
    };
  }

  private NumericUpDown MakeNumeric(decimal value, decimal min, decimal max, decimal increment, int decimals)
  {
    return new NumericUpDown
    {
      Minimum = min,
      Maximum = max,
      Value = Math.Clamp(value, min, max),
      Increment = increment,
      DecimalPlaces = decimals,
      Width = S(70),
      BackColor = ControlBg,
      ForeColor = LabelColor,
      BorderStyle = BorderStyle.FixedSingle,
      Margin = new Padding(0, 0, 0, 0)
    };
  }

  private void UpdateCustomAspectVisibility()
  {
    bool isCustom = aspectCombo.SelectedIndex == (int)DocumentRectifyAspectMode.Custom;
    customAspectLabel.Visible = isCustom;
    customAspectW.Visible = isCustom;
    customAspectH.Visible = isCustom;
    if (customAspectW.Parent != null)
    {
      customAspectW.Parent.Visible = isCustom;
    }
  }

  /// <summary>
  /// Auto-detect document corners from <see cref="origSurface"/>.
  /// Strategy:
  ///   1. Build content mask (luminance differs from border-median background).
  ///   2. Compute a robust bbox via 1st/99th percentile on x and y to discard stray outlier pixels.
  ///   3. Within that bbox, find the four diagonal extrema (max of ±x ± y) — these are the
  ///      true corners of the dominant content quad, even when slightly rotated.
  /// </summary>
  private bool AutoDetectCorners()
  {
    if (origSurface == null) { return false; }

    int w = origSurface.Width;
    int h = origSurface.Height;
    if (w < 8 || h < 8) { return false; }

    // Downsample for speed (max ~400 long edge).
    int maxDim = Math.Max(w, h);
    int step = Math.Max(1, maxDim / 400);

    float bg = EstimateBackgroundLuminance(origSurface, w, h, step);
    const float lumThresh = 0.06f;

    // Build a downsampled boolean content mask so we can do a cheap neighborhood
    // filter to reject isolated bright noise pixels (a major source of corner drift).
    int gw = (w + step - 1) / step;
    int gh = (h + step - 1) / step;
    var raw = new bool[gw * gh];

    unsafe
    {
      for (int gy = 0; gy < gh; gy++)
      {
        int y = Math.Min(gy * step, h - 1);
        var row = (uint*)((byte*)origSurface.Scan0.VoidStar + y * origSurface.Stride);
        int rowOff = gy * gw;
        for (int gx = 0; gx < gw; gx++)
        {
          int x = Math.Min(gx * step, w - 1);
          float lum = LuminanceFromBgra(row[x]);
          raw[rowOff + gx] = MathF.Abs(lum - bg) >= lumThresh;
        }
      }
    }

    // Erode: keep a content cell only if at least 5 of the 9 cells in its 3x3 neighborhood
    // are also content. Removes salt-and-pepper noise while preserving solid document regions.
    var clean = new bool[gw * gh];
    for (int gy = 1; gy < gh - 1; gy++)
    {
      for (int gx = 1; gx < gw - 1; gx++)
      {
        if (!raw[gy * gw + gx]) { continue; }
        int n = 0;
        for (int dy = -1; dy <= 1; dy++)
        {
          int rowOff = (gy + dy) * gw;
          for (int dx = -1; dx <= 1; dx++)
          {
            if (raw[rowOff + gx + dx]) { n++; }
          }
        }
        if (n >= 5) { clean[gy * gw + gx] = true; }
      }
    }

    // Pass 1: collect surviving content pixel coordinates (in original image units).
    var xs = new System.Collections.Generic.List<int>(8192);
    var ys = new System.Collections.Generic.List<int>(8192);
    for (int gy = 0; gy < gh; gy++)
    {
      int rowOff = gy * gw;
      int y = gy * step;
      for (int gx = 0; gx < gw; gx++)
      {
        if (!clean[rowOff + gx]) { continue; }
        xs.Add(gx * step);
        ys.Add(y);
      }
    }

    if (xs.Count < (gw * gh) / 50) { return false; }

    // Robust bbox via tight percentiles to discard stray outliers.
    int xMin = Percentile(xs, 0.02);
    int xMax = Percentile(xs, 0.98);
    int yMin = Percentile(ys, 0.02);
    int yMax = Percentile(ys, 0.98);

    if (xMax <= xMin || yMax <= yMin) { return false; }

    // Expand by a small margin so the actual document edge isn't truncated by the percentile clip.
    int marginX = Math.Max(2, (xMax - xMin) / 50);
    int marginY = Math.Max(2, (yMax - yMin) / 50);
    xMin = Math.Max(0, xMin - marginX);
    yMin = Math.Max(0, yMin - marginY);
    xMax = Math.Min(w - 1, xMax + marginX);
    yMax = Math.Min(h - 1, yMax + marginY);

    // Pass 2: find 4 diagonal extrema among content pixels inside the robust bbox.
    int bestTL = int.MaxValue, bestBR = int.MinValue;
    int bestTR = int.MinValue, bestBL = int.MaxValue;
    int tlx = xMin, tly = yMin, trx = xMax, try_ = yMin;
    int brx = xMax, bry = yMax, blx = xMin, bly = yMax;

    for (int i = 0; i < xs.Count; i++)
    {
      int x = xs[i];
      int y = ys[i];
      if (x < xMin || x > xMax || y < yMin || y > yMax) { continue; }

      int sum = x + y;
      int diff = x - y;

      if (sum < bestTL) { bestTL = sum; tlx = x; tly = y; }
      if (sum > bestBR) { bestBR = sum; brx = x; bry = y; }
      if (diff > bestTR) { bestTR = diff; trx = x; try_ = y; }
      if (diff < bestBL) { bestBL = diff; blx = x; bly = y; }
    }

    float fw = Math.Max(1f, w - 1);
    float fh = Math.Max(1f, h - 1);

    corners[0] = new PointF(tlx / fw, tly / fh);
    corners[1] = new PointF(trx / fw, try_ / fh);
    corners[2] = new PointF(brx / fw, bry / fh);
    corners[3] = new PointF(blx / fw, bly / fh);
    return true;
  }

  private static int Percentile(System.Collections.Generic.List<int> values, double p)
  {
    var copy = new int[values.Count];
    values.CopyTo(copy);
    Array.Sort(copy);
    int idx = (int)Math.Clamp(p * (copy.Length - 1), 0, copy.Length - 1);
    return copy[idx];
  }

  /// <summary>
  /// Estimate background luminance from a 1-pixel-thick border ring using the median
  /// (robust to dialog/widgets that touch an edge).
  /// </summary>
  private static unsafe float EstimateBackgroundLuminance(Surface surf, int w, int h, int step)
  {
    var samples = new System.Collections.Generic.List<float>(2 * (w + h) / step);

    // Top + bottom rows.
    for (int yEdge = 0; yEdge < 2; yEdge++)
    {
      int y = yEdge == 0 ? 0 : h - 1;
      var row = (uint*)((byte*)surf.Scan0.VoidStar + y * surf.Stride);
      for (int x = 0; x < w; x += step)
      {
        samples.Add(LuminanceFromBgra(row[x]));
      }
    }

    // Left + right columns (skip corners, already counted).
    for (int y = step; y < h - 1; y += step)
    {
      var row = (uint*)((byte*)surf.Scan0.VoidStar + y * surf.Stride);
      samples.Add(LuminanceFromBgra(row[0]));
      samples.Add(LuminanceFromBgra(row[w - 1]));
    }

    if (samples.Count == 0) { return 1f; }
    samples.Sort();
    return samples[samples.Count / 2];
  }

  private static float LuminanceFromBgra(uint bgra)
  {
    // PDN Surface is straight Bgra32 from GetSourceBitmapBgra32 copy.
    float b = ((bgra >> 0) & 0xFF) / 255f;
    float g = ((bgra >> 8) & 0xFF) / 255f;
    float r = ((bgra >> 16) & 0xFF) / 255f;
    return 0.2126f * r + 0.7152f * g + 0.0722f * b;
  }

  protected override void OnFormClosing(FormClosingEventArgs e)
  {
    if (DialogResult == DialogResult.OK)
    {
      PushUpdate();
    }

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
      uiToolTip.Dispose();
      previewUpdateTimer.Dispose();
    }

    base.OnDispose(disposing);
  }

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
