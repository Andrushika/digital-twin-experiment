using UnityEngine;
using System.Collections;
using System.IO;

public class Capture30Frames : MonoBehaviour
{
    [Header("設定")]
    public Camera targetCamera;           // 要擷取的相機
    public string cameraName = "Camera1"; // 用來建立資料夾名稱
    public int frameCountToCapture = 30;  // 要擷取的幀數
    public int targetFPS = 30;            // 目標幀率 (用來控制間隔)

    private RenderTexture renderTexture;
    private Texture2D captureTexture;

    void Start()
    {
        // 取得相機解析度
        // int width = targetCamera.pixelWidth;
        // int height = targetCamera.pixelHeight;

        int width = 640;
        int height = 480;

        // 建立 RenderTexture 與 Texture2D 用來抓取畫面
        renderTexture = new RenderTexture(width, height, 24);
        targetCamera.targetTexture = renderTexture;
        captureTexture = new Texture2D(width, height, TextureFormat.RGB24, false);

        // 建立輸出資料夾，例如：Application.dataPath/chessboard_video/Camera1
        string folderPath = Path.Combine("D:/CodeProject/gait-analysis-ai-calculate-part/gait-analysis/unity_data_streaming/chessboard_raw", cameraName);
        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
        }

        // 開始擷取幀數的 Coroutine
        StartCoroutine(CaptureFrames(folderPath));
    }

    IEnumerator CaptureFrames(string folderPath)
    {
        Debug.Log("開始擷取前 " + frameCountToCapture + " 幀");

        for (int i = 0; i < frameCountToCapture; i++)
        {
            // 等待畫面渲染完成
            yield return new WaitForEndOfFrame();

            // 擷取畫面
            RenderTexture.active = renderTexture;
            captureTexture.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
            captureTexture.Apply();
            RenderTexture.active = null;

            // 將畫面編碼成 PNG
            byte[] imageBytes = captureTexture.EncodeToPNG();
            string fileName = string.Format("frame_{0:D4}.png", i);
            string filePath = Path.Combine(folderPath, fileName);
            File.WriteAllBytes(filePath, imageBytes);
            Debug.Log("已儲存: " + filePath);

            // 等待一段時間以達到目標幀率
            yield return new WaitForSeconds(1f / targetFPS);
        }

        Debug.Log("擷取完畢，遊戲停止");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
