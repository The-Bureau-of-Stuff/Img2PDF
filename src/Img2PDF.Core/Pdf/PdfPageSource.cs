namespace Img2PDF.Core.Pdf;

/// <summary>One page to compose: a source image path and its final (EXIF + user) rotation.</summary>
public sealed record PdfPageSource(string ImagePath, int RotationDegrees);
