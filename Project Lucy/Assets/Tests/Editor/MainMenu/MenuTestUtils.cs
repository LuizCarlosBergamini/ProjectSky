using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

/// <summary>
/// Lookup and raycast helpers shared by the main menu PlayMode tests.
/// </summary>
public static class MenuTestUtils
{
    /// <summary>Finds a GameObject by exact name in the loaded scenes, inactive ones included.</summary>
    public static GameObject FindInScenes(string name)
    {
        for (var i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded) continue;
            foreach (var root in scene.GetRootGameObjects())
            {
                var match = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);
                if (match != null) return match.gameObject;
            }
        }
        return null;
    }

    /// <summary>The screen-space centre of a UI element, for whichever render mode its canvas uses.</summary>
    public static Vector2 ScreenCenter(RectTransform rect)
    {
        var canvas = rect.GetComponentInParent<Canvas>().rootCanvas;
        var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        var corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        var worldCenter = (corners[0] + corners[2]) / 2f;
        return RectTransformUtility.WorldToScreenPoint(camera, worldCenter);
    }

    /// <summary>Everything the EventSystem would hit at a screen position, topmost first.</summary>
    public static List<RaycastResult> RaycastAt(Vector2 screenPosition)
    {
        var results = new List<RaycastResult>();
        var pointer = new PointerEventData(EventSystem.current) { position = screenPosition };
        EventSystem.current.RaycastAll(pointer, results);
        return results;
    }

    /// <summary>Screen-space rectangle covered by a UI element.</summary>
    public static Rect ScreenRect(RectTransform rect)
    {
        var canvas = rect.GetComponentInParent<Canvas>().rootCanvas;
        var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        var corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        var min = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
        var max = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    /// <summary>
    /// Fails clearly if the Editor is not in Play Mode. The Test Framework only acts on EnterPlayMode and
    /// ExitPlayMode when the test method itself yields them, not from a nested helper, so every Play Mode test
    /// yields them directly and calls this right after entering.
    /// </summary>
    public static void AssertInPlayMode()
    {
        Assert.That(EditorApplication.isPlaying, Is.True, "Play Mode did not start.");
    }
}
