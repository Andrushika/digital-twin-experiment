using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Module A 級別的資料結構
/// 儲存 raw 3D joints (來自 Python/Mocap) 與時間戳
/// 
/// 支援 MediaPipe 33 點格式
/// </summary>
public class HumanPoseData
{
    // ===== MediaPipe 33 點身體關節 =====
    // https://developers.google.com/mediapipe/solutions/vision/pose_landmarker
    public enum JointType
    {
        // 頭部與臉部 (0-9)
        Nose = 0,
        L_Eye_Inner = 1,
        L_Eye = 2,
        L_Eye_Outer = 3,
        R_Eye_Inner = 4,
        R_Eye = 5,
        R_Eye_Outer = 6,
        L_Ear = 7,
        R_Ear = 8,
        Mouth_Left = 9,
        Mouth_Right = 10,

        // 上身 (11-20)
        L_Shoulder = 11,
        R_Shoulder = 12,
        L_Elbow = 13,
        R_Elbow = 14,
        L_Wrist = 15,
        R_Wrist = 16,
        L_Pinky = 17,
        R_Pinky = 18,
        L_Index = 19,
        R_Index = 20,

        // 腰 (21-22)
        L_Hip = 23,
        R_Hip = 24,

        // 左腿 (25-27)
        L_Knee = 25,
        L_Ankle = 27,
        L_Heel = 29,

        // 右腿 (28-30)
        R_Knee = 26,
        R_Ankle = 28,
        R_Heel = 30,

        // 腳尖 (31-32)
        L_Foot_Index = 31,
        R_Foot_Index = 32
    }

    // 當前幀數據
    public int FrameIndex { get; set; }
    public double Timestamp { get; set; }  // 秒數或毫秒
    
    // 33 個關節在 3D 空間的座標
    public Vector3[] Joints { get; private set; }
    
    // 每個關節的信心度 (0-1，mocap 的品質指標)
    public float[] Confidence { get; private set; }
    
    // 是否為有效幀
    public bool IsValid { get; set; }

    public HumanPoseData()
    {
        Joints = new Vector3[33];
        Confidence = new float[33];
        IsValid = true;
        FrameIndex = 0;
        Timestamp = 0;
    }

    /// <summary>
    /// 從 CSV 或原生陣列填充 joints
    /// 預期格式: x1,y1,z1,c1, x2,y2,z2,c2, ..., x33,y33,z33,c33 (132 個浮點數含信心度)
    /// 或無信心度: x1,y1,z1,x2,y2,z2,...,x33,y33,z33 (99 個浮點數)
    /// </summary>
    public void SetJointsFromArray(float[] data)
    {
        // MediaPipe 格式: 每個點有 x, y, z, 可能有 confidence (4 個值)
        // 或只有 x, y, z (3 個值)
        
        if (data.Length >= 132)
        {
            // 有信心度的格式 (33 * 4)
            for (int i = 0; i < 33; i++)
            {
                Joints[i] = new Vector3(data[i * 4], data[i * 4 + 1], data[i * 4 + 2]);
                Confidence[i] = data[i * 4 + 3];
            }
        }
        else if (data.Length >= 99)
        {
            // 無信心度的格式 (33 * 3)
            for (int i = 0; i < 33; i++)
            {
                Joints[i] = new Vector3(data[i * 3], data[i * 3 + 1], data[i * 3 + 2]);
                Confidence[i] = 1f;
            }
        }
        else
        {
            Debug.LogError($"[HumanPoseData] Expected at least 99 floats (33*3), got {data.Length}");
            IsValid = false;
            return;
        }

        IsValid = true;
    }

    /// <summary>
    /// 設定特定關節
    /// </summary>
    public void SetJoint(JointType joint, Vector3 position, float confidence = 1f)
    {
        Joints[(int)joint] = position;
        Confidence[(int)joint] = confidence;
    }

    /// <summary>
    /// 取得特定關節
    /// </summary>
    public Vector3 GetJoint(JointType joint)
    {
        return Joints[(int)joint];
    }

    /// <summary>
    /// 內部座標系轉換 (mocap 可能是 YXZ 或其他軸)
    /// </summary>
    public void ApplyAxisMapping(Vector3 scale, bool flipX = false, bool flipY = false, bool flipZ = false)
    {
        for (int i = 0; i < 33; i++)
        {
            Vector3 j = Joints[i];
            j.x *= (flipX ? -1 : 1) * scale.x;
            j.y *= (flipY ? -1 : 1) * scale.y;
            j.z *= (flipZ ? -1 : 1) * scale.z;
            Joints[i] = j;
        }
    }
}
