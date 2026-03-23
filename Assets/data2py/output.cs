using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using LitJson;
using System.IO;
using System.Text;

public class output : MonoBehaviour
{
    static Animator animation;

    // torso
    public static Transform Head;
    public static Transform Neck;
    public static Transform Spine;
	public static Transform Hip;

    // Left hand
    public static Transform L_Shoulder;
    public static Transform L_Elbow;
    public static Transform L_Hand;
    public static Transform L_Thumb_Proximal;
    public static Transform L_Thumb_Intermediate;
    public static Transform L_Thumb_Distal;
    public static Transform L_Index_Proximal;
    public static Transform L_Index_Intermediate;
    public static Transform L_Index_Distal;
    public static Transform L_Middle_Proximal;
    public static Transform L_Middle_Intermediate;
    public static Transform L_Middle_Distal;
    public static Transform L_Ring_Proximal;
    public static Transform L_Ring_Intermediate;
    public static Transform L_Ring_Distal;
    public static Transform L_Little_Proximal;
    public static Transform L_Little_Intermediate;
    public static Transform L_Little_Distal;

    // Left leg
    public static Transform L_Hip;
    public static Transform L_Knee;
    public static Transform L_Foot;
    public static Transform L_Toe;

    // Right hand
    public static Transform R_Shoulder;
    public static Transform R_Elbow;
    public static Transform R_Hand;
    public static Transform R_Thumb_Proximal;
    public static Transform R_Thumb_Intermediate;
    public static Transform R_Thumb_Distal;
    public static Transform R_Index_Proximal;
    public static Transform R_Index_Intermediate;
    public static Transform R_Index_Distal;
    public static Transform R_Middle_Proximal;
    public static Transform R_Middle_Intermediate;
    public static Transform R_Middle_Distal;
    public static Transform R_Ring_Proximal;
    public static Transform R_Ring_Intermediate;
    public static Transform R_Ring_Distal;
    public static Transform R_Little_Proximal;
    public static Transform R_Little_Intermediate;
    public static Transform R_Little_Distal;

    // Right leg
    public static Transform R_Hip;
    public static Transform R_Knee;
    public static Transform R_Foot;
    public static Transform R_Toe;

    int count = 0 ;  // file number
    int animator_n = 0;
    int i = 0;
    int k = 0;
    
    void Start()
    {
        animation = gameObject.GetComponent<Animator>();
        
        Head = animation.GetBoneTransform(HumanBodyBones.Head);
        Neck = animation.GetBoneTransform(HumanBodyBones.Neck);
        Spine = animation.GetBoneTransform(HumanBodyBones.Spine);
		Hip = animation.GetBoneTransform(HumanBodyBones.Hips);

        L_Shoulder = animation.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        L_Elbow = animation.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        L_Hand = animation.GetBoneTransform(HumanBodyBones.LeftHand);
        L_Toe = animation.GetBoneTransform(HumanBodyBones.LeftToes);
        
        L_Thumb_Proximal = animation.GetBoneTransform(HumanBodyBones.LeftThumbProximal);
        L_Thumb_Intermediate = animation.GetBoneTransform(HumanBodyBones.LeftThumbIntermediate);
        L_Thumb_Distal = animation.GetBoneTransform(HumanBodyBones.LeftThumbDistal);
        L_Index_Proximal = animation.GetBoneTransform(HumanBodyBones.LeftIndexProximal);
        L_Index_Intermediate = animation.GetBoneTransform(HumanBodyBones.LeftIndexIntermediate);
        L_Index_Distal = animation.GetBoneTransform(HumanBodyBones.LeftIndexDistal);
        L_Middle_Proximal = animation.GetBoneTransform(HumanBodyBones.LeftMiddleProximal);
        L_Middle_Intermediate = animation.GetBoneTransform(HumanBodyBones.LeftMiddleIntermediate);
        L_Middle_Distal = animation.GetBoneTransform(HumanBodyBones.LeftMiddleDistal);
        L_Ring_Proximal = animation.GetBoneTransform(HumanBodyBones.LeftRingProximal);
        L_Ring_Intermediate = animation.GetBoneTransform(HumanBodyBones.LeftRingIntermediate);
        L_Ring_Distal = animation.GetBoneTransform(HumanBodyBones.LeftRingDistal);
        L_Little_Proximal = animation.GetBoneTransform(HumanBodyBones.LeftLittleProximal);
        L_Little_Intermediate = animation.GetBoneTransform(HumanBodyBones.LeftLittleIntermediate);
        L_Little_Distal = animation.GetBoneTransform(HumanBodyBones.LeftLittleDistal);

        L_Hip = animation.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
        L_Knee = animation.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
        L_Foot = animation.GetBoneTransform(HumanBodyBones.LeftFoot);

        R_Shoulder = animation.GetBoneTransform(HumanBodyBones.RightUpperArm);
        R_Elbow = animation.GetBoneTransform(HumanBodyBones.RightLowerArm);
        R_Hand = animation.GetBoneTransform(HumanBodyBones.RightHand);
        R_Toe = animation.GetBoneTransform(HumanBodyBones.RightToes);

        R_Thumb_Proximal = animation.GetBoneTransform(HumanBodyBones.RightThumbProximal);
        R_Thumb_Intermediate = animation.GetBoneTransform(HumanBodyBones.RightThumbIntermediate);
        R_Thumb_Distal = animation.GetBoneTransform(HumanBodyBones.RightThumbDistal);
        R_Index_Proximal = animation.GetBoneTransform(HumanBodyBones.RightIndexProximal);
        R_Index_Intermediate = animation.GetBoneTransform(HumanBodyBones.RightIndexIntermediate);
        R_Index_Distal = animation.GetBoneTransform(HumanBodyBones.RightIndexDistal);
        R_Middle_Proximal = animation.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
        R_Middle_Intermediate = animation.GetBoneTransform(HumanBodyBones.RightMiddleIntermediate);
        R_Middle_Distal = animation.GetBoneTransform(HumanBodyBones.RightMiddleDistal);
        R_Ring_Proximal = animation.GetBoneTransform(HumanBodyBones.RightRingProximal);
        R_Ring_Intermediate = animation.GetBoneTransform(HumanBodyBones.RightRingIntermediate);
        R_Ring_Distal = animation.GetBoneTransform(HumanBodyBones.RightRingDistal);
        R_Little_Proximal = animation.GetBoneTransform(HumanBodyBones.RightLittleProximal);
        R_Little_Intermediate = animation.GetBoneTransform(HumanBodyBones.RightLittleIntermediate);
        R_Little_Distal = animation.GetBoneTransform(HumanBodyBones.RightLittleDistal);

        R_Hip = animation.GetBoneTransform(HumanBodyBones.RightUpperLeg);
        R_Knee = animation.GetBoneTransform(HumanBodyBones.RightLowerLeg);
        R_Foot = animation.GetBoneTransform(HumanBodyBones.RightFoot);
    }

    private string writeJson(string[] key, string[] value)  // 寫入json檔案
    {
        if (key.Length != value.Length)
        {
            throw new System.Exception("key.Length != value.Length");
        }

        StringBuilder sb = new StringBuilder();
        JsonWriter jw = new JsonWriter(sb);//資料將會寫在sb內

        jw.WriteObjectStart();  // 寫入"{"到sb內
        for (int i = 0; i < key.Length; i++)
        {
            jw.WritePropertyName(key[i]);
            jw.Write(value[i]);
        }
        jw.WriteObjectEnd();  // 寫入"}"到sb內

        return sb.ToString();
    }

    // Update is called once per frame
    void FixedUpdate()
    {
        if (animation.GetCurrentAnimatorStateInfo(0).IsName("final"))
        {
            Debug.Log("it's end now");
        }
        else
        {
            string j = i.ToString();
            if (animation.GetCurrentAnimatorStateInfo(0).IsName("T-Pose "+j))
            {
                if (k == 0)
                {
                    animator_n++;
                    k++;
                }
            }
            else
            {
                if(k!=0)
                {
                    i++;
                    count = 0;
                }
                k = 0;
                string root_path = @"Assets/data2py/fbx_data/";  //  資料夾位置
                string file_path = root_path + "Violin_" + animator_n.ToString()+ ".json";  // 寫入資料夾
                
                string[] body = new string[] { 
                    "head.x", "head.y", "head.z",
                    "neck.x", "neck.y", "neck.z",
                    "R_shoulder.x", "R_shoulder.y", "R_shoulder.z",
                    "R_Elbow.x", "R_Elbow.y", "R_Elbow.z",
                    "R_hand.x", "R_hand.y", "R_hand.z",
                    "L_shoulder.x", "L_shoulder.y", "L_shoulder.z",
                    "L_Elbow.x", "L_Elbow.y", "L_Elbow.z",
                    "L_hand.x", "L_hand.y", "L_hand.z",
                    "hip.x", "hip.y", "hip.z",
                    "R_hip.x", "R_hip.y", "R_hip.z",
                    "R_knee.x", "R_knee.y", "R_knee.z",
                    "R_foot.x", "R_foot.y", "R_foot.z",
                    "L_hip.x", "L_hip.y", "L_hip.z",
                    "L_knee.x", "L_knee.y", "L_knee.z",
                    "L_foot.x", "L_foot.y", "L_foot.z",
                    "LeftToeBase.x", "LeftToeBase.y", "LeftToeBase.z",
                    "RightToeBase.x", "RightToeBase.y", "RightToeBase.z",
                    "LeftHandThumb1.x", "LeftHandThumb1.y", "LeftHandThumb1.z",
                    "LeftHandThumb2.x", "LeftHandThumb2.y", "LeftHandThumb2.z",
                    "LeftHandThumb3.x", "LeftHandThumb3.y", "LeftHandThumb3.z",
                    "LeftHandIndex1.x", "LeftHandIndex1.y", "LeftHandIndex1.z",
                    "LeftHandIndex2.x", "LeftHandIndex2.y", "LeftHandIndex2.z",
                    "LeftHandIndex3.x", "LeftHandIndex3.y", "LeftHandIndex3.z",
                    "LeftHandMiddle1.x", "LeftHandMiddle1.y", "LeftHandMiddle1.z",
                    "LeftHandMiddle2.x", "LeftHandMiddle2.y", "LeftHandMiddle2.z",
                    "LeftHandMiddle3.x", "LeftHandMiddle3.y", "LeftHandMiddle3.z",
                    "LeftHandRing1.x", "LeftHandRing1.y", "LeftHandRing1.z",
                    "LeftHandRing2.x", "LeftHandRing2.y", "LeftHandRing2.z",
                    "LeftHandRing3.x", "LeftHandRing3.y", "LeftHandRing3.z",
                    "LeftHandPinky1.x", "LeftHandPinky1.y", "LeftHandPinky1.z",
                    "LeftHandPinky2.x", "LeftHandPinky2.y", "LeftHandPinky2.z",
                    "LeftHandPinky3.x", "LeftHandPinky3.y", "LeftHandPinky3.z",
                    "RightHandThumb1.x", "RightHandThumb1.y", "RightHandThumb1.z",
                    "RightHandThumb2.x", "RightHandThumb2.y", "RightHandThumb2.z",
                    "RightHandThumb3.x", "RightHandThumb3.y", "RightHandThumb3.z",
                    "RightHandIndex1.x", "RightHandIndex1.y", "RightHandIndex1.z",
                    "RightHandIndex2.x", "RightHandIndex2.y", "RightHandIndex2.z",
                    "RightHandIndex3.x", "RightHandIndex3.y", "RightHandIndex3.z",
                    "RightHandMiddle1.x", "RightHandMiddle1.y", "RightHandMiddle1.z",
                    "RightHandMiddle2.x", "RightHandMiddle2.y", "RightHandMiddle2.z",
                    "RightHandMiddle3.x", "RightHandMiddle3.y", "RightHandMiddle3.z",
                    "RightHandRing1.x", "RightHandRing1.y", "RightHandRing1.z",
                    "RightHandRing2.x", "RightHandRing2.y", "RightHandRing2.z",
                    "RightHandRing3.x", "RightHandRing3.y", "RightHandRing3.z",
                    "RightHandPinky1.x", "RightHandPinky1.y", "RightHandPinky1.z",
                    "RightHandPinky2.x", "RightHandPinky2.y", "RightHandPinky2.z",
                    "RightHandPinky3.x", "RightHandPinky3.y", "RightHandPinky3.z"
                };


                string[] body_coord = new string[] {
                    Head.position.x.ToString(), Head.position.y.ToString(), Head.position.z.ToString(),
                    Neck.position.x.ToString(), Neck.position.y.ToString(), Neck.position.z.ToString(),
                    R_Shoulder.position.x.ToString(), R_Shoulder.position.y.ToString(), R_Shoulder.position.z.ToString(),
                    R_Elbow.position.x.ToString(), R_Elbow.position.y.ToString(), R_Elbow.position.z.ToString(),
                    R_Hand.position.x.ToString(), R_Hand.position.y.ToString(), R_Hand.position.z.ToString(),
                    L_Shoulder.position.x.ToString(), L_Shoulder.position.y.ToString(), L_Shoulder.position.z.ToString(),
                    L_Elbow.position.x.ToString(), L_Elbow.position.y.ToString(), L_Elbow.position.z.ToString(),
                    L_Hand.position.x.ToString(), L_Hand.position.y.ToString(), L_Hand.position.z.ToString(),
                    Hip.position.x.ToString(), Hip.position.y.ToString(), Hip.position.z.ToString(),
                    R_Hip.position.x.ToString(), R_Hip.position.y.ToString(), R_Hip.position.z.ToString(),
                    R_Knee.position.x.ToString(), R_Knee.position.y.ToString(), R_Knee.position.z.ToString(),
                    R_Foot.position.x.ToString(), R_Foot.position.y.ToString(), R_Foot.position.z.ToString(),
                    L_Hip.position.x.ToString(), L_Hip.position.y.ToString(), L_Hip.position.z.ToString(),
                    L_Knee.position.x.ToString(), L_Knee.position.y.ToString(), L_Knee.position.z.ToString(),
                    L_Foot.position.x.ToString(), L_Foot.position.y.ToString(), L_Foot.position.z.ToString(),
                    L_Toe.position.x.ToString(), L_Toe.position.y.ToString(), L_Toe.position.z.ToString(),
                    R_Toe.position.x.ToString(), R_Toe.position.y.ToString(), R_Toe.position.z.ToString(),
                    L_Thumb_Proximal.position.x.ToString(), L_Thumb_Proximal.position.y.ToString(), L_Thumb_Proximal.position.z.ToString(),
                    L_Thumb_Intermediate.position.x.ToString(), L_Thumb_Intermediate.position.y.ToString(), L_Thumb_Intermediate.position.z.ToString(),
                    L_Thumb_Distal.position.x.ToString(), L_Thumb_Distal.position.y.ToString(), L_Thumb_Distal.position.z.ToString(),
                    L_Index_Proximal.position.x.ToString(), L_Index_Proximal.position.y.ToString(), L_Index_Proximal.position.z.ToString(),
                    L_Index_Intermediate.position.x.ToString(), L_Index_Intermediate.position.y.ToString(), L_Index_Intermediate.position.z.ToString(),
                    L_Index_Distal.position.x.ToString(), L_Index_Distal.position.y.ToString(), L_Index_Distal.position.z.ToString(),
                    L_Middle_Proximal.position.x.ToString(), L_Middle_Proximal.position.y.ToString(), L_Middle_Proximal.position.z.ToString(),
                    L_Middle_Intermediate.position.x.ToString(), L_Middle_Intermediate.position.y.ToString(), L_Middle_Intermediate.position.z.ToString(),
                    L_Middle_Distal.position.x.ToString(), L_Middle_Distal.position.y.ToString(), L_Middle_Distal.position.z.ToString(),
                    L_Ring_Proximal.position.x.ToString(), L_Ring_Proximal.position.y.ToString(), L_Ring_Proximal.position.z.ToString(),
                    L_Ring_Intermediate.position.x.ToString(), L_Ring_Intermediate.position.y.ToString(), L_Ring_Intermediate.position.z.ToString(),
                    L_Ring_Distal.position.x.ToString(), L_Ring_Distal.position.y.ToString(), L_Ring_Distal.position.z.ToString(),
                    L_Little_Proximal.position.x.ToString(), L_Little_Proximal.position.y.ToString(), L_Little_Proximal.position.z.ToString(),
                    L_Little_Intermediate.position.x.ToString(), L_Little_Intermediate.position.y.ToString(), L_Little_Intermediate.position.z.ToString(),
                    L_Little_Distal.position.x.ToString(), L_Little_Distal.position.y.ToString(), L_Little_Distal.position.z.ToString(),
                    R_Thumb_Proximal.position.x.ToString(), R_Thumb_Proximal.position.y.ToString(), R_Thumb_Proximal.position.z.ToString(),
                    R_Thumb_Intermediate.position.x.ToString(), R_Thumb_Intermediate.position.y.ToString(), R_Thumb_Intermediate.position.z.ToString(),
                    R_Thumb_Distal.position.x.ToString(), R_Thumb_Distal.position.y.ToString(), R_Thumb_Distal.position.z.ToString(),
                    R_Index_Proximal.position.x.ToString(), R_Index_Proximal.position.y.ToString(), R_Index_Proximal.position.z.ToString(),
                    R_Index_Intermediate.position.x.ToString(), R_Index_Intermediate.position.y.ToString(), R_Index_Intermediate.position.z.ToString(),
                    R_Index_Distal.position.x.ToString(), R_Index_Distal.position.y.ToString(), R_Index_Distal.position.z.ToString(),
                    R_Middle_Proximal.position.x.ToString(), R_Middle_Proximal.position.y.ToString(), R_Middle_Proximal.position.z.ToString(),
                    R_Middle_Intermediate.position.x.ToString(), R_Middle_Intermediate.position.y.ToString(), R_Middle_Intermediate.position.z.ToString(),
                    R_Middle_Distal.position.x.ToString(), R_Middle_Distal.position.y.ToString(), R_Middle_Distal.position.z.ToString(),
                    R_Ring_Proximal.position.x.ToString(), R_Ring_Proximal.position.y.ToString(), R_Ring_Proximal.position.z.ToString(),
                    R_Ring_Intermediate.position.x.ToString(), R_Ring_Intermediate.position.y.ToString(), R_Ring_Intermediate.position.z.ToString(),
                    R_Ring_Distal.position.x.ToString(), R_Ring_Distal.position.y.ToString(), R_Ring_Distal.position.z.ToString(),
                    R_Little_Proximal.position.x.ToString(), R_Little_Proximal.position.y.ToString(), R_Little_Proximal.position.z.ToString(),
                    R_Little_Intermediate.position.x.ToString(), R_Little_Intermediate.position.y.ToString(), R_Little_Intermediate.position.z.ToString(),
                    R_Little_Distal.position.x.ToString(), R_Little_Distal.position.y.ToString(), R_Little_Distal.position.z.ToString()
                };

                string sb = writeJson(body, body_coord);
                using (StreamWriter sw = new StreamWriter(file_path, true))  // 將資料寫入，若原先已有資料則寫在原先資料的後面
                {
                    sw.WriteLine(sb);  // 將資料寫入
                }
                count = count + 1;
            }
        }
    }
}