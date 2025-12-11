using UnityEngine;

public class LevelLoader : MonoBehaviour
{
    public void Load(string sceneName)
    {
        if (LevelManager.instance == null) return;
        LevelManager.instance.LoadScene(sceneName);
    }
}
