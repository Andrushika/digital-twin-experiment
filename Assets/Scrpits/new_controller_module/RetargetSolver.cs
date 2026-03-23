using UnityEngine;

/// <summary>
/// MODULE C: Retarget Solver
/// 
/// 責任：
/// 1. 接收 Module B 的人體結構化姿態 (InterpretedPose)
/// 2. 計算 Avatar 的每根骨骼應該旋轉多少
/// 3. 套用旋轉到 Unity Animator bones
/// 
/// 核心方法：World-Space Delta
/// ─────────────────────────────────
/// 對每根骨骼：
///   delta   = srcCurrentWorld * Inverse(srcRestWorld)
///   targetW = delta * avatarRestWorld
///   bone.localRotation = Inverse(bone.parent.rotation) * targetW
/// 
/// 優點：
/// - 自動處理 Unity 骨骼階層中間骨骼 (Shoulder, Neck, UpperChest 等)
/// - 不需手動計算每層 local space
/// - 骨骼寫入在 LateUpdate，確保不被 Animator 覆蓋
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
    private Transform r_upperArmBone;
    private Transform r_lowerArmBone;
    
    private Transform l_upperLegBone;
    private Transform l_lowerLegBone;
    private Transform r_upperLegBone;
    private Transform r_lowerLegBone;

    // 用來計算下肢 rest 方向的子骨骼
    private Transform l_handBone;
    private Transform r_handBone;
    private Transform l_footBone;
    private Transform r_footBone;

    // ===== Calibration state =====
    private bool calibrated;
    private float autoComputedScale = 1f;

    // Source rest world rotations （第一幀的「來源」世界旋轉）
    private Quaternion srcRestPelvisW, srcRestSpineW, srcRestChestW, srcRestNeckW, srcRestHeadW;

    // Avatar rest bone directions （Avatar bind-pose 中骨骼的物理方向：parent→child）
    // 這是 swing 的起點，確保來源方向被正確映射到 Avatar 骨骼上
    private Vector3 avRestLUpperArmDir, avRestLLowerArmDir;
    private Vector3 avRestRUpperArmDir, avRestRLowerArmDir;
    private Vector3 avRestLUpperLegDir, avRestLLowerLegDir;
    private Vector3 avRestRUpperLegDir, avRestRLowerLegDir;

    // Avatar rest world rotations （第一幀的 Avatar 世界旋轉）
    private Quaternion avRestHipW, avRestSpineW, avRestChestW, avRestNeckW, avRestHeadW;
    private Quaternion avRestLUpperArmW, avRestLLowerArmW;
    private Quaternion avRestRUpperArmW, avRestRLowerArmW;
    private Quaternion avRestLUpperLegW, avRestLLowerLegW;
    private Quaternion avRestRUpperLegW, avRestRLowerLegW;

    // Position calibration
    private Vector3 srcRestPelvisPos;
    private Vector3 avRestHipPos;

    // ===== Pending pose (buffered for LateUpdate) =====
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
        r_upperArmBone = targetAvatar.GetBoneTransform(HumanBodyBones.RightUpperArm);
        r_lowerArmBone = targetAvatar.GetBoneTransform(HumanBodyBones.RightLowerArm);

        l_upperLegBone = targetAvatar.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
        l_lowerLegBone = targetAvatar.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
        r_upperLegBone = targetAvatar.GetBoneTransform(HumanBodyBones.RightUpperLeg);
        r_lowerLegBone = targetAvatar.GetBoneTransform(HumanBodyBones.RightLowerLeg);

        l_handBone = targetAvatar.GetBoneTransform(HumanBodyBones.LeftHand);
        r_handBone = targetAvatar.GetBoneTransform(HumanBodyBones.RightHand);
        l_footBone = targetAvatar.GetBoneTransform(HumanBodyBones.LeftFoot);
        r_footBone = targetAvatar.GetBoneTransform(HumanBodyBones.RightFoot);

        Debug.Log("[RetargetSolver] Bones cached");
    }

    // ===== 外部 API（由 MotionController 在 Update 中呼叫）=====
    // 只把姿態暫存，真正寫入骨骼在 LateUpdate，
    // 確保 Animator 已經 evaluate 完畢、不會再覆蓋。

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

    /// <summary>
    /// LateUpdate: 在 Animator evaluate 之後才寫入骨骼
    /// </summary>
    private void LateUpdate()
    {
        if (pendingPose == null || hipBone == null)
            return;

        if (!calibrated)
            Calibrate(pendingPose);

        ApplyPoseInternal(pendingPose, pendingIncludeLimbs);
    }

    // ===== 校正（第一幀 → 記錄 rest 姿態）=====

    private void Calibrate(PoseInterpreter.InterpretedPose pose)
    {
        if (hipBone == null) return;

        // --- Source rest rotations ---
        srcRestPelvisW = pose.Pelvis.Rotation;
        srcRestChestW  = pose.Chest.Rotation;
        srcRestSpineW  = Quaternion.Slerp(srcRestPelvisW, srcRestChestW, 0.5f);
        srcRestHeadW   = pose.Head.Rotation;
        srcRestNeckW   = Quaternion.Slerp(srcRestChestW, srcRestHeadW, 0.5f);

        // Avatar rest bone directions：用實際骨骼位置算出 bind-pose 中骨骼指向
        // ★ 這才是 swing 的起點，不能用來源 rest 方向！
        //   因為來源 rest（如自然站立手垂下）和 Avatar rest（T-pose 手打開）
        //   方向完全不同，混用會導致動作反轉。
        avRestLUpperArmDir = SafeBoneDir(l_upperArmBone, l_lowerArmBone);
        avRestLLowerArmDir = SafeBoneDir(l_lowerArmBone, l_handBone);
        avRestRUpperArmDir = SafeBoneDir(r_upperArmBone, r_lowerArmBone);
        avRestRLowerArmDir = SafeBoneDir(r_lowerArmBone, r_handBone);

        avRestLUpperLegDir = SafeBoneDir(l_upperLegBone, l_lowerLegBone);
        avRestLLowerLegDir = SafeBoneDir(l_lowerLegBone, l_footBone);
        avRestRUpperLegDir = SafeBoneDir(r_upperLegBone, r_lowerLegBone);
        avRestRLowerLegDir = SafeBoneDir(r_lowerLegBone, r_footBone);

        Debug.Log($"[RetargetSolver] Avatar rest dirs: LUpperArm={avRestLUpperArmDir}, RUpperArm={avRestRUpperArmDir}, " +
                  $"LUpperLeg={avRestLUpperLegDir}, RUpperLeg={avRestRUpperLegDir}");

        // --- Avatar rest world rotations ---
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

        // --- Position calibration ---
        srcRestPelvisPos = MapSourcePosition(pose.Pelvis.Position);
        avRestHipPos     = hipBone.position;

        // --- Auto scale ---
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

        // ── Position（offset-based，而非絕對乘 scale）──
        // 校正時記錄了來源和 Avatar 的初始髖位；
        // 之後只傳遞「移動量」，就不會因為 scale 把角色推進地面。
        Vector3 currentSrcPos = MapSourcePosition(pose.Pelvis.Position);
        hipBone.position = avRestHipPos + (currentSrcPos - srcRestPelvisPos) * finalScale;

        // ── Torso（由上到下：hips → spine → chest → neck → head）──
        Quaternion srcPelvisW = pose.Pelvis.Rotation;
        Quaternion srcChestW  = pose.Chest.Rotation;
        Quaternion srcSpineW  = Quaternion.Slerp(srcPelvisW, srcChestW, 0.5f);
        Quaternion srcHeadW   = pose.Head.Rotation;
        Quaternion srcNeckW   = Quaternion.Slerp(srcChestW, srcHeadW, 0.5f);

        // Hips = root，直接設 world rotation
        SetBoneWorldRotation(hipBone, srcPelvisW, srcRestPelvisW, avRestHipW);

        // Spine / Chest / Neck / Head → 用 parent.rotation 自動處理中間骨骼
        SetBoneLocalFromWorldDelta(spineBone, srcSpineW,  srcRestSpineW,  avRestSpineW);
        SetBoneLocalFromWorldDelta(chestBone, srcChestW,  srcRestChestW,  avRestChestW);
        SetBoneLocalFromWorldDelta(neckBone,  srcNeckW,   srcRestNeckW,   avRestNeckW);
        SetBoneLocalFromWorldDelta(headBone,  srcHeadW,   srcRestHeadW,   avRestHeadW);

        if (!includeLimbs) return;

        // ── Limbs（Parent-Relative Swing）──
        // 核心思路：把肢體方向從「來源軀幹的局部空間」對映到「Avatar 軀幹的局部空間」
        // 這樣無論來源和 Avatar 面向哪裡、rest pose 差異多大，都能正確映射。
        //
        // 步驟：
        //   1. localDir = Inv(srcParentQ) * srcLimbDir   → 來源肢體在來源軀幹局部的方向
        //   2. targetDir = avParentBone.rotation * localDir → 用 Avatar 的軀幹轉回世界空間
        //   3. swing = FromToRotation(avRestBoneDir, targetDir) → 從 Avatar rest 轉到目標
        //
        // 為什麼不直接用世界方向？
        //   因為來源和 Avatar 的面向 / rest pose 不同，直接對映世界方向會導致
        //   左右交叉或方向反轉。

        Quaternion srcChestInv  = Quaternion.Inverse(pose.Chest.Rotation);
        Quaternion srcPelvisInv = Quaternion.Inverse(pose.Pelvis.Rotation);

        // ── Arms（相對於胸部）──
        SetLimbRelative(l_upperArmBone, pose.L_UpperArm.Forward, srcChestInv, chestBone, avRestLUpperArmDir, avRestLUpperArmW);
        SetLimbRelative(l_lowerArmBone, pose.L_LowerArm.Forward, srcChestInv, chestBone, avRestLLowerArmDir, avRestLLowerArmW);
        SetLimbRelative(r_upperArmBone, pose.R_UpperArm.Forward, srcChestInv, chestBone, avRestRUpperArmDir, avRestRUpperArmW);
        SetLimbRelative(r_lowerArmBone, pose.R_LowerArm.Forward, srcChestInv, chestBone, avRestRLowerArmDir, avRestRLowerArmW);

        // ── Legs（相對於骨盆）──
        SetLimbRelative(l_upperLegBone, pose.L_UpperLeg.Forward, srcPelvisInv, hipBone, avRestLUpperLegDir, avRestLUpperLegW);
        SetLimbRelative(l_lowerLegBone, pose.L_LowerLeg.Forward, srcPelvisInv, hipBone, avRestLLowerLegDir, avRestLLowerLegW);
        SetLimbRelative(r_upperLegBone, pose.R_UpperLeg.Forward, srcPelvisInv, hipBone, avRestRUpperLegDir, avRestRUpperLegW);
        SetLimbRelative(r_lowerLegBone, pose.R_LowerLeg.Forward, srcPelvisInv, hipBone, avRestRLowerLegDir, avRestRLowerLegW);
    }

    // ===== 核心：World-Space Delta =====

    /// <summary>
    /// Root bone（Hips）：直接設定 world rotation。
    /// </summary>
    private void SetBoneWorldRotation(Transform bone, Quaternion srcCurrentW,
                                       Quaternion srcRestW, Quaternion avRestW)
    {
        if (bone == null) return;
        Quaternion delta = srcCurrentW * Quaternion.Inverse(srcRestW);
        bone.rotation = delta * avRestW;
    }

    /// <summary>
    /// 子骨骼：算出目標 world rotation 後，用實際 parent.rotation 轉成 localRotation。
    /// 這樣即使 Unity 骨骼階層有額外中間骨骼（Shoulder、UpperChest 等），
    /// 也不需要手動分別處理每一層。
    /// </summary>
    private void SetBoneLocalFromWorldDelta(Transform bone, Quaternion srcCurrentW,
                                             Quaternion srcRestW, Quaternion avRestW)
    {
        if (bone == null) return;
        Quaternion delta = srcCurrentW * Quaternion.Inverse(srcRestW);
        Quaternion targetWorld = delta * avRestW;
        bone.localRotation = Quaternion.Inverse(bone.parent.rotation) * targetWorld;
    }

    /// <summary>
    /// 四肢 Parent-Relative Swing：
    /// 
    /// 1. 把來源肢體方向轉到來源軀幹的局部空間 → 方向與面向/rest pose 無關
    /// 2. 用 Avatar 已更新的軀幹旋轉轉回世界空間 → 自動匹配 Avatar 的朝向
    /// 3. Swing-Only 從 Avatar rest 方向轉到目標方向 → twist 保留 bind pose 自然值
    /// 
    /// 為什麼不直接用世界方向？
    /// 因為來源資料和 Avatar 可能面向不同方向，直接映射世界方向會導致左右交叉。
    /// 透過「軀幹相對」中繼，任何面向差異都由軀幹旋轉自動處理。
    /// </summary>
    private void SetLimbRelative(Transform bone, Vector3 srcCurrentDir,
                                  Quaternion srcParentInv, Transform avParentBone,
                                  Vector3 avRestBoneDir, Quaternion avRestW)
    {
        if (bone == null || avParentBone == null) return;
        if (srcCurrentDir.sqrMagnitude < 1e-6f || avRestBoneDir.sqrMagnitude < 1e-6f) return;

        // Step 1: 來源肢體方向 → 來源軀幹局部空間
        Vector3 localDir = srcParentInv * srcCurrentDir;

        // Step 2: 用 Avatar 的軀幹旋轉（已在前面 torso pass 更新）轉回世界空間
        Vector3 targetDir = avParentBone.rotation * localDir;

        // Step 3: Swing from avatar rest direction to target direction
        Quaternion swing = Quaternion.FromToRotation(avRestBoneDir, targetDir);
        Quaternion targetWorld = swing * avRestW;
        bone.localRotation = Quaternion.Inverse(bone.parent.rotation) * targetWorld;
    }

    /// <summary>
    /// 計算從骨骼 from 指向骨骼 to 的單位方向（物理骨骼方向）
    /// </summary>
    private Vector3 SafeBoneDir(Transform from, Transform to)
    {
        if (from == null || to == null) return Vector3.forward;
        Vector3 dir = to.position - from.position;
        return dir.sqrMagnitude > 1e-8f ? dir.normalized : Vector3.forward;
    }

    // ===== Helpers =====

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
}
