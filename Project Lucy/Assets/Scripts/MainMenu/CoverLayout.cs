using UnityEngine;

/// <summary>
/// The maths behind CoverImage, kept apart so it can be tested without a canvas: like CSS "background-size: cover",
/// the content is scaled uniformly until it fills the container on both axes, and whatever sticks out is cropped.
/// </summary>
public static class CoverLayout
{
    /// <summary>
    /// Size the content must have to cover <paramref name="container"/> without distortion: never smaller than the
    /// container on either axis and always with the content's aspect ratio. Content or container without area gives
    /// the container size back (nothing sensible to keep the aspect of).
    /// </summary>
    public static Vector2 ComputeCoverSize(Vector2 container, Vector2 content)
    {
        if (container.x <= 0f || container.y <= 0f || content.x <= 0f || content.y <= 0f) return container;

        float scale = Mathf.Max(container.x / content.x, container.y / content.y);
        return content * scale;
    }
}
