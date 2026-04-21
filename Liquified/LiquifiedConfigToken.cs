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
    public int Quality { get; set; } = 2;
    public List<BrushStroke> Strokes { get; set; } = new();

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
        Quality = copyMe.Quality;

        // Deep copy strokes
        Strokes = new List<BrushStroke>(copyMe.Strokes.Count);
        foreach (var s in copyMe.Strokes)
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
            {
                clone.Points.Add((float[])pt.Clone());
            }
            Strokes.Add(clone);
        }
    }

    public override object Clone() => new LiquifiedConfigToken(this);
}
