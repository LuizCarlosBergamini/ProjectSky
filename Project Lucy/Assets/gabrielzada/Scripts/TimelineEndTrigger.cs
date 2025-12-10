using UnityEngine;
using UnityEngine.Playables;

public class TimelineEndTrigger : MonoBehaviour
{
    private PlayableDirector dir;

    private void Start()
    {
        dir = GetComponent<PlayableDirector>();
        dir.stopped += OnTimelineFinished;
    }

    private void OnTimelineFinished(PlayableDirector d)
    {
        Debug.Log("Cutscene terminada!");
        SceneControl.instance.NextLevel();
    }
}
