using UnityEngine;

/// <summary>
/// Slider value to mixer decibels. The slider is linear amplitude (0.5 = half the amplitude = -6 dB), which
/// follows the ear far better than writing 0..1 straight into the mixer. 0 maps to -80 dB, the mixer's floor,
/// which Unity treats as silence.
/// </summary>
public static class VolumeMath
{
    public const float MinDecibels = -80f;
    public const float MaxDecibels = 0f;

    // 20 * log10(0.0001) = -80 dB: anything at or below this is muted outright.
    public const float MuteThreshold = 0.0001f;

    public static float LinearToDecibels(float linear)
    {
        if (float.IsNaN(linear) || linear <= MuteThreshold) return MinDecibels;
        return Mathf.Clamp(20f * Mathf.Log10(Mathf.Min(linear, 1f)), MinDecibels, MaxDecibels);
    }

    public static float DecibelsToLinear(float decibels)
    {
        if (decibels <= MinDecibels) return 0f;
        return Mathf.Clamp01(Mathf.Pow(10f, decibels / 20f));
    }
}
