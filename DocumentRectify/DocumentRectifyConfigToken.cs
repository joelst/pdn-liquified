using PaintDotNet.Effects;
using System;

namespace DocumentRectifyPlugin;

/// <summary>
/// Aspect ratio handling for the rectified output.
/// </summary>
public enum DocumentRectifyAspectMode
{
  /// <summary>Stretch quad to fill the entire canvas (no aspect preservation).</summary>
  FillCanvas = 0,
  /// <summary>Use the apparent aspect ratio of the dragged quad.</summary>
  PreserveDetected = 1,
  /// <summary>Use the source image canvas aspect ratio.</summary>
  Source = 2,
  /// <summary>1:1 square.</summary>
  Square = 3,
  /// <summary>US Letter (8.5 x 11).</summary>
  Letter = 4,
  /// <summary>US Legal (8.5 x 14).</summary>
  Legal = 5,
  /// <summary>ISO A4 (1 : sqrt(2)).</summary>
  A4 = 6,
  /// <summary>User-specified width/height ratio.</summary>
  Custom = 7
}

/// <summary>
/// Config token for document rectification.
/// Corner order is TL, TR, BR, BL in normalized [0..1] coordinates.
/// </summary>
[Serializable]
public sealed class DocumentRectifyConfigToken : EffectConfigToken
{
  public int Quality { get; set; } = 2;

  public float[] Corners { get; set; } =
  {
        0.10f, 0.10f, // TL
        0.90f, 0.10f, // TR
        0.90f, 0.90f, // BR
        0.10f, 0.90f  // BL
    };

  public DocumentRectifyAspectMode AspectMode { get; set; } = DocumentRectifyAspectMode.PreserveDetected;

  /// <summary>Custom width/height aspect ratio. Used when <see cref="AspectMode"/> is Custom.</summary>
  public float CustomAspect { get; set; } = 8.5f / 11f;

  public DocumentRectifyConfigToken()
  {
  }

  private DocumentRectifyConfigToken(DocumentRectifyConfigToken copyMe)
      : base(copyMe)
  {
    Quality = copyMe.Quality;
    Corners = (float[])copyMe.Corners.Clone();
    AspectMode = copyMe.AspectMode;
    CustomAspect = copyMe.CustomAspect;
  }

  public override object Clone() => new DocumentRectifyConfigToken(this);
}
