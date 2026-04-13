using ComputeSharp;
using ComputeSharp.D2D1;
using ComputeSharp.D2D1.Interop;
using PaintDotNet.Direct2D1;
using PaintDotNet.Direct2D1.Effects;
using PaintDotNet.Effects;
using PaintDotNet.Effects.Gpu;
using PaintDotNet.Rendering;
using System;
using System.Collections.Generic;

namespace LiquifyPlugin;

/// <summary>
/// Liquify brush modes.
/// Keep in sync with the shader's mode dispatch in <see cref="LiquifySampleMapShader.Execute"/>.
/// </summary>
public enum LiquifyMode
{
    ForwardWarp = 0,
    Pucker = 1,
    Bloat = 2,
    TwistCW = 3,
    TwistCCW = 4,
    PushLeft = 5,
    Reconstruct = 6,
    Turbulence = 7
}

/// <summary>
/// A single brush stroke: mode, parameters, and the sequence of brush application points.
/// </summary>
public sealed class BrushStroke
{
    public LiquifyMode Mode { get; set; }
    public int BrushSize { get; set; }
    public double Strength { get; set; }
    public double Density { get; set; }
    public List<float[]> Points { get; set; } = new();
}

/// <summary>
/// GPU-accelerated Liquify effect for Paint.NET 5.x.
/// Uses <see cref="SampleMapRenderer"/> with RGSS multisampling for high-quality distortion.
/// Brush strokes are serialized as compact JSON and dispatched to the GPU in batches of 16
/// via <see cref="LiquifySampleMapShader"/>.
/// </summary>
internal sealed partial class LiquifyEffect : GpuImageEffect<LiquifyConfigToken>
{
    // --- Device resources ---
    private Guid sampleMapShaderID;
    private ScenePositionEffect[]? scenePositionEffects;
    private HlslBinaryOperatorEffect[]? subPxScenePositionEffects;

    // Per-batch: each batch holds 16 stroke points and feeds one shader instance.
    // Multiple batches are chained: output of batch N feeds input of batch N+1.
    private IDeviceEffect[]?[]? batchSampleMapEffects; // [batch][sampleIndex]

    // --- Packed stroke data ---
    private const int BatchSize = 16;
    private const int MaxPackedPoints = 256;
    private readonly List<PackedStrokePoint> packedPoints = new();

    private struct PackedStrokePoint
    {
        public float Cx, Cy, Radius, Strength, Mode, Density;
    }

    // ─────────────────────────────────────────────
    //  Construction
    // ─────────────────────────────────────────────

    public LiquifyEffect()
        : base(
            "Liquify5",
            "Tools",
            GpuImageEffectOptions.Create() with
            {
                IsConfigurable = true
            })
    {
    }

    // ─────────────────────────────────────────────
    //  Config form
    // ─────────────────────────────────────────────

    protected override IEffectConfigForm OnCreateConfigForm()
    {
        return new LiquifyConfigForm();
    }

    // ─────────────────────────────────────────────
    //  Device context setup
    // ─────────────────────────────────────────────

    protected override void OnSetDeviceContext(IDeviceContext deviceContext)
    {
        deviceContext.Factory.RegisterEffectFromBlob(
            D2D1PixelShaderEffect.GetRegistrationBlob<LiquifySampleMapShader>(out this.sampleMapShaderID));

        base.OnSetDeviceContext(deviceContext);
    }

    // ─────────────────────────────────────────────
    //  Effect graph creation
    // ─────────────────────────────────────────────

    protected override IDeviceImage OnCreateOutput(IDeviceContext deviceContext)
    {
        ParseStrokesFromToken();

        int quality = this.Token.Quality;
        int sampleCount = quality * quality;

        int totalPoints = Math.Min(packedPoints.Count, MaxPackedPoints);
        int batchCount = Math.Max(1, (totalPoints + BatchSize - 1) / BatchSize);

        // Allocate per-sample-offset arrays for each batch
        this.batchSampleMapEffects = new IDeviceEffect[batchCount][];
        this.scenePositionEffects = new ScenePositionEffect[sampleCount];
        this.subPxScenePositionEffects = new HlslBinaryOperatorEffect[sampleCount];

        // Create shared scene-position + subpixel-offset chains (reused across batches)
        for (int i = 0; i < sampleCount; i++)
        {
            this.scenePositionEffects[i] = new ScenePositionEffect(deviceContext);

            this.subPxScenePositionEffects[i] = new HlslBinaryOperatorEffect(deviceContext);
            this.subPxScenePositionEffects[i].Properties.Parameter1.SetValue(HlslEffectParameter.Input);
            this.subPxScenePositionEffects[i].Properties.Input1.Set(this.scenePositionEffects[i]);
            this.subPxScenePositionEffects[i].Properties.Operator.SetValue(HlslBinaryOperator.Add);
            this.subPxScenePositionEffects[i].Properties.Parameter2.SetValue(HlslEffectParameter.Value);
        }

        // Chain batches: batch 0 reads from SourceImage, batch N reads from SampleMapRenderer of batch N-1
        IDeviceImage currentInput = this.Environment.SourceImage;

        for (int b = 0; b < batchCount; b++)
        {
            this.batchSampleMapEffects![b] = new IDeviceEffect[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                this.batchSampleMapEffects[b]![i] = deviceContext.CreateEffect(this.sampleMapShaderID);
                this.batchSampleMapEffects[b]![i].SetInput(0, this.subPxScenePositionEffects![i]);
            }

            var renderer = new SampleMapRenderer(deviceContext);
            renderer.Properties.Input.Set(currentInput);
            renderer.Properties.EdgeMode.SetValue(SampleMapEdgeMode.Clamp);
            renderer.Properties.SampleMaps.SetCount(sampleCount);

            for (int i = 0; i < sampleCount; i++)
            {
                renderer.Properties.SampleMaps[i].Set(this.batchSampleMapEffects[b]![i]);
            }

            currentInput = renderer;
        }

        return currentInput;
    }

    // ─────────────────────────────────────────────
    //  Token change inspection
    // ─────────────────────────────────────────────

    protected override InspectTokenAction OnInspectTokenChanges(
        LiquifyConfigToken oldToken,
        LiquifyConfigToken newToken)
    {
        // Rebuild graph if quality changes (alters RGSS sample count)
        if (oldToken.Quality != newToken.Quality)
        {
            return InspectTokenAction.RecreateOutput;
        }

        // Rebuild graph if batch count changes
        int oldBatchCount = CountBatches(oldToken.Strokes);
        int newBatchCount = CountBatches(newToken.Strokes);
        if (oldBatchCount != newBatchCount)
        {
            return InspectTokenAction.RecreateOutput;
        }

        return InspectTokenAction.UpdateOutput;
    }

    private int CountBatches(List<BrushStroke>? strokes)
    {
        int pointCount = CountPackedPoints(strokes);
        return Math.Max(1, (pointCount + BatchSize - 1) / BatchSize);
    }

    private static int CountPackedPoints(List<BrushStroke>? strokes)
    {
        if (strokes == null) return 0;
        int total = 0;
        foreach (var s in strokes)
        {
            total += s.Points?.Count ?? 0;
        }
        return Math.Min(total, MaxPackedPoints);
    }

    // ─────────────────────────────────────────────
    //  Output update (sets shader constants)
    // ─────────────────────────────────────────────

    protected override void OnUpdateOutput(IDeviceContext deviceContext)
    {
        ParseStrokesFromToken();

        int quality = this.Token.Quality;
        int sampleCount = quality * quality;

        int totalPoints = Math.Min(packedPoints.Count, MaxPackedPoints);
        int batchCount = this.batchSampleMapEffects?.Length ?? 0;

        // Update sub-pixel offsets
        for (int i = 0; i < sampleCount; i++)
        {
            Vector2Float subPxOffset = GetRgssOffset(quality, i);
            this.subPxScenePositionEffects![i].Properties.Value2.SetValue(new Vector4Float(subPxOffset, 0, 0));
        }

        // Update each batch's shader constants
        for (int b = 0; b < batchCount; b++)
        {
            int offset = b * BatchSize;
            int countInBatch = Math.Min(BatchSize, totalPoints - offset);
            if (countInBatch < 0) countInBatch = 0;

            var shader = new LiquifySampleMapShader(
                countInBatch,
                PackPoint(offset + 0),
                PackPoint(offset + 1),
                PackPoint(offset + 2),
                PackPoint(offset + 3),
                PackPoint(offset + 4),
                PackPoint(offset + 5),
                PackPoint(offset + 6),
                PackPoint(offset + 7),
                PackPoint(offset + 8),
                PackPoint(offset + 9),
                PackPoint(offset + 10),
                PackPoint(offset + 11),
                PackPoint(offset + 12),
                PackPoint(offset + 13),
                PackPoint(offset + 14),
                PackPoint(offset + 15));

            var cb = D2D1PixelShader.GetConstantBuffer(shader);

            for (int i = 0; i < sampleCount; i++)
            {
                this.batchSampleMapEffects![b]![i].SetValue(D2D1PixelShaderEffectProperty.ConstantBuffer, cb);
            }
        }

        base.OnUpdateOutput(deviceContext);
    }

    // ─────────────────────────────────────────────
    //  Cleanup
    // ─────────────────────────────────────────────

    protected override void OnInvalidateDeviceResources()
    {
        this.scenePositionEffects = null;
        this.subPxScenePositionEffects = null;
        this.batchSampleMapEffects = null;
        base.OnInvalidateDeviceResources();
    }

    // ─────────────────────────────────────────────
    //  Helpers — stroke packing
    // ─────────────────────────────────────────────

    private void ParseStrokesFromToken()
    {
        packedPoints.Clear();
        var strokes = this.Token.Strokes;
        if (strokes == null || strokes.Count == 0) return;

        foreach (var s in strokes)
        {
            if (s.Points == null || s.Points.Count == 0) continue;
            float radius = Math.Max(1f, s.BrushSize * 0.5f);
            float strength = Math.Clamp((float)s.Strength, 0f, 1f);
            float density = Math.Clamp((float)s.Density, 0f, 1f);
            float mode = (float)(int)s.Mode;

            foreach (var pt in s.Points)
            {
                if (pt.Length < 2) continue;
                if (packedPoints.Count >= MaxPackedPoints) return;
                packedPoints.Add(new PackedStrokePoint
                {
                    Cx = pt[0],
                    Cy = pt[1],
                    Radius = radius,
                    Strength = strength,
                    Mode = mode,
                    Density = density
                });
            }
        }
    }

    /// <summary>
    /// Pack a stroke point into a float4 for the shader constant buffer.
    /// Format: (cx, cy, radius + density*0.0001, mode + strength).
    /// </summary>
    private float4 PackPoint(int index)
    {
        if (index < 0 || index >= packedPoints.Count)
        {
            // Neutral: mode -1 signals "skip" in shader
            return new float4(0, 0, 1f, -1f);
        }

        var p = packedPoints[index];
        float radiusAndDensity = p.Radius + MathF.Min(0.9999f, MathF.Max(0f, p.Density)) * 0.0001f;
        float modeAndStrength = p.Mode + MathF.Min(0.9999f, MathF.Max(0f, p.Strength));
        return new float4(p.Cx, p.Cy, radiusAndDensity, modeAndStrength);
    }

    // ─────────────────────────────────────────────
    //  RGSS helper (inlined from EffectHelpers)
    // ─────────────────────────────────────────────

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

    // ═════════════════════════════════════════════
    //  GPU PIXEL SHADER — Sample Map
    // ═════════════════════════════════════════════

    /// <summary>
    /// Batched sample-map shader that processes up to 16 stroke points per pass.
    /// Input 0 is the scene position (from ScenePositionEffect + RGSS offset).
    /// Output is float4(sampleX, sampleY, *, alpha) — where to read from the source image.
    /// </summary>
    [D2DInputCount(1)]
    [D2DInputSimple(0)]
    [D2DInputDescription(0, D2D1Filter.MinMagMipPoint)]
    [D2DShaderProfile(D2D1ShaderProfile.PixelShader50)]
    [D2DGeneratedPixelShaderDescriptor]
    [AutoConstructor]
    internal readonly partial struct LiquifySampleMapShader : ID2D1PixelShader
    {
        private readonly int strokeCount;
        private readonly float4 s0;
        private readonly float4 s1;
        private readonly float4 s2;
        private readonly float4 s3;
        private readonly float4 s4;
        private readonly float4 s5;
        private readonly float4 s6;
        private readonly float4 s7;
        private readonly float4 s8;
        private readonly float4 s9;
        private readonly float4 s10;
        private readonly float4 s11;
        private readonly float4 s12;
        private readonly float4 s13;
        private readonly float4 s14;
        private readonly float4 s15;

        public float4 Execute()
        {
            // Read scene position from input ONCE outside the loop
            // (D2D.GetInput is a texture sample — forbidden inside varying-iteration loops)
            float4 inputVal = D2D.GetInput(0);
            float2 pos = inputVal.XY;
            float2 originalPos = pos;

            for (int i = 0; i < this.strokeCount; i++)
            {
                float4 data = GetStrokeData(i);

                // Decode packed values
                float cx = data.X;
                float cy = data.Y;
                float radiusAndDensity = data.Z;
                float modeAndStrength = data.W;

                // Skip sentinel
                if (modeAndStrength < 0f) continue;

                float radius = Hlsl.Floor(radiusAndDensity);
                // Density encoded in fractional part * 10000
                float density = Hlsl.Frac(radiusAndDensity) * 10000f;
                density = Hlsl.Saturate(density);

                float mode = Hlsl.Floor(modeAndStrength);
                float strength = modeAndStrength - mode;

                float2 center = new float2(cx, cy);
                float2 delta = pos - center;
                float dist2 = Hlsl.Dot(delta, delta);
                float r2 = radius * radius;

                if (dist2 > r2 || dist2 < 1e-6f)
                {
                    continue;
                }

                float dist = Hlsl.Sqrt(dist2);
                float invRadius = 1f / radius;

                // Smoothstep falloff modulated by density
                float t = 1f - (dist * invRadius);
                float falloff = t * t * (3f - 2f * t);
                falloff = Hlsl.Pow(Hlsl.Abs(falloff), 2f - density);

                float eff = strength * falloff;

                if (mode < 0.5f)
                {
                    // Mode 0: Forward Warp — push pixels along drag direction
                    float pushFactor = eff * 20f;
                    float invDist = 1f / dist;
                    pos -= delta * (pushFactor * invDist);
                }
                else if (mode < 1.5f)
                {
                    // Mode 1: Pucker — contract toward brush center
                    float factor = eff * 15f;
                    float invDist = 1f / dist;
                    pos += delta * (factor * invDist);
                }
                else if (mode < 2.5f)
                {
                    // Mode 2: Bloat — expand outward from brush center
                    float factor = eff * 25f;
                    float invDist = 1f / dist;
                    pos -= delta * (factor * invDist);
                }
                else if (mode < 3.5f)
                {
                    // Mode 3: Twist CW
                    float ang = eff * 0.5f;
                    float s = 0f;
                    float c = 1f;
                    Hlsl.SinCos(ang, out s, out c);
                    pos = center + new float2(delta.X * c - delta.Y * s, delta.X * s + delta.Y * c);
                }
                else if (mode < 4.5f)
                {
                    // Mode 4: Twist CCW
                    float ang = -eff * 0.5f;
                    float s = 0f;
                    float c = 1f;
                    Hlsl.SinCos(ang, out s, out c);
                    pos = center + new float2(delta.X * c - delta.Y * s, delta.X * s + delta.Y * c);
                }
                else if (mode < 5.5f)
                {
                    // Mode 5: Push Left — perpendicular displacement
                    float factor = eff * 20f;
                    float invDist = 1f / dist;
                    pos += new float2(delta.Y, -delta.X) * (factor * invDist);
                }
                else if (mode < 6.5f)
                {
                    // Mode 6: Reconstruct — move sampling position back toward undistorted position
                    pos = Hlsl.Lerp(pos, originalPos, eff);
                }
                else
                {
                    // Mode 7: Turbulence — pseudo-random jitter
                    float factor = eff * 15f;
                    float hash1 = Hlsl.Frac(Hlsl.Sin(Hlsl.Dot(pos, new float2(12.9898f, 78.233f))) * 43758.5453f);
                    float hash2 = Hlsl.Frac(Hlsl.Sin(Hlsl.Dot(pos, new float2(39.346f, 11.135f))) * 43758.5453f);
                    pos += new float2(hash1 - 0.5f, hash2 - 0.5f) * factor;
                }
            }

            // Sample map output: (X, Y, *, Alpha)
            return new float4(pos, 0f, 1f);
        }

        private float4 GetStrokeData(int i)
        {
            // HLSL does not support array indexing on private fields
            if (i == 0) return s0;
            if (i == 1) return s1;
            if (i == 2) return s2;
            if (i == 3) return s3;
            if (i == 4) return s4;
            if (i == 5) return s5;
            if (i == 6) return s6;
            if (i == 7) return s7;
            if (i == 8) return s8;
            if (i == 9) return s9;
            if (i == 10) return s10;
            if (i == 11) return s11;
            if (i == 12) return s12;
            if (i == 13) return s13;
            if (i == 14) return s14;
            return s15;
        }
    }
}
