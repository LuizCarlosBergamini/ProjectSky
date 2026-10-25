using UnityEngine;

public class Parallax : MonoBehaviour
{
    private float length, startpos;
    public GameObject cam;
    public float parallaxEffect;

    private Transform camTransform;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        startpos = transform.position.x;
        length = GetComponent<SpriteRenderer>().bounds.size.x;

        // Follow the real camera, not a Cinemachine virtual camera. In the Hub "cam" points to the player's
        // virtual camera, which CameraManager disables when a trigger swaps to another camera; it then stops
        // moving, so the background froze while the view kept going and the edges of the tiles showed up.
        // The Main Camera is driven by the CinemachineBrain, so it always matches what is on screen.
        camTransform = Camera.main != null ? Camera.main.transform : cam.transform;
    }

    // Update is called once per frame
    void FixedUpdate()
    {
        float temp = (camTransform.position.x * (1 - parallaxEffect));
        float dist = (camTransform.position.x * parallaxEffect);
        transform.position = new Vector3(startpos + dist, transform.position.y, transform.position.z);
        
        if (temp > startpos + length) startpos += length;
        else if (temp < startpos - length) startpos -= length;
    }
}
