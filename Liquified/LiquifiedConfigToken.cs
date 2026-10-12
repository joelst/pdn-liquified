using PaintDotNet.Effects;
using System;
using System.Collections.Generic;

namespace LiquifiedPlugin;

/// <summary>
/// Strongly-typed config token carrying all Liquified effect parameters and brush strokes.
/// </summary>
[Serializable]
public sealed class LiquifiedConfigToken : EffectConfigToken
{
    public LiquifiedMode Mode { get; set; }
    public int BrushSize { get; set; } = 80;
    public double Strength { get; set; } = 0.5;
    public double Density { get; set; } = 0.5;
    public int Rate { get; set; } = 20;
    public double ReconstructAmount { get; set; }
    public int Quality { get; set; } = 2;
    public List<BrushStroke> Strokes { get; set; } = new();
    public bool[] FrozenMask { get; set; } = Array.Empty<bool>();
    public bool FrozenMaskIsInitial { get; set; }

    public LiquifiedConfigToken()
    {
    }

    private LiquifiedConfigToken(LiquifiedConfigToken copyMe)
        : base(copyMe)
    {
        Mode = copyMe.Mode;
        BrushSize = copyMe.BrushSize;
        Strength = copyMe.Strength;
        Density = copyMe.Density;
        Rate = copyMe.Rate;
        ReconstructAmount = copyMe.ReconstructAmount;
        Quality = copyMe.Quality;
        FrozenMask = (bool[])(copyMe.FrozenMask ?? Array.Empty<bool>()).Clone();
        FrozenMaskIsInitial = copyMe.FrozenMaskIsInitial;

        // Deep copy strokes
        Strokes = new List<BrushStroke>(copyMe.Strokes?.Count ?? 0);
        if (copyMe.Strokes is not null)
        {
            foreach (var s in copyMe.Strokes)
            {
                if (s is null)
                    continue;

                var clone = new BrushStroke
                {
                    Action = s.Action,
                    Mode = s.Mode,
                    BrushSize = s.BrushSize,
                    Strength = s.Strength,
                    Density = s.Density,
                    Points = new List<float[]>(s.Points?.Count ?? 0)
                };
                if (s.Points is not null)
                {
                    foreach (var pt in s.Points)
                    {
                        if (pt is not null)
                            clone.Points.Add((float[])pt.Clone());
                    }
                }
                Strokes.Add(clone);
            }
        }
    }

    public override object Clone() => new LiquifiedConfigToken(this);
}
