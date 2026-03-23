using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Phase 1 Debug Visualizer
/// 
/// 視覺化檢查
/// 1. 原始 joints 位置 (紅點)
/// 2. 解釋後的人體部位位置 (綠點)
/// 3. 骨骼連線 (黃線)
/// 4. 人體座標系 (RGB 軸)
/// </summary>
public class Phase1_DebugVisualizer : MonoBehaviour
{
    [SerializeField] private DataReceiver dataReceiver;
    [SerializeField] private bool showRawJoints = true;
    [SerializeField] private bool showInterpretedPose = true;
    [SerializeField] private bool showSkeletonLines = true;
    [SerializeField] private bool showCoordinateSystems = false;

    [SerializeField] private float rawJointSize = 0.02f;
    [SerializeField] private float interpretedJointSize = 0.03f;
    [SerializeField] private float lineWidth = 0.01f;

    private PoseInterpreter poseInterpreter = new PoseInterpreter();
    private PoseInterpreter.InterpretedPose lastInterpretedPose;

    private void Start()
    {
        if (dataReceiver == null)
            dataReceiver = GetComponent<DataReceiver>();
    }

    private void Update()
    {
        HumanPoseData latestFrame = dataReceiver.GetLatestFrame();
        if (latestFrame != null && latestFrame.IsValid)
        {
            lastInterpretedPose = poseInterpreter.Interpret(latestFrame);
        }
    }

    private void OnDrawGizmos()
    {
        if (!enabled || dataReceiver == null)
            return;

        HumanPoseData latestFrame = dataReceiver.GetLatestFrame();
        if (latestFrame == null)
            return;

        // Phase 1a: 畫原始 joints
        if (showRawJoints)
        {
            DrawRawJoints(latestFrame);
        }

        // Phase 1b: 畫解釋後的姿態
        if (showInterpretedPose && lastInterpretedPose != null)
        {
            DrawInterpretedPose(lastInterpretedPose);
        }

        // Phase 1c: 畫骨骼連線
        if (showSkeletonLines && latestFrame != null)
        {
            DrawSkeletonLines(latestFrame);
        }
    }

    /// <summary>
    /// 畫原始 joints (紅色小球)
    /// 會依照資料長度繪製，目前 MediaPipe 為 33 點
    /// </summary>
    private void DrawRawJoints(HumanPoseData pose)
    {
        Gizmos.color = Color.red;
        for (int i = 0; i < pose.Joints.Length; i++)
        {
            Vector3 pos = pose.Joints[i];
            Gizmos.DrawWireSphere(pos, rawJointSize);
        }
    }

    /// <summary>
    /// 畫解釋後的人體部位 (綠色大球)
    /// </summary>
    private void DrawInterpretedPose(PoseInterpreter.InterpretedPose pose)
    {
        if (pose == null)
            return;

        Gizmos.color = Color.green;

        // Torso
        DrawBodyPartFrame(pose.Pelvis, interpretedJointSize);
        DrawBodyPartFrame(pose.Chest, interpretedJointSize);
        DrawBodyPartFrame(pose.Head, interpretedJointSize);

        // Limbs
        DrawBodyPartFrame(pose.L_UpperArm, interpretedJointSize);
        DrawBodyPartFrame(pose.L_LowerArm, interpretedJointSize);
        DrawBodyPartFrame(pose.R_UpperArm, interpretedJointSize);
        DrawBodyPartFrame(pose.R_LowerArm, interpretedJointSize);

        DrawBodyPartFrame(pose.L_UpperLeg, interpretedJointSize);
        DrawBodyPartFrame(pose.L_LowerLeg, interpretedJointSize);
        DrawBodyPartFrame(pose.R_UpperLeg, interpretedJointSize);
        DrawBodyPartFrame(pose.R_LowerLeg, interpretedJointSize);
    }

    /// <summary>
    /// 畫人體部位的座標框
    /// </summary>
    private void DrawBodyPartFrame(PoseInterpreter.BodyPartFrame frame, float size)
    {
        // 中心點
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(frame.Position, size);

        // 坐標軸
        if (showCoordinateSystems)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(frame.Position, frame.Position + frame.Right * 0.05f);

            Gizmos.color = Color.green;
            Gizmos.DrawLine(frame.Position, frame.Position + frame.Up * 0.05f);

            Gizmos.color = Color.blue;
            Gizmos.DrawLine(frame.Position, frame.Position + frame.Forward * 0.05f);
        }
    }

    /// <summary>
    /// 畫骨骼連線 (標準的骨架拓撲)
    /// </summary>
    private void DrawSkeletonLines(HumanPoseData pose)
    {
        Gizmos.color = Color.yellow;

        // 軀幹
        Vector3 l_hip = pose.GetJoint(HumanPoseData.JointType.L_Hip);
        Vector3 r_hip = pose.GetJoint(HumanPoseData.JointType.R_Hip);
        Vector3 l_shoulder = pose.GetJoint(HumanPoseData.JointType.L_Shoulder);
        Vector3 r_shoulder = pose.GetJoint(HumanPoseData.JointType.R_Shoulder);
        Vector3 nose = pose.GetJoint(HumanPoseData.JointType.Nose);

        Gizmos.DrawLine(l_hip, r_hip);                               // 髖部連線
        Gizmos.DrawLine(l_shoulder, r_shoulder);                   // 肩膀連線
        Gizmos.DrawLine((l_hip + r_hip) / 2, (l_shoulder + r_shoulder) / 2);  // 脊椎
        Gizmos.DrawLine((l_shoulder + r_shoulder) / 2, nose);        // 頸部

        // 左臂
        Vector3 l_elbow = pose.GetJoint(HumanPoseData.JointType.L_Elbow);
        Vector3 l_wrist = pose.GetJoint(HumanPoseData.JointType.L_Wrist);
        Gizmos.DrawLine(l_shoulder, l_elbow);
        Gizmos.DrawLine(l_elbow, l_wrist);

        // 右臂
        Vector3 r_elbow = pose.GetJoint(HumanPoseData.JointType.R_Elbow);
        Vector3 r_wrist = pose.GetJoint(HumanPoseData.JointType.R_Wrist);
        Gizmos.DrawLine(r_shoulder, r_elbow);
        Gizmos.DrawLine(r_elbow, r_wrist);

        // 左腿
        Vector3 l_knee = pose.GetJoint(HumanPoseData.JointType.L_Knee);
        Vector3 l_ankle = pose.GetJoint(HumanPoseData.JointType.L_Ankle);
        Gizmos.DrawLine(l_hip, l_knee);
        Gizmos.DrawLine(l_knee, l_ankle);

        // 右腿
        Vector3 r_knee = pose.GetJoint(HumanPoseData.JointType.R_Knee);
        Vector3 r_ankle = pose.GetJoint(HumanPoseData.JointType.R_Ankle);
        Gizmos.DrawLine(r_hip, r_knee);
        Gizmos.DrawLine(r_knee, r_ankle);
    }
}
