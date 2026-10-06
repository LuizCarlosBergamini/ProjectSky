using NUnit.Framework;

/// <summary>
/// The volume sliders map 0..1 to mixer decibels logarithmically, with 0 meaning silence.
/// </summary>
public class VolumeMappingTests
{
    private const float Tolerance = 0.01f;

    [Test]
    public void FullVolume_IsZeroDecibels()
    {
        Assert.That(VolumeMath.LinearToDecibels(1f), Is.EqualTo(0f).Within(Tolerance));
    }

    [Test]
    public void HalfVolume_IsAboutMinusSixDecibels()
    {
        Assert.That(VolumeMath.LinearToDecibels(0.5f), Is.EqualTo(-6.0206f).Within(Tolerance));
    }

    [Test]
    public void TenPercent_IsMinusTwentyDecibels()
    {
        Assert.That(VolumeMath.LinearToDecibels(0.1f), Is.EqualTo(-20f).Within(Tolerance));
    }

    [Test]
    public void Zero_IsMutedAtTheMixerFloor()
    {
        // -80 dB is the AudioMixer's floor and is silent.
        Assert.That(VolumeMath.LinearToDecibels(0f), Is.LessThanOrEqualTo(-80f));
    }

    [Test]
    public void Zero_IsAFiniteNumber()
    {
        // Mathf.Log10(0) is -Infinity, which the mixer rejects; the mute case has to be handled explicitly.
        var db = VolumeMath.LinearToDecibels(0f);
        Assert.That(float.IsNaN(db) || float.IsInfinity(db), Is.False, $"Got {db}");
    }

    [TestCase(-1f)]
    [TestCase(-0.0001f)]
    public void BelowZero_IsTreatedAsMute(float value)
    {
        Assert.That(VolumeMath.LinearToDecibels(value), Is.LessThanOrEqualTo(-80f));
    }

    [Test]
    public void AboveOne_DoesNotBoostPastZeroDecibels()
    {
        Assert.That(VolumeMath.LinearToDecibels(2f), Is.LessThanOrEqualTo(0f + Tolerance));
    }

    [Test]
    public void Mapping_IsStrictlyIncreasingAcrossTheSliderRange()
    {
        var previous = VolumeMath.LinearToDecibels(0f);
        for (var i = 1; i <= 100; i++)
        {
            var current = VolumeMath.LinearToDecibels(i / 100f);
            Assert.That(current, Is.GreaterThan(previous), $"Not increasing at {i / 100f}");
            previous = current;
        }
    }

    [Test]
    public void NaN_IsTreatedAsMute()
    {
        Assert.That(VolumeMath.LinearToDecibels(float.NaN), Is.EqualTo(VolumeMath.MinDecibels));
    }

    [Test]
    public void DecibelsToLinear_IsTheInverse()
    {
        for (var i = 1; i <= 100; i++)
        {
            var linear = i / 100f;
            Assert.That(VolumeMath.DecibelsToLinear(VolumeMath.LinearToDecibels(linear)), Is.EqualTo(linear).Within(0.001f));
        }
        Assert.That(VolumeMath.DecibelsToLinear(VolumeMath.MinDecibels), Is.EqualTo(0f));
    }

    [Test]
    public void Mapping_IsNotLinear()
    {
        // A linear 0..1 to -80..0 mapping would put 0.5 at -40 dB.
        Assert.That(VolumeMath.LinearToDecibels(0.5f), Is.GreaterThan(-20f));
    }
}
