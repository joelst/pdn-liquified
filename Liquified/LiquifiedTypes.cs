using System;
using System.Collections.Generic;

namespace LiquifiedPlugin;

public enum LiquifiedHistoryAction
{
    Brush,
    MaskNone,
    MaskAll,
    MaskInvert,
    RestoreAll
}

/// <summary>Liquified brush modes. Numeric values are persisted in effect tokens.</summary>
public enum LiquifiedMode
{
    ForwardWarp = 0,
    Pucker = 1,
    Bloat = 2,
    TwistCW = 3,
    TwistCCW = 4,
    PushLeft = 5,
    Reconstruct = 6,
    Turbulence = 7,
    Freeze = 8,
    Unfreeze = 9
}

/// <summary>A brush stroke and its ordered application points.</summary>
[Serializable]
public sealed class BrushStroke
{
    public LiquifiedHistoryAction Action { get; set; }
    public LiquifiedMode Mode { get; set; }
    public int BrushSize { get; set; }
    public double Strength { get; set; }
    public double Density { get; set; }
    public List<float[]> Points { get; set; } = new();
}
