using UnityEngine;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// MODULE A: Data Receiver
/// 
/// 責任：
/// 1. 從外部 (Python UDP/Socket) 或本地檔案接收原始 joints 資料
/// 2. 管理幀編號與時間戳
/// 3. 維持環形 buffer 供下層模組讀取
/// 4. 處理資料遺失與異常
/// 5. 座標系映射（將來源座標系轉成 Unity 座標系）
/// </summary>
public class DataReceiver : MonoBehaviour
{
    [System.Serializable]
    public enum InputMode
    {
        File,       // 讀本地 CSV
        UDPSocket,  // 接收 Python UDP
        TCPSocket,  // 接收 Python TCP
        DirectTest  // 測試模式 (直接注入)
    }

    [SerializeField] private InputMode inputMode = InputMode.DirectTest;
    [SerializeField] private string csvFilePath = "";
    [SerializeField] private float csvPlaybackSpeed = 1f;
    [SerializeField] private bool csvLoop = false;
    [SerializeField] private int udpPort = 5555;
    [SerializeField] private string tcpHost = "127.0.0.1";
    [SerializeField] private int tcpPort = 12345;

    // ===== 座標系轉換 =====

    /// <summary>
    /// 常用座標系預設：
    /// - MediaPipe_World:      x=右, y=上, z=朝攝影機 → Unity: X=x, Y=y, Z=-z
    /// - MediaPipe_Normalized: x/y ∈ [0,1] 螢幕座標 (y 向下) → 需要 center + flip Y
    /// - Custom:               手動設定所有參數
    /// </summary>
    [System.Serializable]
    public enum CoordinatePreset
    {
        MediaPipe_World,       // World Landmarks（公尺、髖部原點）
        MediaPipe_Normalized,  // Normalized Landmarks（[0,1] 螢幕座標）
        Custom                 // 自訂
    }

    public enum AxisSource
    {
        X, Y, Z, NegX, NegY, NegZ,
    }

    [Header("Coordinate Mapping")]
    [SerializeField] private CoordinatePreset coordinatePreset = CoordinatePreset.MediaPipe_World;
    [Tooltip("將來源資料繞 Y 軸旋轉指定角度（度）\n例如：資料人物面向 -X，Avatar 面向 +Z → 填 90")]
    [SerializeField] private float sourceYawOffset = 0f;
    [Tooltip("第一幀自動偵測人物面向，自動計算需要旋轉多少度才能面向 +Z")]
    [SerializeField] private bool autoDetectYaw = true;

    [Header("Custom Coordinate Settings (only when preset = Custom)")]
    [SerializeField] private bool inputIsNormalized01 = false;
    [SerializeField] private bool centerNormalizedInput = true;
    [SerializeField] private bool invertNormalizedY = true;
    [SerializeField] private float normalizedToWorldScale = 2.0f;
    [SerializeField] private AxisSource unityXFrom = AxisSource.X;
    [SerializeField] private AxisSource unityYFrom = AxisSource.Y;
    [SerializeField] private AxisSource unityZFrom = AxisSource.NegZ;
    [SerializeField] private Vector3 worldScale = Vector3.one;
    
    // Buffer 設定
    private const int BUFFER_SIZE = 300;
    private Queue<HumanPoseData> poseBuffer = new Queue<HumanPoseData>();
    
    // CSV 讀取
    private string[] csvLines;
    private int csvCurrentLine = 0;
    private int csvDataStartLine = 0;  // 跳過 header 後的起始行
    private float csvLineTimer = 0f;
    private float csvLineInterval = 0.033f;  // 預設 30fps
    private int csvColumnOffset = 0;         // 前面有幾欄非座標資料（如 frame_index）
    
    // 狀態
    private int frameCount = 0;
    private double simulationTime = 0;
    private bool isReceiving = false;
    private bool firstFrameDiagnosed = false;
    private bool yawAutoDetected = false;
    private Quaternion yawRotation = Quaternion.identity;
    
    public HumanPoseData LastFrame { get; private set; }
    
    public delegate void OnNewFrameHandler(HumanPoseData pose);
    public event OnNewFrameHandler OnNewFrame;

    private void Start()
    {
        InitializeReceiver();
    }

    /// <summary>
    /// 供外部在 runtime 設定資料來源並初始化。
    /// </summary>
    public void ConfigureFileInput(string relativeCsvPath, float playbackSpeed)
    {
        csvFilePath = relativeCsvPath;
        csvPlaybackSpeed = Mathf.Max(0.01f, playbackSpeed);
        inputMode = InputMode.File;
        InitializeReceiver();
    }

    private void InitializeReceiver()
    {
        switch (inputMode)
        {
            case InputMode.File:
                LoadCSVFile();
                break;
            case InputMode.UDPSocket:
                Debug.Log("[DataReceiver] UDP socket initialized on port " + udpPort);
                break;
            case InputMode.TCPSocket:
                Debug.Log("[DataReceiver] TCP socket initialized");
                break;
            case InputMode.DirectTest:
                Debug.Log("[DataReceiver] Direct test mode - ready for manual injection");
                break;
        }
        
        isReceiving = true;
    }

    /// <summary>
    /// 從 CSV 檔讀取所有行，自動偵測 header 和欄位偏移
    /// </summary>
    private void LoadCSVFile()
    {
        if (string.IsNullOrEmpty(csvFilePath))
        {
            Debug.LogError("[DataReceiver] CSV file path is empty!");
            return;
        }

        // 多路徑搜尋
        string fullPath = csvFilePath;
        if (!File.Exists(fullPath))
            fullPath = Path.Combine(Application.dataPath, csvFilePath);
        if (!File.Exists(fullPath))
            fullPath = Path.Combine(Application.persistentDataPath, csvFilePath);
        if (!File.Exists(fullPath))
            fullPath = Path.Combine(Application.dataPath, "..", csvFilePath);
        
        if (!File.Exists(fullPath))
        {
            Debug.LogError($"[DataReceiver] CSV file not found. Tried:\n" +
                           $"  {csvFilePath}\n" +
                           $"  {Path.Combine(Application.dataPath, csvFilePath)}\n" +
                           $"  {Path.Combine(Application.persistentDataPath, csvFilePath)}");
            return;
        }

        csvLines = File.ReadAllLines(fullPath);
        csvDataStartLine = 0;
        csvColumnOffset = 0;

        // 自動偵測 header 行 & 欄位偏移
        if (csvLines.Length > 0)
        {
            DetectCSVFormat(csvLines[0], csvLines.Length > 1 ? csvLines[1] : null);
        }

        csvCurrentLine = csvDataStartLine;
        firstFrameDiagnosed = false;
        yawAutoDetected = false;
        yawRotation = Quaternion.AngleAxis(sourceYawOffset, Vector3.up);
        
        int dataLines = csvLines.Length - csvDataStartLine;
        Debug.Log($"[DataReceiver] Loaded CSV: {dataLines} data lines from {fullPath}" +
                  $" (header={csvDataStartLine > 0}, columnOffset={csvColumnOffset})");
    }

    /// <summary>
    /// 自動偵測 CSV 格式：是否有 header、前面是否有非座標欄位（如 frame_index）
    /// </summary>
    private void DetectCSVFormat(string firstLine, string secondLine)
    {
        string[] parts = firstLine.Split(',');

        // 檢查第一行是否為 header（含非數字文字）
        bool isHeader = false;
        for (int i = 0; i < Mathf.Min(parts.Length, 3); i++)
        {
            float dummy;
            if (!float.TryParse(parts[i].Trim(), System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out dummy))
            {
                isHeader = true;
                break;
            }
        }

        if (isHeader)
        {
            csvDataStartLine = 1;
            Debug.Log($"[DataReceiver] Detected header: {firstLine.Substring(0, Mathf.Min(firstLine.Length, 80))}...");
        }

        // 用第一筆資料行判斷欄位偏移
        string dataLine = isHeader ? secondLine : firstLine;
        if (dataLine != null)
        {
            string[] dataParts = dataLine.Split(',');
            int numCols = dataParts.Length;

            // MediaPipe 33 點: 99 (xyz) 或 132 (xyzc)
            // 可能前面有 frame_index, timestamp 等欄位
            if (numCols >= 99)
            {
                int remainder99 = (numCols - 99);
                int remainder132 = (numCols - 132);

                if (remainder99 >= 0 && remainder99 <= 3)
                    csvColumnOffset = remainder99;
                else if (remainder132 >= 0 && remainder132 <= 3)
                    csvColumnOffset = remainder132;
                else
                    csvColumnOffset = 0;  // fallback

                if (csvColumnOffset > 0)
                    Debug.Log($"[DataReceiver] Detected {csvColumnOffset} leading column(s) (e.g. frame_index)");
            }
        }
    }

    private void Update()
    {
        simulationTime += Time.deltaTime;

        // 根據模式處理資料
        switch (inputMode)
        {
            case InputMode.File:
                UpdateCSVPlayback();
                break;
        }
    }

    /// <summary>
    /// CSV 檔播放邏輯
    /// </summary>
    private void UpdateCSVPlayback()
    {
        if (csvLines == null || csvLines.Length == 0)
            return;

        csvLineTimer += Time.deltaTime * csvPlaybackSpeed;
        
        if (csvLineTimer >= csvLineInterval)
        {
            if (csvCurrentLine < csvLines.Length)
            {
                string line = csvLines[csvCurrentLine];
                ReceiveCSVLine(line);
                csvCurrentLine++;
                csvLineTimer = 0f;
            }
            else if (csvLoop)
            {
                csvCurrentLine = csvDataStartLine;
                csvLineTimer = 0f;
                Debug.Log("[DataReceiver] CSV playback looping");
            }
            else
            {
                Debug.Log("[DataReceiver] CSV playback reached end");
            }
        }
    }

    /// <summary>
    /// 注入一幀資料 (測試用或直接從 Python 呼叫)
    /// </summary>
    public void ReceiveFrame(HumanPoseData pose)
    {
        pose.FrameIndex = frameCount;
        pose.Timestamp = simulationTime;
        
        if (poseBuffer.Count >= BUFFER_SIZE)
            poseBuffer.Dequeue();  // 丟掉最舊的
        
        poseBuffer.Enqueue(pose);
        LastFrame = pose;
        
        OnNewFrame?.Invoke(pose);  // 觸發事件
        
        frameCount++;
    }

    /// <summary>
    /// 從 CSV 字串解析一行
    /// 自動處理 header、前導欄位（frame_index 等）、信心度
    /// </summary>
    public void ReceiveCSVLine(string csvLine)
    {
        if (string.IsNullOrWhiteSpace(csvLine))
            return;

        string[] parts = csvLine.Split(',');
        
        // 跳過 header 行
        if (parts.Length > 0)
        {
            float dummy;
            if (!float.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out dummy))
                return;
        }

        // 去掉前導欄位（如 frame_index, timestamp）
        int dataCount = parts.Length - csvColumnOffset;
        if (dataCount < 99)
        {
            Debug.LogWarning($"[DataReceiver] Line has {dataCount} data columns (need ≥99). Raw columns={parts.Length}, offset={csvColumnOffset}");
            return;
        }

        float[] floatValues = new float[dataCount];
        for (int i = 0; i < dataCount; i++)
        {
            if (!float.TryParse(parts[i + csvColumnOffset].Trim(),
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture,
                                out floatValues[i]))
            {
                Debug.LogWarning($"[DataReceiver] Failed to parse float at column {i + csvColumnOffset}: '{parts[i + csvColumnOffset]}'");
                return;
            }
        }

        HumanPoseData pose = new HumanPoseData();
        pose.SetJointsFromArray(floatValues);

        if (pose.IsValid)
        {
            ProcessPoseCoordinates(pose);
        }
        
        if (pose.IsValid)
        {
            // 第一幀診斷
            if (!firstFrameDiagnosed)
            {
                DiagnoseFirstFrame(pose);
                firstFrameDiagnosed = true;
            }

            ReceiveFrame(pose);
        }
    }

    /// <summary>
    /// 取得最新一幀
    /// </summary>
    public HumanPoseData GetLatestFrame()
    {
        return LastFrame;
    }

    /// <summary>
    /// 取得特定幀 (buffer 查詢)
    /// </summary>
    public bool TryGetFrame(int frameIndex, out HumanPoseData frame)
    {
        frame = null;
        foreach (HumanPoseData p in poseBuffer)
        {
            if (p.FrameIndex == frameIndex)
            {
                frame = p;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 取得全部 buffer (Phase 1 debug 用)
    /// </summary>
    public Queue<HumanPoseData> GetBuffer()
    {
        return poseBuffer;
    }

    public bool IsReceiving() => isReceiving;
    public int GetFrameCount() => frameCount;
    public double GetSimulationTime() => simulationTime;

    private void ProcessPoseCoordinates(HumanPoseData pose)
    {
        // 根據 preset 覆蓋設定值
        bool isNorm;
        bool center;
        bool invertY;
        float normScale;
        AxisSource xFrom, yFrom, zFrom;
        Vector3 scale;

        ResolvePreset(out isNorm, out center, out invertY, out normScale,
                      out xFrom, out yFrom, out zFrom, out scale);

        Vector3[] joints = pose.Joints;
        for (int i = 0; i < joints.Length; i++)
        {
            Vector3 source = joints[i];

            if (isNorm)
            {
                if (center)
                {
                    source.x -= 0.5f;
                    source.y -= 0.5f;
                }
                if (invertY)
                {
                    source.y = -source.y;
                }
                source *= normScale;
            }

            float mappedX = ResolveAxis(xFrom, source);
            float mappedY = ResolveAxis(yFrom, source);
            float mappedZ = ResolveAxis(zFrom, source);
            joints[i] = Vector3.Scale(new Vector3(mappedX, mappedY, mappedZ), scale);
        }

        // 自動偵測朝向（只佚第一幀執行一次）
        if (autoDetectYaw && !yawAutoDetected)
        {
            AutoDetectYawFromPose(joints);
            yawAutoDetected = true;
        }

        // 套用 Y 軸旋轉（手動 offset + auto detect 的結果）
        if (yawRotation != Quaternion.identity)
        {
            // 以髖部中心為軸心旋轉，避免位置偏移
            Vector3 pivot = Vector3.Lerp(
                joints[(int)HumanPoseData.JointType.L_Hip],
                joints[(int)HumanPoseData.JointType.R_Hip], 0.5f);
            for (int i = 0; i < joints.Length; i++)
            {
                joints[i] = yawRotation * (joints[i] - pivot) + pivot;
            }
        }
    }

    /// <summary>
    /// 用第一幀的鼻子和髖部位置自動判斷人物面向，
    /// 計算需要旋轉多少度才能讓人物面向 +Z。
    /// 將結果疊加到現有的 sourceYawOffset 上。
    /// </summary>
    private void AutoDetectYawFromPose(Vector3[] joints)
    {
        Vector3 l_hip = joints[(int)HumanPoseData.JointType.L_Hip];
        Vector3 r_hip = joints[(int)HumanPoseData.JointType.R_Hip];
        Vector3 l_shoulder = joints[(int)HumanPoseData.JointType.L_Shoulder];
        Vector3 r_shoulder = joints[(int)HumanPoseData.JointType.R_Shoulder];
        Vector3 nose = joints[(int)HumanPoseData.JointType.Nose];

        Vector3 hipCenter = Vector3.Lerp(l_hip, r_hip, 0.5f);
        Vector3 shoulderCenter = Vector3.Lerp(l_shoulder, r_shoulder, 0.5f);
        Vector3 up = (shoulderCenter - hipCenter).normalized;

        // 計算人物正面方向：用 hip right × up
        Vector3 hipRight = (r_hip - l_hip).normalized;
        Vector3 rawForward = Vector3.Cross(hipRight, up).normalized;

        // 用 nose 確認方向 (避免正負虛義)
        Vector3 noseHint = Vector3.ProjectOnPlane(nose - hipCenter, up);
        if (noseHint.sqrMagnitude > 1e-6f && Vector3.Dot(rawForward, noseHint) < 0f)
            rawForward = -rawForward;

        // 求前向在 XZ 平面上的角度（相對於 +Z）
        Vector3 flatForward = new Vector3(rawForward.x, 0f, rawForward.z).normalized;
        if (flatForward.sqrMagnitude < 1e-6f)
        {
            Debug.Log("[DataReceiver] Auto yaw detect: person facing straight up/down, skip");
            return;
        }

        float autoYaw = -Mathf.Atan2(flatForward.x, flatForward.z) * Mathf.Rad2Deg;

        // 疊加到手動 offset
        float totalYaw = sourceYawOffset + autoYaw;
        yawRotation = Quaternion.AngleAxis(totalYaw, Vector3.up);

        Debug.Log($"[DataReceiver] Auto yaw detect: person faces {flatForward} " +
                  $"(angle from +Z = {autoYaw:F1}°) → total yaw correction = {totalYaw:F1}°");
    }

    /// <summary>
    /// 根據 preset 解析座標映射參數
    /// </summary>
    private void ResolvePreset(out bool isNorm, out bool center, out bool invertY, out float normScale,
                                out AxisSource xFrom, out AxisSource yFrom, out AxisSource zFrom,
                                out Vector3 scale)
    {
        switch (coordinatePreset)
        {
            case CoordinatePreset.MediaPipe_World:
                // World Landmarks: x=右, y=上, z=朝攝影機
                // Unity:           x=右, y=上, z=遠離攝影機
                // → X=X, Y=Y, Z=-Z
                isNorm = false;
                center = false;
                invertY = false;
                normScale = 1f;
                xFrom = AxisSource.X;
                yFrom = AxisSource.Y;
                zFrom = AxisSource.NegZ;
                scale = Vector3.one;
                return;

            case CoordinatePreset.MediaPipe_Normalized:
                // Normalized Landmarks: x∈[0,1]→右, y∈[0,1]→下, z=深度
                // → center, flip Y, scale, Z=-Z
                isNorm = true;
                center = true;
                invertY = true;
                normScale = normalizedToWorldScale;
                xFrom = AxisSource.X;
                yFrom = AxisSource.Y;
                zFrom = AxisSource.NegZ;
                scale = Vector3.one;
                return;

            default: // Custom
                isNorm = inputIsNormalized01;
                center = centerNormalizedInput;
                invertY = invertNormalizedY;
                normScale = normalizedToWorldScale;
                xFrom = unityXFrom;
                yFrom = unityYFrom;
                zFrom = unityZFrom;
                scale = worldScale;
                return;
        }
    }

    /// <summary>
    /// 第一幀座標系診斷：印出關鍵點座標，幫你判斷映射是否正確
    /// </summary>
    private void DiagnoseFirstFrame(HumanPoseData pose)
    {
        Vector3 nose      = pose.GetJoint(HumanPoseData.JointType.Nose);
        Vector3 l_hip     = pose.GetJoint(HumanPoseData.JointType.L_Hip);
        Vector3 r_hip     = pose.GetJoint(HumanPoseData.JointType.R_Hip);
        Vector3 l_shoulder = pose.GetJoint(HumanPoseData.JointType.L_Shoulder);
        Vector3 r_shoulder = pose.GetJoint(HumanPoseData.JointType.R_Shoulder);
        Vector3 l_ankle   = pose.GetJoint(HumanPoseData.JointType.L_Ankle);

        Vector3 hipCenter  = Vector3.Lerp(l_hip, r_hip, 0.5f);
        Vector3 shoulderCenter = Vector3.Lerp(l_shoulder, r_shoulder, 0.5f);

        Vector3 upDir = (shoulderCenter - hipCenter).normalized;
        float height = Vector3.Distance(nose, l_ankle);
        float shoulderWidth = Vector3.Distance(l_shoulder, r_shoulder);

        string status;
        // 基本檢查：人體的「肩膀中心」應該在「髖部中心」之上
        if (upDir.y > 0.5f)
            status = "OK - 人物正立 (Y 朝上)";
        else if (upDir.y < -0.5f)
            status = "WARNING - 人物顛倒！請檢查 Y 軸映射";
        else if (Mathf.Abs(upDir.x) > 0.5f)
            status = "WARNING - 人物橫躺！Y 軸可能映射到了 X";
        else
            status = "WARNING - 方向不明確，請手動檢查";

        Debug.Log($"[DataReceiver] ═══ First Frame Diagnostic ═══\n" +
                  $"  Preset: {coordinatePreset}\n" +
                  $"  Nose:     {nose}\n" +
                  $"  HipCenter:{hipCenter}\n" +
                  $"  Shoulder→Hip up vector: {upDir}  → {status}\n" +
                  $"  Approx height:  {height:F3}m\n" +
                  $"  Shoulder width: {shoulderWidth:F3}m\n" +
                  $"  (Realistic values: height 1.5~1.9m, shoulder 0.3~0.5m)");
    }

    private float ResolveAxis(AxisSource axis, Vector3 source)
    {
        switch (axis)
        {
            case AxisSource.X:
                return source.x;
            case AxisSource.Y:
                return source.y;
            case AxisSource.Z:
                return source.z;
            case AxisSource.NegX:
                return -source.x;
            case AxisSource.NegY:
                return -source.y;
            case AxisSource.NegZ:
                return -source.z;
            default:
                return source.x;
        }
    }
}
