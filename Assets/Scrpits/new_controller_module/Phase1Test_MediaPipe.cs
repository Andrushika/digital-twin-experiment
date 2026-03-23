using UnityEngine;

/// <summary>
/// Phase 1 Test Script (MediaPipe 33-Point Format)
/// 
/// 用途：快速測試資料打通與 debug visualization
/// 支援 MediaPipe 33 點格式
/// 
/// 使用方式：
/// 1. 建立一個 empty GameObject
/// 2. 掛上 MotionController (會自動建立 DataReceiver)
/// 3. 掛上這個 Phase1Test 腳本
/// 4. 在 Editor 按下按鈕或用程式呼叫方法
/// </summary>
public class Phase1Test_MediaPipe : MonoBehaviour
{
    private MotionController motionController;
    private Vector2 scrollPosition;
    private GUIStyle titleStyle;
    private Coroutine demoSequence;

    private void EnsureGuiStyles()
    {
        if (titleStyle != null)
            return;

        // GUI.skin 只能在 OnGUI 內存取，這裡先用安全 fallback。
        titleStyle = new GUIStyle()
        {
            fontSize = 14,
            fontStyle = FontStyle.Bold,
            wordWrap = true,
        };
    }

    private void Start()
    {
        motionController = GetComponent<MotionController>();
        if (motionController == null)
        {
            motionController = GetComponentInParent<MotionController>();
        }

        if (motionController == null)
        {
            Debug.LogError("[Phase1Test] No MotionController found!");
            return;
        }

        Debug.Log("[Phase1Test] Ready for testing (MediaPipe 33-point format)");
    }

    // ===== GUI Buttons (在 Scene view 用) =====

    private void OnGUI()
    {
        EnsureGuiStyles();

        if (GUI.skin != null)
        {
            titleStyle.normal.textColor = GUI.skin.label.normal.textColor;
        }

        float panelWidth = Mathf.Clamp(Screen.width - 20f, 220f, 360f);
        float panelHeight = Mathf.Min(520f, Screen.height - 20f);

        GUILayout.BeginArea(new Rect(10, 10, panelWidth, panelHeight));
        scrollPosition = GUILayout.BeginScrollView(scrollPosition, false, true);
        GUILayout.Label("Phase 1 Test (MediaPipe)", titleStyle);

        if (motionController == null)
        {
            GUILayout.Label("ERROR: No MotionController!", 
                new GUIStyle(GUI.skin.label) { normal = { textColor = Color.red } });
            GUILayout.EndScrollView();
            GUILayout.EndArea();
            return;
        }

        GUILayout.Space(10);
        GUILayout.Label($"Status: {(motionController.IsRunning() ? "RUNNING" : "STOPPED")}", 
            new GUIStyle(GUI.skin.label) { normal = { textColor = motionController.IsRunning() ? Color.green : Color.red } });
        GUILayout.Label($"Phase: {motionController.GetCurrentPhase()}");
        GUILayout.Label($"Frames: {motionController.GetFrameCount()}");

        GUILayout.Space(20);
        GUILayout.Label("Phase");

        if (GUILayout.Button("Phase1 Data+Debug", GUILayout.Height(36), GUILayout.ExpandWidth(true)))
        {
            motionController.StartPhase(MotionController.Phase.Phase1_DataFlow);
            motionController.SetDebugVisualizationEnabled(true);
        }

        if (GUILayout.Button("Phase2 Torso", GUILayout.Height(36), GUILayout.ExpandWidth(true)))
        {
            motionController.StartPhase(MotionController.Phase.Phase2_TorsoControl);
        }

        if (GUILayout.Button("Phase4 Full", GUILayout.Height(36), GUILayout.ExpandWidth(true)))
        {
            motionController.StartPhase(MotionController.Phase.Phase4_RotationDriven);
        }

        GUILayout.Space(10);

        if (GUILayout.Button("Pause", GUILayout.Height(30), GUILayout.ExpandWidth(true)))
        {
            motionController.Pause();
        }

        if (GUILayout.Button("Resume", GUILayout.Height(30), GUILayout.ExpandWidth(true)))
        {
            motionController.Resume();
        }

        if (GUILayout.Button("Stop", GUILayout.Height(30), GUILayout.ExpandWidth(true)))
        {
            motionController.Stop();
        }

        GUILayout.Space(20);
        GUILayout.Label("Inject");

        if (GUILayout.Button("Inject T-Pose", GUILayout.Height(34), GUILayout.ExpandWidth(true)))
        {
            InjectTestFrame_TPose();
        }

        if (GUILayout.Button("Inject Walking", GUILayout.Height(34), GUILayout.ExpandWidth(true)))
        {
            InjectTestFrame_Walking();
        }

        if (GUILayout.Button("Inject Left Hand Forward", GUILayout.Height(34), GUILayout.ExpandWidth(true)))
        {
            InjectLeftHandForward();
        }

        if (GUILayout.Button("Inject Both Hands Up", GUILayout.Height(34), GUILayout.ExpandWidth(true)))
        {
            InjectBothHandsUp();
        }

        if (GUILayout.Button("Inject Squat", GUILayout.Height(34), GUILayout.ExpandWidth(true)))
        {
            InjectSquat();
        }

        if (GUILayout.Button("Play Demo Sequence", GUILayout.Height(34), GUILayout.ExpandWidth(true)))
        {
            StartDemoSequence();
        }

        if (GUILayout.Button("Stop Demo Sequence", GUILayout.Height(30), GUILayout.ExpandWidth(true)))
        {
            StopDemoSequence();
        }

        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    /// <summary>
    /// 注入 T-Pose (MediaPipe 33 點)
    /// </summary>
    private void InjectTestFrame_TPose()
    {
        float[] data = new float[99];  // 33 * 3 (無信心度)

        Vector3 head = new Vector3(0, 1.7f, 0);
        Vector3 l_shoulder = new Vector3(-0.4f, 1.5f, 0);
        Vector3 r_shoulder = new Vector3(0.4f, 1.5f, 0);
        Vector3 l_elbow = new Vector3(-0.8f, 1.5f, 0);
        Vector3 r_elbow = new Vector3(0.8f, 1.5f, 0);
        Vector3 l_wrist = new Vector3(-1.2f, 1.5f, 0);
        Vector3 r_wrist = new Vector3(1.2f, 1.5f, 0);
        Vector3 l_hip = new Vector3(-0.2f, 1.0f, 0);
        Vector3 r_hip = new Vector3(0.2f, 1.0f, 0);
        Vector3 l_knee = new Vector3(-0.2f, 0.5f, 0);
        Vector3 r_knee = new Vector3(0.2f, 0.5f, 0);
        Vector3 l_ankle = new Vector3(-0.2f, 0.0f, 0);
        Vector3 r_ankle = new Vector3(0.2f, 0.0f, 0);

        // 頭部 (0-10)
        SetVector3(data, 0, head);
        SetVector3(data, 1, head + Vector3.left * 0.05f);
        SetVector3(data, 2, head + Vector3.left * 0.1f);
        SetVector3(data, 3, head + Vector3.left * 0.15f);
        SetVector3(data, 4, head + Vector3.right * 0.05f);
        SetVector3(data, 5, head + Vector3.right * 0.1f);
        SetVector3(data, 6, head + Vector3.right * 0.15f);
        SetVector3(data, 7, head + Vector3.left * 0.2f);
        SetVector3(data, 8, head + Vector3.right * 0.2f);
        SetVector3(data, 9, head + Vector3.down * 0.1f + Vector3.left * 0.1f);
        SetVector3(data, 10, head + Vector3.down * 0.1f + Vector3.right * 0.1f);

        // 上身 (11-20)
        SetVector3(data, 11, l_shoulder);
        SetVector3(data, 12, r_shoulder);
        SetVector3(data, 13, l_elbow);
        SetVector3(data, 14, r_elbow);
        SetVector3(data, 15, l_wrist);
        SetVector3(data, 16, r_wrist);
        SetVector3(data, 17, l_wrist + Vector3.left * 0.1f);
        SetVector3(data, 18, r_wrist + Vector3.right * 0.1f);
        SetVector3(data, 19, l_wrist + Vector3.right * 0.1f);
        SetVector3(data, 20, r_wrist + Vector3.left * 0.1f);

        // 腰 (23-24) [21-22 skip]
        SetVector3(data, 21, Vector3.zero);  // skip
        SetVector3(data, 22, Vector3.zero);  // skip
        SetVector3(data, 23, l_hip);
        SetVector3(data, 24, r_hip);

        // 腿部 (25-30)
        SetVector3(data, 25, l_knee);
        SetVector3(data, 26, r_knee);
        SetVector3(data, 27, l_ankle);
        SetVector3(data, 28, r_ankle);
        SetVector3(data, 29, l_ankle + Vector3.down * 0.05f);
        SetVector3(data, 30, r_ankle + Vector3.down * 0.05f);

        // 腳尖 (31-32)
        SetVector3(data, 31, l_ankle + Vector3.down * 0.1f);
        SetVector3(data, 32, r_ankle + Vector3.down * 0.1f);

        HumanPoseData pose = new HumanPoseData();
        pose.SetJointsFromArray(data);
        motionController.InjectPoseData(pose);
        Debug.Log("[Phase1Test] Injected T-Pose (MediaPipe 33-point)");
    }

    /// <summary>
    /// 注入行走姿態
    /// </summary>
    private void InjectTestFrame_Walking()
    {
        float[] data = new float[99];

        Vector3 head = new Vector3(0, 1.7f, 0.1f);
        Vector3 l_shoulder = new Vector3(-0.4f, 1.5f, -0.1f);
        Vector3 r_shoulder = new Vector3(0.4f, 1.5f, 0.1f);
        Vector3 l_elbow = new Vector3(-0.7f, 1.3f, -0.2f);
        Vector3 r_elbow = new Vector3(0.7f, 1.3f, 0.2f);
        Vector3 l_wrist = new Vector3(-0.9f, 1.1f, -0.3f);
        Vector3 r_wrist = new Vector3(0.9f, 1.1f, 0.3f);
        Vector3 l_hip = new Vector3(-0.25f, 1.0f, -0.1f);
        Vector3 r_hip = new Vector3(0.15f, 1.0f, 0.1f);
        Vector3 l_knee = new Vector3(-0.25f, 0.5f, 0);
        Vector3 r_knee = new Vector3(0.15f, 0.6f, 0);
        Vector3 l_ankle = new Vector3(-0.25f, 0.0f, 0.1f);
        Vector3 r_ankle = new Vector3(0.15f, 0.1f, 0);

        // 頭部
        SetVector3(data, 0, head);
        for (int i = 1; i <= 10; i++)
            SetVector3(data, i, head);

        // 上身
        SetVector3(data, 11, l_shoulder);
        SetVector3(data, 12, r_shoulder);
        SetVector3(data, 13, l_elbow);
        SetVector3(data, 14, r_elbow);
        SetVector3(data, 15, l_wrist);
        SetVector3(data, 16, r_wrist);
        for (int i = 17; i <= 20; i++)
            SetVector3(data, i, Vector3.Lerp(l_shoulder, r_shoulder, 0.5f));

        // 腰
        SetVector3(data, 23, l_hip);
        SetVector3(data, 24, r_hip);

        // 腿部
        SetVector3(data, 25, l_knee);
        SetVector3(data, 26, r_knee);
        SetVector3(data, 27, l_ankle);
        SetVector3(data, 28, r_ankle);
        SetVector3(data, 29, l_ankle + Vector3.down * 0.05f);
        SetVector3(data, 30, r_ankle + Vector3.down * 0.05f);
        SetVector3(data, 31, l_ankle + Vector3.down * 0.1f);
        SetVector3(data, 32, r_ankle + Vector3.down * 0.1f);

        HumanPoseData pose = new HumanPoseData();
        pose.SetJointsFromArray(data);
        motionController.InjectPoseData(pose);
        Debug.Log("[Phase1Test] Injected Walking frame");
    }

    private void InjectLeftHandForward()
    {
        float[] data = BuildBaseTPoseData();

        // 左手往前伸，右手保持在側邊
        SetVector3(data, 13, new Vector3(-0.7f, 1.45f, 0.15f)); // L_Elbow
        SetVector3(data, 15, new Vector3(-0.95f, 1.4f, 0.45f)); // L_Wrist
        SetVector3(data, 17, new Vector3(-1.05f, 1.4f, 0.48f));
        SetVector3(data, 19, new Vector3(-0.9f, 1.42f, 0.5f));

        InjectPose(data, "[Phase1Test] Injected Left Hand Forward");
    }

    private void InjectBothHandsUp()
    {
        float[] data = BuildBaseTPoseData();

        // 雙手上舉
        SetVector3(data, 13, new Vector3(-0.35f, 1.85f, 0.05f));
        SetVector3(data, 15, new Vector3(-0.2f, 2.15f, 0.1f));
        SetVector3(data, 14, new Vector3(0.35f, 1.85f, 0.05f));
        SetVector3(data, 16, new Vector3(0.2f, 2.15f, 0.1f));

        SetVector3(data, 17, new Vector3(-0.23f, 2.2f, 0.12f));
        SetVector3(data, 18, new Vector3(0.23f, 2.2f, 0.12f));
        SetVector3(data, 19, new Vector3(-0.16f, 2.18f, 0.14f));
        SetVector3(data, 20, new Vector3(0.16f, 2.18f, 0.14f));

        InjectPose(data, "[Phase1Test] Injected Both Hands Up");
    }

    private void InjectSquat()
    {
        float[] data = BuildBaseTPoseData();

        // 髖下降 + 膝彎曲
        SetVector3(data, 23, new Vector3(-0.2f, 0.8f, 0.05f));
        SetVector3(data, 24, new Vector3(0.2f, 0.8f, 0.05f));
        SetVector3(data, 25, new Vector3(-0.22f, 0.45f, 0.28f));
        SetVector3(data, 26, new Vector3(0.22f, 0.45f, 0.28f));
        SetVector3(data, 27, new Vector3(-0.24f, 0.08f, 0.12f));
        SetVector3(data, 28, new Vector3(0.24f, 0.08f, 0.12f));

        InjectPose(data, "[Phase1Test] Injected Squat");
    }

    private void StartDemoSequence()
    {
        StopDemoSequence();
        demoSequence = StartCoroutine(DemoSequenceRoutine());
    }

    private void StopDemoSequence()
    {
        if (demoSequence != null)
        {
            StopCoroutine(demoSequence);
            demoSequence = null;
        }
    }

    private System.Collections.IEnumerator DemoSequenceRoutine()
    {
        InjectTestFrame_TPose();
        yield return new WaitForSeconds(0.6f);

        InjectLeftHandForward();
        yield return new WaitForSeconds(0.6f);

        InjectBothHandsUp();
        yield return new WaitForSeconds(0.6f);

        InjectSquat();
        yield return new WaitForSeconds(0.6f);

        InjectTestFrame_Walking();
        yield return new WaitForSeconds(0.6f);

        InjectTestFrame_TPose();
        demoSequence = null;
    }

    private float[] BuildBaseTPoseData()
    {
        float[] data = new float[99];

        Vector3 head = new Vector3(0, 1.7f, 0);
        Vector3 l_shoulder = new Vector3(-0.4f, 1.5f, 0);
        Vector3 r_shoulder = new Vector3(0.4f, 1.5f, 0);
        Vector3 l_elbow = new Vector3(-0.8f, 1.5f, 0);
        Vector3 r_elbow = new Vector3(0.8f, 1.5f, 0);
        Vector3 l_wrist = new Vector3(-1.2f, 1.5f, 0);
        Vector3 r_wrist = new Vector3(1.2f, 1.5f, 0);
        Vector3 l_hip = new Vector3(-0.2f, 1.0f, 0);
        Vector3 r_hip = new Vector3(0.2f, 1.0f, 0);
        Vector3 l_knee = new Vector3(-0.2f, 0.5f, 0);
        Vector3 r_knee = new Vector3(0.2f, 0.5f, 0);
        Vector3 l_ankle = new Vector3(-0.2f, 0.0f, 0);
        Vector3 r_ankle = new Vector3(0.2f, 0.0f, 0);

        SetVector3(data, 0, head);
        SetVector3(data, 1, head + Vector3.left * 0.05f);
        SetVector3(data, 2, head + Vector3.left * 0.1f);
        SetVector3(data, 3, head + Vector3.left * 0.15f);
        SetVector3(data, 4, head + Vector3.right * 0.05f);
        SetVector3(data, 5, head + Vector3.right * 0.1f);
        SetVector3(data, 6, head + Vector3.right * 0.15f);
        SetVector3(data, 7, head + Vector3.left * 0.2f);
        SetVector3(data, 8, head + Vector3.right * 0.2f);
        SetVector3(data, 9, head + Vector3.down * 0.1f + Vector3.left * 0.1f);
        SetVector3(data, 10, head + Vector3.down * 0.1f + Vector3.right * 0.1f);

        SetVector3(data, 11, l_shoulder);
        SetVector3(data, 12, r_shoulder);
        SetVector3(data, 13, l_elbow);
        SetVector3(data, 14, r_elbow);
        SetVector3(data, 15, l_wrist);
        SetVector3(data, 16, r_wrist);
        SetVector3(data, 17, l_wrist + Vector3.left * 0.1f);
        SetVector3(data, 18, r_wrist + Vector3.right * 0.1f);
        SetVector3(data, 19, l_wrist + Vector3.right * 0.1f);
        SetVector3(data, 20, r_wrist + Vector3.left * 0.1f);

        SetVector3(data, 21, Vector3.zero);
        SetVector3(data, 22, Vector3.zero);
        SetVector3(data, 23, l_hip);
        SetVector3(data, 24, r_hip);

        SetVector3(data, 25, l_knee);
        SetVector3(data, 26, r_knee);
        SetVector3(data, 27, l_ankle);
        SetVector3(data, 28, r_ankle);
        SetVector3(data, 29, l_ankle + Vector3.down * 0.05f);
        SetVector3(data, 30, r_ankle + Vector3.down * 0.05f);
        SetVector3(data, 31, l_ankle + Vector3.down * 0.1f);
        SetVector3(data, 32, r_ankle + Vector3.down * 0.1f);

        return data;
    }

    private void InjectPose(float[] data, string log)
    {
        HumanPoseData pose = new HumanPoseData();
        pose.SetJointsFromArray(data);
        motionController.InjectPoseData(pose);
        Debug.Log(log);
    }

    /// <summary>
    /// 輔助函數：在陣列中設定 Vector3
    /// 格式：x, y, z (連續)
    /// </summary>
    private void SetVector3(float[] data, int jointIndex, Vector3 value)
    {
        int baseIndex = jointIndex * 3;
        data[baseIndex] = value.x;
        data[baseIndex + 1] = value.y;
        data[baseIndex + 2] = value.z;
    }
}
