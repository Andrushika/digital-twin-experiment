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
    private const float ToeForwardOffset = 0.12f;
    private const float HandFingerExtend = 0.08f;

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
        if (targetAnimator == null || clip == null)
        {
            Debug.LogError("[GroundTruthRecorder] targetAnimator and clip are required for BakeFromClip");
            return;
        }
        if (targetAnimator.avatar == null || !targetAnimator.avatar.isHuman)
        {
            Debug.LogError("[GroundTruthRecorder] targetAnimator must use a Humanoid Avatar");
            return;
        }

        _frames.Clear();
        float duration = clip.length;
        float dt = 1f / Mathf.Max(1f, fps);
        int total = Mathf.Max(1, Mathf.CeilToInt(duration / dt) + 1);

        for (int i = 0; i < total; i++)
        {
            float t = Mathf.Min(i * dt, duration);
            clip.SampleAnimation(targetAnimator.gameObject, t);
            _frames.Add(SampleCurrentPose());
        }

        WriteCSV();
        Debug.Log($"[GroundTruthRecorder] Baked {_frames.Count} frames @ {fps}fps " +
                  $"(clip length {duration:F3}s) → {ResolveOutputPath()}");
    }

    [ContextMenu("Begin Live Recording")]
    public void BeginLiveRecording()
    {
        if (targetAnimator == null || targetAnimator.avatar == null || !targetAnimator.avatar.isHuman)
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
    /// 從目前 avatar 的 bone.position 抽出 33 個 keypoints。
    /// 回傳 99 個 float (x,y,z * 33)，已套用 useMediaPipeConvention（如啟用）。
    /// </summary>
    private float[] SampleCurrentPose()
    {
        Vector3[] j = new Vector3[33];

        Transform head = targetAnimator.GetBoneTransform(HumanBodyBones.Head);
        Transform leftShoulder = targetAnimator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        Transform rightShoulder = targetAnimator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        Transform leftElbow = targetAnimator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        Transform rightElbow = targetAnimator.GetBoneTransform(HumanBodyBones.RightLowerArm);
        Transform leftHand = targetAnimator.GetBoneTransform(HumanBodyBones.LeftHand);
        Transform rightHand = targetAnimator.GetBoneTransform(HumanBodyBones.RightHand);
        Transform leftHip = targetAnimator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
        Transform rightHip = targetAnimator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
        Transform leftKnee = targetAnimator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
        Transform rightKnee = targetAnimator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
        Transform leftFoot = targetAnimator.GetBoneTransform(HumanBodyBones.LeftFoot);
        Transform rightFoot = targetAnimator.GetBoneTransform(HumanBodyBones.RightFoot);
        Transform leftToes = targetAnimator.GetBoneTransform(HumanBodyBones.LeftToes);
        Transform rightToes = targetAnimator.GetBoneTransform(HumanBodyBones.RightToes);

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

        // 手指：pinky / index 指尖（優先用 finger bone，不存在就用 hand 估算）
        EstimateFingerTip(leftHand, HumanBodyBones.LeftIndexDistal, HumanBodyBones.LeftIndexProximal,
            isLeft: true, isIndex: true, out Vector3 lIndex);
        EstimateFingerTip(leftHand, HumanBodyBones.LeftLittleDistal, HumanBodyBones.LeftLittleProximal,
            isLeft: true, isIndex: false, out Vector3 lPinky);
        EstimateFingerTip(rightHand, HumanBodyBones.RightIndexDistal, HumanBodyBones.RightIndexProximal,
            isLeft: false, isIndex: true, out Vector3 rIndex);
        EstimateFingerTip(rightHand, HumanBodyBones.RightLittleDistal, HumanBodyBones.RightLittleProximal,
            isLeft: false, isIndex: false, out Vector3 rPinky);
        j[(int)HumanPoseData.JointType.L_Index] = lIndex;
        j[(int)HumanPoseData.JointType.L_Pinky] = lPinky;
        j[(int)HumanPoseData.JointType.R_Index] = rIndex;
        j[(int)HumanPoseData.JointType.R_Pinky] = rPinky;

        // 臉部：head 沒有 nose/eye/ear/mouth 的 humanoid 對應，從 head transform 估算
        EstimateFaceKeypoints(head, j);

        // 套用座標慣例 + 攤平成 float[]
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
    /// heel = foot.position - footForward * HeelBackOffset
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

        heel = foot.position - footForward * HeelBackOffset;
    }

    /// <summary>
    /// 手指尖端：優先取 distal bone，再退到 proximal bone，
    /// 都沒有就用 hand transform + 預估方向。
    /// </summary>
    private void EstimateFingerTip(Transform hand, HumanBodyBones distalBone, HumanBodyBones proximalBone,
        bool isLeft, bool isIndex, out Vector3 tip)
    {
        Transform distal = targetAnimator.GetBoneTransform(distalBone);
        if (distal != null)
        {
            // distal bone 的位置是手指最遠關節 root，再沿手指長度推一小段到指尖
            tip = distal.position + distal.forward * 0.025f;
            return;
        }

        Transform proximal = targetAnimator.GetBoneTransform(proximalBone);
        if (proximal != null)
        {
            tip = proximal.position + proximal.forward * 0.06f;
            return;
        }

        if (hand == null)
        {
            tip = Vector3.zero;
            return;
        }

        // 沒有任何手指骨骼：以 hand bone 為基準推估
        // Unity Humanoid 慣例：手部 X 軸沿手臂延伸（左手=-X 右手=+X 朝外）
        Vector3 fingerDir = isLeft ? -hand.right : hand.right;
        Vector3 sideways = hand.up; // pinky/index 在 hand 平面上的橫向偏移
        float side = isIndex ? 0.025f : -0.025f;
        if (!isLeft) side = -side; // 右手對稱
        tip = hand.position + fingerDir * HandFingerExtend + sideways * side;
    }

    /// <summary>
    /// 從 head bone 估算 nose/eye/ear/mouth — Humanoid avatar 通常沒有這些骨骼。
    /// 偏移使用 head transform 的本地座標軸 (forward / up / right)。
    /// </summary>
    private void EstimateFaceKeypoints(Transform head, Vector3[] j)
    {
        if (head == null)
        {
            // 退而求其次：讓所有臉部點等於頸部頂端，至少不會 NaN
            Transform neck = targetAnimator.GetBoneTransform(HumanBodyBones.Neck);
            Vector3 fallback = neck != null ? neck.position : Vector3.zero;
            for (int i = (int)HumanPoseData.JointType.Nose; i <= (int)HumanPoseData.JointType.Mouth_Right; i++)
                j[i] = fallback;
            return;
        }

        Vector3 fwd = head.forward;
        Vector3 up = head.up;
        Vector3 right = head.right;

        // 嘗試使用真正的眼睛骨骼（如果 avatar 有設定）
        Transform leftEyeBone = targetAnimator.GetBoneTransform(HumanBodyBones.LeftEye);
        Transform rightEyeBone = targetAnimator.GetBoneTransform(HumanBodyBones.RightEye);
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
