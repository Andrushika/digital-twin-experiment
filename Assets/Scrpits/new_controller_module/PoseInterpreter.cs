using UnityEngine;

/// <summary>
/// MODULE B: Pose Interpreter
/// 
/// 責任：
/// 1. 從 17 個原始關節點 (COCO) 計算人體主要部位的姿態
/// 2. 建立 pelvis/chest/head 的局部座標系 (frame)
/// 3. 計算四肢相對於各部位的方向與距離
/// 
/// 為 Module C 提供「人體級」的結構化資訊，而非零散的點位
/// </summary>
public class PoseInterpreter
{
    /// <summary>
    /// 人體主要部位的姿態幀
    /// 包含位置、旋轉、和該部位的主要骨架向量
    /// </summary>
    public class BodyPartFrame
    {
        public Vector3 Position { get; set; }
        public Quaternion Rotation { get; set; }
        public float Confidence { get; set; }

        // 該部位的局部座標軸
        public Vector3 Forward { get; set; }   // Z
        public Vector3 Right { get; set; }     // X
        public Vector3 Up { get; set; }        // Y

        /// <summary>
        /// 是否具有來自幾何約束（非啟發式投影）的可靠 twist 資訊。
        /// 例如：手掌平面（wrist+pinky+index）、腳底平面（ankle+heel+foot_index）、
        /// 或三關節彎曲平面（肘/膝彎曲方向）。
        /// </summary>
        public bool HasReliableTwist { get; set; }

        public BodyPartFrame()
        {
            Position = Vector3.zero;
            Rotation = Quaternion.identity;
            Confidence = 1f;
            Forward = Vector3.forward;
            Right = Vector3.right;
            Up = Vector3.up;
            HasReliableTwist = false;
        }
    }

    /// <summary>
    /// 完整的人體結構化姿態
    /// </summary>
    public class InterpretedPose
    {
        public BodyPartFrame Pelvis;        // 髖部 (下身根)
        public BodyPartFrame Chest;         // 胸部 (上身根)
        public BodyPartFrame Head;          // 頭部
        
        public BodyPartFrame L_UpperArm;    // 左上臂
        public BodyPartFrame L_LowerArm;    // 左前臂
        public BodyPartFrame R_UpperArm;    // 右上臂
        public BodyPartFrame R_LowerArm;    // 右前臂
        
        public BodyPartFrame L_UpperLeg;    // 左大腿
        public BodyPartFrame L_LowerLeg;    // 左小腿
        public BodyPartFrame R_UpperLeg;    // 右大腿
        public BodyPartFrame R_LowerLeg;    // 右小腿

        // 直接從 keypoints 測量的方向，用於 RetargetSolver 的 pole hint
        public Vector3 L_FootForward;       // 左腳 heel→foot_index 方向
        public Vector3 R_FootForward;       // 右腳 heel→foot_index 方向
        public Vector3 L_PalmNormal;        // 左手掌法線 cross(wrist→index, wrist→pinky)
        public Vector3 R_PalmNormal;        // 右手掌法線

        public InterpretedPose()
        {
            Pelvis = new BodyPartFrame();
            Chest = new BodyPartFrame();
            Head = new BodyPartFrame();
            L_UpperArm = new BodyPartFrame();
            L_LowerArm = new BodyPartFrame();
            R_UpperArm = new BodyPartFrame();
            R_LowerArm = new BodyPartFrame();
            L_UpperLeg = new BodyPartFrame();
            L_LowerLeg = new BodyPartFrame();
            R_UpperLeg = new BodyPartFrame();
            R_LowerLeg = new BodyPartFrame();
            L_FootForward = Vector3.forward;
            R_FootForward = Vector3.forward;
            L_PalmNormal = Vector3.up;
            R_PalmNormal = Vector3.up;
        }
    }

    private InterpretedPose interpretedPose = new InterpretedPose();
    private const float Epsilon = 1e-6f;

    /// <summary>
    /// Phase 2: 從 raw joints 解釋人體姿態
    /// 逐步建立完整的 torso frame hierarchy
    /// 
    /// Frame 定義原則：
    /// 1. 骨盆 frame：下身根，用髖部寬度定義左右軸
    /// 2. 胸部 frame：上身根，用肩膀寬度定義左右軸
    /// 3. 頭部 frame：由上身引導
    /// 4. 四肢 frame：參考上級 frame，避免孤立定義
    /// </summary>
    public InterpretedPose Interpret(HumanPoseData rawPose)
    {
        if (rawPose == null || !rawPose.IsValid)
            return interpretedPose;

        // 取得關鍵點
        Vector3 l_hip = rawPose.GetJoint(HumanPoseData.JointType.L_Hip);
        Vector3 r_hip = rawPose.GetJoint(HumanPoseData.JointType.R_Hip);
        Vector3 l_shoulder = rawPose.GetJoint(HumanPoseData.JointType.L_Shoulder);
        Vector3 r_shoulder = rawPose.GetJoint(HumanPoseData.JointType.R_Shoulder);
        Vector3 nose = rawPose.GetJoint(HumanPoseData.JointType.Nose);

        Vector3 pelvis_center = Vector3.Lerp(l_hip, r_hip, 0.5f);
        Vector3 chest_center = Vector3.Lerp(l_shoulder, r_shoulder, 0.5f);

        // ===== PHASE 2a: Pelvis Frame (下身根) =====
        BuildPelvisFrame(pelvis_center, l_hip, r_hip, chest_center, nose, rawPose);

        // ===== PHASE 2b: Chest Frame (上身根) =====
        BuildChestFrame(chest_center, l_shoulder, r_shoulder, pelvis_center, nose, rawPose);

        // ===== PHASE 2c: Head Frame (頭部) =====
        BuildHeadFrame(nose, chest_center, interpretedPose.Chest, rawPose);

        // ===== PHASE 3: Limbs (四肢) =====
        InterpretLimbs(rawPose);

        return interpretedPose;
    }

    /// <summary>
    /// 建立骨盆 frame (下身根)
    /// 
    /// right axis: 左髖到右髖（身體左右方向）
    /// up axis: 胸部到骨盆（體幹向上方向）
    /// forward axis: 叉乘得出（身體前後方向）
    /// </summary>
    private void BuildPelvisFrame(Vector3 pelvis_center, Vector3 l_hip, Vector3 r_hip,
                                   Vector3 chest_center, Vector3 nose, HumanPoseData rawPose)
    {
        BodyPartFrame prev = CloneFrame(interpretedPose.Pelvis);

        Vector3 right = SafeNormalize(r_hip - l_hip, prev.Right);
        Vector3 up = SafeNormalize(chest_center - pelvis_center, prev.Up);
        Vector3 forward = SafeNormalize(Vector3.Cross(right, up), prev.Forward);

        // 用 nose 提示人體前向，避免 cross(right, up) 正負方向不確定。
        Vector3 faceHint = Vector3.ProjectOnPlane(nose - pelvis_center, up);
        if (faceHint.sqrMagnitude > Epsilon && Vector3.Dot(forward, faceHint) < 0f)
            forward = -forward;

        // 重新正交化，確保三軸互相垂直
        right = SafeNormalize(Vector3.Cross(up, forward), prev.Right);
        forward = SafeNormalize(Vector3.Cross(right, up), prev.Forward);

        EnsureAxisContinuity(prev, ref right, ref up, ref forward);

        interpretedPose.Pelvis.Position = pelvis_center;
        interpretedPose.Pelvis.Right = right;
        interpretedPose.Pelvis.Up = up;
        interpretedPose.Pelvis.Forward = forward;
        interpretedPose.Pelvis.Rotation = EnsureQuaternionContinuity(prev.Rotation, FrameToQuaternion(right, up, forward));
        interpretedPose.Pelvis.Confidence = (rawPose.Confidence[(int)HumanPoseData.JointType.L_Hip] +
                                            rawPose.Confidence[(int)HumanPoseData.JointType.R_Hip]) / 2f;
    }

    /// <summary>
    /// 建立胸部 frame (上身根)
    /// 
    /// right axis: 左肩到右肩（肩膀寬度）
    /// up axis: 頸部方向（胸向頭）
    /// forward axis: 叉乘得出
    /// </summary>
    private void BuildChestFrame(Vector3 chest_center, Vector3 l_shoulder, Vector3 r_shoulder,
                                  Vector3 pelvis_center, Vector3 nose, HumanPoseData rawPose)
    {
        BodyPartFrame prev = CloneFrame(interpretedPose.Chest);

        Vector3 right = SafeNormalize(r_shoulder - l_shoulder, prev.Right);
        Vector3 up = SafeNormalize(chest_center - pelvis_center, prev.Up);
        Vector3 forward = SafeNormalize(Vector3.Cross(right, up), prev.Forward);

        Vector3 faceHint = Vector3.ProjectOnPlane(nose - chest_center, up);
        if (faceHint.sqrMagnitude > Epsilon && Vector3.Dot(forward, faceHint) < 0f)
            forward = -forward;

        // 重新正交化
        right = SafeNormalize(Vector3.Cross(up, forward), prev.Right);
        forward = SafeNormalize(Vector3.Cross(right, up), prev.Forward);

        EnsureAxisContinuity(prev, ref right, ref up, ref forward);

        interpretedPose.Chest.Position = chest_center;
        interpretedPose.Chest.Right = right;
        interpretedPose.Chest.Up = up;
        interpretedPose.Chest.Forward = forward;
        interpretedPose.Chest.Rotation = EnsureQuaternionContinuity(prev.Rotation, FrameToQuaternion(right, up, forward));
        interpretedPose.Chest.Confidence = (rawPose.Confidence[(int)HumanPoseData.JointType.L_Shoulder] +
                                           rawPose.Confidence[(int)HumanPoseData.JointType.R_Shoulder]) / 2f;
    }

    /// <summary>
    /// 建立頭部 frame
    /// 參考胸部 frame 定義
    /// </summary>
    private void BuildHeadFrame(Vector3 nose, Vector3 chest_center, BodyPartFrame chest_frame, HumanPoseData rawPose)
    {
        BodyPartFrame prev = CloneFrame(interpretedPose.Head);

        // 頭部先跟隨胸部姿態，nose 只作為少量 yaw/pitch 提示。
        Vector3 up = chest_frame.Up;
        Vector3 noseHint = Vector3.ProjectOnPlane(nose - chest_center, up);

        Vector3 forwardHint = noseHint.sqrMagnitude > Epsilon
            ? SafeNormalize(noseHint, chest_frame.Forward)
            : chest_frame.Forward;

        Vector3 forward = SafeNormalize(Vector3.Slerp(chest_frame.Forward, forwardHint, 0.25f), chest_frame.Forward);
        Vector3 right = SafeNormalize(Vector3.Cross(up, forward), chest_frame.Right);
        forward = SafeNormalize(Vector3.Cross(right, up), chest_frame.Forward);

        EnsureAxisContinuity(prev, ref right, ref up, ref forward);

        interpretedPose.Head.Position = nose;
        interpretedPose.Head.Right = right;
        interpretedPose.Head.Up = up;
        interpretedPose.Head.Forward = forward;
        interpretedPose.Head.Rotation = EnsureQuaternionContinuity(prev.Rotation, FrameToQuaternion(right, up, forward));
        interpretedPose.Head.Confidence = rawPose.Confidence[(int)HumanPoseData.JointType.Nose];
    }

    /// <summary>
    /// Phase 3: 從 raw joints 解釋四肢姿態
    /// 四肢參考上級 frame 的方向，避免孤立扭轉
    /// </summary>
    private void InterpretLimbs(HumanPoseData rawPose)
    {
        // 左臂
        Vector3 l_shoulder = rawPose.GetJoint(HumanPoseData.JointType.L_Shoulder);
        Vector3 l_elbow = rawPose.GetJoint(HumanPoseData.JointType.L_Elbow);
        Vector3 l_wrist = rawPose.GetJoint(HumanPoseData.JointType.L_Wrist);

        BuildUpperLimbFrame(l_shoulder, l_elbow, interpretedPose.Chest, interpretedPose.L_UpperArm, true,
                    rawPose.Confidence[(int)HumanPoseData.JointType.L_Shoulder],
                    rawPose.Confidence[(int)HumanPoseData.JointType.L_Elbow]);
        BuildLowerLimbFrame(l_shoulder, l_elbow, l_wrist, interpretedPose.L_UpperArm, interpretedPose.Chest,
                    interpretedPose.L_LowerArm, true,
                    rawPose.Confidence[(int)HumanPoseData.JointType.L_Elbow],
                    rawPose.Confidence[(int)HumanPoseData.JointType.L_Wrist]);

        // 右臂
        Vector3 r_shoulder = rawPose.GetJoint(HumanPoseData.JointType.R_Shoulder);
        Vector3 r_elbow = rawPose.GetJoint(HumanPoseData.JointType.R_Elbow);
        Vector3 r_wrist = rawPose.GetJoint(HumanPoseData.JointType.R_Wrist);

        BuildUpperLimbFrame(r_shoulder, r_elbow, interpretedPose.Chest, interpretedPose.R_UpperArm, false,
                    rawPose.Confidence[(int)HumanPoseData.JointType.R_Shoulder],
                    rawPose.Confidence[(int)HumanPoseData.JointType.R_Elbow]);
        BuildLowerLimbFrame(r_shoulder, r_elbow, r_wrist, interpretedPose.R_UpperArm, interpretedPose.Chest,
                    interpretedPose.R_LowerArm, false,
                    rawPose.Confidence[(int)HumanPoseData.JointType.R_Elbow],
                    rawPose.Confidence[(int)HumanPoseData.JointType.R_Wrist]);

        // 左腿
        Vector3 l_hip = rawPose.GetJoint(HumanPoseData.JointType.L_Hip);
        Vector3 l_knee = rawPose.GetJoint(HumanPoseData.JointType.L_Knee);
        Vector3 l_ankle = rawPose.GetJoint(HumanPoseData.JointType.L_Ankle);

        BuildUpperLimbFrame(l_hip, l_knee, interpretedPose.Pelvis, interpretedPose.L_UpperLeg, true,
                    rawPose.Confidence[(int)HumanPoseData.JointType.L_Hip],
                    rawPose.Confidence[(int)HumanPoseData.JointType.L_Knee]);
        BuildLowerLimbFrame(l_hip, l_knee, l_ankle, interpretedPose.L_UpperLeg, interpretedPose.Pelvis,
                    interpretedPose.L_LowerLeg, true,
                    rawPose.Confidence[(int)HumanPoseData.JointType.L_Knee],
                    rawPose.Confidence[(int)HumanPoseData.JointType.L_Ankle]);

        // 右腿
        Vector3 r_hip = rawPose.GetJoint(HumanPoseData.JointType.R_Hip);
        Vector3 r_knee = rawPose.GetJoint(HumanPoseData.JointType.R_Knee);
        Vector3 r_ankle = rawPose.GetJoint(HumanPoseData.JointType.R_Ankle);

        BuildUpperLimbFrame(r_hip, r_knee, interpretedPose.Pelvis, interpretedPose.R_UpperLeg, false,
                            rawPose.Confidence[(int)HumanPoseData.JointType.R_Hip],
                            rawPose.Confidence[(int)HumanPoseData.JointType.R_Knee]);
        BuildLowerLimbFrame(r_hip, r_knee, r_ankle, interpretedPose.R_UpperLeg, interpretedPose.Pelvis,
                            interpretedPose.R_LowerLeg, false,
                            rawPose.Confidence[(int)HumanPoseData.JointType.R_Knee],
                            rawPose.Confidence[(int)HumanPoseData.JointType.R_Ankle]);

        // NOTE: Phase 3b twist refinement (RefineUpperLimbTwistFromBend, RefineLowerArmTwist,
        // RefineLowerLegTwist) 已移除。幾何約束（彎曲平面、手掌法線）噪音過大，
        // 造成 twist jump。改由 RetargetSolver 從父骨骼 delta 繼承 twist。

        // ===== 計算直接測量的方向（供 RetargetSolver 當 pole hint） =====
        ComputeFootForwardVectors(rawPose);
        ComputePalmNormalVectors(rawPose);
    }

    /// <summary>
    /// 建立上肢/上腿 frame
    /// twist 參考使用「父級 forward 投影到骨軸垂直平面」
    /// </summary>
    private void BuildUpperLimbFrame(Vector3 proximal, Vector3 distal, BodyPartFrame parent_frame,
                                     BodyPartFrame output, bool isLeft, float confidenceProximal, float confidenceDistal)
    {
        BodyPartFrame prev = CloneFrame(output);
        Vector3 bone_axis = SafeNormalize(distal - proximal, prev.Forward);

        Vector3 up_axis = ComputeProjectedReference(
            parent_frame.Forward,
            bone_axis,
            parent_frame.Up,
            parent_frame.Right,
            prev.Up);

        Vector3 right_axis = SafeNormalize(Vector3.Cross(up_axis, bone_axis), prev.Right);
        up_axis = SafeNormalize(Vector3.Cross(bone_axis, right_axis), up_axis);

        ApplyLeftRightMirrorRule(parent_frame, isLeft, ref right_axis, ref up_axis);
        // 四肢不做 EnsureAxisContinuity：bone_axis 是由關節位置直接決定的 ground truth，
        // 不應因與前幀夾角 >90° 而被翻轉。twist 歧義已由 ApplyLeftRightMirrorRule 處理。

        output.Position = Vector3.Lerp(proximal, distal, 0.5f);
        output.Forward = bone_axis;
        output.Right = right_axis;
        output.Up = up_axis;
        output.Rotation = EnsureQuaternionContinuity(prev.Rotation, FrameToQuaternion(right_axis, up_axis, bone_axis));
        output.Confidence = (confidenceProximal + confidenceDistal) * 0.5f;
    }

    /// <summary>
    /// 建立下肢/下腿 frame
    /// 先用彎曲平面決定 twist，再退回父級投影參考。
    /// </summary>
    private void BuildLowerLimbFrame(Vector3 root, Vector3 joint, Vector3 distal,
                                     BodyPartFrame parent_frame, BodyPartFrame torso_frame,
                                     BodyPartFrame output, bool isLeft, float confidenceJoint, float confidenceDistal)
    {
        BodyPartFrame prev = CloneFrame(output);
        Vector3 bone_axis = SafeNormalize(distal - joint, prev.Forward);

        Vector3 upperAxis = SafeNormalize(joint - root, parent_frame.Forward);
        Vector3 bendNormal = Vector3.Cross(upperAxis, bone_axis);

        Vector3 primaryRef = bendNormal.sqrMagnitude > Epsilon
            ? Vector3.Cross(bendNormal.normalized, bone_axis)
            : parent_frame.Up;

        Vector3 up_axis = ComputeProjectedReference(
            primaryRef,
            bone_axis,
            torso_frame.Forward,
            parent_frame.Right,
            prev.Up);

        Vector3 right_axis = SafeNormalize(Vector3.Cross(up_axis, bone_axis), prev.Right);
        up_axis = SafeNormalize(Vector3.Cross(bone_axis, right_axis), up_axis);

        ApplyLeftRightMirrorRule(torso_frame, isLeft, ref right_axis, ref up_axis);
        // 四肢不做 EnsureAxisContinuity（同 BuildUpperLimbFrame 的理由）

        output.Position = Vector3.Lerp(joint, distal, 0.5f);
        output.Forward = bone_axis;
        output.Right = right_axis;
        output.Up = up_axis;
        output.Rotation = EnsureQuaternionContinuity(prev.Rotation, FrameToQuaternion(right_axis, up_axis, bone_axis));
        output.Confidence = (confidenceJoint + confidenceDistal) * 0.5f;
    }

    // ===== 直接測量方向（供 RetargetSolver pole hint） =====

    /// <summary>
    /// 從 heel→foot_index 計算腳朝前方向。
    /// 這是直接測量值，不經過投影或 cross product，
    /// 所以在蹲下過渡時不會退化或翻轉。
    /// </summary>
    private void ComputeFootForwardVectors(HumanPoseData rawPose)
    {
        Vector3 l_heel = rawPose.GetJoint(HumanPoseData.JointType.L_Heel);
        Vector3 l_footIdx = rawPose.GetJoint(HumanPoseData.JointType.L_Foot_Index);
        Vector3 l_fwd = l_footIdx - l_heel;
        interpretedPose.L_FootForward = l_fwd.sqrMagnitude > Epsilon
            ? l_fwd.normalized
            : interpretedPose.Pelvis.Forward;

        Vector3 r_heel = rawPose.GetJoint(HumanPoseData.JointType.R_Heel);
        Vector3 r_footIdx = rawPose.GetJoint(HumanPoseData.JointType.R_Foot_Index);
        Vector3 r_fwd = r_footIdx - r_heel;
        interpretedPose.R_FootForward = r_fwd.sqrMagnitude > Epsilon
            ? r_fwd.normalized
            : interpretedPose.Pelvis.Forward;
    }

    /// <summary>
    /// 從 wrist/index/pinky 三點計算手掌法線。
    /// 手掌法線 = cross(wrist→index, wrist→pinky)。
    /// 代表手掌面朝的方向（前臂的 pronation/supination）。
    /// </summary>
    private void ComputePalmNormalVectors(HumanPoseData rawPose)
    {
        // 左手
        Vector3 l_wrist = rawPose.GetJoint(HumanPoseData.JointType.L_Wrist);
        Vector3 l_index = rawPose.GetJoint(HumanPoseData.JointType.L_Index);
        Vector3 l_pinky = rawPose.GetJoint(HumanPoseData.JointType.L_Pinky);
        Vector3 l_toIndex = (l_index - l_wrist);
        Vector3 l_toPinky = (l_pinky - l_wrist);
        Vector3 l_normal = Vector3.Cross(l_toIndex, l_toPinky);
        interpretedPose.L_PalmNormal = l_normal.sqrMagnitude > Epsilon
            ? l_normal.normalized
            : interpretedPose.Chest.Up;

        // 右手
        Vector3 r_wrist = rawPose.GetJoint(HumanPoseData.JointType.R_Wrist);
        Vector3 r_index = rawPose.GetJoint(HumanPoseData.JointType.R_Index);
        Vector3 r_pinky = rawPose.GetJoint(HumanPoseData.JointType.R_Pinky);
        Vector3 r_toIndex = (r_index - r_wrist);
        Vector3 r_toPinky = (r_pinky - r_wrist);
        Vector3 r_normal = Vector3.Cross(r_toIndex, r_toPinky);
        interpretedPose.R_PalmNormal = r_normal.sqrMagnitude > Epsilon
            ? r_normal.normalized
            : interpretedPose.Chest.Up;
    }

    // ===== Twist Refinement Methods (使用額外 keypoints) =====

    /// <summary>
    /// 上肢/上腿 twist 精煉：利用三關節彎曲平面。
    ///
    /// 原理：shoulder→elbow→wrist 三點定義一個彎曲平面，
    /// 該平面的法線就是肘/膝的指向方向（即 twist 軸的 up 分量）。
    ///
    /// 安全機制：
    /// - 彎曲角 < 10° → 不使用（接近伸直時 cross product 噪音太大）
    /// - 彎曲角 10°~25° → 與原始 frame 混合過渡
    /// - 彎曲角 > 25° → 完全使用彎曲平面 twist
    /// - 與前幀 twist 差異 > 90° → 翻轉法線方向（避免突然跳轉）
    /// </summary>
    private const float BEND_MIN_DEG = 10f;
    private const float BEND_FULL_DEG = 25f;

    private void RefineUpperLimbTwistFromBend(Vector3 root, Vector3 joint, Vector3 distal,
                                               BodyPartFrame output, BodyPartFrame parentFrame, bool isLeft)
    {
        Vector3 boneAxis = output.Forward;
        Vector3 upperToJoint = SafeNormalize(joint - root, boneAxis);
        Vector3 jointToDistal = SafeNormalize(distal - joint, boneAxis);

        // 計算彎曲角度
        float bendAngle = Vector3.Angle(upperToJoint, jointToDistal);
        if (bendAngle < BEND_MIN_DEG)
            return; // 太直，彎曲平面不可靠

        Vector3 bendNormal = Vector3.Cross(upperToJoint, jointToDistal);
        if (bendNormal.sqrMagnitude < Epsilon)
            return;

        bendNormal = bendNormal.normalized;

        // 時序連續性：若 bendNormal 與前幀 right 夾角 > 90°，翻轉
        if (Vector3.Dot(bendNormal, output.Right) < 0f)
            bendNormal = -bendNormal;

        Vector3 right = bendNormal;
        Vector3 up = SafeNormalize(Vector3.Cross(boneAxis, right), output.Up);
        right = SafeNormalize(Vector3.Cross(up, boneAxis), right);

        ApplyLeftRightMirrorRule(parentFrame, isLeft, ref right, ref up);

        // 平滑混合：彎曲角 10°~25° 之間線性混合
        float blend = Mathf.Clamp01((bendAngle - BEND_MIN_DEG) / (BEND_FULL_DEG - BEND_MIN_DEG));

        // 與原始 frame（無 twist 精煉）的 right/up 混合
        right = Vector3.Slerp(output.Right, right, blend);
        up = Vector3.Slerp(output.Up, up, blend);

        // 重新正交化
        right = SafeNormalize(Vector3.ProjectOnPlane(right, boneAxis), output.Right);
        up = SafeNormalize(Vector3.Cross(boneAxis, right), output.Up);

        BodyPartFrame prev = CloneFrame(output);
        output.Right = right;
        output.Up = up;
        output.Rotation = EnsureQuaternionContinuity(prev.Rotation, FrameToQuaternion(right, up, boneAxis));
        output.HasReliableTwist = (blend > 0.5f);
    }

    /// <summary>
    /// 前臂 twist 精煉：利用手掌平面（wrist + pinky + index）。
    ///
    /// 原理：wrist、pinky、index 三點定義手掌平面，
    /// 該平面的法線代表手掌朝向（前臂的 pronation/supination）。
    /// </summary>
    private void RefineLowerArmTwist(HumanPoseData rawPose, BodyPartFrame output, bool isLeft)
    {
        HumanPoseData.JointType wristType = isLeft ? HumanPoseData.JointType.L_Wrist : HumanPoseData.JointType.R_Wrist;
        HumanPoseData.JointType pinkyType = isLeft ? HumanPoseData.JointType.L_Pinky : HumanPoseData.JointType.R_Pinky;
        HumanPoseData.JointType indexType = isLeft ? HumanPoseData.JointType.L_Index : HumanPoseData.JointType.R_Index;

        Vector3 wrist = rawPose.GetJoint(wristType);
        Vector3 pinky = rawPose.GetJoint(pinkyType);
        Vector3 index = rawPose.GetJoint(indexType);

        // 手掌內兩個向量
        Vector3 wristToPinky = pinky - wrist;
        Vector3 wristToIndex = index - wrist;

        if (wristToPinky.sqrMagnitude < Epsilon || wristToIndex.sqrMagnitude < Epsilon)
            return;

        // 手掌法線 = cross(wrist→index, wrist→pinky)
        // 對左手：法線朝手掌正面（掌心方向）
        // 對右手：法線朝手背方向，需要反轉
        Vector3 palmNormal = Vector3.Cross(wristToIndex.normalized, wristToPinky.normalized);
        if (palmNormal.sqrMagnitude < Epsilon)
            return;

        palmNormal = palmNormal.normalized;

        // 時序連續性：若 palmNormal 與前幀 up 夾角 > 90°，翻轉
        if (Vector3.Dot(palmNormal, output.Up) < 0f)
            palmNormal = -palmNormal;

        Vector3 boneAxis = output.Forward;

        // palmNormal 投影到骨軸的垂直平面上，作為 up 方向
        Vector3 up = Vector3.ProjectOnPlane(palmNormal, boneAxis);
        if (up.sqrMagnitude < Epsilon)
            return;

        up = up.normalized;
        Vector3 right = SafeNormalize(Vector3.Cross(up, boneAxis), output.Right);
        up = SafeNormalize(Vector3.Cross(boneAxis, right), up);

        BodyPartFrame prev = CloneFrame(output);
        output.Right = right;
        output.Up = up;
        output.Rotation = EnsureQuaternionContinuity(prev.Rotation, FrameToQuaternion(right, up, boneAxis));
        output.HasReliableTwist = true;
    }

    /// <summary>
    /// 小腿 twist 精煉：利用腳部方向（ankle + heel + foot_index）。
    ///
    /// 原理：ankle、heel、foot_index 三點定義腳底平面，
    /// 該平面的法線代表腳底朝向（脛骨的內/外旋）。
    /// foot_index（腳尖）的方向也代表腳趾朝向。
    /// </summary>
    private void RefineLowerLegTwist(HumanPoseData rawPose, BodyPartFrame output, bool isLeft)
    {
        HumanPoseData.JointType ankleType = isLeft ? HumanPoseData.JointType.L_Ankle : HumanPoseData.JointType.R_Ankle;
        HumanPoseData.JointType heelType = isLeft ? HumanPoseData.JointType.L_Heel : HumanPoseData.JointType.R_Heel;
        HumanPoseData.JointType footIdxType = isLeft ? HumanPoseData.JointType.L_Foot_Index : HumanPoseData.JointType.R_Foot_Index;

        Vector3 ankle = rawPose.GetJoint(ankleType);
        Vector3 heel = rawPose.GetJoint(heelType);
        Vector3 footIdx = rawPose.GetJoint(footIdxType);

        // 腳的前向：heel → foot_index
        Vector3 footForward = footIdx - heel;
        if (footForward.sqrMagnitude < Epsilon)
            return;

        footForward = footForward.normalized;

        Vector3 boneAxis = output.Forward; // knee→ankle direction

        // 腳前向投影到骨軸的垂直平面上
        // 這代表「腳尖朝向」在脛骨截面上的分量 → 脛骨的 twist
        Vector3 footFwdProj = Vector3.ProjectOnPlane(footForward, boneAxis);
        if (footFwdProj.sqrMagnitude < Epsilon)
            return;

        // 腳前向（投影後）作為 frame 的 forward 參考
        // 但我們需要的是垂直於骨軸的 up/right
        // 腳底法線 = cross(heel→foot_index, heel→ankle) → 腳底朝上
        Vector3 heelToAnkle = ankle - heel;
        Vector3 footUp = Vector3.Cross(footForward, heelToAnkle.normalized);
        if (footUp.sqrMagnitude < Epsilon)
            return;

        footUp = footUp.normalized;

        // 時序連續性：若 footUp 與前幀 up 夾角 > 90°，翻轉
        if (Vector3.Dot(footUp, output.Up) < 0f)
            footUp = -footUp;

        // 投影到骨軸垂直平面
        Vector3 up = Vector3.ProjectOnPlane(footUp, boneAxis);
        if (up.sqrMagnitude < Epsilon)
            return;

        up = up.normalized;
        Vector3 right = SafeNormalize(Vector3.Cross(up, boneAxis), output.Right);
        up = SafeNormalize(Vector3.Cross(boneAxis, right), up);

        BodyPartFrame prev = CloneFrame(output);
        output.Right = right;
        output.Up = up;
        output.Rotation = EnsureQuaternionContinuity(prev.Rotation, FrameToQuaternion(right, up, boneAxis));
        output.HasReliableTwist = true;
    }

    /// <summary>
    /// 從三個軸向量轉換成 Quaternion
    /// right, up, forward 需為正交向量
    /// </summary>
    private Quaternion FrameToQuaternion(Vector3 right, Vector3 up, Vector3 forward)
    {
        if (forward.sqrMagnitude < Epsilon || up.sqrMagnitude < Epsilon)
            return Quaternion.identity;

        return Quaternion.LookRotation(forward.normalized, up.normalized);
    }

    /// <summary>
    /// 正規化向量，若長度太小則退回 fallback。
    /// </summary>
    private Vector3 SafeNormalize(Vector3 value, Vector3 fallback)
    {
        if (value.sqrMagnitude > Epsilon)
            return value.normalized;
        if (fallback.sqrMagnitude > Epsilon)
            return fallback.normalized;
        return Vector3.forward;
    }

    /// <summary>
    /// 將參考向量投影到與骨軸垂直平面，降低退化與跳變。
    /// </summary>
    private Vector3 ComputeProjectedReference(Vector3 primary, Vector3 boneAxis, Vector3 fallbackA, Vector3 fallbackB, Vector3 fallbackC)
    {
        Vector3 projected = Vector3.ProjectOnPlane(primary, boneAxis);
        if (projected.sqrMagnitude < Epsilon)
            projected = Vector3.ProjectOnPlane(fallbackA, boneAxis);
        if (projected.sqrMagnitude < Epsilon)
            projected = Vector3.ProjectOnPlane(fallbackB, boneAxis);
        if (projected.sqrMagnitude < Epsilon)
            projected = Vector3.ProjectOnPlane(fallbackC, boneAxis);

        return SafeNormalize(projected, fallbackA);
    }

    /// <summary>
    /// 左右肢體鏡像規則，維持左右 frame 符號一致。
    /// </summary>
    private void ApplyLeftRightMirrorRule(BodyPartFrame referenceFrame, bool isLeft, ref Vector3 rightAxis, ref Vector3 upAxis)
    {
        Vector3 lateralHint = isLeft ? -referenceFrame.Right : referenceFrame.Right;
        if (Vector3.Dot(rightAxis, lateralHint) < 0f)
        {
            rightAxis = -rightAxis;
            upAxis = -upAxis;
        }
    }

    /// <summary>
    /// 若新幀和上一幀方向相反，翻轉軸向保持連續性。
    /// 翻轉時必須成對翻轉兩個軸，確保座標系手性 (handedness) 不變。
    /// </summary>
    private void EnsureAxisContinuity(BodyPartFrame previous, ref Vector3 right, ref Vector3 up, ref Vector3 forward)
    {
        if (Vector3.Dot(previous.Forward, forward) < 0f)
        {
            forward = -forward;
            right = -right;
        }

        if (Vector3.Dot(previous.Up, up) < 0f)
        {
            up = -up;
            right = -right;  // 同時翻轉 right 以維持手性
        }
    }

    /// <summary>
    /// q 與 -q 代表同姿態，這裡統一符號避免濾波抖動。
    /// </summary>
    private Quaternion EnsureQuaternionContinuity(Quaternion previous, Quaternion current)
    {
        if (Quaternion.Dot(previous, current) < 0f)
            return new Quaternion(-current.x, -current.y, -current.z, -current.w);
        return current;
    }

    /// <summary>
    /// 複製 frame，避免在更新途中讀寫同一物件造成污染。
    /// </summary>
    private BodyPartFrame CloneFrame(BodyPartFrame source)
    {
        return new BodyPartFrame
        {
            Position = source.Position,
            Rotation = source.Rotation,
            Confidence = source.Confidence,
            Forward = source.Forward,
            Right = source.Right,
            Up = source.Up,
            HasReliableTwist = source.HasReliableTwist
        };
    }
}
