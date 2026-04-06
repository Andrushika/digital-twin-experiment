using UnityEngine;

/// <summary>
/// MODULE C: Retarget Solver
///
/// 責任：
/// 1. 接收 Module B 的人體結構化姿態 (InterpretedPose)
/// 2. 計算 Avatar 的每根骨骼應該旋轉多少
/// 3. 套用旋轉到 Unity Animator bones
///
/// 核心方法：
/// ─────────────────────────────────
/// 軀幹：World-Space Delta
///   delta   = srcCurrentWorld * Inverse(srcRestWorld)
///   targetW = delta * avatarRestWorld
///
/// 四肢：Absolute Direction Mapping + Stabilized Twist
///   1. Swing = FromToRotation(avRestBoneDir, srcDir) → 方向對齊
///   2. Twist = 時序穩定的繞骨軸扭轉（帶連續性修正 + 低通濾波）
///   3. targetW = twist * swing * avRestW
/// </summary>
public class RetargetSolver : MonoBehaviour
{
    [SerializeField] private Animator targetAvatar;

    [SerializeField] private float scaleFactor = 1f;
    [SerializeField] private bool autoScaleFromTorso = true;
    [SerializeField] private Vector2 autoScaleClamp = new Vector2(0.5f, 3.0f);

    [SerializeField] private Vector3 axisScale = Vector3.one;
    [SerializeField] private bool flipX = false;
    [SerializeField] private bool flipY = false;
    [SerializeField] private bool flipZ = false;

    [Header("Twist Control")]
    [Tooltip("是否套用 twist（骨骼自體旋轉）。MediaPipe 無 twist ground truth，建議關閉。")]
    [SerializeField] private bool applyTwist = false;

    // ===== Bone cache =====
    private Transform hipBone;
    private Transform spineBone;
    private Transform chestBone;
    private Transform neckBone;
    private Transform headBone;

    private Transform l_upperArmBone;
    private Transform l_lowerArmBone;
    private Transform l_handBone;
    private Transform r_upperArmBone;
    private Transform r_lowerArmBone;
    private Transform r_handBone;

    private Transform l_upperLegBone;
    private Transform l_lowerLegBone;
    private Transform l_footBone;
    private Transform r_upperLegBone;
    private Transform r_lowerLegBone;
    private Transform r_footBone;

    // ===== Calibration state =====
    private bool calibrated;
    private float autoComputedScale = 1f;

    // Source rest world rotations
    private Quaternion srcRestPelvisW, srcRestSpineW, srcRestChestW, srcRestNeckW, srcRestHeadW;

    // Source rest limb rotations
    private Quaternion srcRestLUpperArmW, srcRestLLowerArmW;
    private Quaternion srcRestRUpperArmW, srcRestRLowerArmW;
    private Quaternion srcRestLUpperLegW, srcRestLLowerLegW;
    private Quaternion srcRestRUpperLegW, srcRestRLowerLegW;

    // Avatar rest world rotations
    private Quaternion avRestHipW, avRestSpineW, avRestChestW, avRestNeckW, avRestHeadW;
    private Quaternion avRestLUpperArmW, avRestLLowerArmW;
    private Quaternion avRestRUpperArmW, avRestRLowerArmW;
    private Quaternion avRestLUpperLegW, avRestLLowerLegW;
    private Quaternion avRestRUpperLegW, avRestRLowerLegW;

    // Avatar rest bone physical directions
    private Vector3 avRestDirLUpperArm, avRestDirLLowerArm;
    private Vector3 avRestDirRUpperArm, avRestDirRLowerArm;
    private Vector3 avRestDirLUpperLeg, avRestDirLLowerLeg;
    private Vector3 avRestDirRUpperLeg, avRestDirRLowerLeg;

    // Position calibration
    private Vector3 srcRestPelvisPos;
    private Vector3 avRestHipPos;

    // ===== Pending pose =====
    private PoseInterpreter.InterpretedPose pendingPose;
    private bool pendingIncludeLimbs;

    private void Start()
    {
        if (targetAvatar == null)
            targetAvatar = GetComponent<Animator>();

        CacheBones();
    }

    private void CacheBones()
    {
        if (targetAvatar == null)
        {
            Debug.LogError("[RetargetSolver] No animator found!");
            return;
        }

        hipBone   = targetAvatar.GetBoneTransform(HumanBodyBones.Hips);
        spineBone = targetAvatar.GetBoneTransform(HumanBodyBones.Spine);
        chestBone = targetAvatar.GetBoneTransform(HumanBodyBones.Chest);
        neckBone  = targetAvatar.GetBoneTransform(HumanBodyBones.Neck);
        headBone  = targetAvatar.GetBoneTransform(HumanBodyBones.Head);

        l_upperArmBone = targetAvatar.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        l_lowerArmBone = targetAvatar.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        l_handBone     = targetAvatar.GetBoneTransform(HumanBodyBones.LeftHand);
        r_upperArmBone = targetAvatar.GetBoneTransform(HumanBodyBones.RightUpperArm);
        r_lowerArmBone = targetAvatar.GetBoneTransform(HumanBodyBones.RightLowerArm);
        r_handBone     = targetAvatar.GetBoneTransform(HumanBodyBones.RightHand);

        l_upperLegBone = targetAvatar.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
        l_lowerLegBone = targetAvatar.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
        l_footBone     = targetAvatar.GetBoneTransform(HumanBodyBones.LeftFoot);
        r_upperLegBone = targetAvatar.GetBoneTransform(HumanBodyBones.RightUpperLeg);
        r_lowerLegBone = targetAvatar.GetBoneTransform(HumanBodyBones.RightLowerLeg);
        r_footBone     = targetAvatar.GetBoneTransform(HumanBodyBones.RightFoot);

        Debug.Log("[RetargetSolver] Bones cached");
    }

    // ===== 外部 API =====

    public void ApplyPose(PoseInterpreter.InterpretedPose pose)
    {
        pendingPose = pose;
        pendingIncludeLimbs = true;
    }

    public void ApplyPoseTorsoOnly(PoseInterpreter.InterpretedPose pose)
    {
        pendingPose = pose;
        pendingIncludeLimbs = false;
    }

    private void LateUpdate()
    {
        if (pendingPose == null || hipBone == null)
            return;

        if (!calibrated)
            Calibrate(pendingPose);

        ApplyPoseInternal(pendingPose, pendingIncludeLimbs);
    }

    // ===== 校正 =====

    private void Calibrate(PoseInterpreter.InterpretedPose pose)
    {
        if (hipBone == null) return;

        // Source rest rotations
        srcRestPelvisW = pose.Pelvis.Rotation;
        srcRestChestW  = pose.Chest.Rotation;
        srcRestSpineW  = Quaternion.Slerp(srcRestPelvisW, srcRestChestW, 0.5f);
        srcRestHeadW   = pose.Head.Rotation;
        srcRestNeckW   = Quaternion.Slerp(srcRestChestW, srcRestHeadW, 0.5f);

        srcRestLUpperArmW = pose.L_UpperArm.Rotation;
        srcRestLLowerArmW = pose.L_LowerArm.Rotation;
        srcRestRUpperArmW = pose.R_UpperArm.Rotation;
        srcRestRLowerArmW = pose.R_LowerArm.Rotation;
        srcRestLUpperLegW = pose.L_UpperLeg.Rotation;
        srcRestLLowerLegW = pose.L_LowerLeg.Rotation;
        srcRestRUpperLegW = pose.R_UpperLeg.Rotation;
        srcRestRLowerLegW = pose.R_LowerLeg.Rotation;

        // Avatar rest rotations
        avRestHipW   = hipBone.rotation;
        avRestSpineW = spineBone != null ? spineBone.rotation : avRestHipW;
        avRestChestW = chestBone != null ? chestBone.rotation : avRestSpineW;
        avRestNeckW  = neckBone  != null ? neckBone.rotation  : avRestChestW;
        avRestHeadW  = headBone  != null ? headBone.rotation  : avRestNeckW;

        avRestLUpperArmW = l_upperArmBone != null ? l_upperArmBone.rotation : avRestChestW;
        avRestLLowerArmW = l_lowerArmBone != null ? l_lowerArmBone.rotation : avRestLUpperArmW;
        avRestRUpperArmW = r_upperArmBone != null ? r_upperArmBone.rotation : avRestChestW;
        avRestRLowerArmW = r_lowerArmBone != null ? r_lowerArmBone.rotation : avRestRUpperArmW;

        avRestLUpperLegW = l_upperLegBone != null ? l_upperLegBone.rotation : avRestHipW;
        avRestLLowerLegW = l_lowerLegBone != null ? l_lowerLegBone.rotation : avRestLUpperLegW;
        avRestRUpperLegW = r_upperLegBone != null ? r_upperLegBone.rotation : avRestHipW;
        avRestRLowerLegW = r_lowerLegBone != null ? r_lowerLegBone.rotation : avRestRUpperLegW;

        // Avatar rest bone directions
        avRestDirLUpperArm = SafeBoneDir(l_upperArmBone, l_lowerArmBone);
        avRestDirLLowerArm = SafeBoneDir(l_lowerArmBone, l_handBone);
        avRestDirRUpperArm = SafeBoneDir(r_upperArmBone, r_lowerArmBone);
        avRestDirRLowerArm = SafeBoneDir(r_lowerArmBone, r_handBone);
        avRestDirLUpperLeg = SafeBoneDir(l_upperLegBone, l_lowerLegBone);
        avRestDirLLowerLeg = SafeBoneDir(l_lowerLegBone, l_footBone);
        avRestDirRUpperLeg = SafeBoneDir(r_upperLegBone, r_lowerLegBone);
        avRestDirRLowerLeg = SafeBoneDir(r_lowerLegBone, r_footBone);

        // Position
        srcRestPelvisPos = MapSourcePosition(pose.Pelvis.Position);
        avRestHipPos     = hipBone.position;

        // Auto scale
        if (autoScaleFromTorso && chestBone != null)
        {
            float srcLen = Vector3.Distance(srcRestPelvisPos, MapSourcePosition(pose.Chest.Position));
            float avLen  = Vector3.Distance(hipBone.position, chestBone.position);
            autoComputedScale = srcLen > 1e-4f
                ? Mathf.Clamp(avLen / srcLen, autoScaleClamp.x, autoScaleClamp.y)
                : 1f;
        }
        else
        {
            autoComputedScale = 1f;
        }

        calibrated = true;
        Debug.Log($"[RetargetSolver] Calibrated (scale={scaleFactor * autoComputedScale:F3})");
    }

    // ===== 套用姿態 =====

    private void ApplyPoseInternal(PoseInterpreter.InterpretedPose pose, bool includeLimbs)
    {
        float finalScale = scaleFactor * (autoScaleFromTorso ? autoComputedScale : 1f);

        // Position
        Vector3 currentSrcPos = MapSourcePosition(pose.Pelvis.Position);
        hipBone.position = avRestHipPos + (currentSrcPos - srcRestPelvisPos) * finalScale;

        // Torso
        Quaternion srcPelvisW = pose.Pelvis.Rotation;
        Quaternion srcChestW  = pose.Chest.Rotation;
        Quaternion srcSpineW  = Quaternion.Slerp(srcPelvisW, srcChestW, 0.5f);
        Quaternion srcHeadW   = pose.Head.Rotation;
        Quaternion srcNeckW   = Quaternion.Slerp(srcChestW, srcHeadW, 0.5f);

        SetBoneWorldRotation(hipBone, srcPelvisW, srcRestPelvisW, avRestHipW);
        SetBoneLocalFromWorldDelta(spineBone, srcSpineW,  srcRestSpineW,  avRestSpineW);
        SetBoneLocalFromWorldDelta(chestBone, srcChestW,  srcRestChestW,  avRestChestW);
        SetBoneLocalFromWorldDelta(neckBone,  srcNeckW,   srcRestNeckW,   avRestNeckW);
        SetBoneLocalFromWorldDelta(headBone,  srcHeadW,   srcRestHeadW,   avRestHeadW);

        if (!includeLimbs) return;

        // Limbs — Absolute Direction Mapping
        SetLimbDirect(l_upperArmBone, pose.L_UpperArm, srcRestLUpperArmW,
                      avRestDirLUpperArm, avRestLUpperArmW);
        SetLimbDirect(l_lowerArmBone, pose.L_LowerArm, srcRestLLowerArmW,
                      avRestDirLLowerArm, avRestLLowerArmW);

        SetLimbDirect(r_upperArmBone, pose.R_UpperArm, srcRestRUpperArmW,
                      avRestDirRUpperArm, avRestRUpperArmW);
        SetLimbDirect(r_lowerArmBone, pose.R_LowerArm, srcRestRLowerArmW,
                      avRestDirRLowerArm, avRestRLowerArmW);

        SetLimbDirect(l_upperLegBone, pose.L_UpperLeg, srcRestLUpperLegW,
                      avRestDirLUpperLeg, avRestLUpperLegW);
        SetLimbDirect(l_lowerLegBone, pose.L_LowerLeg, srcRestLLowerLegW,
                      avRestDirLLowerLeg, avRestLLowerLegW);

        SetLimbDirect(r_upperLegBone, pose.R_UpperLeg, srcRestRUpperLegW,
                      avRestDirRUpperLeg, avRestRUpperLegW);
        SetLimbDirect(r_lowerLegBone, pose.R_LowerLeg, srcRestRLowerLegW,
                      avRestDirRLowerLeg, avRestRLowerLegW);
    }

    // ===== Torso =====

    private void SetBoneWorldRotation(Transform bone, Quaternion srcCurrentW,
                                       Quaternion srcRestW, Quaternion avRestW)
    {
        if (bone == null) return;
        Quaternion delta = srcCurrentW * Quaternion.Inverse(srcRestW);
        bone.rotation = delta * avRestW;
    }

    private void SetBoneLocalFromWorldDelta(Transform bone, Quaternion srcCurrentW,
                                             Quaternion srcRestW, Quaternion avRestW)
    {
        if (bone == null) return;
        Quaternion delta = srcCurrentW * Quaternion.Inverse(srcRestW);
        Quaternion targetWorld = delta * avRestW;
        bone.localRotation = Quaternion.Inverse(bone.parent.rotation) * targetWorld;
    }

    // ===== Limbs: Absolute Direction Mapping =====

    /// <summary>
    /// 絕對方向映射：
    ///
    /// 1. Swing = FromToRotation(avRestBoneDir, srcDir) → 骨骼方向對齊
    /// 2. Twist = 預設關閉（applyTwist=false），因為 MediaPipe 只提供關節位置，
    ///    不提供骨骼自體旋轉。任何 twist 推測都是幾何噪音，會導致麻花捲扭轉。
    ///    如果有其他 twist 來源（如 IMU），可開啟 applyTwist。
    /// </summary>
    private void SetLimbDirect(Transform bone,
                               PoseInterpreter.BodyPartFrame srcFrame, Quaternion srcRestW,
                               Vector3 avRestBoneDir, Quaternion avRestBoneW)
    {
        if (bone == null) return;

        // 1. Swing：把 Avatar rest 骨骼方向對齊到來源方向
        Vector3 srcDir = srcFrame.Forward;
        Quaternion swing = Quaternion.FromToRotation(avRestBoneDir, srcDir);
        Quaternion swungW = swing * avRestBoneW;

        if (applyTwist)
        {
            // 可選：從來源 Quaternion 提取 twist（需要可靠的 twist 來源）
            Quaternion srcCurrentW = srcFrame.Rotation;
            Vector3 srcRestUp = srcRestW * Vector3.up;
            Vector3 srcCurrentUp = srcCurrentW * Vector3.up;

            Vector3 projRestUp = Vector3.ProjectOnPlane(srcRestUp, srcDir);
            Vector3 projCurrentUp = Vector3.ProjectOnPlane(srcCurrentUp, srcDir);

            if (projRestUp.sqrMagnitude > 0.05f && projCurrentUp.sqrMagnitude > 0.05f)
            {
                float twistAngle = Vector3.SignedAngle(projRestUp.normalized, projCurrentUp.normalized, srcDir);
                Quaternion twist = Quaternion.AngleAxis(twistAngle, srcDir);
                swungW = twist * swungW;
            }
        }

        bone.localRotation = Quaternion.Inverse(bone.parent.rotation) * swungW;
    }

    // ===== Helpers =====

    private Vector3 SafeBoneDir(Transform from, Transform to)
    {
        if (from == null || to == null) return Vector3.forward;
        Vector3 dir = to.position - from.position;
        return dir.sqrMagnitude > 1e-8f ? dir.normalized : Vector3.forward;
    }

    private Vector3 MapSourcePosition(Vector3 source)
    {
        float x = (flipX ? -source.x : source.x) * axisScale.x;
        float y = (flipY ? -source.y : source.y) * axisScale.y;
        float z = (flipZ ? -source.z : source.z) * axisScale.z;
        return new Vector3(x, y, z);
    }

    public void SetTargetAvatar(Animator animator)
    {
        targetAvatar = animator;
        calibrated = false;
        autoComputedScale = 1f;
        pendingPose = null;
        CacheBones();
    }

    /// <summary>
    /// 供 Editor 驗證腳本在非 Play 模式下直接套用一幀姿態。
    /// </summary>
    public void ApplyPoseImmediate(PoseInterpreter.InterpretedPose pose)
    {
        if (hipBone == null) CacheBones();
        if (hipBone == null) return;

        if (!calibrated)
            Calibrate(pose);

        ApplyPoseInternal(pose, true);
    }
}
