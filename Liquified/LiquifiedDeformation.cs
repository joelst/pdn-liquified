using System;
using System.Collections.Generic;
using System.Threading;

namespace LiquifiedPlugin;

internal enum LiquifiedMaskOperation
{
    None,
    All,
    Invert
}

/// <summary>
/// Builds the inverse sample map used by both the preview and final GPU renderer.
/// Values are source coordinates (x,y) for each output pixel, in row-major order.
/// </summary>
internal static class LiquifiedDeformation
{
    internal static bool TryGetForwardMeshPoint(float[] map, int width, int height,
        float sourceX, float sourceY, out float outputX, out float outputY)
    {
        outputX = outputY = 0;
        if (width < 2 || height < 2 || !float.IsFinite(sourceX) || !float.IsFinite(sourceY))
            return false;
        outputX = Math.Clamp(sourceX, 0, width - 1);
        outputY = Math.Clamp(sourceY, 0, height - 1);
        if (map.Length != checked(width * height * 2) || width < 2 || height < 2)
            return false;

        for (int iteration = 0; iteration < 40; iteration++)
        {
            SampleMap(map, width, height, outputX, outputY,
                out float sx, out float sy, out float jxx, out float jxy, out float jyx, out float jyy);
            float determinant = jxx * jyy - jxy * jyx;
            // Folded or singular inverse-map cells do not define a unique forward mesh.
            if (!float.IsFinite(determinant) || determinant <= 0.0001f)
                return false;
            float rx = sourceX - sx;
            float ry = sourceY - sy;
            float error = rx * rx + ry * ry;
            if (error <= 0.01f)
                return true;
            float dx = (jyy * rx - jxy * ry) / determinant;
            float dy = (jxx * ry - jyx * rx) / determinant;
            float length = MathF.Sqrt(dx * dx + dy * dy);
            if (!float.IsFinite(length))
                return false;
            if (length > 32)
            {
                dx *= 32 / length;
                dy *= 32 / length;
            }
            bool improved = false;
            for (int search = 0; search < 12; search++)
            {
                float candidateX = Math.Clamp(outputX + dx, 0, width - 1);
                float candidateY = Math.Clamp(outputY + dy, 0, height - 1);
                SampleMap(map, width, height, candidateX, candidateY,
                    out float candidateSx, out float candidateSy, out _, out _, out _, out _);
                float ex = sourceX - candidateSx;
                float ey = sourceY - candidateSy;
                if (ex * ex + ey * ey < error)
                {
                    outputX = candidateX;
                    outputY = candidateY;
                    improved = true;
                    break;
                }
                dx *= 0.5f;
                dy *= 0.5f;
            }
            if (!improved)
                return false;
        }
        return false;
    }

    private static void SampleMap(float[] map, int width, int height, float x, float y,
        out float sx, out float sy, out float jxx, out float jxy, out float jyx, out float jyy)
    {
        int ix = Math.Min((int)x, width - 2);
        int iy = Math.Min((int)y, height - 2);
        float fx = x - ix;
        float fy = y - iy;
        int a = (iy * width + ix) * 2;
        int b = a + 2;
        int c = a + width * 2;
        int d = c + 2;
        float topX = map[a] + (map[b] - map[a]) * fx;
        float topY = map[a + 1] + (map[b + 1] - map[a + 1]) * fx;
        float bottomX = map[c] + (map[d] - map[c]) * fx;
        float bottomY = map[c + 1] + (map[d + 1] - map[c + 1]) * fx;
        sx = topX + (bottomX - topX) * fy;
        sy = topY + (bottomY - topY) * fy;
        jxx = (map[b] - map[a]) * (1 - fy) + (map[d] - map[c]) * fy;
        jyx = (map[b + 1] - map[a + 1]) * (1 - fy) + (map[d + 1] - map[c + 1]) * fy;
        jxy = bottomX - topX;
        jyy = bottomY - topY;
    }

    internal const int MaximumDocumentDimension = 16384;
    internal const long MaximumWorkingSetBytes = 1L << 30;
    internal const int EstimatedWorkingBytesPerPixel = 128;

    internal static void ValidateDocumentSize(int width, int height, int deviceMaximumDimension = MaximumDocumentDimension)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Image dimensions must be positive.");
        _ = checked(width * height * 4);
        int maximum = Math.Min(MaximumDocumentDimension, deviceMaximumDimension);
        if (width > maximum || height > maximum)
            throw new NotSupportedException($"Liquified supports documents up to {maximum:N0} pixels per dimension on this device; no map downsampling is performed.");
        long estimate = checked((long)width * height * EstimatedWorkingBytesPerPixel);
        if (estimate > MaximumWorkingSetBytes)
            throw new NotSupportedException($"Liquified requires an estimated {estimate / (1024d * 1024):N0} MiB for this full-resolution document, exceeding its 1,024 MiB working-buffer budget. Reduce document dimensions; output is never silently downsampled.");
    }

    internal static void ValidateBrushSettings(
        LiquifiedMode mode,
        int brushSize,
        double strength,
        double density)
    {
        if (!Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(mode));
        if (brushSize is < 5 or > 1200)
            throw new ArgumentOutOfRangeException(nameof(brushSize));
        if (!double.IsFinite(strength) || strength is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(strength));
        if (!double.IsFinite(density) || density is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(density));
    }

    internal static float[] BuildDisplacementTexture(float[] sampleMap, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(sampleMap);
        int pixelCount = checked(width * height);
        if (sampleMap.Length != checked(pixelCount * 2))
            throw new ArgumentException("Sample map dimensions do not match the image.", nameof(sampleMap));

        float[] texture = new float[checked(pixelCount * 4)];
        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                int pixelIndex = row + x;
                int mapOffset = pixelIndex * 2;
                int textureOffset = pixelIndex * 4;
                float sampleX = sampleMap[mapOffset];
                float sampleY = sampleMap[mapOffset + 1];
                if (!float.IsFinite(sampleX) || !float.IsFinite(sampleY))
                    throw new ArgumentException("Sample map coordinates must be finite.", nameof(sampleMap));
                texture[textureOffset] = sampleX - x;
                texture[textureOffset + 1] = sampleY - y;
            }
        }
        return texture;
    }

    internal static void ApplyMaskOperation(bool[] mask, LiquifiedMaskOperation operation)
    {
        ArgumentNullException.ThrowIfNull(mask);
        switch (operation)
        {
            case LiquifiedMaskOperation.None:
                Array.Clear(mask);
                break;
            case LiquifiedMaskOperation.All:
                Array.Fill(mask, true);
                break;
            case LiquifiedMaskOperation.Invert:
                for (int i = 0; i < mask.Length; i++)
                    mask[i] = !mask[i];
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }

    internal static void Validate(
        int width,
        int height,
        IReadOnlyList<BrushStroke>? strokes,
        double reconstructAmount,
        int rate,
        int quality,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateDocumentSize(width, height);
        if (!double.IsFinite(reconstructAmount) || reconstructAmount < 0 || reconstructAmount > 1)
            throw new ArgumentOutOfRangeException(nameof(reconstructAmount));
        if (rate is < 1 or > 120)
            throw new ArgumentOutOfRangeException(nameof(rate));
        if (quality is < 1 or > 4)
            throw new ArgumentOutOfRangeException(nameof(quality));

        if (strokes is null)
            return;

        foreach (BrushStroke? stroke in strokes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (stroke is null)
                throw new ArgumentException("Stroke history contains a null entry.", nameof(strokes));
            if (!Enum.IsDefined(stroke.Action))
                throw new ArgumentException("Stroke history contains an unknown action.", nameof(strokes));
            if (stroke.Action != LiquifiedHistoryAction.Brush)
                continue;
            if (!Enum.IsDefined(stroke.Mode))
                throw new ArgumentException("Stroke history contains an unknown mode.", nameof(strokes));
            if (stroke.BrushSize is < 1 or > 1200)
                throw new ArgumentException("Stroke brush size is outside the supported range.", nameof(strokes));
            if (!double.IsFinite(stroke.Strength) || stroke.Strength is < 0 or > 1 ||
                !double.IsFinite(stroke.Density) || stroke.Density is < 0 or > 1)
                throw new ArgumentException("Stroke parameters must be finite values between zero and one.", nameof(strokes));

            if (stroke.Points is null)
                throw new ArgumentException("Stroke points cannot be null.", nameof(strokes));

            foreach (float[]? point in stroke.Points)
            {
                if (point is null || point.Length < 2 ||
                    !float.IsFinite(point[0]) || !float.IsFinite(point[1]) ||
                    point[0] < 0 || point[0] > width || point[1] < 0 || point[1] > height)
                    throw new ArgumentException("Stroke points must contain finite x and y coordinates.", nameof(strokes));
                if (point.Length >= 3 && (!float.IsFinite(point[2]) || point[2] < 0 || point[2] > 1))
                    throw new ArgumentException("Point pressure must be between zero and one.", nameof(strokes));
                if (point.Length == 4 || (point.Length >= 5 &&
                    (!float.IsFinite(point[3]) || !float.IsFinite(point[4]) ||
                     Math.Abs(point[3]) > width || Math.Abs(point[4]) > height)))
                    throw new ArgumentException("Brush movement must contain finite x and y components within document dimensions.", nameof(strokes));
            }
        }
    }

    internal static float[] BuildSampleMap(
        int width,
        int height,
        IReadOnlyList<BrushStroke>? strokes,
        double reconstructAmount,
        int rate,
        int quality,
        bool[]? frozenMask = null,
        CancellationToken cancellationToken = default,
        float[]? initialSampleMap = null,
        bool[]? maskWorkspace = null,
        bool applyMaskStrokes = true)
    {
        Validate(width, height, strokes, reconstructAmount, rate, quality, cancellationToken);
        int pixelCount = checked(width * height);
        if (frozenMask is not null && frozenMask.Length != pixelCount)
            throw new ArgumentException("Freeze mask dimensions do not match the image.", nameof(frozenMask));
        if (maskWorkspace is not null && maskWorkspace.Length != pixelCount)
            throw new ArgumentException("Freeze mask workspace dimensions do not match the image.", nameof(maskWorkspace));
        bool[] mask = maskWorkspace ?? new bool[pixelCount];
        if (frozenMask is null)
            Array.Clear(mask);
        else if (!ReferenceEquals(mask, frozenMask))
            Array.Copy(frozenMask, mask, pixelCount);

        int mapLength = checked(pixelCount * 2);
        if (initialSampleMap is not null && initialSampleMap.Length != mapLength)
            throw new ArgumentException("Initial sample map dimensions do not match the image.", nameof(initialSampleMap));
        float[] map = new float[mapLength];
        if (initialSampleMap is not null)
        {
            Array.Copy(initialSampleMap, map, mapLength);
        }
        else
        {
            for (int y = 0; y < height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int row = y * width;
                for (int x = 0; x < width; x++)
                {
                    int offset = (row + x) * 2;
                    map[offset] = x;
                    map[offset + 1] = y;
                }
            }
        }

        float precedingReach = 0;
        if (initialSampleMap is not null)
        {
            for (int y = 0; y < height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int row = y * width;
                for (int x = 0; x < width; x++)
                {
                    int offset = (row + x) * 2;
                    if (!float.IsFinite(map[offset]) || !float.IsFinite(map[offset + 1]))
                        throw new ArgumentException("Initial sample map coordinates must be finite.", nameof(initialSampleMap));
                    float dx = map[offset] - x;
                    float dy = map[offset + 1] - y;
                    precedingReach = Math.Max(precedingReach, MathF.Sqrt(dx * dx + dy * dy));
                }
            }
        }

        // The conservative reach bound guarantees that displaced coordinates which later
        // enter a brush are not omitted from the local ROI. A sufficiently long history
        // naturally expands the ROI to the document bounds rather than losing accuracy.

        if (strokes is not null)
        {
            foreach (BrushStroke stroke in strokes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (stroke.Action != LiquifiedHistoryAction.Brush)
                {
                    if (stroke.Action == LiquifiedHistoryAction.RestoreAll)
                    {
                        for (int y = 0; y < height; y++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            for (int x = 0; x < width; x++)
                            {
                                int index = (y * width + x) * 2;
                                map[index] = x;
                                map[index + 1] = y;
                            }
                        }
                        precedingReach = 0;
                    }
                    else
                    {
                        ApplyMaskOperation(mask, stroke.Action switch
                        {
                            LiquifiedHistoryAction.MaskNone => LiquifiedMaskOperation.None,
                            LiquifiedHistoryAction.MaskAll => LiquifiedMaskOperation.All,
                            LiquifiedHistoryAction.MaskInvert => LiquifiedMaskOperation.Invert,
                            _ => throw new ArgumentOutOfRangeException(nameof(stroke.Action))
                        });
                    }
                    continue;
                }
                float radius = Math.Max(1f, stroke.BrushSize * 0.5f);
                float strength = (float)stroke.Strength;
                float density = (float)stroke.Density;
                bool maskOperation = stroke.Mode is LiquifiedMode.Freeze or LiquifiedMode.Unfreeze;

                float[]? previousPoint = null;
                foreach (float[] point in stroke.Points)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    float centerX = point[0];
                    float centerY = point[1];
                    if (maskOperation)
                    {
                        if (applyMaskStrokes)
                            ApplyMaskPoint(mask, width, height, centerX, centerY, radius,
                                stroke.Mode == LiquifiedMode.Freeze, cancellationToken);
                        continue;
                    }

                    float pressure = point.Length >= 3 ? point[2] : 1f;
                    float moveX = point.Length >= 5 ? point[3] : previousPoint is null ? 0 : point[0] - previousPoint[0];
                    float moveY = point.Length >= 5 ? point[4] : previousPoint is null ? 0 : point[1] - previousPoint[1];
                    previousPoint = point;
                    float maxReachBeforePoint = (float)Math.Min(precedingReach, Math.Max(width, height) * 2d);
                    int left = Math.Max(0, FloorToInt(centerX - radius - maxReachBeforePoint));
                    int top = Math.Max(0, FloorToInt(centerY - radius - maxReachBeforePoint));
                    int right = Math.Min(width, CeilingToInt(centerX + radius + maxReachBeforePoint) + 1);
                    int bottom = Math.Min(height, CeilingToInt(centerY + radius + maxReachBeforePoint) + 1);
                    float radiusSquared = radius * radius;
                    for (int y = top; y < bottom; y++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        int row = y * width;
                        for (int x = left; x < right; x++)
                        {
                            int pixelIndex = row + x;
                            if (mask[pixelIndex])
                                continue;

                            int mapIndex = pixelIndex * 2;
                            float posX = map[mapIndex];
                            float posY = map[mapIndex + 1];
                            float dx = posX - centerX;
                            float dy = posY - centerY;
                            float distanceSquared = dx * dx + dy * dy;
                            if (distanceSquared > radiusSquared ||
                                (distanceSquared < 1e-6f && stroke.Mode != LiquifiedMode.ForwardWarp))
                                continue;

                            float distance = MathF.Sqrt(distanceSquared);
                            float t = 1f - distance / radius;
                            float falloff = t * t * (3f - 2f * t);
                            falloff = MathF.Pow(MathF.Abs(falloff), 2f - density);
                            float effectiveStrength = strength * pressure * falloff;
                            ApplyMode(stroke.Mode, centerX, centerY, x, y, dx, dy, distance, effectiveStrength, moveX, moveY,
                                ref posX, ref posY);
                            if (!float.IsFinite(posX) || !float.IsFinite(posY))
                                throw new ArithmeticException("Liquify deformation produced non-finite sample coordinates.");
                            map[mapIndex] = posX;
                            map[mapIndex + 1] = posY;
                        }
                    }

                    if (IsWarpMode(stroke.Mode))
                        precedingReach += stroke.Mode == LiquifiedMode.ForwardWarp
                            ? MathF.Sqrt(moveX * moveX + moveY * moveY) * strength * pressure
                            : GetMaximumDisplacement(stroke.Mode, radius, strength * pressure);
                }
            }
        }

        if (reconstructAmount > 0)
            ApplyGlobalReconstruction(map, mask, width, height, (float)reconstructAmount, cancellationToken);

        return map;
    }

    internal static void ApplyGlobalReconstruction(float[] map, bool[] mask, int width, int height,
        float amount, CancellationToken cancellationToken = default)
    {
        if (!float.IsFinite(amount) || amount < 0 || amount > 1 ||
            map.Length != checked(width * height * 2) || mask.Length != checked(width * height))
            throw new ArgumentException("Invalid reconstruction amount or map dimensions.");
        for (int y = 0; y < height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                int pixelIndex = row + x;
                if (mask[pixelIndex])
                    continue;
                int offset = pixelIndex * 2;
                map[offset] += (x - map[offset]) * amount;
                map[offset + 1] += (y - map[offset + 1]) * amount;
            }
        }
    }

    private static void ApplyMaskPoint(
        bool[] mask,
        int width,
        int height,
        float centerX,
        float centerY,
        float radius,
        bool freeze,
        CancellationToken cancellationToken)
    {
        int left = Math.Max(0, FloorToInt(centerX - radius));
        int top = Math.Max(0, FloorToInt(centerY - radius));
        int right = Math.Min(width, CeilingToInt(centerX + radius) + 1);
        int bottom = Math.Min(height, CeilingToInt(centerY + radius) + 1);
        float radiusSquared = radius * radius;
        for (int y = top; y < bottom; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            float dy = y - centerY;
            int row = y * width;
            for (int x = left; x < right; x++)
            {
                float dx = x - centerX;
                if (dx * dx + dy * dy <= radiusSquared)
                    mask[row + x] = freeze;
            }
        }
    }

    internal static bool IsWarpMode(LiquifiedMode mode) =>
        mode is not LiquifiedMode.Freeze and not LiquifiedMode.Unfreeze;

    private static float GetMaximumDisplacement(LiquifiedMode mode, float radius, float strength) =>
        mode switch
        {
            LiquifiedMode.ForwardWarp => 20f * strength,
            LiquifiedMode.Pucker => 15f * strength,
            LiquifiedMode.Bloat => 25f * strength,
            LiquifiedMode.TwistCW or LiquifiedMode.TwistCCW => 2f * radius * strength,
            LiquifiedMode.PushLeft => 20f * strength,
            LiquifiedMode.Turbulence => 15f * strength * 0.7071068f,
            _ => 0f
        };

    private static void ApplyMode(
        LiquifiedMode mode,
        float centerX,
        float centerY,
        float originalX,
        float originalY,
        float dx,
        float dy,
        float distance,
        float strength,
        float moveX,
        float moveY,
        ref float posX,
        ref float posY)
    {
        float inverseDistance = distance > 0 ? 1f / distance : 0;
        switch (mode)
        {
            case LiquifiedMode.ForwardWarp:
                posX -= moveX * strength;
                posY -= moveY * strength;
                break;
            case LiquifiedMode.Pucker:
                posX += dx * inverseDistance * strength * 15f;
                posY += dy * inverseDistance * strength * 15f;
                break;
            case LiquifiedMode.Bloat:
                posX -= dx * inverseDistance * strength * 25f;
                posY -= dy * inverseDistance * strength * 25f;
                break;
            case LiquifiedMode.TwistCW:
            case LiquifiedMode.TwistCCW:
                {
                    float angle = strength * 0.5f * (mode == LiquifiedMode.TwistCW ? 1f : -1f);
                    float cosine = MathF.Cos(angle);
                    float sine = MathF.Sin(angle);
                    posX = centerX + dx * cosine - dy * sine;
                    posY = centerY + dx * sine + dy * cosine;
                    break;
                }
            case LiquifiedMode.PushLeft:
                posX += dy * inverseDistance * strength * 20f;
                posY -= dx * inverseDistance * strength * 20f;
                break;
            case LiquifiedMode.Reconstruct:
                posX += (originalX - posX) * strength;
                posY += (originalY - posY) * strength;
                break;
            case LiquifiedMode.Turbulence:
                float hash1 = Frac(MathF.Sin(posX * 12.9898f + posY * 78.233f) * 43758.5453f);
                float hash2 = Frac(MathF.Sin(posX * 39.346f + posY * 11.135f) * 43758.5453f);
                posX += (hash1 - 0.5f) * strength * 15f;
                posY += (hash2 - 0.5f) * strength * 15f;
                break;
        }
    }

    private static float Frac(float value) => value - MathF.Floor(value);
    private static int FloorToInt(float value) => value <= int.MinValue ? int.MinValue : value >= int.MaxValue ? int.MaxValue : (int)MathF.Floor(value);
    private static int CeilingToInt(float value) => value <= int.MinValue ? int.MinValue : value >= int.MaxValue ? int.MaxValue : (int)MathF.Ceiling(value);
}
