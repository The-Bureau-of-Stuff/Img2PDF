using Img2PDF.Core.Pdf;

namespace Img2PDF.Core.Tests;

public class DeskewCompensatingScaleTests
{
    private const double Tolerance = 0.0001;

    [Fact]
    public void ZeroAngle_ScaleIsOne()
    {
        double scale = PdfComposer.ComputeDeskewCompensatingScale(width: 600, height: 800, extraRotationDegrees: 0);

        Assert.Equal(1.0, scale, Tolerance);
    }

    [Fact]
    public void SquareRotated45Degrees_ScaleIsOneOverSqrtTwo()
    {
        // For a square, rotating by 45 degrees grows the bounding box by exactly sqrt(2).
        double scale = PdfComposer.ComputeDeskewCompensatingScale(width: 500, height: 500, extraRotationDegrees: 45);

        Assert.Equal(1.0 / Math.Sqrt(2), scale, Tolerance);
    }

    [Fact]
    public void WideRectRotated90Degrees_ScaleIsAspectRatio()
    {
        // At 90 degrees the bounding box exactly swaps width/height, so the compensating scale
        // is just the smaller-to-larger aspect ratio.
        double scale = PdfComposer.ComputeDeskewCompensatingScale(width: 200, height: 100, extraRotationDegrees: 90);

        Assert.Equal(0.5, scale, Tolerance);
    }

    [Fact]
    public void NegativeAndPositiveAngleOfSameMagnitude_ProduceTheSameScale()
    {
        double positive = PdfComposer.ComputeDeskewCompensatingScale(width: 612, height: 792, extraRotationDegrees: 3.5);
        double negative = PdfComposer.ComputeDeskewCompensatingScale(width: 612, height: 792, extraRotationDegrees: -3.5);

        Assert.Equal(positive, negative, Tolerance);
    }

    [Fact]
    public void SmallDeskewAngle_ScaleIsCloseToButBelowOne()
    {
        // A realistic deskew correction (a few degrees) shrinks the image somewhat, but the
        // aspect ratio matters — a tall page loses more of its shorter (width) dimension to the
        // rotated bounding box. Expected value below is the formula's own result for this exact
        // case (612x792 Letter-portrait-ish rect at 3.5 degrees), computed independently by hand.
        double scale = PdfComposer.ComputeDeskewCompensatingScale(width: 612, height: 792, extraRotationDegrees: 3.5);

        Assert.Equal(0.9284, scale, 0.001);
    }
}
