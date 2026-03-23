using UnityEngine;

/// <summary>
/// QuickStart - 快速啟動腳本
/// 
/// 使用方式：
/// 1. 將此腳本掛到你的 Avatar GameObject 上
/// 2. 在 Inspector 設定 CSV 檔案路徑
/// 3. 設定目標 Phase (通常選 Phase4_RotationDriven)
/// 4. 按 Play - Avatar 就會自動跟著動作驅動
/// 
/// CSV 格式 (MediaPipe 33 點)：
/// x1,y1,z1,c1, x2,y2,z2,c2, ..., x33,y33,z33,c33  (帶信心度)
/// 或
/// x1,y1,z1, x2,y2,z2, ..., x33,y33,z33  (無信心度)
/// </summary>
public class QuickStart : MonoBehaviour
{
    [SerializeField] private string csvFileName = "pose_data.csv";
    [SerializeField] private bool playOnStart = true;
    [SerializeField] private MotionController.Phase targetPhase = MotionController.Phase.Phase4_RotationDriven;
    [SerializeField] private float playbackSpeed = 1f;
    
    private MotionController motionController;
    private Vector2 scrollPosition;
    private GUIStyle titleStyle;

    private void EnsureGuiStyles()
    {
        if (titleStyle != null)
            return;

        // GUI.skin 只能在 OnGUI 內存取，這裡先用安全 fallback。
        titleStyle = new GUIStyle()
        {
            fontSize = 13,
            fontStyle = FontStyle.Bold,
            wordWrap = true,
        };
    }

    private void Start()
    {
        Setup();

        if (playOnStart)
        {
            StartMotion();
        }
    }

    /// <summary>
    /// 初始化系統
    /// </summary>
    private void Setup()
    {
        // 檢查或建立 MotionController
        motionController = GetComponent<MotionController>();
        if (motionController == null)
        {
            motionController = gameObject.AddComponent<MotionController>();
            Debug.Log("[QuickStart] MotionController created");
        }

        // 取得 DataReceiver
        DataReceiver dataReceiver = GetComponent<DataReceiver>();
        if (dataReceiver == null)
        {
            dataReceiver = gameObject.AddComponent<DataReceiver>();
            Debug.Log("[QuickStart] DataReceiver created");
        }

        // 設定 CSV 檔路徑
        // CSV 應該放在 Assets/Motion Data/ 資料夾下
        string csvPath = "Motion Data/" + csvFileName;
        dataReceiver.ConfigureFileInput(csvPath, playbackSpeed);

        Debug.Log($"[QuickStart] Setup complete. CSV path: {csvPath}");
    }

    /// <summary>
    /// 啟動動作驅動
    /// </summary>
    public void StartMotion()
    {
        if (motionController == null)
            Setup();

        motionController.StartPhase(targetPhase);
        Debug.Log($"[QuickStart] Motion started with {targetPhase}");
    }

    /// <summary>
    /// 暫停
    /// </summary>
    public void Pause()
    {
        if (motionController != null)
            motionController.Pause();
    }

    /// <summary>
    /// 恢復
    /// </summary>
    public void Resume()
    {
        if (motionController != null)
            motionController.Resume();
    }

    /// <summary>
    /// 停止
    /// </summary>
    public void Stop()
    {
        if (motionController != null)
            motionController.Stop();
    }

    /// <summary>
    /// 在 Scene 顯示控制按鈕
    /// </summary>
    private void OnGUI()
    {
        EnsureGuiStyles();

        // 進入 OnGUI 後再套用 skin 的字體外觀。
        if (GUI.skin != null)
        {
            titleStyle.normal.textColor = GUI.skin.label.normal.textColor;
        }

        float panelWidth = Mathf.Clamp(Screen.width - 20f, 220f, 300f);
        float panelHeight = Mathf.Min(260f, Screen.height - 20f);

        GUILayout.BeginArea(new Rect(10, 10, panelWidth, panelHeight));
        scrollPosition = GUILayout.BeginScrollView(scrollPosition, false, true);
        GUILayout.Label("Motion Control", titleStyle);

        if (motionController == null)
        {
            GUILayout.Label("ERROR: MotionController not initialized!", new GUIStyle(GUI.skin.label) { normal = { textColor = Color.red } });
        }
        else
        {
            GUILayout.Label($"Status: {(motionController.IsRunning() ? "RUNNING" : "STOPPED")}", 
                new GUIStyle(GUI.skin.label) { normal = { textColor = motionController.IsRunning() ? Color.green : Color.red } });
            GUILayout.Label($"Frames: {motionController.GetFrameCount()}");

            GUILayout.Space(10);

            if (GUILayout.Button("Start", GUILayout.Height(30), GUILayout.ExpandWidth(true)))
                StartMotion();

            if (GUILayout.Button("Pause", GUILayout.Height(30), GUILayout.ExpandWidth(true)))
                Pause();

            if (GUILayout.Button("Resume", GUILayout.Height(30), GUILayout.ExpandWidth(true)))
                Resume();

            if (GUILayout.Button("Stop", GUILayout.Height(30), GUILayout.ExpandWidth(true)))
                Stop();
        }

        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }
}
