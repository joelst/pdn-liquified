using ComputeSharp;
using ComputeSharp.D2D1;
using ComputeSharp.D2D1.Interop;
using PaintDotNet.Direct2D1;
using PaintDotNet.Direct2D1.Effects;
using PaintDotNet.Effects;
using PaintDotNet.Effects.Gpu;
using PaintDotNet.Rendering;
using System;

namespace LiquifiedPlugin;

/// <summary>
/// GPU renderer for the shared CPU-generated inverse sample map. The original source
/// image is sampled once; the map is uploaded as one float displacement texture.
/// </summary>
internal sealed partial class LiquifiedEffect : GpuImageEffect<LiquifiedConfigToken>
{
    private Guid sampleMapShaderID;
    private ScenePositionEffect[]? scenePositionEffects;
    private HlslBinaryOperatorEffect[]? subPxScenePositionEffects;
    private IDeviceEffect[]? sampleMapEffects;
    private IDeviceBitmap? displacementBitmap;
    private bool[]? maskWorkspace;

    public LiquifiedEffect()
        : base(
            "Liquified",
            "Tools",
            GpuImageEffectOptions.Create() with { IsConfigurable = true })
    {
    }

    protected override IEffectConfigForm OnCreateConfigForm() => new LiquifiedConfigForm();

    protected override void OnSetDeviceContext(IDeviceContext deviceContext)
    {
        deviceContext.Factory.RegisterEffectFromBlob(
            D2D1PixelShaderEffect.GetRegistrationBlob<LiquifiedSampleMapShader>(out sampleMapShaderID));
        base.OnSetDeviceContext(deviceContext);
    }

    protected override IDeviceImage OnCreateOutput(IDeviceContext deviceContext)
    {
        int quality = Token.Quality;
        if (quality is < 1 or > 4)
            throw new ArgumentOutOfRangeException(nameof(Token.Quality), "Quality must be between one and four.");

        int sampleCount = quality * quality;
        scenePositionEffects = new ScenePositionEffect[sampleCount];
        subPxScenePositionEffects = new HlslBinaryOperatorEffect[sampleCount];
        sampleMapEffects = new IDeviceEffect[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            scenePositionEffects[i] = new ScenePositionEffect(deviceContext);

            subPxScenePositionEffects[i] = new HlslBinaryOperatorEffect(deviceContext);
            subPxScenePositionEffects[i].Properties.Parameter1.SetValue(HlslEffectParameter.Input);
            subPxScenePositionEffects[i].Properties.Input1.Set(scenePositionEffects[i]);
            subPxScenePositionEffects[i].Properties.Operator.SetValue(HlslBinaryOperator.Add);
            subPxScenePositionEffects[i].Properties.Parameter2.SetValue(HlslEffectParameter.Value);

            sampleMapEffects[i] = deviceContext.CreateEffect(sampleMapShaderID);
            sampleMapEffects[i].SetInput(0, subPxScenePositionEffects[i]);
        }

        var renderer = new SampleMapRenderer(deviceContext);
        renderer.Properties.Input.Set(Environment.SourceImage);
        renderer.Properties.EdgeMode.SetValue(SampleMapEdgeMode.Clamp);
        renderer.Properties.SampleMaps.SetCount(sampleCount);
        for (int i = 0; i < sampleCount; i++)
            renderer.Properties.SampleMaps[i].Set(sampleMapEffects[i]);

        return renderer;
    }

    protected override InspectTokenAction OnInspectTokenChanges(
        LiquifiedConfigToken oldToken,
        LiquifiedConfigToken newToken) =>
        oldToken.Quality != newToken.Quality
            ? InspectTokenAction.RecreateOutput
            : InspectTokenAction.UpdateOutput;

    protected override void OnUpdateOutput(IDeviceContext deviceContext)
    {
        int width = Environment.Document.Size.Width;
        int height = Environment.Document.Size.Height;
        LiquifiedDeformation.ValidateDocumentSize(width, height, deviceContext.MaximumBitmapSize);
        LiquifiedDeformation.ValidateBrushSettings(Token.Mode, Token.BrushSize, Token.Strength, Token.Density);
        LiquifiedDeformation.Validate(
            width, height, Token.Strokes, Token.ReconstructAmount, Token.Rate, Token.Quality);

        int pixelCount = checked(width * height);
        bool[]? frozenMask = Token.FrozenMask;
        if (frozenMask is { Length: > 0 } && frozenMask.Length != pixelCount)
            throw new ArgumentException("Freeze mask dimensions do not match the document.", nameof(Token.FrozenMask));
        if (maskWorkspace is null || maskWorkspace.Length != pixelCount)
            maskWorkspace = new bool[pixelCount];

        float[] map = LiquifiedDeformation.BuildSampleMap(
            width,
            height,
            Token.Strokes,
            Token.ReconstructAmount,
            Token.Rate,
            Token.Quality,
            frozenMask is { Length: > 0 } ? frozenMask : null,
            maskWorkspace: maskWorkspace,
            applyMaskStrokes: Token.FrozenMaskIsInitial || frozenMask is not { Length: > 0 });

        float[] displacementPixels = LiquifiedDeformation.BuildDisplacementTexture(map, width, height);

        displacementBitmap?.Dispose();
        unsafe
        {
            fixed (float* pixels = displacementPixels)
            {
                displacementBitmap = deviceContext.CreateBitmap(
                    new SizeInt32(width, height),
                    pixels,
                    checked(width * sizeof(float) * 4),
                    new BitmapProperties(DevicePixelFormats.Rgb128Float));
            }
        }

        int quality = Token.Quality;
        int sampleCount = quality * quality;
        var shader = new LiquifiedSampleMapShader(new float4(width, height, 0, 0));
        ReadOnlyMemory<byte> constantBuffer = D2D1PixelShader.GetConstantBuffer(shader);
        for (int i = 0; i < sampleCount; i++)
        {
            Vector2Float offset = GetRgssOffset(quality, i);
            subPxScenePositionEffects![i].Properties.Value2.SetValue(new Vector4Float(offset, 0, 0));
            sampleMapEffects![i].SetInput(1, displacementBitmap);
            sampleMapEffects[i].SetValue(D2D1PixelShaderEffectProperty.ConstantBuffer, constantBuffer.Span);
        }

        base.OnUpdateOutput(deviceContext);
    }

    protected override void OnInvalidateDeviceResources()
    {
        displacementBitmap?.Dispose();
        displacementBitmap = null;
        scenePositionEffects = null;
        subPxScenePositionEffects = null;
        sampleMapEffects = null;
        maskWorkspace = null;
        base.OnInvalidateDeviceResources();
    }

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

    [D2DInputCount(2)]
    [D2DInputSimple(0)]
    [D2DInputDescription(0, D2D1Filter.MinMagMipPoint)]
    [D2DInputComplex(1)]
    [D2DInputDescription(1, D2D1Filter.MinMagMipLinear)]
    [D2DRequiresScenePosition]
    [D2DShaderProfile(D2D1ShaderProfile.PixelShader50)]
    [D2DGeneratedPixelShaderDescriptor]
    [AutoConstructor]
    internal readonly partial struct LiquifiedSampleMapShader : ID2D1PixelShader
    {
        private readonly float4 imageSize;

        public float4 Execute()
        {
            float2 position = D2D.GetInput(0).XY;
            // Scene positions are pixel centers (x+0.5,y+0.5); uploading one
            // displacement texel per integer-indexed map pixel makes this an exact
            // texel-center lookup. The CPU preview samples the matching x/y map entry.
            float2 halfTexel = new float2(0.5f, 0.5f);
            float2 mapPosition = Hlsl.Clamp(position, halfTexel, imageSize.XY - halfTexel);
            float2 displacement = D2D.SampleInputAtPosition(1, mapPosition).XY;
            return new float4(position + displacement, 0, 1);
        }
    }
}
