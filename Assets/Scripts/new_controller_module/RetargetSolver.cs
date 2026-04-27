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
///   delta   = srcCurrentW * Inverse(srcRestW)
///   targetW = delta * avRestW
///
/// 四肢：Parent-Inherited Swing（業界標準 FK retarget）
///   parentDelta  = parentCurrentW * Inverse(parentRestW)
///   inheritedDir = parentDelta * avRestBoneDir
///   swing        = FromToRotation(inheritedDir, srcDir)  ← 最小旋轉，無 roll
///   targetW      = swing * parentDelta * avRestW
///
/// Twist 完全靠層級繼承（chest→arm, hip→leg），不從 pole hint 硬算。
/// 避免 MediaPipe 手掌/腳掌法線雜訊造成的「扭抹布」artifact。
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

    // Avatar rest world rotations
    private Quaternion avRestHipW, avRestSpineW, avRestChestW, avRestNeckW, avRestHeadW;
    private Quaternion avRestLUpperArmW, avRestLLowerArmW;
    private Quaternion avRestRUpperArmW, avRestRLowerArmW;
    private Quaternion avRestLUpperLegW, avRestLLowerLegW;
    private Quaternion avRestRUpperLegW, avRestRLowerLegW;

    // Avatar rest bone physical directions (world space)
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

        // Avatar rest bone directions (world space)
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

        // Limbs — Parent-Inherited Swing
        // 順序很重要：parent 必須先設好（torso 已完成），子骨才能讀到正確的 parent 世界旋轉
        Quaternion chestCurrentW = chestBone != null ? chestBone.rotation : avRestChestW;

        SetLimbSwingInherit(l_upperArmBone, pose.L_UpperArm.Forward,
                            chestCurrentW, avRestChestW,
                            avRestDirLUpperArm, avRestLUpperArmW);
        SetLimbSwingInherit(l_lowerArmBone, pose.L_LowerArm.Forward,
                            l_upperArmBone != null ? l_upperArmBone.rotation : avRestLUpperArmW,
                            avRestLUpperArmW,
                            avRestDirLLowerArm, avRestLLowerArmW);

        SetLimbSwingInherit(r_upperArmBone, pose.R_UpperArm.Forward,
                            chestCurrentW, avRestChestW,
                            avRestDirRUpperArm, avRestRUpperArmW);
        SetLimbSwingInherit(r_lowerArmBone, pose.R_LowerArm.Forward,
                            r_upperArmBone != null ? r_upperArmBone.rotation : avRestRUpperArmW,
                            avRestRUpperArmW,
                            avRestDirRLowerArm, avRestRLowerArmW);

        Quaternion hipCurrentW = hipBone.rotation;

        SetLimbSwingInherit(l_upperLegBone, pose.L_UpperLeg.Forward,
                            hipCurrentW, avRestHipW,
                            avRestDirLUpperLeg, avRestLUpperLegW);
        SetLimbSwingInherit(l_lowerLegBone, pose.L_LowerLeg.Forward,
                            l_upperLegBone != null ? l_upperLegBone.rotation : avRestLUpperLegW,
                            avRestLUpperLegW,
                            avRestDirLLowerLeg, avRestLLowerLegW);

        SetLimbSwingInherit(r_upperLegBone, pose.R_UpperLeg.Forward,
                            hipCurrentW, avRestHipW,
                            avRestDirRUpperLeg, avRestRUpperLegW);
        SetLimbSwingInherit(r_lowerLegBone, pose.R_LowerLeg.Forward,
                            r_upperLegBone != null ? r_upperLegBone.rotation : avRestRUpperLegW,
                            avRestRUpperLegW,
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

    // ===== Limbs: Parent-Inherited Swing =====

    /// <summary>
    /// 四肢 FK retarget（業界標準）：
    ///
    /// 1. parentDelta = parentCurrentW * Inverse(parentRestW)
    ///    → parent 從 rest 到現在轉了多少
    /// 2. inheritedDir = parentDelta * avRestBoneDir
    ///    → 若子骨剛性跟隨 parent，骨骼方向會在哪
    /// 3. swing = FromToRotation(inheritedDir, srcDir)
    ///    → 最小旋轉把骨骼校正到 MediaPipe 量測方向（無多餘 roll）
    /// 4. targetW = swing * parentDelta * avRestW
    ///
    /// 為什麼這樣自然：
    /// - Twist 不是硬算出來的，而是沿著骨骼層級自然繼承下來
    ///   (pelvis → upperLeg → lowerLeg → foot, chest → upperArm → lowerArm → hand)
    /// - 身體轉向時，整條手臂/腿會跟著 parent 一起轉 → 腳尖/手指會跟著面向
    /// - FromToRotation 是最小旋轉，不帶任何 roll，關節不會出現「扭抹布」
    /// </summary>
    private void SetLimbSwingInherit(Transform bone, Vector3 srcDir,
                                      Quaternion parentCurrentW, Quaternion parentRestW,
                                      Vector3 avRestBoneDir, Quaternion avRestBoneW)
    {
        if (bone == null) return;

        Quaternion parentDelta = parentCurrentW * Quaternion.Inverse(parentRestW);
        Vector3 inheritedDir = parentDelta * avRestBoneDir;

        Quaternion swing = Quaternion.FromToRotation(inheritedDir, srcDir);
        Quaternion targetW = swing * parentDelta * avRestBoneW;

        bone.localRotation = Quaternion.Inverse(bone.parent.rotation) * targetW;
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
