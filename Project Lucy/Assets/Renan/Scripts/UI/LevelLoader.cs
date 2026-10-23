using UnityEngine;

public class LevelLoader : MonoBehaviour
{
    public void Load(string sceneName)
    {
        LevelManager.Load(sceneName);
    }
}
