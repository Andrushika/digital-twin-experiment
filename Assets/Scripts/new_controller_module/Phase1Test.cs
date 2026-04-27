using UnityEngine;

/// <summary>
/// Phase 1 Test Script
/// 
/// 用途：快速測試資料打通與 debug visualization
/// 
/// 使用方式：
/// 1. 建立一個 empty GameObject
/// 2. 掛上 MotionController (會自動建立 DataReceiver)
/// 3. 掛上這個 Phase1Test 腳本
/// 4. 在 Editor 按下按鈕或用程式呼叫方法
/// 
/// 可以用 Python 直接推送測試資料到 Unity
/// </summary>
public class Phase1Test : MonoBehaviour
{
    private MotionController motionController;

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

        Debug.Log("[Phase1Test] Ready for testing");
    }

    // ===== GUI Buttons (在 Scene view 用) =====

    private void OnGUI()
    {
        GUILayout.BeginArea(new Rect(10, 10, 400, 500));
        GUILayout.Label("=== Phase 1 Motion Controller Test ===", new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold });

        if (motionController == null)
        {
            GUILayout.Label("ERROR: No MotionController!", new GUIStyle(GUI.skin.label) { normal = { textColor = Color.red } });
            GUILayout.EndArea();
            return;
        }

        GUILayout.Space(10);
        GUILayout.Label($"Status: {(motionController.IsRunning() ? "RUNNING" : "STOPPED")}", 
            new GUIStyle(GUI.skin.label) { normal = { textColor = motionController.IsRunning() ? Color.green : Color.red } });
        GUILayout.Label($"Phase: {motionController.GetCurrentPhase()}");
        GUILayout.Label($"Frames: {motionController.GetFrameCount()}");

        GUILayout.Space(20);
        GUILayout.Label("--- Phase Selection ---");

        if (GUILayout.Button("Phase 1: Data Flow + Debug", GUILayout.Height(40)))
        {
            motionController.StartPhase(MotionController.Phase.Phase1_DataFlow);
            motionController.SetDebugVisualizationEnabled(true);
        }

        if (GUILayout.Button("Phase 2: Torso Control", GUILayout.Height(40)))
        {
            motionController.StartPhase(MotionController.Phase.Phase2_TorsoControl);
        }

        if (GUILayout.Button("Phase 3: Limb Control", GUILayout.Height(40)))
        {
            motionController.StartPhase(MotionController.Phase.Phase3_LimbControl);
        }

        if (GUILayout.Button("Phase 4: Full Rotation", GUILayout.Height(40)))
        {
            motionController.StartPhase(MotionController.Phase.Phase4_RotationDriven);
        }

        GUILayout.Space(10);

        if (GUILayout.Button("Pause", GUILayout.Height(30)))
        {
            motionController.Pause();
        }

        if (GUILayout.Button("Resume", GUILayout.Height(30)))
        {
            motionController.Resume();
        }

        if (GUILayout.Button("Stop", GUILayout.Height(30)))
        {
            motionController.Stop();
        }

        GUILayout.Space(20);
        GUILayout.Label("--- Test Data ---");

        if (GUILayout.Button("Inject Test Frame (T-Pose)", GUILayout.Height(35)))
        {
            InjectTestFrame_TPose();
        }

        if (GUILayout.Button("Inject Test Frame (Walking)", GUILayout.Height(35)))
        {
            InjectTestFrame_Walking();
        }

        GUILayout.EndArea();
    }

    /// <summary>
    /// 注入測試幀：標準 T-Pose
    /// </summary>
    private void InjectTestFrame_TPose()
    {
        float[] tposeData = new float[51];
        
        // Nose
        tposeData[0] = 0f; tposeData[1] = 1.7f; tposeData[2] = 0f;
        
        // Eyes
        tposeData[3] = -0.1f; tposeData[4] = 1.65f; tposeData[5] = 0f;
        tposeData[6] = 0.1f; tposeData[7] = 1.65f; tposeData[8] = 0f;
        
        // Ears
        tposeData[9] = -0.2f; tposeData[10] = 1.65f; tposeData[11] = 0f;
        tposeData[12] = 0.2f; tposeData[13] = 1.65f; tposeData[14] = 0f;
        
        // Shoulders
        tposeData[15] = -0.4f; tposeData[16] = 1.5f; tposeData[17] = 0f;  // L_Shoulder
        tposeData[18] = 0.4f; tposeData[19] = 1.5f; tposeData[20] = 0f;   // R_Shoulder
        
        // Elbows (T-Pose: 直角)
        tposeData[21] = -0.8f; tposeData[22] = 1.5f; tposeData[23] = 0f;  // L_Elbow
        tposeData[24] = 0.8f; tposeData[25] = 1.5f; tposeData[26] = 0f;   // R_Elbow
        
        // Wrists
        tposeData[27] = -1.2f; tposeData[28] = 1.5f; tposeData[29] = 0f;  // L_Wrist
        tposeData[30] = 1.2f; tposeData[31] = 1.5f; tposeData[32] = 0f;   // R_Wrist
        
        // Hips
        tposeData[33] = -0.2f; tposeData[34] = 1.0f; tposeData[35] = 0f;  // L_Hip
        tposeData[36] = 0.2f; tposeData[37] = 1.0f; tposeData[38] = 0f;   // R_Hip
        
        // Knees
        tposeData[39] = -0.2f; tposeData[40] = 0.5f; tposeData[41] = 0f;  // L_Knee
        tposeData[42] = 0.2f; tposeData[43] = 0.5f; tposeData[44] = 0f;   // R_Knee
        
        // Ankles
        tposeData[45] = -0.2f; tposeData[46] = 0.0f; tposeData[47] = 0f;  // L_Ankle
        tposeData[48] = 0.2f; tposeData[49] = 0.0f; tposeData[50] = 0f;   // R_Ankle

        HumanPoseData pose = new HumanPoseData();
        pose.SetJointsFromArray(tposeData);
        
        motionController.InjectPoseData(pose);
        Debug.Log("[Phase1Test] Injected T-Pose frame");
    }

    /// <summary>
    /// 注入測試幀：簡單行走姿態
    /// </summary>
    private void InjectTestFrame_Walking()
    {
        float[] walkingData = new float[51];
        
        // 頭部 (略微前傾)
        walkingData[0] = 0f; walkingData[1] = 1.7f; walkingData[2] = 0.1f;
        
        // 眼、耳
        walkingData[3] = -0.1f; walkingData[4] = 1.65f; walkingData[5] = 0.1f;
        walkingData[6] = 0.1f; walkingData[7] = 1.65f; walkingData[8] = 0.1f;
        walkingData[9] = -0.2f; walkingData[10] = 1.65f; walkingData[11] = 0.1f;
        walkingData[12] = 0.2f; walkingData[13] = 1.65f; walkingData[14] = 0.1f;
        
        // 肩膀 (扭轉)
        walkingData[15] = -0.4f; walkingData[16] = 1.5f; walkingData[17] = -0.1f;
        walkingData[18] = 0.4f; walkingData[19] = 1.5f; walkingData[20] = 0.1f;
        
        // 手臂 (搖擺)
        walkingData[21] = -0.7f; walkingData[22] = 1.3f; walkingData[23] = -0.2f;
        walkingData[24] = 0.7f; walkingData[25] = 1.3f; walkingData[26] = 0.2f;
        
        walkingData[27] = -0.9f; walkingData[28] = 1.1f; walkingData[29] = -0.3f;
        walkingData[30] = 0.9f; walkingData[31] = 1.1f; walkingData[32] = 0.3f;
        
        // 髖部 (輕微扭轉)
        walkingData[33] = -0.25f; walkingData[34] = 1.0f; walkingData[35] = -0.1f;
        walkingData[36] = 0.15f; walkingData[37] = 1.0f; walkingData[38] = 0.1f;
        
        // 腿部 (步態)
        walkingData[39] = -0.25f; walkingData[40] = 0.5f; walkingData[41] = 0f;
        walkingData[42] = 0.15f; walkingData[43] = 0.6f; walkingData[44] = 0f;
        
        walkingData[45] = -0.25f; walkingData[46] = 0.0f; walkingData[47] = 0.1f;
        walkingData[48] = 0.15f; walkingData[49] = 0.1f; walkingData[50] = 0f;

        HumanPoseData pose = new HumanPoseData();
        pose.SetJointsFromArray(walkingData);
        
        motionController.InjectPoseData(pose);
        Debug.Log("[Phase1Test] Injected Walking frame");
    }

    /// <summary>
    /// 模擬 Python 持續推送資料 (測試用)
    /// </summary>
    public void SimulatePythonStream()
    {
        // 可以用 Coroutine 定期推送测試資料
        StartCoroutine(StreamTestData());
    }

    private System.Collections.IEnumerator StreamTestData()
    {
        float t = 0f;
        while (Application.isPlaying)
        {
            // 模擬連續的 T-Pose 到 walking 插值
            float[] data = new float[51];
            
            // 簡單的正弦波擾動
            data[0] = Mathf.Sin(t) * 0.2f;  // Nose X
            data[1] = 1.7f;                  // Nose Y (固定)
            data[2] = 0f;                    // Nose Z
            
            // ... (其他點位簡化處理)
            for (int i = 3; i < 51; i++)
                data[i] = 0f;

            HumanPoseData pose = new HumanPoseData();
            pose.SetJointsFromArray(data);
            motionController.InjectPoseData(pose);

            t += Time.deltaTime;
            yield return new WaitForSeconds(0.033f);  // ~30 fps
        }
    }
}
