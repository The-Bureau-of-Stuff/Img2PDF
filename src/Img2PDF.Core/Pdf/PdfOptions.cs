namespace Img2PDF.Core.Pdf;

public enum PageSizeOption
{
    A4,
    Letter,
    MatchImage,
}

public enum MarginsOption
{
    None,
    Narrow,
    Standard,
}

public enum OrientationOption
{
    Auto,
    ForcePortrait,
}

public enum QualityOption
{
    Original,
    High,
    Medium,
    Small,
}

public sealed record PdfOptions(
    PageSizeOption PageSize = PageSizeOption.A4,
    MarginsOption Margins = MarginsOption.None,
    OrientationOption Orientation = OrientationOption.Auto,
    QualityOption Quality = QualityOption.Original,
    bool Greyscale = false,
    bool Searchable = false);
