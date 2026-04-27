using System.Collections;
using UnityEngine;

public class MultiCameraCapture : MonoBehaviour
{
    public Camera[] cameras; // Assign the cameras you want to capture in the inspector
    public int captureWidth = 640; // Width of the capture
    public int captureHeight = 480; // Height of the capture
    private float captureInterval = 1f / 30f;
    public Animator motionAnimator;
    private Coroutine captureCoroutine;

    private void Start()
    {
        // StartCoroutine(CaptureAtInterval());
        captureCoroutine = StartCoroutine(CaptureAtInterval());
    }

    private IEnumerator CaptureAtInterval()
    {
        // while (true)
        while (motionAnimator != null && !IsAnimationOver())
        {
            yield return new WaitForSeconds(captureInterval); // Wait for the interval duration

            // Capture the screenshots at the same time
            foreach (var camera in cameras)
            {
                StartCoroutine(CaptureScreenshot(camera));
            }
        }
    }

    private IEnumerator CaptureScreenshot(Camera camera)
    {
        int cameraIndex = System.Array.IndexOf(cameras, camera);
        string cameraFolder = Application.dataPath + "/Captures/Camera_" + cameraIndex;
        System.IO.Directory.CreateDirectory(cameraFolder);

        RenderTexture rt = new RenderTexture(captureWidth, captureHeight, 24);
        camera.targetTexture = rt;
        yield return new WaitForEndOfFrame(); // Ensure the frame is rendered

        Texture2D screenShot = new Texture2D(captureWidth, captureHeight, TextureFormat.RGB24, false);
        RenderTexture.active = rt;
        screenShot.ReadPixels(new Rect(0, 0, captureWidth, captureHeight), 0, 0);
        camera.targetTexture = null;
        RenderTexture.active = null;
        Destroy(rt);

        byte[] bytes = screenShot.EncodeToPNG();
        string filename = $"{cameraFolder}/CameraCapture_{System.DateTime.Now:yyyy-MM-dd_HH-mm-ss-fff}.png";
        System.IO.File.WriteAllBytes(filename, bytes);
        // Debug.Log($"Saved Camera {cameraIndex} Capture to {filename}");
    }
    private bool IsAnimationOver()
    {
        // Assume the animation you're interested in is on layer 0, adjust if necessary
        AnimatorStateInfo stateInfo = motionAnimator.GetCurrentAnimatorStateInfo(0);
        return stateInfo.normalizedTime >= 1.0f; // This means the animation is done
    }

    // Optional: A method to manually stop capturing if needed
    public void StopCapture()
    {
        if (captureCoroutine != null)
        {
            StopCoroutine(captureCoroutine);
        }
    }
}