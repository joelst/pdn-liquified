using ComputeSharp;
using ComputeSharp.D2D1;
using ComputeSharp.D2D1.Interop;
using PaintDotNet.Direct2D1;
using PaintDotNet.Direct2D1.Effects;
using PaintDotNet.Effects;
using PaintDotNet.Effects.Gpu;
using PaintDotNet.Rendering;
using System;

namespace DocumentRectifyPlugin;

/// <summary>
/// Rectifies perspective distortion by mapping a source quad to the full output rectangle.
/// The effect performs local image processing only (no network or external I/O).
/// </summary>
internal sealed partial class DocumentRectifyEffect : GpuImageEffect<DocumentRectifyConfigToken>
{
  private Guid sampleMapShaderID;
  private ScenePositionEffect[]? scenePositionEffects;
  private HlslBinaryOperatorEffect[]? subPxScenePositionEffects;
  private IDeviceEffect[]? sampleMapEffects;

  public DocumentRectifyEffect()
      : base(
          "Document Rectify",
          "Tools",
          GpuImageEffectOptions.Create() with
          {
            IsConfigurable = true
          })
  {
  }

  protected override IEffectConfigForm OnCreateConfigForm()
  {
    return new DocumentRectifyConfigForm();
  }

  protected override void OnSetDeviceContext(IDeviceContext deviceContext)
  {
    deviceContext.Factory.RegisterEffectFromBlob(
        D2D1PixelShaderEffect.GetRegistrationBlob<DocumentRectifySampleMapShader>(out this.sampleMapShaderID));

    base.OnSetDeviceContext(deviceContext);
  }

  protected override IDeviceImage OnCreateOutput(IDeviceContext deviceContext)
  {
    int quality = Math.Clamp(this.Token.Quality, 1, 4);
    int sampleCount = quality * quality;

    this.scenePositionEffects = new ScenePositionEffect[sampleCount];
    this.subPxScenePositionEffects = new HlslBinaryOperatorEffect[sampleCount];
    this.sampleMapEffects = new IDeviceEffect[sampleCount];

    for (int i = 0; i < sampleCount; i++)
    {
      this.scenePositionEffects[i] = new ScenePositionEffect(deviceContext);

      this.subPxScenePositionEffects[i] = new HlslBinaryOperatorEffect(deviceContext);
      this.subPxScenePositionEffects[i].Properties.Parameter1.SetValue(HlslEffectParameter.Input);
      this.subPxScenePositionEffects[i].Properties.Input1.Set(this.scenePositionEffects[i]);
      this.subPxScenePositionEffects[i].Properties.Operator.SetValue(HlslBinaryOperator.Add);
      this.subPxScenePositionEffects[i].Properties.Parameter2.SetValue(HlslEffectParameter.Value);

      this.sampleMapEffects[i] = deviceContext.CreateEffect(this.sampleMapShaderID);
      this.sampleMapEffects[i].SetInput(0, this.subPxScenePositionEffects[i]);
    }

    var renderer = new SampleMapRenderer(deviceContext);
    renderer.Properties.Input.Set(this.Environment.SourceImage);
    renderer.Properties.EdgeMode.SetValue(SampleMapEdgeMode.Clamp);
    renderer.Properties.SampleMaps.SetCount(sampleCount);

    for (int i = 0; i < sampleCount; i++)
    {
      renderer.Properties.SampleMaps[i].Set(this.sampleMapEffects[i]);
    }

    return renderer;
  }

  protected override InspectTokenAction OnInspectTokenChanges(
      DocumentRectifyConfigToken oldToken,
      DocumentRectifyConfigToken newToken)
  {
    if (oldToken.Quality != newToken.Quality)
    {
      return InspectTokenAction.RecreateOutput;
    }

    return InspectTokenAction.UpdateOutput;
  }

  protected override void OnUpdateOutput(IDeviceContext deviceContext)
  {
    int quality = Math.Clamp(this.Token.Quality, 1, 4);
    int sampleCount = quality * quality;

    float canvasW = Math.Max(1f, this.Environment.Document.Size.Width - 1);
    float canvasH = Math.Max(1f, this.Environment.Document.Size.Height - 1);

    GetCornersInPixels(this.Token.Corners, canvasW, canvasH,
        out float tlx, out float tly,
        out float trx, out float try_,
        out float brx, out float bry,
        out float blx, out float bly);

    GetTargetRect(
        this.Token.AspectMode,
        this.Token.CustomAspect,
        canvasW, canvasH,
        tlx, tly, trx, try_, brx, bry, blx, bly,
        out float rectX, out float rectY, out float rectW, out float rectH);

    if (!TryComputeHomography(
        rectX, rectY, tlx, tly,
        rectX + rectW, rectY, trx, try_,
        rectX + rectW, rectY + rectH, brx, bry,
        rectX, rectY + rectH, blx, bly,
        out float a, out float b, out float c,
        out float d, out float e, out float f,
        out float g, out float h))
    {
      a = 1f; b = 0f; c = 0f;
      d = 0f; e = 1f; f = 0f;
      g = 0f; h = 0f;
    }

    var shader = new DocumentRectifySampleMapShader(
        new float4(a, b, c, d),
        new float4(e, f, g, h),
        new float4(canvasW, canvasH, 0f, 0f),
        new float4(rectX, rectY, rectX + rectW, rectY + rectH));

    var cb = D2D1PixelShader.GetConstantBuffer(shader);

    for (int i = 0; i < sampleCount; i++)
    {
      Vector2Float subPxOffset = GetRgssOffset(quality, i);
      this.subPxScenePositionEffects![i].Properties.Value2.SetValue(new Vector4Float(subPxOffset, 0, 0));
      this.sampleMapEffects![i].SetValue(D2D1PixelShaderEffectProperty.ConstantBuffer, cb);
    }

    base.OnUpdateOutput(deviceContext);
  }

  /// <summary>
  /// Compute the destination rectangle in canvas coordinates that the source quad will be mapped to.
  /// The rectangle is centered in the canvas and sized to fit while preserving the requested aspect.
  /// </summary>
  private static void GetTargetRect(
      DocumentRectifyAspectMode mode,
      float customAspect,
      float canvasW,
      float canvasH,
      float tlx, float tly,
      float trx, float try_,
      float brx, float bry,
      float blx, float bly,
      out float x, out float y, out float w, out float h)
  {
    if (mode == DocumentRectifyAspectMode.FillCanvas)
    {
      x = 0f;
      y = 0f;
      w = canvasW;
      h = canvasH;
      return;
    }

    float aspect; // width / height
    switch (mode)
    {
      case DocumentRectifyAspectMode.Square:
        aspect = 1f;
        break;
      case DocumentRectifyAspectMode.Source:
        aspect = canvasW / Math.Max(1f, canvasH);
        break;
      case DocumentRectifyAspectMode.Letter:
        aspect = 8.5f / 11f;
        break;
      case DocumentRectifyAspectMode.Legal:
        aspect = 8.5f / 14f;
        break;
      case DocumentRectifyAspectMode.A4:
        aspect = 1f / MathF.Sqrt(2f);
        break;
      case DocumentRectifyAspectMode.Custom:
        aspect = customAspect <= 0f ? 1f : customAspect;
        break;
      case DocumentRectifyAspectMode.PreserveDetected:
      default:
        aspect = EstimateQuadAspect(tlx, tly, trx, try_, brx, bry, blx, bly);
        break;
    }

    aspect = Math.Clamp(aspect, 0.05f, 20f);

    float canvasAspect = canvasW / Math.Max(1f, canvasH);
    if (aspect >= canvasAspect)
    {
      w = canvasW;
      h = canvasW / aspect;
    }
    else
    {
      h = canvasH;
      w = canvasH * aspect;
    }

    x = (canvasW - w) * 0.5f;
    y = (canvasH - h) * 0.5f;
  }

  private static float EstimateQuadAspect(
      float tlx, float tly,
      float trx, float try_,
      float brx, float bry,
      float blx, float bly)
  {
    float topLen = Distance(tlx, tly, trx, try_);
    float botLen = Distance(blx, bly, brx, bry);
    float leftLen = Distance(tlx, tly, blx, bly);
    float rightLen = Distance(trx, try_, brx, bry);

    float w = 0.5f * (topLen + botLen);
    float h = 0.5f * (leftLen + rightLen);
    if (h < 1e-3f)
    {
      return 1f;
    }
    return w / h;
  }

  private static float Distance(float x0, float y0, float x1, float y1)
  {
    float dx = x1 - x0;
    float dy = y1 - y0;
    return MathF.Sqrt(dx * dx + dy * dy);
  }

  protected override void OnInvalidateDeviceResources()
  {
    this.scenePositionEffects = null;
    this.subPxScenePositionEffects = null;
    this.sampleMapEffects = null;
    base.OnInvalidateDeviceResources();
  }

  private static void GetCornersInPixels(
      float[] corners,
      float imgW,
      float imgH,
      out float tlx,
      out float tly,
      out float trx,
      out float try_,
      out float brx,
      out float bry,
      out float blx,
      out float bly)
  {
    tlx = 0f; tly = 0f;
    trx = imgW; try_ = 0f;
    brx = imgW; bry = imgH;
    blx = 0f; bly = imgH;

    if (corners == null || corners.Length < 8)
    {
      return;
    }

    tlx = Math.Clamp(corners[0], 0f, 1f) * imgW;
    tly = Math.Clamp(corners[1], 0f, 1f) * imgH;
    trx = Math.Clamp(corners[2], 0f, 1f) * imgW;
    try_ = Math.Clamp(corners[3], 0f, 1f) * imgH;
    brx = Math.Clamp(corners[4], 0f, 1f) * imgW;
    bry = Math.Clamp(corners[5], 0f, 1f) * imgH;
    blx = Math.Clamp(corners[6], 0f, 1f) * imgW;
    bly = Math.Clamp(corners[7], 0f, 1f) * imgH;
  }

  private static bool TryComputeHomography(
      float x0, float y0, float u0, float v0,
      float x1, float y1, float u1, float v1,
      float x2, float y2, float u2, float v2,
      float x3, float y3, float u3, float v3,
      out float a, out float b, out float c,
      out float d, out float e, out float f,
      out float g, out float h)
  {
    // Solve for:
    // u = (a*x + b*y + c) / (g*x + h*y + 1)
    // v = (d*x + e*y + f) / (g*x + h*y + 1)
    // Unknowns: a b c d e f g h
    float[,] m = new float[8, 9]
    {
            { x0, y0, 1, 0, 0, 0, -u0 * x0, -u0 * y0, u0 },
            { 0, 0, 0, x0, y0, 1, -v0 * x0, -v0 * y0, v0 },
            { x1, y1, 1, 0, 0, 0, -u1 * x1, -u1 * y1, u1 },
            { 0, 0, 0, x1, y1, 1, -v1 * x1, -v1 * y1, v1 },
            { x2, y2, 1, 0, 0, 0, -u2 * x2, -u2 * y2, u2 },
            { 0, 0, 0, x2, y2, 1, -v2 * x2, -v2 * y2, v2 },
            { x3, y3, 1, 0, 0, 0, -u3 * x3, -u3 * y3, u3 },
            { 0, 0, 0, x3, y3, 1, -v3 * x3, -v3 * y3, v3 }
    };

    if (!SolveGaussJordan(m, 8))
    {
      a = b = c = d = e = f = g = h = 0f;
      return false;
    }

    a = m[0, 8];
    b = m[1, 8];
    c = m[2, 8];
    d = m[3, 8];
    e = m[4, 8];
    f = m[5, 8];
    g = m[6, 8];
    h = m[7, 8];
    return true;
  }

  private static bool SolveGaussJordan(float[,] m, int n)
  {
    for (int col = 0; col < n; col++)
    {
      int pivot = col;
      float maxAbs = MathF.Abs(m[pivot, col]);
      for (int row = col + 1; row < n; row++)
      {
        float abs = MathF.Abs(m[row, col]);
        if (abs > maxAbs)
        {
          maxAbs = abs;
          pivot = row;
        }
      }

      if (maxAbs < 1e-8f)
      {
        return false;
      }

      if (pivot != col)
      {
        for (int j = col; j <= n; j++)
        {
          float tmp = m[col, j];
          m[col, j] = m[pivot, j];
          m[pivot, j] = tmp;
        }
      }

      float invPivot = 1f / m[col, col];
      for (int j = col; j <= n; j++)
      {
        m[col, j] *= invPivot;
      }

      for (int row = 0; row < n; row++)
      {
        if (row == col)
        {
          continue;
        }

        float factor = m[row, col];
        if (MathF.Abs(factor) < 1e-12f)
        {
          continue;
        }

        for (int j = col; j <= n; j++)
        {
          m[row, j] -= factor * m[col, j];
        }
      }
    }

    return true;
  }

  private static Vector2Float GetRgssOffset(int quality, int index)
  {
    if (quality == 1 && index == 0)
    {
      return default;
    }

    int count = quality * quality;
    float y = (index + 1.0f) / (count + 1.0f);
    float x = y * quality;
    x -= (int)x;
    return new Vector2Float(x - 0.5f, y - 0.5f);
  }

  [D2DInputCount(1)]
  [D2DInputSimple(0)]
  [D2DInputDescription(0, D2D1Filter.MinMagMipPoint)]
  [D2DRequiresScenePosition]
  [D2DShaderProfile(D2D1ShaderProfile.PixelShader50)]
  [D2DGeneratedPixelShaderDescriptor]
  [AutoConstructor]
  internal readonly partial struct DocumentRectifySampleMapShader : ID2D1PixelShader
  {
    // coeff0 = (a, b, c, d)
    private readonly float4 coeff0;

    // coeff1 = (e, f, g, h)
    private readonly float4 coeff1;

    // meta = (imgW, imgH, 0, 0)
    private readonly float4 meta;

    // rect = (xMin, yMin, xMax, yMax) destination rectangle in canvas pixels
    private readonly float4 rect;

    public float4 Execute()
    {
      float2 pos = D2D.GetInput(0).XY;

      // Outside the destination rect: emit transparent (alpha = 0).
      if (pos.X < rect.X || pos.Y < rect.Y || pos.X > rect.Z || pos.Y > rect.W)
      {
        return new float4(0, 0, 0, 0);
      }

      float a = coeff0.X;
      float b = coeff0.Y;
      float c = coeff0.Z;
      float d = coeff0.W;

      float e = coeff1.X;
      float f = coeff1.Y;
      float g = coeff1.Z;
      float h = coeff1.W;

      float denom = g * pos.X + h * pos.Y + 1.0f;
      if (Hlsl.Abs(denom) < 1e-6f)
      {
        denom = denom < 0 ? -1e-6f : 1e-6f;
      }

      float sx = (a * pos.X + b * pos.Y + c) / denom;
      float sy = (d * pos.X + e * pos.Y + f) / denom;

      sx = Hlsl.Clamp(sx, 0.0f, meta.X);
      sy = Hlsl.Clamp(sy, 0.0f, meta.Y);

      return new float4(sx, sy, 0, 1);
    }
  }
}
