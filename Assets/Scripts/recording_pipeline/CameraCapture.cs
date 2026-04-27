using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using LitJson;
using System.IO;
using System.Text;

public enum BodyPart
{
    Head, Neck, Spine, Hip, 
    L_Shoulder, L_Elbow, L_Hand, L_Hip, L_Knee, L_Foot, L_Toe, 
    R_Shoulder, R_Elbow, R_Hand, R_Hip, R_Knee, R_Foot, R_Toe
}

public struct BodyCoordinates
{
    public float x, y, z;
}

public class CameraCapture : MonoBehaviour
{
    public Camera camera; // Assign the cameras you want to capture in the inspector
    public int cameraIndex = 0;
    public int captureWidth = 640; // Width of the capture
    public int captureHeight = 480; // Height of the capture
    private float captureInterval = 1f / 30f;
    public Animator motionAnimator;
    private Coroutine captureCoroutine;
    private int screenshotCounter = 0;

    // capture coordinates
    private Dictionary<BodyPart, Transform> bodyParts = new Dictionary<BodyPart, Transform>();
    private int count = 0;
    private int frameIndex = 0;
    public bool WriteFile = false;
    public string FileName = "test_GT_1";
    public bool detect_aruco = false;

    private void Start()
    {
        // StartCoroutine(CaptureAtInterval());
        captureCoroutine = StartCoroutine(CaptureAtInterval());
        float aspectRatio = camera.aspect;
            
        // Vertical field of view in radians
        float fovRadians = camera.fieldOfView * Mathf.Deg2Rad;
        
        // Calculate the focal length in pixels
        float focalLength = (0.5f * Screen.height) / Mathf.Tan(0.5f * fovRadians);
        
        // Principal point, assuming it's in the center of the screen
        float cx = Screen.width * 0.5f;
        float cy = Screen.height * 0.5f;
        
        // Constructing the intrinsic matrix
        Matrix4x4 intrinsicMatrix = Matrix4x4.zero;
        intrinsicMatrix[0, 0] = focalLength; // fx
        intrinsicMatrix[1, 1] = focalLength / aspectRatio; // fy
        intrinsicMatrix[0, 2] = cx; // cx
        intrinsicMatrix[1, 2] = cy; // cy
        intrinsicMatrix[2, 2] = 1;
        
        Debug.Log("Intrinsic Matrix: \n" + intrinsicMatrix);

        // animation = gameObject.GetComponent<Animator>();
        InitializeBodyParts();
    }

    private void InitializeBodyParts()
    {
        bodyParts.Add(BodyPart.Head, motionAnimator.GetBoneTransform(HumanBodyBones.Head));
        bodyParts.Add(BodyPart.Neck, motionAnimator.GetBoneTransform(HumanBodyBones.Neck));
        bodyParts.Add(BodyPart.Spine, motionAnimator.GetBoneTransform(HumanBodyBones.Spine));
        bodyParts.Add(BodyPart.Hip, motionAnimator.GetBoneTransform(HumanBodyBones.Hips));
        bodyParts.Add(BodyPart.L_Shoulder, motionAnimator.GetBoneTransform(HumanBodyBones.LeftShoulder));
        bodyParts.Add(BodyPart.L_Elbow, motionAnimator.GetBoneTransform(HumanBodyBones.LeftLowerArm));
        bodyParts.Add(BodyPart.L_Hand, motionAnimator.GetBoneTransform(HumanBodyBones.LeftHand));
        bodyParts.Add(BodyPart.L_Hip, motionAnimator.GetBoneTransform(HumanBodyBones.LeftUpperLeg));
        bodyParts.Add(BodyPart.L_Knee, motionAnimator.GetBoneTransform(HumanBodyBones.LeftLowerLeg));
        bodyParts.Add(BodyPart.L_Foot, motionAnimator.GetBoneTransform(HumanBodyBones.LeftFoot));
        bodyParts.Add(BodyPart.L_Toe, motionAnimator.GetBoneTransform(HumanBodyBones.LeftToes));
        bodyParts.Add(BodyPart.R_Shoulder, motionAnimator.GetBoneTransform(HumanBodyBones.RightShoulder));
        bodyParts.Add(BodyPart.R_Elbow, motionAnimator.GetBoneTransform(HumanBodyBones.RightLowerArm));
        bodyParts.Add(BodyPart.R_Hand, motionAnimator.GetBoneTransform(HumanBodyBones.RightHand));
        bodyParts.Add(BodyPart.R_Hip, motionAnimator.GetBoneTransform(HumanBodyBones.RightUpperLeg));
        bodyParts.Add(BodyPart.R_Knee, motionAnimator.GetBoneTransform(HumanBodyBones.RightLowerLeg));
        bodyParts.Add(BodyPart.R_Foot, motionAnimator.GetBoneTransform(HumanBodyBones.RightFoot));
        bodyParts.Add(BodyPart.R_Toe, motionAnimator.GetBoneTransform(HumanBodyBones.RightToes));
    }

    private IEnumerator CaptureAtInterval()
    {
        // while (true)
        while (motionAnimator != null && !IsAnimationOver())
        {
            yield return new WaitForSeconds(captureInterval); // Wait for the interval duration

            // Capture the screenshots at the same time
            StartCoroutine(CaptureScreenshot(camera));

            if (WriteFile) {
                string currentFrameIndex = frameIndex.ToString();
                if (motionAnimator.GetCurrentAnimatorStateInfo(0).IsName($"T-Pose {currentFrameIndex}"))
                {
                    if (count == 0)
                    {
                        count++;
                    }
                }
                else
                {
                    if (count != 0)
                    {
                        frameIndex++;
                        count = 0;
                    }

                    string rootPath = Path.Combine("Assets", "data2py", "Gait data");
                    string filePath = Path.Combine(rootPath, $"{FileName}.json");

                    var bodyCoordinates = new Dictionary<string, string>();
                    foreach (var part in bodyParts)
                    {
                        bodyCoordinates.Add($"{part.Key.ToString().ToLower()}.x", part.Value.position.x.ToString());
                        bodyCoordinates.Add($"{part.Key.ToString().ToLower()}.y", part.Value.position.y.ToString());
                        bodyCoordinates.Add($"{part.Key.ToString().ToLower()}.z", part.Value.position.z.ToString());
                    }

                    string json = WriteJson(bodyCoordinates);

                    using (StreamWriter sw = new StreamWriter(filePath, true))
                    {
                        sw.WriteLine(json);
                    }

                    count++;
                }
            }
        }
        Debug.Log("Capture Finished");
    }

    private IEnumerator CaptureScreenshot(Camera camera)
    {
        string cameraFolder = Application.dataPath + "/Captures/Camera_" + cameraIndex;
        if (detect_aruco)
            cameraFolder = Application.dataPath + "/Captures/Aruco";
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
        string filename =  $"{cameraFolder}/CameraCapture_{screenshotCounter:D4}.png";
        if (detect_aruco)
            filename =  $"{cameraFolder}/camera_{cameraIndex}.png";
        System.IO.File.WriteAllBytes(filename, bytes);
        screenshotCounter++;
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

    private string WriteJson(Dictionary<string, string> keyValuePairs)
    {
        StringBuilder sb = new StringBuilder();
        JsonWriter jw = new JsonWriter(sb);

        jw.WriteObjectStart();
        foreach (var pair in keyValuePairs)
        {
            jw.WritePropertyName(pair.Key);
            jw.Write(pair.Value);
        }
        jw.WriteObjectEnd();

        return sb.ToString();
    }
}