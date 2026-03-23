using UnityEngine;
using System;
using System.Net.Sockets;
using System.Threading;
using System.Collections.Generic;

public class MultiCameraStreamer : MonoBehaviour
{
    [Serializable]
    public struct CameraInfo
    {
        public Camera camera;
        public int captureWidth;
        public int captureHeight;
        public int cameraID;
    }

    public string serverIP = "127.0.0.1";
    public int serverPort = 5000;
    public CameraInfo[] cameras;

    private TcpClient client;
    private NetworkStream netStream;

    // 每台相機各自的 RenderTexture、Texture2D
    private RenderTexture[] rts;
    private Texture2D[] texs;

    // 用來暫存待傳送的影像資料
    private Queue<byte[]> frameQueue = new Queue<byte[]>();
    private object queueLock = new object();

    void Start()
    {
        // 初始化
        rts = new RenderTexture[cameras.Length];
        texs = new Texture2D[cameras.Length];

        for (int i = 0; i < cameras.Length; i++)
        {
            rts[i] = new RenderTexture(
                cameras[i].captureWidth,
                cameras[i].captureHeight,
                24,
                RenderTextureFormat.ARGB32
            );
            cameras[i].camera.targetTexture = rts[i];

            texs[i] = new Texture2D(
                cameras[i].captureWidth,
                cameras[i].captureHeight,
                TextureFormat.RGB24,
                false
            );
        }

        // 嘗試連線到 Python
        try
        {
            client = new TcpClient(serverIP, serverPort);
            netStream = client.GetStream();
            Debug.Log($"Connected to {serverIP}:{serverPort}");
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to connect: {e}");
        }

        // 開一個執行緒，專門負責「從 Queue 取資料 -> 寫網路」
        Thread sendThread = new Thread(SendLoop);
        sendThread.IsBackground = true;
        sendThread.Start();
        Debug.Log("SendLoop thread started");
    }

    void Update()
    {
        // **在主線程擷取相機畫面**，組成一個封包 (byte[])
        if (netStream == null) return; // 還沒連上就不抓

        try
        {
            List<byte> payload = new List<byte>();

            // (1) 先寫入相機數量
            int cameraCount = cameras.Length;
            payload.AddRange(BitConverter.GetBytes(cameraCount));

            // (2) 依序抓取每台相機的畫面
            for (int i = 0; i < cameraCount; i++)
            {
                RenderTexture.active = rts[i];
                texs[i].ReadPixels(new Rect(0, 0, rts[i].width, rts[i].height), 0, 0);
                texs[i].Apply();
                RenderTexture.active = null;

                byte[] imageBytes = texs[i].EncodeToJPG();

                // cameraID
                payload.AddRange(BitConverter.GetBytes(cameras[i].cameraID));
                // 影像長度
                payload.AddRange(BitConverter.GetBytes(imageBytes.Length));
                // 影像內容
                payload.AddRange(imageBytes);
            }

            // (3) 封裝成 finalData：前面再插入 4 bytes (總長度)
            int totalLength = payload.Count;
            List<byte> finalData = new List<byte>();
            finalData.AddRange(BitConverter.GetBytes(totalLength));
            finalData.AddRange(payload);

            // (4) 把 finalData 放進 Queue，讓子執行緒負責傳
            lock (queueLock)
            {
                frameQueue.Enqueue(finalData.ToArray());
            }
        }
        catch (Exception e)
        {
            Debug.LogError("Capture error: " + e);
        }
    }

    void SendLoop()
    {
        while (true)
        {
            if (netStream == null) return; // 若尚未連線成功，先不做事

            byte[] dataToSend = null;
            // 從 Queue 拿一筆資料
            lock (queueLock)
            {
                if (frameQueue.Count > 0)
                {
                    dataToSend = frameQueue.Dequeue();
                }
            }

            // 若有資料就發送
            if (dataToSend != null)
            {
                try
                {
                    netStream.Write(dataToSend, 0, dataToSend.Length);
                }
                catch (Exception e)
                {
                    Debug.LogError($"Send error: {e}");
                    break;
                }
            }

            // 控制傳送頻率
            Thread.Sleep(33); 
        }
    }

    void OnApplicationQuit()
    {
        if (netStream != null) netStream.Close();
        if (client != null) client.Close();

        // 回收
        for (int i = 0; i < cameras.Length; i++)
        {
            cameras[i].camera.targetTexture = null;
            if (rts[i] != null) rts[i].Release();
        }
        RenderTexture.active = null;
    }
}
