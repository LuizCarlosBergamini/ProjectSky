using UnityEngine;
using UnityEngine.Playables;
using static UnityEngine.InputSystem.DefaultInputActions;

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

    private void Update()
    {
        // if space is pressed, skip the timeline
        if (Input.GetKeyDown(KeyCode.Space))
        {
            dir.Stop();
        }
    }
}
