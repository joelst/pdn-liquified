using PaintDotNet.Effects;
using System;

namespace GridWarpPlugin;

/// <summary>
/// Config token for the Grid Warpified effect, carrying grid dimensions and control point displacements.
/// </summary>
[Serializable]
public sealed class GridWarpConfigToken : EffectConfigToken
{
    /// <summary>Number of cells on the X axis. Control points per row = GridWidth+1.</summary>
    public int GridWidth { get; set; } = 4;

    /// <summary>Number of cells on the Y axis. Control points per column = GridHeight+1.</summary>
    public int GridHeight { get; set; } = 4;

    /// <summary>
    /// Legacy square-grid property kept for compatibility with older serialized tokens.
    /// Setting this updates both width and height.
    /// </summary>
    public int GridSize
    {
        get => GridWidth;
        set
        {
            GridWidth = value;
            GridHeight = value;
        }
    }

    /// <summary>RGSS quality level (1-7).</summary>
    public int Quality { get; set; } = 4;

    /// <summary>
    /// Flattened displacement array. Length = (GridWidth+1) * (GridHeight+1) * 2.
    /// Layout: [pointIndex * 2] = dx, [pointIndex * 2 + 1] = dy.
    /// Point index = row * (GridWidth+1) + col.
    /// </summary>
    public float[] Displacements { get; set; } = Array.Empty<float>();

    /// <summary>Preview editing constraints; frozen points cannot be moved.</summary>
    public bool[] FrozenPoints { get; set; } = Array.Empty<bool>();

    /// <summary>Editing groups. Zero is unlinked; equal positive IDs move together.</summary>
    public int[] LinkedGroups { get; set; } = Array.Empty<int>();

    public GridWarpConfigToken() { }

    private GridWarpConfigToken(GridWarpConfigToken copyMe) : base(copyMe)
    {
        GridWidth = copyMe.GridWidth;
        GridHeight = copyMe.GridHeight;
        Quality = copyMe.Quality;
        Displacements = copyMe.Displacements == null
            ? Array.Empty<float>()
            : (float[])copyMe.Displacements.Clone();
        FrozenPoints = copyMe.FrozenPoints == null ? Array.Empty<bool>() : (bool[])copyMe.FrozenPoints.Clone();
        LinkedGroups = copyMe.LinkedGroups == null ? Array.Empty<int>() : (int[])copyMe.LinkedGroups.Clone();
    }

    public override object Clone() => new GridWarpConfigToken(this);
}
