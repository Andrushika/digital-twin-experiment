using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Twist 正確性驗證器 — 拿動畫本身的 bone rotation 當 ground truth，
/// 跟 TwistSolver 從 joint 位置算出來的值逐幀比對。
///
/// 核心想法：
///   一段動畫被 author 出來時，每個 bone.rotation 已經包含了正確的 twist。
///   我們可以把這個「真值」分解出來：
///     trueTwist(t) = decompose( bone.rotation(t)  vs  parent-inherited swing-only baseline )
///
///   pipeline 算出來的：
///     computedTwist(t) = TwistSolver.Compute(InterpretedPose from sampled joints)
///
///   兩者比對 → 如果差異很小 + 方向一致，演算法就沒問題；
///                差異 ±180° 跳變或符號反向 → 演算法或座標系 bug。
///
/// 用法：
///   1. 同 GameObject 上掛 GroundTruthRecorder（用它的 joint 採樣）
///   2. 設定 clip + fps
///   3. 右鍵組件「Run Twist Comparison」
///   4. Console 看摘要，CSV 看每幀詳細資料
/// </summary>
public class TwistDiagnostic : MonoBehaviour
{
    public enum CalibrationSource
    {
        CurrentScenePose,
        RestClip,
        MotionClipFirstFrame
    }

    [Header("Source")]
    [Tooltip("用同 GameObject 上的 GroundTruthRecorder 來採樣 joint 位置")]
    [SerializeField] private GroundTruthRecorder recorder;
    [Tooltip("要分析的動畫片段（一般跟 GroundTruthRecorder 用同一個）")]
    [SerializeField] private AnimationClip clip;
    [Tooltip("採樣頻率")]
    [SerializeField] private float fps = 30f;

    [Header("Calibration")]
    [Tooltip("CurrentScenePose = 使用執行診斷前 avatar 當下姿勢（例如 Play 前靜態 T-pose）")]
    [SerializeField] private CalibrationSource calibrationSource = CalibrationSource.CurrentScenePose;
    [Tooltip("CalibrationSource=RestClip 時指定 T/A-pose 等校正 clip。")]
    [SerializeField] private AnimationClip restClip;
    [Tooltip("校正 clip 的取樣時間（秒）。通常 T/A-pose clip 用 0。")]
    [SerializeField] private float restTime = 0f;
    [Tooltip("Run 完自動還原執行前的 avatar hierarchy pose，避免 Edit Mode SampleAnimation 污染場景。")]
    [SerializeField] private bool restorePoseAfterRun = true;

    [Header("Comparison")]
    [Tooltip("|true|<這個值就視為「沒 twist」，避免雜訊把 0.5° vs -0.3° 當成 sign 反向")]
    [SerializeField] private float signMatchDeadzone = 5f;
    [Tooltip("超過這個百分比的 sign 反向 → 報警")]
    [SerializeField] private float signMismatchAlertPct = 30f;

    [Header("Output")]
    [SerializeField] private string outputCsvPath = "twist_diag.csv";
    [SerializeField] private string outputDebugCsvPath = "twist_diag_debug.csv";

    private static readonly string[] ChannelNames =
    {
        "L_UpperArm", "L_LowerArm", "R_UpperArm", "R_LowerArm",
        "L_UpperLeg", "L_LowerLeg", "R_UpperLeg", "R_LowerLeg"
    };

    [ContextMenu("Run Twist Comparison")]
    public void Run()
    {
        if (!Validate(out Animator animator, out HumanBodyBones[] bones, out HumanBodyBones[] parents))
            return;

        TransformPose[] savedPose = restorePoseAfterRun
            ? CaptureHierarchyPose(animator.transform)
            : null;

        try
        {
            RunComparison(animator, bones, parents);
        }
        finally
        {
            if (savedPose != null)
                RestoreHierarchyPose(savedPose);
        }
    }

    [ContextMenu("Restore Humanoid Neutral Pose")]
    public void RestoreHumanoidNeutralPose()
    {
        if (!TryFindHumanoidAnimator(out Animator animator))
            return;

#if UNITY_EDITOR
        UnityEditor.Undo.RegisterFullObjectHierarchyUndo(animator.gameObject, "Restore Humanoid Neutral Pose");
#endif
        HumanPoseHandler handler = new HumanPoseHandler(animator.avatar, animator.transform);
        HumanPose pose = new HumanPose();
        handler.GetHumanPose(ref pose);

        if (pose.muscles == null || pose.muscles.Length != HumanTrait.MuscleCount)
            pose.muscles = new float[HumanTrait.MuscleCount];
        else
            System.Array.Clear(pose.muscles, 0, pose.muscles.Length);

        handler.SetHumanPose(ref pose);
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(animator.gameObject);
#endif
        Debug.Log("[TwistDiagnostic] Restored humanoid neutral pose by zeroing human muscles. If this is not the exact prefab T-pose, use Undo or prefab/scene revert.");
    }

    private void RunComparison(Animator animator, HumanBodyBones[] bones, HumanBodyBones[] parents)
    {
        PoseInterpreter interp = new PoseInterpreter();
        TwistSolver solver = new TwistSolver();

        // === Step 1: rest = scene T/A-pose, explicit rest clip/time, or clip 第一幀 ===
        string calibrationLabel = ApplyCalibrationPose(animator);

        Quaternion[] restRotW = new Quaternion[8];
        Quaternion[] restParentW = new Quaternion[8];
        Vector3[] restAxisW = new Vector3[8];
        for (int b = 0; b < 8; b++)
        {
            Transform bone = animator.GetBoneTransform(bones[b]);
            Transform parent = animator.GetBoneTransform(parents[b]);
            Transform child = GetChildBoneForAxis(animator, bones[b]);
            if (bone == null || parent == null || child == null)
            {
                Debug.LogError($"[TwistDiagnostic] Avatar 缺少 bone：{bones[b]} / 父 {parents[b]} / 子 (用於算軸)");
                return;
            }
            restRotW[b] = bone.rotation;
            restParentW[b] = parent.rotation;
            restAxisW[b] = (child.position - bone.position).normalized;
        }

        HumanPoseData restPose = SamplePoseFromJoints(recorder.SampleJointPositionsWorld());
        var interpRest = interp.Interpret(restPose);
        solver.Calibrate(interpRest);

        // === Step 2: iterate ===
        float duration = clip.length;
        float dt = 1f / Mathf.Max(1f, fps);
        int total = Mathf.Max(2, Mathf.CeilToInt(duration / dt) + 1);

        ChannelStats[] stats = new ChannelStats[8];
        for (int i = 0; i < 8; i++) stats[i] = new ChannelStats();

        StringBuilder csv = new StringBuilder();
        csv.AppendLine("frame,time,bone,true_deg,computed_deg,diff_deg,sign_match");
        StringBuilder debugCsv = new StringBuilder();
        debugCsv.AppendLine("frame,time,bone,true_deg,computed_deg,diff_deg,raw_deg,pre_clamp_deg,clamped_deg,filtered_deg,expected_proj_mag,current_proj_mag,ref_axis_angle_deg,degenerate,held_previous_raw,ref_to_bone_forward_deg,ref_to_bone_up_deg,ref_to_bone_right_deg,axis_x,axis_y,axis_z,ref_x,ref_y,ref_z");
        CultureInfo inv = CultureInfo.InvariantCulture;

        for (int i = 0; i < total; i++)
        {
            float t = Mathf.Min(i * dt, duration);
            clip.SampleAnimation(animator.gameObject, t);

            // 採樣當前 joint 位置 → InterpretedPose → TwistSolver
            HumanPoseData pose = SamplePoseFromJoints(recorder.SampleJointPositionsWorld());
            var interpPose = interp.Interpret(pose);
            var computed = solver.Compute(interpPose, dt);
            float[] compArr = ToArray(computed);
            TwistSolver.ChannelDebug[] debugArr = ToArray(solver.LastDebug);

            // 算每根 bone 的 true twist
            for (int b = 0; b < 8; b++)
            {
                Transform bone = animator.GetBoneTransform(bones[b]);
                Transform parent = animator.GetBoneTransform(parents[b]);
                Transform child = GetChildBoneForAxis(animator, bones[b]);
                Vector3 currentAxisW = (child.position - bone.position).normalized;

                float trueDeg = ComputeTrueTwist(
                    bone.rotation, restRotW[b],
                    parent.rotation, restParentW[b],
                    restAxisW[b], currentAxisW);

                float compDeg = compArr[b];
                float diff = Mathf.DeltaAngle(trueDeg, compDeg);
                bool signMatch = SignsAgree(trueDeg, compDeg, signMatchDeadzone);

                csv.AppendLine(string.Format(inv,
                    "{0},{1:F4},{2},{3:F2},{4:F2},{5:F2},{6}",
                    i, t, ChannelNames[b], trueDeg, compDeg, diff, signMatch ? "Y" : "N"));

                TwistSolver.ChannelDebug dbg = debugArr[b];
                Vector3 refVector = GetReferenceVector(interpPose, b);
                Vector3 axisVector = GetSourceAxis(interpPose, b);
                Transform referenceBone = GetReferenceBone(animator, bones[b]);
                float refToForward = referenceBone != null ? AngleOrZero(refVector, referenceBone.forward) : 0f;
                float refToUp = referenceBone != null ? AngleOrZero(refVector, referenceBone.up) : 0f;
                float refToRight = referenceBone != null ? AngleOrZero(refVector, referenceBone.right) : 0f;

                debugCsv.AppendLine(string.Format(inv,
                    "{0},{1:F4},{2},{3:F2},{4:F2},{5:F2},{6:F2},{7:F2},{8:F2},{9:F2},{10:F4},{11:F4},{12:F2},{13},{14},{15:F2},{16:F2},{17:F2},{18:F5},{19:F5},{20:F5},{21:F5},{22:F5},{23:F5}",
                    i, t, ChannelNames[b],
                    trueDeg, compDeg, diff,
                    dbg.RawDeg, dbg.PreClampDeg, dbg.ClampedDeg, dbg.FilteredDeg,
                    dbg.ExpectedProjectionMagnitude, dbg.CurrentProjectionMagnitude,
                    dbg.ReferenceAxisAngleDeg,
                    dbg.Degenerate ? "Y" : "N",
                    dbg.HeldPreviousRaw ? "Y" : "N",
                    refToForward, refToUp, refToRight,
                    axisVector.x, axisVector.y, axisVector.z,
                    refVector.x, refVector.y, refVector.z));

                stats[b].Add(trueDeg, compDeg, signMatch);
            }
        }

        // === Step 3: 寫 CSV + Console summary ===
        string fullPath = ResolveOutputPath();
        string debugFullPath = ResolveOutputPath(outputDebugCsvPath);
        string dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        string debugDir = Path.GetDirectoryName(debugFullPath);
        if (!string.IsNullOrEmpty(debugDir) && !Directory.Exists(debugDir))
            Directory.CreateDirectory(debugDir);
        File.WriteAllText(fullPath, csv.ToString());
        File.WriteAllText(debugFullPath, debugCsv.ToString());

        StringBuilder s = new StringBuilder();
        s.AppendLine($"[TwistDiagnostic] {total} frames × 8 bones, clip='{clip.name}'");
        s.AppendLine($"                  rest={calibrationLabel}");
        s.AppendLine("                  true=從動畫 bone rotation 分解出的真值");
        s.AppendLine("                  computed=TwistSolver 從 joint 位置算的值");
        s.AppendLine("    bone          true_range            computed_range        signMismatch  meanAbsDiff  maxAbsDiff");
        s.AppendLine("    -----------   -------------------   -------------------   ------------  -----------  ----------");
        for (int b = 0; b < 8; b++)
        {
            ChannelStats st = stats[b];
            float pct = 100f * st.SignMismatches / Mathf.Max(1, st.Count);
            string flag = pct > signMismatchAlertPct ? "  ⚠ HIGH" : "";
            s.AppendLine(string.Format(
                "    {0,-12}  [{1,7:F1}, {2,6:F1}]   [{3,7:F1}, {4,6:F1}]   {5,4}/{6,-4} {7,5:F1}%  {8,7:F1}°    {9,7:F1}°{10}",
                ChannelNames[b],
                st.TrueMin, st.TrueMax, st.CompMin, st.CompMax,
                st.SignMismatches, st.Count, pct,
                st.MeanAbsDiff(), st.MaxAbsDiff, flag));
        }
        s.AppendLine($"CSV: {fullPath}");
        s.AppendLine($"Debug CSV: {debugFullPath}");
        Debug.Log(s.ToString());
    }

    private static TransformPose[] CaptureHierarchyPose(Transform root)
    {
        List<TransformPose> poses = new List<TransformPose>();
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            poses.Add(new TransformPose
            {
                Transform = t,
                LocalPosition = t.localPosition,
                LocalRotation = t.localRotation,
                LocalScale = t.localScale
            });
        }
        return poses.ToArray();
    }

    private static void RestoreHierarchyPose(TransformPose[] poses)
    {
        foreach (TransformPose p in poses)
        {
            if (p.Transform == null) continue;
            p.Transform.localPosition = p.LocalPosition;
            p.Transform.localRotation = p.LocalRotation;
            p.Transform.localScale = p.LocalScale;
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(p.Transform);
#endif
        }
    }

    private bool Validate(out Animator animator, out HumanBodyBones[] bones, out HumanBodyBones[] parents)
    {
        bones = new[]
        {
            HumanBodyBones.LeftUpperArm,  HumanBodyBones.LeftLowerArm,
            HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm,
            HumanBodyBones.LeftUpperLeg,  HumanBodyBones.LeftLowerLeg,
            HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg,
        };
        parents = new[]
        {
            HumanBodyBones.Chest, HumanBodyBones.LeftUpperArm,
            HumanBodyBones.Chest, HumanBodyBones.RightUpperArm,
            HumanBodyBones.Hips,  HumanBodyBones.LeftUpperLeg,
            HumanBodyBones.Hips,  HumanBodyBones.RightUpperLeg,
        };
        animator = null;

        if (recorder == null) recorder = GetComponent<GroundTruthRecorder>();
        if (recorder == null)
        {
            Debug.LogError("[TwistDiagnostic] 需要同 GameObject 上有 GroundTruthRecorder");
            return false;
        }
        if (clip == null)
        {
            Debug.LogError("[TwistDiagnostic] 需要 AnimationClip");
            return false;
        }

        if (!TryFindHumanoidAnimator(out animator))
        {
            return false;
        }
        return true;
    }

    private bool TryFindHumanoidAnimator(out Animator animator)
    {
        animator = null;
        if (recorder != null)
            recorder.TryResolveTargetAnimator(out animator);
        if (animator == null) animator = GetComponent<Animator>();
        if (animator == null) animator = GetComponentInParent<Animator>();
        if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
        {
            Debug.LogError("[TwistDiagnostic] 找不到 Humanoid Animator。請先在 GroundTruthRecorder 指定 targetAnimator，確保診斷 sample clip 與 keypoints 來自同一隻角色。");
            return false;
        }
        return true;
    }

    private string ApplyCalibrationPose(Animator animator)
    {
        switch (calibrationSource)
        {
            case CalibrationSource.RestClip:
            {
                AnimationClip calibrationClip = restClip != null ? restClip : clip;
                float calibrationTime = Mathf.Clamp(restTime, 0f, calibrationClip.length);
                calibrationClip.SampleAnimation(animator.gameObject, calibrationTime);
                return $"RestClip '{calibrationClip.name}' @ {calibrationTime:F3}s";
            }
            case CalibrationSource.MotionClipFirstFrame:
                clip.SampleAnimation(animator.gameObject, 0f);
                return $"MotionClipFirstFrame '{clip.name}' @ 0.000s";
            case CalibrationSource.CurrentScenePose:
            default:
                return "CurrentScenePose";
        }
    }

    /// <summary>
    /// 從 33 點 Vector3[] 包成 HumanPoseData。
    /// </summary>
    private static HumanPoseData SamplePoseFromJoints(Vector3[] joints)
    {
        HumanPoseData pose = new HumanPoseData();
        for (int i = 0; i < 33 && i < joints.Length; i++)
            pose.SetJoint((HumanPoseData.JointType)i, joints[i]);
        return pose;
    }

    /// <summary>
    /// 用「parent-inherited swing-only」當 baseline，把 bone 的當前世界旋轉拆解出 twist。
    /// 與 RetargetSolver 內 SetLimbSwingInherit 同框架，所以 trueTwist 就是「retarget 該套用的 twist」。
    /// </summary>
    private static float ComputeTrueTwist(
        Quaternion currentRot, Quaternion restRot,
        Quaternion currentParentRot, Quaternion restParentRot,
        Vector3 restAxisW, Vector3 currentAxisW)
    {
        Quaternion parentDelta = currentParentRot * Quaternion.Inverse(restParentRot);
        Vector3 inheritedDir = parentDelta * restAxisW;
        Quaternion swing = Quaternion.FromToRotation(inheritedDir, currentAxisW);
        Quaternion noTwistRot = swing * parentDelta * restRot;

        Quaternion twistDelta = currentRot * Quaternion.Inverse(noTwistRot);

        // 把 twistDelta 沿 currentAxisW 拆解，取 twist 部分的角度（signed）
        Vector3 axis = currentAxisW.normalized;
        SwingTwistMath.DecomposeSwingTwist(twistDelta, axis, out _, out Quaternion twistOnly);

        Vector3 qVec = new Vector3(twistOnly.x, twistOnly.y, twistOnly.z);
        float sinHalfSigned = Vector3.Dot(qVec, axis);
        float angle = 2f * Mathf.Atan2(sinHalfSigned, twistOnly.w) * Mathf.Rad2Deg;

        // wrap 到 [-180, 180]
        while (angle > 180f) angle -= 360f;
        while (angle < -180f) angle += 360f;
        return angle;
    }

    /// <summary>
    /// 取得 bone 對應的「子骨骼」用來算當前軸方向。
    /// </summary>
    private static Transform GetChildBoneForAxis(Animator a, HumanBodyBones bone)
    {
        switch (bone)
        {
            case HumanBodyBones.LeftUpperArm:  return a.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            case HumanBodyBones.LeftLowerArm:  return a.GetBoneTransform(HumanBodyBones.LeftHand);
            case HumanBodyBones.RightUpperArm: return a.GetBoneTransform(HumanBodyBones.RightLowerArm);
            case HumanBodyBones.RightLowerArm: return a.GetBoneTransform(HumanBodyBones.RightHand);
            case HumanBodyBones.LeftUpperLeg:  return a.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            case HumanBodyBones.LeftLowerLeg:  return a.GetBoneTransform(HumanBodyBones.LeftFoot);
            case HumanBodyBones.RightUpperLeg: return a.GetBoneTransform(HumanBodyBones.RightLowerLeg);
            case HumanBodyBones.RightLowerLeg: return a.GetBoneTransform(HumanBodyBones.RightFoot);
            default: return null;
        }
    }

    private static float[] ToArray(TwistSolver.LimbTwists t)
    {
        return new[]
        {
            t.L_UpperArm, t.L_LowerArm, t.R_UpperArm, t.R_LowerArm,
            t.L_UpperLeg, t.L_LowerLeg, t.R_UpperLeg, t.R_LowerLeg,
        };
    }

    private static TwistSolver.ChannelDebug[] ToArray(TwistSolver.LimbTwistDebug t)
    {
        return new[]
        {
            t.L_UpperArm, t.L_LowerArm, t.R_UpperArm, t.R_LowerArm,
            t.L_UpperLeg, t.L_LowerLeg, t.R_UpperLeg, t.R_LowerLeg,
        };
    }

    private static Vector3 GetReferenceVector(PoseInterpreter.InterpretedPose pose, int channel)
    {
        switch (channel)
        {
            case 0:
            case 1:
                return pose.L_PalmNormal;
            case 2:
            case 3:
                return pose.R_PalmNormal;
            case 4:
            case 5:
                return pose.L_FootPlaneNormal;
            case 6:
            case 7:
                return pose.R_FootPlaneNormal;
            default:
                return Vector3.up;
        }
    }

    private static Vector3 GetSourceAxis(PoseInterpreter.InterpretedPose pose, int channel)
    {
        switch (channel)
        {
            case 0: return pose.L_UpperArm.Forward;
            case 1: return pose.L_LowerArm.Forward;
            case 2: return pose.R_UpperArm.Forward;
            case 3: return pose.R_LowerArm.Forward;
            case 4: return pose.L_UpperLeg.Forward;
            case 5: return pose.L_LowerLeg.Forward;
            case 6: return pose.R_UpperLeg.Forward;
            case 7: return pose.R_LowerLeg.Forward;
            default: return Vector3.forward;
        }
    }

    private static Transform GetReferenceBone(Animator a, HumanBodyBones bone)
    {
        switch (bone)
        {
            case HumanBodyBones.LeftUpperArm:
            case HumanBodyBones.LeftLowerArm:
                return a.GetBoneTransform(HumanBodyBones.LeftHand);
            case HumanBodyBones.RightUpperArm:
            case HumanBodyBones.RightLowerArm:
                return a.GetBoneTransform(HumanBodyBones.RightHand);
            case HumanBodyBones.LeftUpperLeg:
            case HumanBodyBones.LeftLowerLeg:
                return a.GetBoneTransform(HumanBodyBones.LeftFoot);
            case HumanBodyBones.RightUpperLeg:
            case HumanBodyBones.RightLowerLeg:
                return a.GetBoneTransform(HumanBodyBones.RightFoot);
            default:
                return null;
        }
    }

    private static float AngleOrZero(Vector3 a, Vector3 b)
    {
        if (a.sqrMagnitude < 1e-10f || b.sqrMagnitude < 1e-10f)
            return 0f;
        return Vector3.Angle(a, b);
    }

    private static bool SignsAgree(float a, float b, float deadzone)
    {
        // 兩個都接近 0 → 視為一致（沒有 twist 就沒有 sign 可比）
        if (Mathf.Abs(a) < deadzone && Mathf.Abs(b) < deadzone) return true;
        // 一個接近 0、另一個有大值 → 不算 sign 反向（算量值差異）
        if (Mathf.Abs(a) < deadzone || Mathf.Abs(b) < deadzone) return true;
        return Mathf.Sign(a) == Mathf.Sign(b);
    }

    private string ResolveOutputPath()
    {
        return ResolveOutputPath(outputCsvPath);
    }

    private string ResolveOutputPath(string path)
    {
        if (Path.IsPathRooted(path)) return path;
        return Path.GetFullPath(Path.Combine(Application.dataPath, "..", path));
    }

    private class ChannelStats
    {
        public int Count;
        public int SignMismatches;
        public float SumAbsDiff;
        public float MaxAbsDiff;
        public float TrueMin = float.PositiveInfinity, TrueMax = float.NegativeInfinity;
        public float CompMin = float.PositiveInfinity, CompMax = float.NegativeInfinity;

        public void Add(float trueDeg, float compDeg, bool signMatch)
        {
            Count++;
            if (!signMatch) SignMismatches++;
            float d = Mathf.Abs(Mathf.DeltaAngle(trueDeg, compDeg));
            SumAbsDiff += d;
            if (d > MaxAbsDiff) MaxAbsDiff = d;
            if (trueDeg < TrueMin) TrueMin = trueDeg;
            if (trueDeg > TrueMax) TrueMax = trueDeg;
            if (compDeg < CompMin) CompMin = compDeg;
            if (compDeg > CompMax) CompMax = compDeg;
        }

        public float MeanAbsDiff() => Count == 0 ? 0f : SumAbsDiff / Count;
    }

    private struct TransformPose
    {
        public Transform Transform;
        public Vector3 LocalPosition;
        public Quaternion LocalRotation;
        public Vector3 LocalScale;
    }
}
