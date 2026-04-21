using ComputeSharp;
using ComputeSharp.D2D1;
using ComputeSharp.D2D1.Interop;
using PaintDotNet.Direct2D1;
using PaintDotNet.Direct2D1.Effects;
using PaintDotNet.Effects;
using PaintDotNet.Effects.Gpu;
using PaintDotNet.Rendering;
using System;

namespace GridWarpPlugin;

/// <summary>
/// GPU-accelerated Grid Warp effect for Paint.NET 5.x.
/// Uses <see cref="SampleMapRenderer"/> with RGSS multisampling for high-quality mesh-based distortion.
/// A grid of draggable control points defines bilinear displacement across the image.
/// Supports grids from 2×2 to 6×6 cells (max 7×7 = 49 control points).
/// The effect performs local image processing only (no network or external I/O).
/// </summary>
internal sealed partial class GridWarpEffect : GpuImageEffect<GridWarpConfigToken>
{
    // --- Device resources ---
    private Guid sampleMapShaderID;
    private ScenePositionEffect[]? scenePositionEffects;
    private HlslBinaryOperatorEffect[]? subPxScenePositionEffects;
    private IDeviceEffect[]? sampleMapEffects;
    private IDeviceBitmap? displacementBitmap;

    public GridWarpEffect()
        : base(
            "Grid Warp",
            "Tools",
            GpuImageEffectOptions.Create() with { IsConfigurable = true })
    {
    }

    protected override IEffectConfigForm OnCreateConfigForm()
    {
        return new GridWarpConfigForm();
    }

    // ─────────────────────────────────────────────
    //  Device setup
    // ─────────────────────────────────────────────

    protected override void OnSetDeviceContext(IDeviceContext deviceContext)
    {
        deviceContext.Factory.RegisterEffectFromBlob(
            D2D1PixelShaderEffect.GetRegistrationBlob<GridWarpSampleMapShader>(out this.sampleMapShaderID));
        base.OnSetDeviceContext(deviceContext);
    }

    // ─────────────────────────────────────────────
    //  Effect graph
    // ─────────────────────────────────────────────

    protected override IDeviceImage OnCreateOutput(IDeviceContext deviceContext)
    {
        int quality = this.Token.Quality;
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

    // ─────────────────────────────────────────────
    //  Token change inspection
    // ─────────────────────────────────────────────

    protected override InspectTokenAction OnInspectTokenChanges(
        GridWarpConfigToken oldToken,
        GridWarpConfigToken newToken)
    {
        if (oldToken.Quality != newToken.Quality)
            return InspectTokenAction.RecreateOutput;

        return InspectTokenAction.UpdateOutput;
    }

    // ─────────────────────────────────────────────
    //  Output update (shader constants + displacement texture)
    // ─────────────────────────────────────────────

    protected override void OnUpdateOutput(IDeviceContext deviceContext)
    {
        int quality = this.Token.Quality;
        int sampleCount = quality * quality;
        int gridWidth = this.Token.GridWidth;
        int gridHeight = this.Token.GridHeight;
        float imgW = (float)this.Environment.Document.Size.Width;
        float imgH = (float)this.Environment.Document.Size.Height;
        float[] displacements = this.Token.Displacements ?? Array.Empty<float>();
        int pointsX = gridWidth + 1;
        int pointsY = gridHeight + 1;
        int pointCount = pointsX * pointsY;

        // Update sub-pixel RGSS offsets
        for (int i = 0; i < sampleCount; i++)
        {
            Vector2Float subPxOffset = GetRgssOffset(quality, i);
            this.subPxScenePositionEffects![i].Properties.Value2.SetValue(
                new Vector4Float(subPxOffset, 0, 0));
        }

        // Upload control-point displacement vectors as an RGB128Float bitmap where
        // each texel stores (dx, dy, 0, 0). The shader bilinearly samples this map.
        float[] dispPixels = new float[pointCount * 4];
        for (int pointIndex = 0; pointIndex < pointCount; pointIndex++)
        {
            int src = pointIndex * 2;
            int dst = pointIndex * 4;
            dispPixels[dst + 0] = src < displacements.Length ? displacements[src] : 0f;
            dispPixels[dst + 1] = src + 1 < displacements.Length ? displacements[src + 1] : 0f;
            dispPixels[dst + 2] = 0f;
            dispPixels[dst + 3] = 0f;
        }

        this.displacementBitmap?.Dispose();
        unsafe
        {
            fixed (float* pDisp = dispPixels)
            {
                this.displacementBitmap = deviceContext.CreateBitmap(
                    new SizeInt32(pointsX, pointsY),
                    pDisp,
                    pointsX * sizeof(float) * 4,
                    new BitmapProperties(DevicePixelFormats.Rgb128Float));
            }
        }

        var shader = new GridWarpSampleMapShader(
            new float4(gridWidth, gridHeight, imgW, imgH));

        var cb = D2D1PixelShader.GetConstantBuffer(shader);
        for (int i = 0; i < sampleCount; i++)
        {
            this.sampleMapEffects![i].SetValue(D2D1PixelShaderEffectProperty.ConstantBuffer, cb);
            this.sampleMapEffects![i].SetInput(1, this.displacementBitmap!);
        }

        base.OnUpdateOutput(deviceContext);
    }

    // ─────────────────────────────────────────────
    //  Cleanup
    // ─────────────────────────────────────────────

    protected override void OnInvalidateDeviceResources()
    {
        this.displacementBitmap?.Dispose();
        this.displacementBitmap = null;
        this.scenePositionEffects = null;
        this.subPxScenePositionEffects = null;
        this.sampleMapEffects = null;
        base.OnInvalidateDeviceResources();
    }

    // ─────────────────────────────────────────────
    //  RGSS helper (inlined from EffectHelpers)
    // ─────────────────────────────────────────────

    private static Vector2Float GetRgssOffset(int quality, int index)
    {
        if (quality == 1 && index == 0)
            return default;

        int count = quality * quality;
        float y = (index + 1.0f) / (count + 1.0f);
        float x = y * quality;
        x -= (int)x;
        return new Vector2Float(x - 0.5f, y - 0.5f);
    }

    // ═════════════════════════════════════════════
    //  GPU PIXEL SHADER — Grid Warp Sample Map
    // ═════════════════════════════════════════════

    /// <summary>
    /// Grid warp sample-map shader. Samples a displacement texture where each texel is a
    /// control point displacement vector (dx,dy,0,0), then uses bilinear sampling between points.
    /// </summary>
    [D2DInputCount(2)]
    [D2DInputSimple(0)]
    [D2DInputDescription(0, D2D1Filter.MinMagMipPoint)]
    [D2DInputComplex(1)]
    [D2DInputDescription(1, D2D1Filter.MinMagMipLinear)]
    [D2DRequiresScenePosition]
    [D2DShaderProfile(D2D1ShaderProfile.PixelShader50)]
    [D2DGeneratedPixelShaderDescriptor]
    [AutoConstructor]
    internal readonly partial struct GridWarpSampleMapShader : ID2D1PixelShader
    {
        /// <summary>(gridWidth, gridHeight, imageWidth, imageHeight). Remaining args are padding.</summary>
        private readonly float4 meta;

        public float4 Execute()
        {
            float4 inputVal = D2D.GetInput(0);
            float2 pos = inputVal.XY;

            int gridWidth = (int)meta.X;
            int gridHeight = (int)meta.Y;
            float imgW = meta.Z;
            float imgH = meta.W;

            if (gridWidth < 1 || gridHeight < 1 || imgW < 1 || imgH < 1)
                return new float4(pos, 0, 1);

            // Convert scene position into displacement texture coordinates where control
            // points are centered at (i+0.5, j+0.5). Input 1 is sampled with linear filter.
            float texX = pos.X * (gridWidth / imgW) + 0.5f;
            float texY = pos.Y * (gridHeight / imgH) + 0.5f;
            float2 disp = D2D.SampleInputAtPosition(1, new float2(texX, texY)).XY;

            // Source = output position - interpolated displacement
            float2 src = pos - disp;
            return new float4(src, 0, 1);
        }
    }
}
