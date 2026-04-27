using System.Collections;
using UnityEngine;
using System.IO;

public class ChessCapture : MonoBehaviour
{
    [Header("Camera Settings")]
    public Camera targetCamera;    // 指定要擷取的相機
    public int cameraIndex = 0;

    [Header("Capture Settings")]
    public int captureWidth = 640;
    public int captureHeight = 480;
    public float captureInterval = 1f / 30f; // 預留給想要定時擷取的功能

    [Header("Output Settings")]
    // 在 Inspector 裡可以自由輸入路徑，例如 "D:/ChessCaptures" 或 "C:/MyProject/Captures"
    public string customFolder = "Captures";

    private void Start()
    {
        // 單次擷取
        StartCoroutine(CaptureScreenshot(targetCamera));
    }

    private IEnumerator CaptureScreenshot(Camera cam)
    {
        // 組合自訂路徑 + Camera 資料夾名稱
        // 例如 customFolder = "D:/ChessCaptures" 時，
        // 會變成 D:/ChessCaptures/Camera_0
        string cameraFolder = Path.Combine(customFolder);
        if (!Directory.Exists(cameraFolder))
            Directory.CreateDirectory(cameraFolder);

        // 建立 RenderTexture
        RenderTexture rt = new RenderTexture(captureWidth, captureHeight, 24);
        cam.targetTexture = rt;

        // 等待渲染完成
        yield return new WaitForEndOfFrame();

        // 擷取畫面
        Texture2D screenShot = new Texture2D(captureWidth, captureHeight, TextureFormat.RGB24, false);
        RenderTexture.active = rt;
        screenShot.ReadPixels(new Rect(0, 0, captureWidth, captureHeight), 0, 0);
        screenShot.Apply();

        // 收尾
        cam.targetTexture = null;
        RenderTexture.active = null;
        Destroy(rt);

        // 編碼並存檔
        byte[] bytes = screenShot.EncodeToPNG();
        string filename = Path.Combine(
            cameraFolder, 
            $"camera_{cameraIndex}.png"
        );
        File.WriteAllBytes(filename, bytes);

        Debug.Log($"Saved Camera {cameraIndex} Capture to {filename}");
    }
}
