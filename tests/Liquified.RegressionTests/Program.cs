using LiquifiedPlugin;
using System.Diagnostics;

Run("more than 256 points", MoreThan256Points);
Run("freeze mask pixel boundary", FreezeBoundary);
Run("chronological freeze unfreeze", ChronologicalFreezeUnfreeze);
Run("stroke ordering", StrokeOrdering);
Run("reconstruct across former batch boundary", ReconstructAcrossBatchBoundary);
Run("HLSL fractional turbulence", TurbulenceFraction);
Run("global reconstruct amount", GlobalReconstructAmount);
Run("mask none all invert", MaskOperations);
Run("pressure and rate validation", PressureAndRate);
Run("pixel-center map convention", PixelCenterConvention);
Run("incremental equals replay", IncrementalEqualsReplay);
Run("incremental mask inversion and painting", IncrementalMaskInversion);
Run("malformed and NaN tokens", RejectMalformedTokens);
Run("one-point stationary application", StationaryApplication);
Run("huge dimensions rejected before allocation", HugeDimensions);
Run("forward drag vectors and legacy history", ForwardDragVectors);
Run("chronological mask actions and restore", MaskActionsAndRestore);
Run("document dimension and memory preflight", DocumentPreflight);
Run("forward mesh direction residual edges and folds", ForwardMesh);
Console.WriteLine("All Liquify deformation regressions passed.");
if (args.Contains("--benchmark", StringComparer.Ordinal))
    Benchmark();

static void Run(string name, Action test)
{
    test();
    Console.WriteLine($"PASS {name}");
}

static void IncrementalMaskInversion()
{
    const int width = 40;
    const int height = 40;
    BrushStroke freeze = Stroke(LiquifiedMode.Freeze, 12, 1, new[] { new[] { 20f, 20f } });
    BrushStroke invert = new() { Action = LiquifiedHistoryAction.MaskInvert };
    BrushStroke drag = Stroke(LiquifiedMode.ForwardWarp, 20, 1,
        new[] { new[] { 20f, 20f, 1f, 3f, 0f } });
    bool[] boundaryMask = new bool[width * height];
    float[] boundaryMap = LiquifiedDeformation.BuildSampleMap(width, height, new[] { freeze },
        0, 20, 1, maskWorkspace: boundaryMask);
    bool[] savedBoundary = (bool[])boundaryMask.Clone();
    bool[] fullMask = new bool[width * height];
    float[] full = LiquifiedDeformation.BuildSampleMap(width, height, new[] { freeze, invert, drag },
        0, 20, 1, maskWorkspace: fullMask);
    bool[] incrementalMask = new bool[width * height];
    float[] incremental = LiquifiedDeformation.BuildSampleMap(width, height, new[] { invert, drag },
        0, 20, 1, boundaryMask, initialSampleMap: boundaryMap, maskWorkspace: incrementalMask);
    Assert(full.SequenceEqual(incremental) && fullMask.SequenceEqual(incrementalMask),
        "Coalesced mask inversion and painting diverged from full replay.");
    Assert(boundaryMask.SequenceEqual(savedBoundary), "Incremental replay mutated its cached boundary mask.");
    float[] incorrect = LiquifiedDeformation.BuildSampleMap(width, height, new[] { invert, drag },
        0, 20, 1, fullMask, initialSampleMap: boundaryMap, maskWorkspace: new bool[width * height]);
    Assert(!full.SequenceEqual(incorrect), "Regression fixture failed to detect using the post-inversion mask.");
    bool[] finalMask = new bool[width * height];
    float[] next = LiquifiedDeformation.BuildSampleMap(width, height, new[] { invert, drag },
        0, 20, 1, incrementalMask, initialSampleMap: incremental, maskWorkspace: finalMask);
    float[] fullNext = LiquifiedDeformation.BuildSampleMap(width, height, new[] { freeze, invert, drag, invert, drag },
        0, 20, 1, maskWorkspace: new bool[width * height]);
    Assert(next.SequenceEqual(fullNext), "A second cached inversion diverged from full replay.");
}

static void HugeDimensions()
{
    long before = GC.GetAllocatedBytesForCurrentThread();
    Throws<OverflowException>(() =>
        LiquifiedDeformation.BuildSampleMap(int.MaxValue, int.MaxValue, Array.Empty<BrushStroke>(), 0, 20, 1));
    Throws<ArgumentOutOfRangeException>(() =>
        LiquifiedDeformation.BuildSampleMap(-1, 2160, Array.Empty<BrushStroke>(), 0, 20, 1));
    long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    Assert(allocated < 1_000_000, "Invalid dimensions allocated an image-sized deformation buffer.");
}

static void DocumentPreflight()
{
    LiquifiedDeformation.ValidateDocumentSize(3840, 2160);
    Throws<NotSupportedException>(() => LiquifiedDeformation.ValidateDocumentSize(16385, 1));
    Throws<NotSupportedException>(() => LiquifiedDeformation.ValidateDocumentSize(7680, 4320));
    Throws<NotSupportedException>(() => LiquifiedDeformation.ValidateDocumentSize(5000, 2, 4096));
}

static void ForwardMesh()
{
    const int width = 64;
    const int height = 64;
    float[] identity = LiquifiedDeformation.BuildSampleMap(width, height, Array.Empty<BrushStroke>(), 0, 20, 1);
    foreach ((float x, float y) in new[] { (0f, 0f), (63f, 63f), (17.5f, 28.25f) })
    {
        Assert(LiquifiedDeformation.TryGetForwardMeshPoint(identity, width, height, x, y, out float ox, out float oy) &&
            Math.Abs(ox - x) < 0.001f && Math.Abs(oy - y) < 0.001f,
            "Identity mesh did not preserve vertices including edges.");
    }
    BrushStroke right = Stroke(LiquifiedMode.ForwardWarp, 48, 1,
        new[] { new[] { 32f, 32f, 1f, 4f, 0f } });
    float[] map = LiquifiedDeformation.BuildSampleMap(width, height, new[] { right }, 0, 20, 1);
    Assert(LiquifiedDeformation.TryGetForwardMeshPoint(map, width, height, 32, 32, out float outputX, out float outputY) &&
        outputX > 32 && Math.Abs(outputY - 32) < 0.1f,
        "Forward mesh moved opposite a rightward content drag.");
    int ix = (int)outputX;
    int iy = (int)outputY;
    float fx = outputX - ix;
    float mappedX = map[(iy * width + ix) * 2] * (1 - fx) + map[(iy * width + ix + 1) * 2] * fx;
    Assert(Math.Abs(mappedX - 32) <= 0.1f, "Solved mesh vertex has excessive inverse-map residual.");

    float[] fold = (float[])identity.Clone();
    for (int y = 0; y < height; y++)
    for (int x = 0; x < width; x++)
        fold[(y * width + x) * 2] = width - 1 - x;
    Assert(!LiquifiedDeformation.TryGetForwardMeshPoint(fold, width, height, 32, 32, out _, out _),
        "A folded map was incorrectly presented as an unambiguous forward mesh.");
    float[] displaced = (float[])identity.Clone();
    for (int i = 0; i < displaced.Length; i += 2)
        displaced[i] += 100;
    Assert(!LiquifiedDeformation.TryGetForwardMeshPoint(displaced, width, height, 0, 0, out _, out _),
        "An unreachable strong-warp vertex was incorrectly plotted.");
}

static void ForwardDragVectors()
{
    int sample = (20 * 40 + 20) * 2;
    BrushStroke right = Stroke(LiquifiedMode.ForwardWarp, 20, 1,
        new[] { new[] { 20f, 20f, 1f, 3f, 0f } });
    BrushStroke left = Stroke(LiquifiedMode.ForwardWarp, 20, 1,
        new[] { new[] { 20f, 20f, 1f, -3f, 0f } });
    float[] rightMap = LiquifiedDeformation.BuildSampleMap(40, 40, new[] { right }, 0, 20, 1);
    float[] leftMap = LiquifiedDeformation.BuildSampleMap(40, 40, new[] { left }, 0, 20, 1);
    Assert(rightMap[sample] == 17 && leftMap[sample] == 23,
        "Inverse forward-warp sampling did not move opposite the supplied drag vector.");
    Assert(rightMap[sample + 1] == 20 && leftMap[sample + 1] == 20,
        "A horizontal drag changed the vertical source coordinate.");
    BrushStroke legacy = Stroke(LiquifiedMode.ForwardWarp, 20, 1,
        new[] { new[] { 17f, 20f }, new[] { 20f, 20f } });
    float[] legacyMap = LiquifiedDeformation.BuildSampleMap(40, 40, new[] { legacy }, 0, 20, 1);
    Assert(legacyMap.SequenceEqual(rightMap), "Legacy point history did not derive its movement from adjacent points.");
    BrushStroke stationary = Stroke(LiquifiedMode.ForwardWarp, 20, 1,
        new[] { new[] { 20f, 20f }, new[] { 20f, 20f } });
    float[] still = LiquifiedDeformation.BuildSampleMap(40, 40, new[] { stationary }, 0, 20, 1);
    Assert(still[sample] == 20 && still[sample + 1] == 20, "Stationary forward warp should not invent a drag vector.");
}

static void MaskActionsAndRestore()
{
    BrushStroke drag = Stroke(LiquifiedMode.ForwardWarp, 20, 1,
        new[] { new[] { 20f, 20f, 1f, 3f, 0f } });
    bool[] mask = new bool[40 * 40];
    float[] before = LiquifiedDeformation.BuildSampleMap(40, 40, new[] { drag }, 0, 20, 1);
    float[] frozen = LiquifiedDeformation.BuildSampleMap(40, 40,
        new[] { drag, new BrushStroke { Action = LiquifiedHistoryAction.MaskAll }, drag },
        0, 20, 1, maskWorkspace: mask);
    Assert(frozen.SequenceEqual(before), "Mask All retroactively removed a prior deformation or failed to block the later drag.");
    float[] restored = LiquifiedDeformation.BuildSampleMap(40, 40,
        new[] { drag, new BrushStroke { Action = LiquifiedHistoryAction.MaskAll },
            new BrushStroke { Action = LiquifiedHistoryAction.RestoreAll } },
        0, 20, 1, maskWorkspace: mask);
    Assert(mask.All(value => value), "Restore All erased the separate freeze mask.");
    Assert(restored[(20 * 40 + 20) * 2] == 20, "Restore All did not return the field to identity.");
    float[] unmasked = LiquifiedDeformation.BuildSampleMap(40, 40,
        new[] { new BrushStroke { Action = LiquifiedHistoryAction.MaskAll },
            new BrushStroke { Action = LiquifiedHistoryAction.MaskInvert }, drag },
        0, 20, 1, maskWorkspace: mask);
    Assert(!mask.Any(value => value) && unmasked.SequenceEqual(before),
        "Chronological mask inversion failed to re-enable the drag.");
}

static void Benchmark()
{
    const int width = 3840;
    const int height = 2160;
    BrushStroke dab = Stroke(LiquifiedMode.TwistCW, 20, 0.01,
        new[] { new[] { width / 2f, height / 2f, 1f } });
    BrushStroke history = Stroke(LiquifiedMode.TwistCW, 20, 0.01,
        Enumerable.Range(0, 300).Select(i => new[] { width / 2f + i % 20, height / 2f, 1f }));

    Console.WriteLine("BENCHMARK 3840x2160: full-resolution map allocation is included; no performance thresholds.");
    Measure("single local dab", () =>
        LiquifiedDeformation.BuildSampleMap(width, height, new[] { dab }, 0, 20, 1));
    Measure("300-point full-history replay", () =>
        LiquifiedDeformation.BuildSampleMap(width, height, new[] { history }, 0, 20, 1));
}

static void Measure(string name, Func<float[]> operation)
{
    long before = GC.GetAllocatedBytesForCurrentThread();
    Stopwatch watch = Stopwatch.StartNew();
    float[] map = operation();
    watch.Stop();
    long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    Console.WriteLine($"BENCHMARK {name}: {watch.Elapsed.TotalMilliseconds:F1} ms; " +
        $"{allocated:N0} allocated bytes; {map.Length:N0} float entries.");
    GC.KeepAlive(map);
}

static void MoreThan256Points()
{
    List<float[]> points = new();
    for (int i = 0; i < 300; i++)
        points.Add(new[] { 5f + i * 3f, 20f, 1f, 1f, 0f });
    float[] map = LiquifiedDeformation.BuildSampleMap(920, 48,
        new[] { Stroke(LiquifiedMode.ForwardWarp, 8, 1, points) }, 0, 20, 1);
    int lastPoint = 5 + 299 * 3;
    Assert(map[(20 * 920 + lastPoint + 1) * 2] != lastPoint + 1,
        "A point after index 255 did not deform its local sample map.");
}

static void FreezeBoundary()
{
    bool[] mask = new bool[64 * 64];
    mask[32 * 64 + 33] = true;
    float[] map = LiquifiedDeformation.BuildSampleMap(64, 64,
        new[] { Stroke(LiquifiedMode.ForwardWarp, 24, 1, new[] { new[] { 32f, 32f, 1f, 1f, 0f } }) },
        0, 20, 1, mask);
    int frozen = (32 * 64 + 33) * 2;
    int adjacent = (32 * 64 + 34) * 2;
    Assert(map[frozen] == 33 && map[frozen + 1] == 32, "The protected boundary pixel moved.");
    Assert(map[adjacent] != 34, "The adjacent unprotected pixel was incorrectly frozen.");
}

static void ChronologicalFreezeUnfreeze()
{
    BrushStroke firstWarp = Stroke(LiquifiedMode.ForwardWarp, 16, 0.6, new[] { new[] { 8f, 8f, 1f, 1f, 0f } });
    BrushStroke freeze = Stroke(LiquifiedMode.Freeze, 8, 1, new[] { new[] { 12f, 8f } });
    BrushStroke secondWarp = Stroke(LiquifiedMode.ForwardWarp, 16, 0.6, new[] { new[] { 8f, 8f, 1f, 1f, 0f } });
    int pixel = (8 * 24 + 12) * 2;
    bool[] workspace = new bool[24 * 24];
    float[] once = LiquifiedDeformation.BuildSampleMap(24, 24, new[] { firstWarp }, 0, 20, 1);
    float[] protectedTwice = LiquifiedDeformation.BuildSampleMap(24, 24,
        new[] { firstWarp, freeze, secondWarp }, 0, 20, 1, maskWorkspace: workspace);
    Assert(workspace[8 * 24 + 12], "Freeze did not update the separate pixel mask.");
    Assert(Math.Abs(protectedTwice[pixel] - once[pixel]) < 1e-5f &&
        Math.Abs(protectedTwice[pixel + 1] - once[pixel + 1]) < 1e-5f,
        "A later warp changed a pixel frozen after the first warp.");

    BrushStroke unfreeze = Stroke(LiquifiedMode.Unfreeze, 8, 1, new[] { new[] { 12f, 8f } });
    float[] unprotected = LiquifiedDeformation.BuildSampleMap(24, 24,
        new[] { firstWarp, freeze, unfreeze, secondWarp }, 0, 20, 1, maskWorkspace: workspace);
    Assert(!workspace[8 * 24 + 12], "Unfreeze did not clear the separate mask.");
    Assert(Math.Abs(unprotected[pixel] - protectedTwice[pixel]) > 0.01f ||
        Math.Abs(unprotected[pixel + 1] - protectedTwice[pixel + 1]) > 0.01f,
        "Unfreezing did not allow the later warp to act.");
}

static void StrokeOrdering()
{
    BrushStroke forward = Stroke(LiquifiedMode.ForwardWarp, 24, 0.75, new[] { new[] { 30f, 30f, 1f, 4f, 0f } });
    BrushStroke twist = Stroke(LiquifiedMode.TwistCW, 24, 0.75, new[] { new[] { 30f, 30f } });
    float[] first = LiquifiedDeformation.BuildSampleMap(64, 64, new[] { forward, twist }, 0, 20, 1);
    float[] second = LiquifiedDeformation.BuildSampleMap(64, 64, new[] { twist, forward }, 0, 20, 1);
    int sample = (35 * 64 + 35) * 2;
    Assert(Math.Abs(first[sample] - second[sample]) + Math.Abs(first[sample + 1] - second[sample + 1]) > 0.01f,
        "Changing the order of non-commuting strokes did not change the sample map.");
}

static void ReconstructAcrossBatchBoundary()
{
    List<BrushStroke> history = new();
    for (int i = 0; i < 17; i++)
        history.Add(Stroke(LiquifiedMode.ForwardWarp, 30, 0.4, new[] { new[] { 32f, 32f, 1f, 0.5f, 0f } }));
    float[] distorted = LiquifiedDeformation.BuildSampleMap(64, 64, history, 0, 20, 1);
    history.Add(Stroke(LiquifiedMode.Reconstruct, 30, 1, new[] { new[] { 32f, 32f } }));
    float[] restored = LiquifiedDeformation.BuildSampleMap(64, 64, history, 0, 20, 1);
    int sample = (35 * 64 + 35) * 2;
    Assert(distorted[sample] != restored[sample] || distorted[sample + 1] != restored[sample + 1],
        "Reconstruct after 16 prior points did not use the complete deformation map.");
    Assert(Math.Abs(restored[sample] - 35) < Math.Abs(distorted[sample] - 35),
        "Reconstruct did not move the source coordinate toward the original output coordinate.");
}

static void TurbulenceFraction()
{
    float[] map = LiquifiedDeformation.BuildSampleMap(8, 8,
        new[] { Stroke(LiquifiedMode.Turbulence, 8, 1, new[] { new[] { 3f, 3f } }) },
        0, 20, 1);
    int sample = (2 * 8 + 3) * 2;
    float hash = MathF.Sin(3 * 12.9898f + 2 * 78.233f) * 43758.5453f;
    float frac = hash - MathF.Floor(hash);
    float t = 1 - 1f / 4;
    float falloff = MathF.Pow(t * t * (3 - 2 * t), 1.5f);
    float expectedX = 3 + (frac - 0.5f) * falloff * 15f;
    Assert(Math.Abs(map[sample] - expectedX) < 0.001f,
        "Turbulence did not use HLSL Frac semantics for negative hash inputs.");
}

static void GlobalReconstructAmount()
{
    BrushStroke warp = Stroke(LiquifiedMode.Bloat, 24, 0.7, new[] { new[] { 32f, 32f } });
    float[] noReconstruct = LiquifiedDeformation.BuildSampleMap(64, 64, new[] { warp }, 0, 20, 1);
    float[] fullyReconstructed = LiquifiedDeformation.BuildSampleMap(64, 64, new[] { warp }, 1, 20, 1);
    int sample = (35 * 64 + 35) * 2;
    Assert(noReconstruct[sample] != fullyReconstructed[sample] || noReconstruct[sample + 1] != fullyReconstructed[sample + 1],
        "Global reconstruct amount did not alter the deformation field.");
    Assert(fullyReconstructed[sample] == 35 && fullyReconstructed[sample + 1] == 35,
        "Full global reconstruct did not return to identity.");
}

static void MaskOperations()
{
    bool[] mask = { true, false, true, false };
    LiquifiedDeformation.ApplyMaskOperation(mask, LiquifiedMaskOperation.Invert);
    Assert(mask.SequenceEqual(new[] { false, true, false, true }), "Invert did not flip each mask pixel.");
    LiquifiedDeformation.ApplyMaskOperation(mask, LiquifiedMaskOperation.All);
    Assert(mask.All(value => value), "All did not protect the complete mask.");
    LiquifiedDeformation.ApplyMaskOperation(mask, LiquifiedMaskOperation.None);
    Assert(mask.All(value => !value), "None did not clear the complete mask.");
}

static void PressureAndRate()
{
    BrushStroke zeroPressure = Stroke(LiquifiedMode.ForwardWarp, 20, 1, new[] { new[] { 30f, 30f, 0f, 5f, 0f } });
    BrushStroke fullPressure = Stroke(LiquifiedMode.ForwardWarp, 20, 1, new[] { new[] { 30f, 30f, 1f, 5f, 0f } });
    float[] zero = LiquifiedDeformation.BuildSampleMap(64, 64, new[] { zeroPressure }, 0, 1, 1);
    float[] full = LiquifiedDeformation.BuildSampleMap(64, 64, new[] { fullPressure }, 0, 120, 1);
    int sample = (30 * 64 + 34) * 2;
    Assert(zero[sample] == 34 && zero[sample + 1] == 30, "Zero pen pressure still affected the brush.");
    Assert(full[sample] != 34, "Full pen pressure did not affect the brush.");
    LiquifiedDeformation.Validate(64, 64, Array.Empty<BrushStroke>(), 0, 120, 4);
    Throws<ArgumentOutOfRangeException>(() =>
        LiquifiedDeformation.Validate(64, 64, Array.Empty<BrushStroke>(), 0, 0, 1));
}

static void PixelCenterConvention()
{
    float[] identity = LiquifiedDeformation.BuildSampleMap(5, 4, Array.Empty<BrushStroke>(), 0, 20, 1);
    float[] texture = LiquifiedDeformation.BuildDisplacementTexture(identity, 5, 4);
    for (int y = 0; y < 4; y++)
    for (int x = 0; x < 5; x++)
    {
        int offset = (y * 5 + x) * 2;
        int textureOffset = (y * 5 + x) * 4;
        Assert(identity[offset] == x && identity[offset + 1] == y,
            "Identity map entries must store integer pixel-index coordinates; the D2D texture uses texel centers at x+0.5/y+0.5.");
        Assert(texture[textureOffset] == 0 && texture[textureOffset + 1] == 0,
            "Identity displacements must be zero at all texel centers and edges.");
    }
}

static void IncrementalEqualsReplay()
{
    BrushStroke first = Stroke(LiquifiedMode.TwistCCW, 26, 0.7, new[] { new[] { 28f, 30f } });
    BrushStroke second = Stroke(LiquifiedMode.Pucker, 24, 0.5, new[] { new[] { 32f, 30f } });
    BrushStroke third = Stroke(LiquifiedMode.Turbulence, 18, 0.4, new[] { new[] { 30f, 29f } });
    float[] replay = LiquifiedDeformation.BuildSampleMap(64, 64, new[] { first, second, third }, 0, 20, 1);
    float[] incremental = LiquifiedDeformation.BuildSampleMap(64, 64, new[] { first }, 0, 20, 1);
    incremental = LiquifiedDeformation.BuildSampleMap(64, 64, new[] { second }, 0, 20, 1,
        initialSampleMap: incremental);
    incremental = LiquifiedDeformation.BuildSampleMap(64, 64, new[] { third }, 0, 20, 1,
        initialSampleMap: incremental);
    Assert(replay.SequenceEqual(incremental), "Incremental map updates differed from full chronological replay.");
}

static void RejectMalformedTokens()
{
    BrushStroke nanStrength = Stroke(LiquifiedMode.ForwardWarp, 10, double.NaN,
        new[] { new[] { 10f, 10f } });
    Throws<ArgumentException>(() =>
        LiquifiedDeformation.Validate(20, 20, new[] { nanStrength }, 0, 20, 1));
    BrushStroke nanPoint = Stroke(LiquifiedMode.ForwardWarp, 10, 1,
        new[] { new[] { float.NaN, 10f } });
    Throws<ArgumentException>(() =>
        LiquifiedDeformation.Validate(20, 20, new[] { nanPoint }, 0, 20, 1));
}

static void StationaryApplication()
{
    BrushStroke oneApplication = Stroke(LiquifiedMode.Bloat, 20, 0.4, new[] { new[] { 20f, 20f } });
    BrushStroke repeated = Stroke(LiquifiedMode.Bloat, 20, 0.4,
        new[] { new[] { 20f, 20f }, new[] { 20f, 20f }, new[] { 20f, 20f } });
    float[] one = LiquifiedDeformation.BuildSampleMap(40, 40, new[] { oneApplication }, 0, 20, 1);
    float[] rateApplied = LiquifiedDeformation.BuildSampleMap(40, 40, new[] { repeated }, 0, 120, 1);
    int sample = (20 * 40 + 23) * 2;
    Assert(one[sample] != rateApplied[sample],
        "Repeated stationary applications did not accumulate at the configured time-based rate.");
}

static BrushStroke Stroke(LiquifiedMode mode, int size, double strength, IEnumerable<float[]> points) => new()
{
    Mode = mode,
    BrushSize = size,
    Strength = strength,
    Density = 0.5,
    Points = points.ToList()
};

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static void Throws<TException>(Action action) where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }
    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}
