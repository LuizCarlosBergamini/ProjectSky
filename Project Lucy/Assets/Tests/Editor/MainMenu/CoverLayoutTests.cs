using NUnit.Framework;
using UnityEngine;

/// <summary>
/// CoverLayout.ComputeCoverSize is the math behind the background's "cover" behaviour: fill the container on
/// both axes while keeping the art's aspect ratio, cropping the overflow rather than stretching or letterboxing.
/// </summary>
public class CoverLayoutTests
{
    private const float Tolerance = 0.01f;

    private static readonly Vector2[] Screens =
    {
        new Vector2(1280, 720), new Vector2(1920, 1080), new Vector2(2560, 1080), // 16:9 and ultrawide
        new Vector2(1024, 768), new Vector2(1080, 1920), new Vector2(800, 800),    // 4:3, portrait, square
    };

    private static readonly Vector2[] Art =
    {
        new Vector2(1920, 1080), new Vector2(1024, 1024), new Vector2(3000, 1000), new Vector2(600, 1200),
    };

    [Test]
    public void Result_CoversTheContainerOnBothAxes()
    {
        foreach (var screen in Screens)
        foreach (var art in Art)
        {
            var size = CoverLayout.ComputeCoverSize(screen, art);
            Assert.That(size.x, Is.GreaterThanOrEqualTo(screen.x - Tolerance), $"screen {screen}, art {art}: width gap");
            Assert.That(size.y, Is.GreaterThanOrEqualTo(screen.y - Tolerance), $"screen {screen}, art {art}: height gap");
        }
    }

    [Test]
    public void Result_KeepsTheArtAspectRatio()
    {
        foreach (var screen in Screens)
        foreach (var art in Art)
        {
            var size = CoverLayout.ComputeCoverSize(screen, art);
            Assert.That(size.x / size.y, Is.EqualTo(art.x / art.y).Within(0.001f), $"screen {screen}, art {art}: stretched");
        }
    }

    [Test]
    public void Result_MatchesTheContainerExactlyOnOneAxis()
    {
        // Cover scales just enough, so one axis fits exactly and only the other overflows.
        foreach (var screen in Screens)
        foreach (var art in Art)
        {
            var size = CoverLayout.ComputeCoverSize(screen, art);
            var fitsWidth = Mathf.Abs(size.x - screen.x) < Tolerance;
            var fitsHeight = Mathf.Abs(size.y - screen.y) < Tolerance;
            Assert.That(fitsWidth || fitsHeight, Is.True, $"screen {screen}, art {art}: overscaled to {size}");
        }
    }

    [Test]
    public void SameAspect_FillsExactly()
    {
        var size = CoverLayout.ComputeCoverSize(new Vector2(1280, 720), new Vector2(1920, 1080));
        Assert.That(size.x, Is.EqualTo(1280f).Within(Tolerance));
        Assert.That(size.y, Is.EqualTo(720f).Within(Tolerance));
    }

    [Test]
    public void WiderArt_OverflowsHorizontally()
    {
        var size = CoverLayout.ComputeCoverSize(new Vector2(1280, 720), new Vector2(3000, 1000));
        Assert.That(size.y, Is.EqualTo(720f).Within(Tolerance));
        Assert.That(size.x, Is.EqualTo(2160f).Within(Tolerance));
    }

    [Test]
    public void TallerArt_OverflowsVertically()
    {
        var size = CoverLayout.ComputeCoverSize(new Vector2(1280, 720), new Vector2(1024, 1024));
        Assert.That(size.x, Is.EqualTo(1280f).Within(Tolerance));
        Assert.That(size.y, Is.EqualTo(1280f).Within(Tolerance));
    }

    [TestCase(0f, 0f)]
    [TestCase(0f, 100f)]
    [TestCase(100f, 0f)]
    public void ZeroSizedArt_FallsBackToTheContainer(float w, float h)
    {
        var size = CoverLayout.ComputeCoverSize(new Vector2(1280, 720), new Vector2(w, h));
        Assert.That(size, Is.EqualTo(new Vector2(1280, 720)));
    }
}
