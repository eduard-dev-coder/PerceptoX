using PerceptoX.Presentation.Services;

namespace PerceptoX.Presentation.Tests;

public sealed class ComparisonLoupeGeometryTests
{
    [Fact]
    public void LetterboxIsExcludedFromPointerCoordinates()
    {
        FittedImage image = ComparisonLoupeGeometry.Fit(400, 300, 800, 200);
        Assert.Equal(new FittedImage(0, 100, 400, 100), image);
        Assert.Null(ComparisonLoupeGeometry.Position(image, 200, 99));
        Assert.Null(ComparisonLoupeGeometry.Position(image, 200, 201));
        Assert.Equal(new RelativeImagePoint(.5, .5), ComparisonLoupeGeometry.Position(image, 200, 150));
        Assert.Equal(new RelativeImagePoint(0, 0), ComparisonLoupeGeometry.Position(image, 0, 100));
        Assert.Equal(new RelativeImagePoint(1, 1), ComparisonLoupeGeometry.Position(image, 400, 200));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public void DifferentAspectRatiosKeepTheSameFractionalPointAtLensCenter(double zoom)
    {
        RelativeImagePoint point = new(1d / 3, .75);
        foreach (FittedImage image in new[] { ComparisonLoupeGeometry.Fit(400, 300, 800, 200), ComparisonLoupeGeometry.Fit(400, 300, 10, 1000) })
        {
            LoupePlacement placement = ComparisonLoupeGeometry.Place(image, point, zoom, 128, 400, 300);
            Assert.Equal(64, placement.ImageLeft + point.X * placement.ImageWidth, precision: 8);
            Assert.Equal(64, placement.ImageTop + point.Y * placement.ImageHeight, precision: 8);
        }
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    public void EdgeLensStaysInBoxWithoutShiftingTheInspectedPoint(double x, double y)
    {
        FittedImage image = ComparisonLoupeGeometry.Fit(400, 300, 800, 600);
        LoupePlacement placement = ComparisonLoupeGeometry.Place(image, new(x, y), 4, 128, 400, 300);
        Assert.InRange(placement.Left, 0, 272); Assert.InRange(placement.Top, 0, 172);
        Assert.Equal(64, placement.ImageLeft + x * placement.ImageWidth);
        Assert.Equal(64, placement.ImageTop + y * placement.ImageHeight);
    }

    [Fact]
    public void TinyOnePixelImageHasStableCoordinates()
    {
        FittedImage image = ComparisonLoupeGeometry.Fit(400, 300, 1, 1);
        Assert.Equal(new FittedImage(50, 0, 300, 300), image);
        Assert.Equal(new RelativeImagePoint(.5, .5), ComparisonLoupeGeometry.Position(image, 200, 150));
    }

    [Fact]
    public void InvalidLayoutOrPointerDoesNotActivateLoupe()
    {
        Assert.Equal(default, ComparisonLoupeGeometry.Fit(0, 300, 1, 1));
        Assert.Equal(default, ComparisonLoupeGeometry.Fit(double.NaN, 300, 1, 1));
        Assert.Null(ComparisonLoupeGeometry.Position(default, 0, 0));
        Assert.Null(ComparisonLoupeGeometry.Position(new(0, 0, 100, 100), double.NaN, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ComparisonLoupeGeometry.Place(new(0, 0, 100, 100), new(.5, .5), 3, 128, 400, 300));
    }
}
