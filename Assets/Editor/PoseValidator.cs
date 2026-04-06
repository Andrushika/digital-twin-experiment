using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.IO;
using System.Text;
using System.Collections.Generic;

/// <summary>
/// PoseValidator: Unity Editor validation script.
///
/// Metrics:
///   1. delta_error: 來源骨骼「相對於 rest 的旋轉角度」vs Avatar 骨骼「相對於 rest 的旋轉角度」之差
///      → 正確衡量 retarget 是否精確傳遞動作，不受 rest pose 差異影響
///   2. direction_error: 來源骨骼方向 vs Avatar 骨骼方向的絕對夾角（參考用）
///   3. upright: 頭部是否在髖部之上
///   4. first_frame_diag: 第一幀的來源/Avatar 骨骼方向，幫助診斷 rest pose 差異
/// </summary>
public class PoseValidator
{
    private const string TARGET_SCENE = "Assets/Scenes/SampleScene.unity";
    private const string CSV_PATH = "Assets/Scrpits/new_controller_module/pose_data.csv";
    private const int SAMPLE_INTERVAL = 10;
    private const int MAX_FRAMES = 200;

    private struct BoneCheck
    {
        public string name;
        public HumanPoseData.JointType proximal;
        public HumanPoseData.JointType distal;
        public HumanBodyBones avatarBone;
        public HumanBodyBones avatarChildBone;

        public BoneCheck(string n, HumanPoseData.JointType p, HumanPoseData.JointType d,
                         HumanBodyBones ab, HumanBodyBones acb)
        { name = n; proximal = p; distal = d; avatarBone = ab; avatarChildBone = acb; }
    }

    private static readonly BoneCheck[] BONE_CHECKS = new BoneCheck[]
    {
        new BoneCheck("L_UpperArm", HumanPoseData.JointType.L_Shoulder, HumanPoseData.JointType.L_Elbow,
                      HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm),
        new BoneCheck("L_LowerArm", HumanPoseData.JointType.L_Elbow, HumanPoseData.JointType.L_Wrist,
                      HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand),
        new BoneCheck("R_UpperArm", HumanPoseData.JointType.R_Shoulder, HumanPoseData.JointType.R_Elbow,
                      HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm),
        new BoneCheck("R_LowerArm", HumanPoseData.JointType.R_Elbow, HumanPoseData.JointType.R_Wrist,
                      HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand),
        new BoneCheck("L_UpperLeg", HumanPoseData.JointType.L_Hip, HumanPoseData.JointType.L_Knee,
                      HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg),
        new BoneCheck("L_LowerLeg", HumanPoseData.JointType.L_Knee, HumanPoseData.JointType.L_Ankle,
                      HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot),
        new BoneCheck("R_UpperLeg", HumanPoseData.JointType.R_Hip, HumanPoseData.JointType.R_Knee,
                      HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg),
        new BoneCheck("R_LowerLeg", HumanPoseData.JointType.R_Knee, HumanPoseData.JointType.R_Ankle,
                      HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot),
    };

    [MenuItem("Tools/Run Pose Validation")]
    public static void Run()
    {
        Debug.Log("[PoseValidator] === Starting Validation ===");

        string projectPath = Path.GetDirectoryName(Application.dataPath);
        string reportPath = Path.Combine(projectPath, "validation_report.json");

        string scenePath = FindScene();
        if (scenePath == null) { WriteErrorReport(reportPath, "No suitable scene found"); return; }

        Debug.Log($"[PoseValidator] Opening scene: {scenePath}");
        EditorSceneManager.OpenScene(scenePath);

        Animator animator = FindAvatarAnimator();
        if (animator == null) { WriteErrorReport(reportPath, "No Animator found in scene"); return; }
        Debug.Log($"[PoseValidator] Found avatar: {animator.gameObject.name}");

        GameObject avatarGO = animator.gameObject;
        RetargetSolver retargetSolver = avatarGO.GetComponent<RetargetSolver>();
        if (retargetSolver == null)
            retargetSolver = avatarGO.AddComponent<RetargetSolver>();

        string csvFullPath = FindCSV();
        if (csvFullPath == null) { WriteErrorReport(reportPath, "No CSV data file found"); return; }

        string[] csvLines = File.ReadAllLines(csvFullPath);
        int startLine = DetectHeaderLine(csvLines);
        Debug.Log($"[PoseValidator] CSV: {csvLines.Length - startLine} data lines from {Path.GetFileName(csvFullPath)}");

        // Collect bone transforms
        Dictionary<HumanBodyBones, Transform> boneMap = new Dictionary<HumanBodyBones, Transform>();
        HumanBodyBones[] allBones = {
            HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest,
            HumanBodyBones.Neck, HumanBodyBones.Head,
            HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
            HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot,
            HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot,
        };
        foreach (var b in allBones)
        {
            Transform t = animator.GetBoneTransform(b);
            if (t != null) boneMap[b] = t;
        }

        // ===== Process frames =====
        PoseInterpreter interpreter = new PoseInterpreter();
        bool firstFrame = true;
        int framesProcessed = 0, framesValidated = 0;

        // Rest-frame snapshots (recorded at frame 0)
        Dictionary<string, Vector3> srcRestDirs = new Dictionary<string, Vector3>();
        Dictionary<string, Vector3> avRestDirs = new Dictionary<string, Vector3>();

        // Error accumulators
        Dictionary<string, List<float>> deltaErrors = new Dictionary<string, List<float>>();
        Dictionary<string, List<float>> absErrors = new Dictionary<string, List<float>>();
        Dictionary<string, List<float>> twistJumps = new Dictionary<string, List<float>>();
        foreach (var bc in BONE_CHECKS)
        {
            deltaErrors[bc.name] = new List<float>();
            absErrors[bc.name] = new List<float>();
            twistJumps[bc.name] = new List<float>();
        }

        // Previous frame rotations for twist jump detection
        Dictionary<string, Quaternion> prevBoneRotations = new Dictionary<string, Quaternion>();

        List<string> frameDetails = new List<string>();
        string firstFrameDiag = "";

        // Auto yaw rotation (replicate DataReceiver logic)
        Quaternion yawRotation = Quaternion.identity;
        bool yawDetected = false;

        for (int i = startLine; i < csvLines.Length && framesProcessed < MAX_FRAMES; i++)
        {
            if (string.IsNullOrWhiteSpace(csvLines[i])) continue;

            HumanPoseData poseData = ParseCSVLine(csvLines[i]);
            if (poseData == null || !poseData.IsValid) continue;

            // Coordinate transform: MediaPipe → Unity
            ApplyMediaPipeWorldTransform(poseData);

            // Auto yaw detection on first frame (replicate DataReceiver behavior)
            if (!yawDetected)
            {
                yawRotation = ComputeAutoYaw(poseData);
                yawDetected = true;
            }

            // Apply yaw rotation
            if (yawRotation != Quaternion.identity)
                ApplyYawRotation(poseData, yawRotation);

            // Pipeline: Interpret → Retarget
            PoseInterpreter.InterpretedPose interpreted = interpreter.Interpret(poseData);

            if (firstFrame)
            {
                retargetSolver.SetTargetAvatar(animator);
                firstFrame = false;
            }
            retargetSolver.ApplyPoseImmediate(interpreted);
            framesProcessed++;

            // Record rest directions at frame 1
            if (framesProcessed == 1)
            {
                StringBuilder diag = new StringBuilder();
                diag.Append("    {");
                List<string> diagParts = new List<string>();

                foreach (var bc in BONE_CHECKS)
                {
                    Vector3 srcDir = GetSourceBoneDir(poseData, bc);
                    Vector3 avDir = GetAvatarBoneDir(boneMap, bc);
                    srcRestDirs[bc.name] = srcDir;
                    avRestDirs[bc.name] = avDir;

                    float restAngle = (srcDir.sqrMagnitude > 0 && avDir.sqrMagnitude > 0)
                        ? Vector3.Angle(srcDir, avDir) : -1f;

                    diagParts.Add($"\"{bc.name}\": {{\"src_dir\": \"{V3S(srcDir)}\", \"av_dir\": \"{V3S(avDir)}\", \"rest_diff\": {restAngle:F1}}}");
                }
                diag.Append(string.Join(", ", diagParts));
                diag.Append("}");
                firstFrameDiag = diag.ToString();
            }

            // Sample for validation (skip frame 1 since it's rest)
            if (framesProcessed <= 1 || framesProcessed % SAMPLE_INTERVAL != 0) continue;
            framesValidated++;

            StringBuilder fr = new StringBuilder();
            fr.Append($"    {{\"frame\": {framesProcessed}");

            foreach (var bc in BONE_CHECKS)
            {
                Vector3 srcDir = GetSourceBoneDir(poseData, bc);
                Vector3 avDir = GetAvatarBoneDir(boneMap, bc);

                if (srcDir.sqrMagnitude < 1e-6f || avDir.sqrMagnitude < 1e-6f) continue;

                // Metric 1: absolute direction error (for reference)
                float absAngle = Vector3.Angle(srcDir, avDir);
                absErrors[bc.name].Add(absAngle);

                // Metric 2: delta error — compare rotation-from-rest
                Vector3 srcRestDir = srcRestDirs.ContainsKey(bc.name) ? srcRestDirs[bc.name] : Vector3.zero;
                Vector3 avRestDir = avRestDirs.ContainsKey(bc.name) ? avRestDirs[bc.name] : Vector3.zero;

                float deltaAngle = 0f;
                if (srcRestDir.sqrMagnitude > 1e-6f && avRestDir.sqrMagnitude > 1e-6f)
                {
                    // How much did the source bone rotate from rest?
                    float srcDelta = Vector3.Angle(srcRestDir, srcDir);
                    // How much did the avatar bone rotate from rest?
                    float avDelta = Vector3.Angle(avRestDir, avDir);
                    // The difference tells us if retarget preserved the motion magnitude
                    deltaAngle = Mathf.Abs(srcDelta - avDelta);
                }
                deltaErrors[bc.name].Add(deltaAngle);

                fr.Append($", \"{bc.name}_abs\": {absAngle:F1}, \"{bc.name}_delta\": {deltaAngle:F1}");
            }

            // Twist jump detection: compare bone localRotation between consecutive validated frames
            foreach (var bc in BONE_CHECKS)
            {
                if (!boneMap.ContainsKey(bc.avatarBone)) continue;
                Quaternion currentRot = boneMap[bc.avatarBone].localRotation;

                if (prevBoneRotations.ContainsKey(bc.name))
                {
                    // Compute rotation difference between consecutive frames
                    Quaternion rotDiff = Quaternion.Inverse(prevBoneRotations[bc.name]) * currentRot;
                    float frameDelta;
                    Vector3 axis;
                    rotDiff.ToAngleAxis(out frameDelta, out axis);
                    if (frameDelta > 180f) frameDelta = 360f - frameDelta;

                    // A twist jump > 45° between sampled frames is suspicious
                    if (frameDelta > 45f)
                    {
                        twistJumps[bc.name].Add(frameDelta);
                        fr.Append($", \"{bc.name}_twist_jump\": {frameDelta:F1}");
                    }
                }
                prevBoneRotations[bc.name] = currentRot;
            }

            // Uprightness check
            if (boneMap.ContainsKey(HumanBodyBones.Hips) && boneMap.ContainsKey(HumanBodyBones.Head))
            {
                bool upright = boneMap[HumanBodyBones.Head].position.y > boneMap[HumanBodyBones.Hips].position.y;
                fr.Append($", \"upright\": {(upright ? "true" : "false")}");
            }

            // Torso facing direction check: source vs avatar forward
            {
                Vector3 srcHipCenter = (poseData.GetJoint(HumanPoseData.JointType.L_Hip)
                                      + poseData.GetJoint(HumanPoseData.JointType.R_Hip)) * 0.5f;
                Vector3 srcShoulderCenter = (poseData.GetJoint(HumanPoseData.JointType.L_Shoulder)
                                           + poseData.GetJoint(HumanPoseData.JointType.R_Shoulder)) * 0.5f;
                Vector3 srcUp = (srcShoulderCenter - srcHipCenter).normalized;
                Vector3 srcRight = (poseData.GetJoint(HumanPoseData.JointType.R_Hip)
                                  - poseData.GetJoint(HumanPoseData.JointType.L_Hip)).normalized;
                Vector3 srcFwd = Vector3.Cross(srcRight, srcUp).normalized;

                if (boneMap.ContainsKey(HumanBodyBones.Hips))
                {
                    Vector3 avFwd = boneMap[HumanBodyBones.Hips].forward;
                    float torsoAngle = Vector3.Angle(srcFwd, avFwd);
                    fr.Append($", \"torso_fwd_err\": {torsoAngle:F1}");
                }
            }

            fr.Append("}");
            frameDetails.Add(fr.ToString());
        }

        // ===== Build report =====
        StringBuilder report = new StringBuilder();
        report.AppendLine("{");
        report.AppendLine("  \"status\": \"completed\",");
        report.AppendLine($"  \"scene\": \"{scenePath}\",");
        report.AppendLine($"  \"csv\": \"{Path.GetFileName(csvFullPath)}\",");
        report.AppendLine($"  \"total_csv_lines\": {csvLines.Length - startLine},");
        report.AppendLine($"  \"frames_processed\": {framesProcessed},");
        report.AppendLine($"  \"frames_validated\": {framesValidated},");
        report.AppendLine($"  \"auto_yaw_deg\": {yawRotation.eulerAngles.y:F1},");

        // First frame diagnostic
        report.AppendLine($"  \"first_frame_diag\": {firstFrameDiag},");

        // Delta errors (primary metric)
        report.AppendLine("  \"delta_errors_deg\": {");
        List<string> deltaSummaries = new List<string>();
        float worstDeltaMean = 0f;
        string worstDeltaBone = "";
        foreach (var bc in BONE_CHECKS)
        {
            var errs = deltaErrors[bc.name];
            if (errs.Count == 0) continue;
            float sum = 0f, max = 0f;
            foreach (float e in errs) { sum += e; if (e > max) max = e; }
            float mean = sum / errs.Count;
            if (mean > worstDeltaMean) { worstDeltaMean = mean; worstDeltaBone = bc.name; }
            deltaSummaries.Add($"    \"{bc.name}\": {{\"mean\": {mean:F1}, \"max\": {max:F1}, \"n\": {errs.Count}}}");
        }
        report.AppendLine(string.Join(",\n", deltaSummaries));
        report.AppendLine("  },");

        // Absolute errors (reference metric)
        report.AppendLine("  \"abs_errors_deg\": {");
        List<string> absSummaries = new List<string>();
        float worstAbsMean = 0f;
        string worstAbsBone = "";
        foreach (var bc in BONE_CHECKS)
        {
            var errs = absErrors[bc.name];
            if (errs.Count == 0) continue;
            float sum = 0f, max = 0f;
            foreach (float e in errs) { sum += e; if (e > max) max = e; }
            float mean = sum / errs.Count;
            if (mean > worstAbsMean) { worstAbsMean = mean; worstAbsBone = bc.name; }
            absSummaries.Add($"    \"{bc.name}\": {{\"mean\": {mean:F1}, \"max\": {max:F1}, \"n\": {errs.Count}}}");
        }
        report.AppendLine(string.Join(",\n", absSummaries));
        report.AppendLine("  },");

        // Twist jump summary
        report.AppendLine("  \"twist_jumps\": {");
        List<string> twistSummaries = new List<string>();
        int totalTwistJumps = 0;
        foreach (var bc in BONE_CHECKS)
        {
            var jumps = twistJumps[bc.name];
            totalTwistJumps += jumps.Count;
            if (jumps.Count == 0) continue;
            float maxJump = 0f;
            foreach (float j in jumps) if (j > maxJump) maxJump = j;
            twistSummaries.Add($"    \"{bc.name}\": {{\"count\": {jumps.Count}, \"max\": {maxJump:F1}}}");
        }
        if (twistSummaries.Count > 0)
            report.AppendLine(string.Join(",\n", twistSummaries));
        report.AppendLine("  },");
        report.AppendLine($"  \"total_twist_jumps\": {totalTwistJumps},");

        // Verdict based on delta errors (the fair metric)
        bool passed = worstDeltaMean < 15f && totalTwistJumps == 0;
        report.AppendLine($"  \"worst_delta_bone\": \"{worstDeltaBone}\",");
        report.AppendLine($"  \"worst_delta_mean_deg\": {worstDeltaMean:F1},");
        report.AppendLine($"  \"worst_abs_bone\": \"{worstAbsBone}\",");
        report.AppendLine($"  \"worst_abs_mean_deg\": {worstAbsMean:F1},");
        report.AppendLine($"  \"verdict\": \"{(passed ? "PASS" : "FAIL")}\",");
        report.AppendLine($"  \"pass_threshold_delta_deg\": 15.0,");

        report.AppendLine("  \"frame_details\": [");
        report.AppendLine(string.Join(",\n", frameDetails));
        report.AppendLine("  ]");
        report.AppendLine("}");

        File.WriteAllText(reportPath, report.ToString());
        Debug.Log($"[PoseValidator] === Validation Complete ===");
        Debug.Log($"[PoseValidator] Verdict: {(passed ? "PASS" : "FAIL")}");
        Debug.Log($"[PoseValidator] Delta metric — worst: {worstDeltaBone} mean={worstDeltaMean:F1}° (threshold 15°)");
        Debug.Log($"[PoseValidator] Abs metric  — worst: {worstAbsBone} mean={worstAbsMean:F1}° (reference only)");
        Debug.Log($"[PoseValidator] Report: {reportPath}");
    }

    // ===== Helpers =====

    private static Vector3 GetSourceBoneDir(HumanPoseData pose, BoneCheck bc)
    {
        Vector3 p = pose.GetJoint(bc.proximal);
        Vector3 d = pose.GetJoint(bc.distal);
        Vector3 dir = d - p;
        return dir.sqrMagnitude > 1e-8f ? dir.normalized : Vector3.zero;
    }

    private static Vector3 GetAvatarBoneDir(Dictionary<HumanBodyBones, Transform> boneMap, BoneCheck bc)
    {
        if (!boneMap.ContainsKey(bc.avatarBone) || !boneMap.ContainsKey(bc.avatarChildBone))
            return Vector3.zero;
        Vector3 dir = boneMap[bc.avatarChildBone].position - boneMap[bc.avatarBone].position;
        return dir.sqrMagnitude > 1e-8f ? dir.normalized : Vector3.zero;
    }

    private static string V3S(Vector3 v) => $"({v.x:F3},{v.y:F3},{v.z:F3})";

    /// <summary>
    /// Replicate DataReceiver.AutoDetectYawFromPose logic.
    /// Computes the yaw rotation needed to face the person toward +Z.
    /// </summary>
    private static Quaternion ComputeAutoYaw(HumanPoseData pose)
    {
        Vector3 lHip = pose.GetJoint(HumanPoseData.JointType.L_Hip);
        Vector3 rHip = pose.GetJoint(HumanPoseData.JointType.R_Hip);
        Vector3 lShoulder = pose.GetJoint(HumanPoseData.JointType.L_Shoulder);
        Vector3 rShoulder = pose.GetJoint(HumanPoseData.JointType.R_Shoulder);
        Vector3 nose = pose.GetJoint(HumanPoseData.JointType.Nose);

        Vector3 hipCenter = Vector3.Lerp(lHip, rHip, 0.5f);
        Vector3 shoulderCenter = Vector3.Lerp(lShoulder, rShoulder, 0.5f);
        Vector3 up = (shoulderCenter - hipCenter).normalized;

        Vector3 hipRight = (rHip - lHip).normalized;
        Vector3 rawForward = Vector3.Cross(hipRight, up).normalized;

        Vector3 noseHint = Vector3.ProjectOnPlane(nose - hipCenter, up);
        if (noseHint.sqrMagnitude > 1e-6f && Vector3.Dot(rawForward, noseHint) < 0f)
            rawForward = -rawForward;

        Vector3 flatForward = new Vector3(rawForward.x, 0f, rawForward.z).normalized;
        if (flatForward.sqrMagnitude < 1e-6f)
            return Quaternion.identity;

        float autoYaw = -Mathf.Atan2(flatForward.x, flatForward.z) * Mathf.Rad2Deg;
        Debug.Log($"[PoseValidator] Auto yaw: {autoYaw:F1}° (person faces {flatForward})");
        return Quaternion.AngleAxis(autoYaw, Vector3.up);
    }

    private static void ApplyYawRotation(HumanPoseData pose, Quaternion yaw)
    {
        Vector3[] joints = pose.Joints;
        Vector3 pivot = Vector3.Lerp(
            joints[(int)HumanPoseData.JointType.L_Hip],
            joints[(int)HumanPoseData.JointType.R_Hip], 0.5f);
        for (int i = 0; i < joints.Length; i++)
            joints[i] = yaw * (joints[i] - pivot) + pivot;
    }

    private static void ApplyMediaPipeWorldTransform(HumanPoseData pose)
    {
        Vector3[] joints = pose.Joints;
        for (int i = 0; i < joints.Length; i++)
        {
            Vector3 s = joints[i];
            joints[i] = new Vector3(-s.x, s.y, -s.z);
        }
    }

    private static int DetectHeaderLine(string[] lines)
    {
        if (lines.Length == 0) return 0;
        string[] parts = lines[0].Split(',');
        float dummy;
        if (parts.Length > 0 && !float.TryParse(parts[0].Trim(),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out dummy))
            return 1;
        return 0;
    }

    private static HumanPoseData ParseCSVLine(string line)
    {
        string[] parts = line.Split(',');
        int offset = 0;
        if (parts.Length > 99)
        {
            int r99 = parts.Length - 99;
            int r132 = parts.Length - 132;
            if (r99 >= 0 && r99 <= 3) offset = r99;
            else if (r132 >= 0 && r132 <= 3) offset = r132;
        }
        int dataCount = parts.Length - offset;
        if (dataCount < 99) return null;

        float[] values = new float[dataCount];
        for (int i = 0; i < dataCount; i++)
        {
            if (!float.TryParse(parts[i + offset].Trim(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out values[i]))
                return null;
        }
        HumanPoseData pose = new HumanPoseData();
        pose.SetJointsFromArray(values);
        return pose;
    }

    private static string FindScene()
    {
        string[] candidates = { TARGET_SCENE, "Assets/Scenes/4M_1.unity", "Assets/Scenes/4M.unity", "Assets/Scenes/4M_2.unity" };
        foreach (string s in candidates)
            if (File.Exists(Path.Combine(Path.GetDirectoryName(Application.dataPath), s))) return s;
        string scenesDir = Path.Combine(Application.dataPath, "Scenes");
        if (Directory.Exists(scenesDir))
        {
            string[] files = Directory.GetFiles(scenesDir, "*.unity");
            if (files.Length > 0) return "Assets/Scenes/" + Path.GetFileName(files[0]);
        }
        return null;
    }

    private static string FindCSV()
    {
        string projectPath = Path.GetDirectoryName(Application.dataPath);
        string[] candidates = {
            Path.Combine(projectPath, CSV_PATH),
            Path.Combine(Application.dataPath, "Scrpits/new_controller_module/pose_data.csv"),
            Path.Combine(Application.dataPath, "Motion data/pose_data.csv"),
        };
        foreach (string p in candidates) if (File.Exists(p)) return p;
        string[] found = Directory.GetFiles(Application.dataPath, "pose_data.csv", SearchOption.AllDirectories);
        return found.Length > 0 ? found[0] : null;
    }

    private static Animator FindAvatarAnimator()
    {
        Animator[] animators = Object.FindObjectsOfType<Animator>();
        foreach (var a in animators)
            if (a.isHuman && a.avatar != null) return a;
        return animators.Length > 0 ? animators[0] : null;
    }

    private static void WriteErrorReport(string path, string error)
    {
        File.WriteAllText(path, $"{{\n  \"status\": \"error\",\n  \"error\": \"{error}\"\n}}");
        Debug.LogError($"[PoseValidator] ERROR: {error}");
    }
}
