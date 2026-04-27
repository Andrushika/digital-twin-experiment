using UnityEngine;

/// <summary>
/// MotionController - 整合所有模組的主控制器
/// 
/// 責任：
/// 1. 協調 Module A (DataReceiver) → B (PoseInterpreter) → C (RetargetSolver) 的資料流
/// 2. 管理各 Phase 的啟用/禁用
/// 3. 提供測試與調試介面
/// 
/// 使用方式：
/// 1. 掛上 Avatar GameObject
/// 2. 設定 input mode (File/UDP/TCP/DirectTest)
/// 3. 按下相應的 Phase 按鈕進行測試
/// </summary>
public class MotionController : MonoBehaviour
{
    [System.Serializable]
    public enum Phase
    {
        Phase0_Standby,              // 待命
        Phase1_DataFlow,             // 打通資料流 + debug visualization
        Phase2_TorsoControl,         // 驅動軀幹 (pelvis/chest/head)
        Phase3_LimbControl,          // 驅動四肢
        Phase4_RotationDriven,       // 完整旋轉驅動
        Phase5_IKConstraint,         // 加 IK 與約束
        Phase6_Stabilization         // 穩定化 (smoothing/limits)
    }

    [SerializeField] private Phase currentPhase = Phase.Phase0_Standby;
    [SerializeField] private Animator targetAvatar;

    // 三大模組
    private DataReceiver dataReceiver;
    private PoseInterpreter poseInterpreter = new PoseInterpreter();
    private RetargetSolver retargetSolver;
    private Phase1_DebugVisualizer debugVisualizer;

    // 狀態
    private bool isRunning = false;
    private PoseInterpreter.InterpretedPose currentInterpretedPose;

    private void Start()
    {
        // 自動取得或建立各個模組
        SetupModules();
    }

    private void SetupModules()
    {
        // DataReceiver (Module A)
        dataReceiver = GetComponent<DataReceiver>();
        if (dataReceiver == null)
        {
            dataReceiver = gameObject.AddComponent<DataReceiver>();
            Debug.Log("[MotionController] DataReceiver created");
        }

        // RetargetSolver (Module C)
        if (targetAvatar == null)
            targetAvatar = GetComponent<Animator>();

        retargetSolver = GetComponent<RetargetSolver>();
        if (retargetSolver == null)
        {
            retargetSolver = gameObject.AddComponent<RetargetSolver>();
            Debug.Log("[MotionController] RetargetSolver created");
        }
        retargetSolver.SetTargetAvatar(targetAvatar);

        // Debug visualizer
        debugVisualizer = GetComponent<Phase1_DebugVisualizer>();
        if (debugVisualizer == null)
        {
            debugVisualizer = gameObject.AddComponent<Phase1_DebugVisualizer>();
            Debug.Log("[MotionController] Phase1_DebugVisualizer created");
        }

        // 訂閱 DataReceiver 的新幀事件
        dataReceiver.OnNewFrame += OnNewFrameReceived;

        Debug.Log("[MotionController] All modules ready");
    }

    private void Update()
    {
        if (!isRunning)
            return;

        // 根據當前 Phase 執行不同邏輯
        switch (currentPhase)
        {
            case Phase.Phase1_DataFlow:
                UpdatePhase1();
                break;
            case Phase.Phase2_TorsoControl:
                UpdatePhase2();
                break;
            case Phase.Phase3_LimbControl:
                UpdatePhase3();
                break;
            case Phase.Phase4_RotationDriven:
                UpdatePhase4();
                break;
            default:
                break;
        }
    }

    /// <summary>
    /// Phase 1: 打通資料流
    /// 收資料 → 解釋 → debug 顯示
    /// (不實際驅動 avatar)
    /// </summary>
    private void UpdatePhase1()
    {
        HumanPoseData latestFrame = dataReceiver.GetLatestFrame();
        if (latestFrame != null && latestFrame.IsValid)
        {
            currentInterpretedPose = poseInterpreter.Interpret(latestFrame);
        }
    }

    /// <summary>
    /// Phase 2: 軀幹控制
    /// 驅動 pelvis, chest, head
    /// </summary>
    private void UpdatePhase2()
    {
        HumanPoseData latestFrame = dataReceiver.GetLatestFrame();
        if (latestFrame != null && latestFrame.IsValid)
        {
            currentInterpretedPose = poseInterpreter.Interpret(latestFrame);
            
            // 只套用軀幹旋轉 (由 RetargetSolver 內部控制)
            if (currentInterpretedPose != null)
            {
                retargetSolver.ApplyPoseTorsoOnly(currentInterpretedPose);
            }
        }
    }

    /// <summary>
    /// Phase 3: 四肢控制
    /// 驅動 limbs (在 Phase 2 基礎上加入四肢)
    /// </summary>
    private void UpdatePhase3()
    {
        HumanPoseData latestFrame = dataReceiver.GetLatestFrame();
        if (latestFrame != null && latestFrame.IsValid)
        {
            currentInterpretedPose = poseInterpreter.Interpret(latestFrame);

            if (currentInterpretedPose != null)
            {
                retargetSolver.ApplyPose(currentInterpretedPose);
            }
        }
    }

    /// <summary>
    /// Phase 4: 完整旋轉驅動
    /// 全身骨架旋轉驅動成立
    /// </summary>
    private void UpdatePhase4()
    {
        UpdatePhase3();  // Phase 4 繼承 Phase 3 的完整功能（含四肢）
    }

    /// <summary>
    /// 新幀到達時的回調
    /// </summary>
    private void OnNewFrameReceived(HumanPoseData pose)
    {
        // 可以在這裡加 log 或監控
        if (pose.FrameIndex % 30 == 0)  // 每 30 幀 log 一次
        {
            Debug.Log($"[MotionController] Frame {pose.FrameIndex} received at {pose.Timestamp:F2}s");
        }
    }

    // ===== 外部控制介面 (測試用) =====

    /// <summary>
    /// 從 CSV 字串注入一幀
    /// 格式: x1,y1,z1,x2,y2,z2,...,x17,y17,z17
    /// </summary>
    public void InjectCSVFrame(string csvLine)
    {
        dataReceiver.ReceiveCSVLine(csvLine);
    }

    /// <summary>
    /// 直接注入 HumanPoseData
    /// (Python 可通過 DLL 呼叫)
    /// </summary>
    public void InjectPoseData(HumanPoseData pose)
    {
        dataReceiver.ReceiveFrame(pose);
    }

    public void CalibrateFromPoseData(HumanPoseData pose)
    {
        if (pose == null || !pose.IsValid)
        {
            Debug.LogWarning("[MotionController] Cannot calibrate: invalid pose");
            return;
        }

        PoseInterpreter.InterpretedPose interpreted = poseInterpreter.Interpret(pose);
        retargetSolver.CalibrateFromPose(interpreted);
    }

    public void CalibrateFromCurrentFrame()
    {
        HumanPoseData latestFrame = dataReceiver.GetLatestFrame();
        if (latestFrame == null)
        {
            Debug.LogWarning("[MotionController] Cannot calibrate: no frame received yet");
            return;
        }

        CalibrateFromPoseData(latestFrame);
    }

    public void ResetRetargetCalibration()
    {
        retargetSolver.ResetCalibration();
    }

    /// <summary>
    /// 啟動指定 Phase
    /// </summary>
    public void StartPhase(Phase phase)
    {
        currentPhase = phase;
        isRunning = true;
        Debug.Log($"[MotionController] Started {phase}");
    }

    /// <summary>
    /// 暫停
    /// </summary>
    public void Pause()
    {
        isRunning = false;
        Debug.Log("[MotionController] Paused");
    }

    /// <summary>
    /// 恢復
    /// </summary>
    public void Resume()
    {
        isRunning = true;
        Debug.Log("[MotionController] Resumed");
    }

    /// <summary>
    /// 停止
    /// </summary>
    public void Stop()
    {
        isRunning = false;
        currentPhase = Phase.Phase0_Standby;
        Debug.Log("[MotionController] Stopped");
    }

    /// <summary>
    /// 設定 debug 顯示
    /// </summary>
    public void SetDebugVisualizationEnabled(bool enabled)
    {
        if (debugVisualizer != null)
            debugVisualizer.enabled = enabled;
    }

    public Phase GetCurrentPhase() => currentPhase;
    public bool IsRunning() => isRunning;
    public int GetFrameCount() => dataReceiver.GetFrameCount();
}
