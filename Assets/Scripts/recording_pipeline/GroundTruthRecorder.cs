using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Ground-Truth Recorder
///
/// 從一段已知的 Avatar 動畫中抽取「無雜訊」的 33 點 keypoints，
/// 輸出成 CSV，可直接餵給 DataReceiver（MediaPipe_World preset）做 pipeline 驗證。
///
/// 兩種輸入模式：
///   - BakeFromClip ：給 Animator + AnimationClip，離線以固定 FPS 取樣（最乾淨）
///   - LiveCapture  ：在 Play 時持續抓 bone.position（不論 avatar 被誰驅動）
///
/// 輸出格式：與 pose_data.csv 完全相同 — 每行 99 個 float (33 joints × x,y,z)，無 header。
/// 預設啟用 useMediaPipeConvention（Unity 世界座標 → 翻 X/Z），讓 DataReceiver 能用預設 preset 直接讀回。
/// </summary>
[DisallowMultipleComponent]
public class GroundTruthRecorder : MonoBehaviour
{
    public enum RecordMode
    {
        BakeFromClip,
        LiveCapture
    }

    [Header("Mode")]
    [SerializeField] private RecordMode mode = RecordMode.BakeFromClip;

    [Header("Source")]
    [Tooltip("有 Humanoid Avatar 的 Animator。BakeFromClip 模式必填；LiveCapture 也建議指定。")]
    [SerializeField] private Animator targetAnimator;

    [Tooltip("要 bake 的動畫片段（BakeFromClip 模式必填）")]
    [SerializeField] private AnimationClip clip;

    [Tooltip("採樣頻率（Hz）。30 fps 與 pose_data.csv 預設一致。")]
    [SerializeField] private float fps = 30f;

    [Tooltip("動畫播放速度倍率（只影響 BakeFromClip）：\n" +
             "  1.0 = 原速\n" +
             "  0.5 = 慢動作（CSV 取樣更密 → DataReceiver 播放時看起來慢 2x）\n" +
             "  2.0 = 快轉（CSV 取樣更疏 → DataReceiver 播放時看起來快 2x）")]
    [Range(0.1f, 5f)]
    [SerializeField] private float speed = 1f;

    [Header("Output")]
    [Tooltip("CSV 輸出路徑。可填絕對路徑、Assets 內相對路徑、或專案根相對路徑。")]
    [SerializeField] private string outputPath = "Assets/Scripts/new_controller_module/ground_truth.csv";

    [Tooltip("套用 X/Z 翻轉，與 DataReceiver 的 MediaPipe_World preset 相容（drop-in 替換 pose_data.csv）")]
    [SerializeField] private bool useMediaPipeConvention = true;

    [Header("Live Mode (optional)")]
    [SerializeField] private KeyCode startKey = KeyCode.R;
    [SerializeField] private KeyCode stopKey = KeyCode.T;
    [Tooltip("Live 模式下，第一次按 start 才開始計時；按 stop 才寫檔")]
    [SerializeField] private bool liveAutoStartOnPlay = false;

    private readonly List<float[]> _frames = new List<float[]>();
    private bool _isLiveRecording = false;
    private float _liveAccumulator = 0f;

    public Animator TargetAnimator => targetAnimator;

    // 臉部 / 額外點 用 head transform 估算的本地偏移（公尺）
    private const float HeadToNoseForward = 0.10f;
    private const float HeadToNoseDown = -0.04f;
    private const float EyeForward = 0.09f;
    private const float EyeUp = 0.03f;
    private const float EyeOuterSide = 0.04f;
    private const float EyeInnerSide = 0.015f;
    private const float EarSide = 0.075f;
    private const float EarUp = 0.02f;
    private const float MouthForward = 0.08f;
    private const float MouthDown = -0.05f;
    private const float MouthSide = 0.02f;

    private const float HeelBackOffset = 0.06f;
    private const float HeelDownOffset = 0.07f;     // heel 在 ankle 下方多少 — 必須 > 0 否則 foot plane 退化
    private const float ToeForwardOffset = 0.12f;
    private const float HandMcpForwardOffset = 0.08f;
    private const float HandMcpSideOffset = 0.025f;

    private void Start()
    {
        if (mode == RecordMode.LiveCapture && liveAutoStartOnPlay)
            BeginLiveRecording();
    }

    private void Update()
    {
        if (mode != RecordMode.LiveCapture) return;

        if (Input.GetKeyDown(startKey) && !_isLiveRecording)
            BeginLiveRecording();

        if (Input.GetKeyDown(stopKey) && _isLiveRecording)
            EndLiveRecordingAndWrite();

        if (_isLiveRecording)
        {
            _liveAccumulator += Time.deltaTime;
            float interval = 1f / Mathf.Max(1f, fps);
            while (_liveAccumulator >= interval)
            {
                _frames.Add(SampleCurrentPose());
                _liveAccumulator -= interval;
            }
        }
    }

    [ContextMenu("Bake Animation To CSV")]
    public void BakeAnimationToCSV()
    {
        if (mode != RecordMode.BakeFromClip)
        {
            Debug.LogError("[GroundTruthRecorder] mode must be BakeFromClip to use this menu");
            return;
        }
        if (clip == null)
        {
            Debug.LogError("[GroundTruthRecorder] clip is required for BakeFromClip");
            return;
        }
        if (!TryResolveTargetAnimator(out Animator animator))
        {
            Debug.LogError("[GroundTruthRecorder] targetAnimator must use a Humanoid Avatar");
            return;
        }

        _frames.Clear();
        float duration = clip.length;
        float spd = Mathf.Max(0.01f, speed);
        // clipDt = 每個 output frame 對應多少 clip 時間
        // speed=0.5 → clipDt 變一半 → 同樣 fps 下取樣更密 → CSV 變長 → 播放慢動作
        float clipDt = spd / Mathf.Max(1f, fps);
        int total = Mathf.Max(1, Mathf.CeilToInt(duration / clipDt) + 1);

        for (int i = 0; i < total; i++)
        {
            float t = Mathf.Min(i * clipDt, duration);
            clip.SampleAnimation(animator.gameObject, t);
            _frames.Add(SampleCurrentPose());
        }

        WriteCSV();
        float playbackSec = _frames.Count / Mathf.Max(1f, fps);
        Debug.Log($"[GroundTruthRecorder] Baked {_frames.Count} frames @ {fps}fps × speed {spd:F2} " +
                  $"(clip {duration:F3}s → CSV plays back {playbackSec:F3}s) → {ResolveOutputPath()}");
    }

    [ContextMenu("Begin Live Recording")]
    public void BeginLiveRecording()
    {
        if (!TryResolveTargetAnimator(out _))
        {
            Debug.LogError("[GroundTruthRecorder] targetAnimator with Humanoid Avatar required");
            return;
        }
        _frames.Clear();
        _liveAccumulator = 0f;
        _isLiveRecording = true;
        Debug.Log($"[GroundTruthRecorder] Live recording started @ {fps}fps");
    }

    [ContextMenu("End Live Recording And Write")]
    public void EndLiveRecordingAndWrite()
    {
        if (!_isLiveRecording)
        {
            Debug.LogWarning("[GroundTruthRecorder] Not currently live-recording");
            return;
        }
        _isLiveRecording = false;
        WriteCSV();
        Debug.Log($"[GroundTruthRecorder] Live recording stopped: {_frames.Count} frames → {ResolveOutputPath()}");
    }

    /// <summary>
    /// 從目前 avatar 的 bone.position 抽出 33 個 keypoints (Unity 世界座標，無 convention 翻轉)。
    /// 公開給 TwistDiagnostic 等工具直接用。
    /// </summary>
    public Vector3[] SampleJointPositionsWorld()
    {
        Vector3[] j = new Vector3[33];
        if (!TryResolveTargetAnimator(out Animator animator)) return j;

        Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
        Transform leftShoulder = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        Transform rightShoulder = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        Transform leftElbow = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        Transform rightElbow = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
        Transform leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        Transform rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        Transform leftHip = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
        Transform rightHip = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
        Transform leftKnee = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
        Transform rightKnee = animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
        Transform leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        Transform rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
        Transform leftToes = animator.GetBoneTransform(HumanBodyBones.LeftToes);
        Transform rightToes = animator.GetBoneTransform(HumanBodyBones.RightToes);

        // 主要關節：直接取 bone.position
        SetIfPresent(j, HumanPoseData.JointType.L_Shoulder, leftShoulder);
        SetIfPresent(j, HumanPoseData.JointType.R_Shoulder, rightShoulder);
        SetIfPresent(j, HumanPoseData.JointType.L_Elbow, leftElbow);
        SetIfPresent(j, HumanPoseData.JointType.R_Elbow, rightElbow);
        SetIfPresent(j, HumanPoseData.JointType.L_Wrist, leftHand);
        SetIfPresent(j, HumanPoseData.JointType.R_Wrist, rightHand);
        SetIfPresent(j, HumanPoseData.JointType.L_Hip, leftHip);
        SetIfPresent(j, HumanPoseData.JointType.R_Hip, rightHip);
        SetIfPresent(j, HumanPoseData.JointType.L_Knee, leftKnee);
        SetIfPresent(j, HumanPoseData.JointType.R_Knee, rightKnee);
        SetIfPresent(j, HumanPoseData.JointType.L_Ankle, leftFoot);
        SetIfPresent(j, HumanPoseData.JointType.R_Ankle, rightFoot);

        // 腳：heel / foot_index
        EstimateFootKeypoints(leftFoot, leftToes,
            out Vector3 lHeel, out Vector3 lFootIdx);
        EstimateFootKeypoints(rightFoot, rightToes,
            out Vector3 rHeel, out Vector3 rFootIdx);
        j[(int)HumanPoseData.JointType.L_Heel] = lHeel;
        j[(int)HumanPoseData.JointType.R_Heel] = rHeel;
        j[(int)HumanPoseData.JointType.L_Foot_Index] = lFootIdx;
        j[(int)HumanPoseData.JointType.R_Foot_Index] = rFootIdx;

        // 手部：pinky / index MCP（指根）。MediaPipe Pose 的手部點在這條 pipeline 以 MCP 語意使用。
        EstimateMcpKeypoint(animator, leftHand, HumanBodyBones.LeftIndexProximal,
            isLeft: true, isIndex: true, out Vector3 lIndex);
        EstimateMcpKeypoint(animator, leftHand, HumanBodyBones.LeftLittleProximal,
            isLeft: true, isIndex: false, out Vector3 lPinky);
        EstimateMcpKeypoint(animator, rightHand, HumanBodyBones.RightIndexProximal,
            isLeft: false, isIndex: true, out Vector3 rIndex);
        EstimateMcpKeypoint(animator, rightHand, HumanBodyBones.RightLittleProximal,
            isLeft: false, isIndex: false, out Vector3 rPinky);
        j[(int)HumanPoseData.JointType.L_Index] = lIndex;
        j[(int)HumanPoseData.JointType.L_Pinky] = lPinky;
        j[(int)HumanPoseData.JointType.R_Index] = rIndex;
        j[(int)HumanPoseData.JointType.R_Pinky] = rPinky;

        // 臉部：head 沒有 nose/eye/ear/mouth 的 humanoid 對應，從 head transform 估算
        EstimateFaceKeypoints(animator, head, j);

        return j;
    }

    public bool TryResolveTargetAnimator(out Animator animator)
    {
        animator = targetAnimator;
        if (animator == null) animator = GetComponent<Animator>();
        if (animator == null) animator = GetComponentInParent<Animator>();

        if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
        {
            animator = null;
            return false;
        }

        if (targetAnimator == null)
            targetAnimator = animator;
        return true;
    }

    /// <summary>
    /// 內部 CSV 採樣：呼叫 SampleJointPositionsWorld 後套用 useMediaPipeConvention，攤平成 float[]。
    /// </summary>
    private float[] SampleCurrentPose()
    {
        Vector3[] j = SampleJointPositionsWorld();
        float[] data = new float[99];
        for (int i = 0; i < 33; i++)
        {
            Vector3 p = j[i];
            if (useMediaPipeConvention)
                p = new Vector3(-p.x, p.y, -p.z);
            data[i * 3 + 0] = p.x;
            data[i * 3 + 1] = p.y;
            data[i * 3 + 2] = p.z;
        }
        return data;
    }

    private static void SetIfPresent(Vector3[] joints, HumanPoseData.JointType type, Transform t)
    {
        if (t != null)
            joints[(int)type] = t.position;
    }

    /// <summary>
    /// foot bone ≈ ankle；toes bone ≈ 腳掌中段。
    /// heel = foot.position - footForward * HeelBackOffset - foot.up * HeelDownOffset
    /// foot_index = toes.position（若無 toes，用 foot.forward 推算）
    /// </summary>
    private void EstimateFootKeypoints(Transform foot, Transform toes,
        out Vector3 heel, out Vector3 footIndex)
    {
        if (foot == null)
        {
            heel = Vector3.zero;
            footIndex = Vector3.zero;
            return;
        }

        Vector3 footForward;
        if (toes != null)
        {
            Vector3 d = toes.position - foot.position;
            footForward = d.sqrMagnitude > 1e-8f ? d.normalized : foot.forward;
            footIndex = toes.position;
        }
        else
        {
            footForward = foot.forward;
            footIndex = foot.position + footForward * ToeForwardOffset;
        }

        heel = foot.position - footForward * HeelBackOffset - foot.up * HeelDownOffset;
    }

    /// <summary>
    /// MCP / 指根位置：優先取 proximal bone root；沒有 finger bone 時用 hand transform 估算。
    /// </summary>
    private void EstimateMcpKeypoint(Animator animator, Transform hand, HumanBodyBones proximalBone,
        bool isLeft, bool isIndex, out Vector3 mcp)
    {
        Transform proximal = animator.GetBoneTransform(proximalBone);
        if (proximal != null)
        {
            mcp = proximal.position;
            return;
        }

        if (hand == null)
        {
            mcp = Vector3.zero;
            return;
        }

        // 沒有 finger bones：以 hand bone 為基準推估 MCP / knuckle 位置。
        // Unity Humanoid 慣例：手部 X 軸沿手臂延伸（左手=-X 右手=+X 朝外）
        Vector3 fingerDir = isLeft ? -hand.right : hand.right;
        Vector3 sideways = hand.up; // pinky/index 在 hand 平面上的橫向偏移
        float side = isIndex ? HandMcpSideOffset : -HandMcpSideOffset;
        if (!isLeft) side = -side; // 右手對稱
        mcp = hand.position + fingerDir * HandMcpForwardOffset + sideways * side;
    }

    /// <summary>
    /// 從 head bone 估算 nose/eye/ear/mouth — Humanoid avatar 通常沒有這些骨骼。
    /// 偏移使用 head transform 的本地座標軸 (forward / up / right)。
    /// </summary>
    private void EstimateFaceKeypoints(Animator animator, Transform head, Vector3[] j)
    {
        if (head == null)
        {
            // 退而求其次：讓所有臉部點等於頸部頂端，至少不會 NaN
            Transform neck = animator.GetBoneTransform(HumanBodyBones.Neck);
            Vector3 fallback = neck != null ? neck.position : Vector3.zero;
            for (int i = (int)HumanPoseData.JointType.Nose; i <= (int)HumanPoseData.JointType.Mouth_Right; i++)
                j[i] = fallback;
            return;
        }

        Vector3 fwd = head.forward;
        Vector3 up = head.up;
        Vector3 right = head.right;

        // 嘗試使用真正的眼睛骨骼（如果 avatar 有設定）
        Transform leftEyeBone = animator.GetBoneTransform(HumanBodyBones.LeftEye);
        Transform rightEyeBone = animator.GetBoneTransform(HumanBodyBones.RightEye);
        Vector3 lEyeCenter = leftEyeBone != null
            ? leftEyeBone.position
            : head.position + fwd * EyeForward + up * EyeUp - right * EyeOuterSide * 0.5f;
        Vector3 rEyeCenter = rightEyeBone != null
            ? rightEyeBone.position
            : head.position + fwd * EyeForward + up * EyeUp + right * EyeOuterSide * 0.5f;

        // 鼻子（face center）
        j[(int)HumanPoseData.JointType.Nose] =
            head.position + fwd * HeadToNoseForward + up * HeadToNoseDown;

        // 眼睛系列：以 eye center 為錨，再對中央/外側做小偏移
        j[(int)HumanPoseData.JointType.L_Eye] = lEyeCenter;
        j[(int)HumanPoseData.JointType.R_Eye] = rEyeCenter;
        j[(int)HumanPoseData.JointType.L_Eye_Inner] = lEyeCenter + right * EyeInnerSide;
        j[(int)HumanPoseData.JointType.L_Eye_Outer] = lEyeCenter - right * EyeInnerSide;
        j[(int)HumanPoseData.JointType.R_Eye_Inner] = rEyeCenter - right * EyeInnerSide;
        j[(int)HumanPoseData.JointType.R_Eye_Outer] = rEyeCenter + right * EyeInnerSide;

        // 耳朵
        j[(int)HumanPoseData.JointType.L_Ear] = head.position - right * EarSide + up * EarUp;
        j[(int)HumanPoseData.JointType.R_Ear] = head.position + right * EarSide + up * EarUp;

        // 嘴巴
        Vector3 mouthCenter = head.position + fwd * MouthForward + up * MouthDown;
        j[(int)HumanPoseData.JointType.Mouth_Left] = mouthCenter - right * MouthSide;
        j[(int)HumanPoseData.JointType.Mouth_Right] = mouthCenter + right * MouthSide;
    }

    private string ResolveOutputPath()
    {
        if (Path.IsPathRooted(outputPath))
            return outputPath;
        // 相對路徑：從專案根（Assets 的上一層）解析
        return Path.GetFullPath(Path.Combine(Application.dataPath, "..", outputPath));
    }

    private void WriteCSV()
    {
        string fullPath = ResolveOutputPath();
        string dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        StringBuilder sb = new StringBuilder(_frames.Count * 1024);
        CultureInfo inv = CultureInfo.InvariantCulture;
        foreach (float[] frame in _frames)
        {
            for (int i = 0; i < frame.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(frame[i].ToString("R", inv));
            }
            sb.Append('\n');
        }
        File.WriteAllText(fullPath, sb.ToString());
    }
}
