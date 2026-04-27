using UnityEngine;

/// <summary>
/// 計算四肢的 twist 角度（沿 bone 自身軸的旋轉）。
///
/// 為什麼需要 twist：
///   parent-inherited swing 用 FromToRotation 對齊 bone 方向，是「最小旋轉」，
///   所以沿 bone 軸的旋轉永遠 = 0。但人會翻手掌、轉腳尖、扭腰，這些動作只看
///   骨骼端點是看不出來的，必須從額外的方向資訊（手掌法線、腳底法線）反推。
///
/// 演算法（每根 bone 同構）：
///   1. Calibration：記下 rest pose 的 bone axis 與 reference 向量（手掌法線 / 腳底法線）
///   2. 每幀：
///      a. 把 rest reference 沿 bone 的 swing 平行搬運到當前 frame
///         (swing = FromToRotation(restAxis, currentAxis))
///         → 得到「假設沒有 twist 時 reference 應該在哪」
///      b. 把 expected 與 current reference 都投影到垂直 currentAxis 的平面
///      c. 投影長度太短（reference 幾乎與 bone 軸平行，例如極端屈腕）→ 退化，hold 上幀值
///      d. SignedAngle(expectedProj, currentProj, currentAxis) = twist 角度
///      e. 保持單圈 signed angle → clamp 到生理範圍 → 1€ filter 平滑
///
/// 對 derived twist（上臂、大腿）：
///   reference 仍是手掌法線 / 腳底法線（最遠端的可靠 reference），但 axis 換成上臂 / 大腿軸。
///   結果是「肩到腕」或「髖到踝」整段的 twist；扣掉前臂 / 小腿的部分，剩下分配給上臂 / 大腿。
/// </summary>
public class TwistSolver
{
    [System.Serializable]
    public class Config
    {
        [Tooltip("投影長度 < 這個值就視為退化，hold 上幀（reference 與 bone 軸近平行）")]
        public float degenerateProjectionThreshold = 0.3f;

        [Header("Clamp (degrees, ±)")]
        public float forearmClamp = 90f;
        public float upperArmClamp = 70f;
        public float shinClamp = 20f;
        public float thighClamp = 40f;

        [Header("1€ Filter — Forearm")]
        public float forearmMinCutoff = 1.0f;
        public float forearmBeta = 0.05f;

        [Header("1€ Filter — Upper Arm (derived, noisier)")]
        public float upperArmMinCutoff = 0.7f;
        public float upperArmBeta = 0.03f;

        [Header("1€ Filter — Shin")]
        public float shinMinCutoff = 0.8f;
        public float shinBeta = 0.04f;

        [Header("1€ Filter — Thigh (derived, noisier)")]
        public float thighMinCutoff = 0.5f;
        public float thighBeta = 0.02f;
    }

    public struct LimbTwists
    {
        public float L_UpperArm;
        public float L_LowerArm;
        public float R_UpperArm;
        public float R_LowerArm;
        public float L_UpperLeg;
        public float L_LowerLeg;
        public float R_UpperLeg;
        public float R_LowerLeg;
    }

    public struct ChannelDebug
    {
        public float RawDeg;                    // distal: raw twist; derived: total limb raw twist
        public float PreClampDeg;               // value entering clamp/filter
        public float ClampedDeg;
        public float FilteredDeg;
        public float ExpectedProjectionMagnitude;
        public float CurrentProjectionMagnitude;
        public float ReferenceAxisAngleDeg;
        public bool Degenerate;
        public bool HeldPreviousRaw;
    }

    public struct LimbTwistDebug
    {
        public ChannelDebug L_UpperArm;
        public ChannelDebug L_LowerArm;
        public ChannelDebug R_UpperArm;
        public ChannelDebug R_LowerArm;
        public ChannelDebug L_UpperLeg;
        public ChannelDebug L_LowerLeg;
        public ChannelDebug R_UpperLeg;
        public ChannelDebug R_LowerLeg;
    }

    private readonly Config _cfg;

    // Rest 參考（calibration 時記錄）
    private bool _calibrated;
    private Vector3 _restL_PalmNormal, _restR_PalmNormal;
    private Vector3 _restL_FootNormal, _restR_FootNormal;
    private Vector3 _restL_LowerArmAxis, _restR_LowerArmAxis;
    private Vector3 _restL_UpperArmAxis, _restR_UpperArmAxis;
    private Vector3 _restL_LowerLegAxis, _restR_LowerLegAxis;
    private Vector3 _restL_UpperLegAxis, _restR_UpperLegAxis;

    // Per-channel state（退化 hold + 1€ filter）
    private ChannelState _l_lowerArm, _r_lowerArm;
    private ChannelState _l_upperArm, _r_upperArm;
    private ChannelState _l_lowerLeg, _r_lowerLeg;
    private ChannelState _l_upperLeg, _r_upperLeg;
    private LimbTwistDebug _lastDebug;

    public TwistSolver(Config config = null)
    {
        _cfg = config ?? new Config();
        _l_lowerArm = new ChannelState(_cfg.forearmMinCutoff, _cfg.forearmBeta);
        _r_lowerArm = new ChannelState(_cfg.forearmMinCutoff, _cfg.forearmBeta);
        _l_upperArm = new ChannelState(_cfg.upperArmMinCutoff, _cfg.upperArmBeta);
        _r_upperArm = new ChannelState(_cfg.upperArmMinCutoff, _cfg.upperArmBeta);
        _l_lowerLeg = new ChannelState(_cfg.shinMinCutoff, _cfg.shinBeta);
        _r_lowerLeg = new ChannelState(_cfg.shinMinCutoff, _cfg.shinBeta);
        _l_upperLeg = new ChannelState(_cfg.thighMinCutoff, _cfg.thighBeta);
        _r_upperLeg = new ChannelState(_cfg.thighMinCutoff, _cfg.thighBeta);
    }

    public bool IsCalibrated => _calibrated;
    public LimbTwistDebug LastDebug => _lastDebug;

    public void Reset()
    {
        _calibrated = false;
        _l_lowerArm.Reset(); _r_lowerArm.Reset();
        _l_upperArm.Reset(); _r_upperArm.Reset();
        _l_lowerLeg.Reset(); _r_lowerLeg.Reset();
        _l_upperLeg.Reset(); _r_upperLeg.Reset();
    }

    /// <summary>
    /// 用第一幀（或指定的 T-pose frame）的 InterpretedPose 記錄所有 reference 的 rest 值。
    /// 後續所有 twist 都對這組 rest 做相對量。
    /// </summary>
    public void Calibrate(PoseInterpreter.InterpretedPose pose)
    {
        _restL_PalmNormal = SafeNormalize(pose.L_PalmNormal, Vector3.up);
        _restR_PalmNormal = SafeNormalize(pose.R_PalmNormal, Vector3.up);
        _restL_FootNormal = SafeNormalize(pose.L_FootPlaneNormal, Vector3.up);
        _restR_FootNormal = SafeNormalize(pose.R_FootPlaneNormal, Vector3.up);

        _restL_LowerArmAxis = SafeNormalize(pose.L_LowerArm.Forward, Vector3.right);
        _restR_LowerArmAxis = SafeNormalize(pose.R_LowerArm.Forward, Vector3.right);
        _restL_UpperArmAxis = SafeNormalize(pose.L_UpperArm.Forward, Vector3.right);
        _restR_UpperArmAxis = SafeNormalize(pose.R_UpperArm.Forward, Vector3.right);

        _restL_LowerLegAxis = SafeNormalize(pose.L_LowerLeg.Forward, Vector3.down);
        _restR_LowerLegAxis = SafeNormalize(pose.R_LowerLeg.Forward, Vector3.down);
        _restL_UpperLegAxis = SafeNormalize(pose.L_UpperLeg.Forward, Vector3.down);
        _restR_UpperLegAxis = SafeNormalize(pose.R_UpperLeg.Forward, Vector3.down);

        _calibrated = true;
    }

    /// <summary>
    /// 算當前 frame 的 8 個 limb twist 角度。dt 給 1€ filter 用（建議 Time.deltaTime）。
    /// 未 calibrate 時直接回 0。
    /// </summary>
    public LimbTwists Compute(PoseInterpreter.InterpretedPose pose, float dt)
    {
        LimbTwists result = default;
        LimbTwistDebug debug = default;
        if (!_calibrated)
        {
            _lastDebug = debug;
            return result;
        }

        // ---- Forearm (P0)：reference = 手掌法線，axis = 前臂 ----
        result.L_LowerArm = SolveAndStore(
            currentAxis: pose.L_LowerArm.Forward,
            currentRef: pose.L_PalmNormal,
            restAxis: _restL_LowerArmAxis,
            restRef: _restL_PalmNormal,
            clamp: _cfg.forearmClamp,
            dt: dt,
            state: _l_lowerArm,
            debug: out debug.L_LowerArm);

        result.R_LowerArm = SolveAndStore(
            currentAxis: pose.R_LowerArm.Forward,
            currentRef: pose.R_PalmNormal,
            restAxis: _restR_LowerArmAxis,
            restRef: _restR_PalmNormal,
            clamp: _cfg.forearmClamp,
            dt: dt,
            state: _r_lowerArm,
            debug: out debug.R_LowerArm);

        // ---- Shin (P1)：reference = 腳底法線，axis = 小腿 ----
        result.L_LowerLeg = SolveAndStore(
            currentAxis: pose.L_LowerLeg.Forward,
            currentRef: pose.L_FootPlaneNormal,
            restAxis: _restL_LowerLegAxis,
            restRef: _restL_FootNormal,
            clamp: _cfg.shinClamp,
            dt: dt,
            state: _l_lowerLeg,
            debug: out debug.L_LowerLeg);

        result.R_LowerLeg = SolveAndStore(
            currentAxis: pose.R_LowerLeg.Forward,
            currentRef: pose.R_FootPlaneNormal,
            restAxis: _restR_LowerLegAxis,
            restRef: _restR_FootNormal,
            clamp: _cfg.shinClamp,
            dt: dt,
            state: _r_lowerLeg,
            debug: out debug.R_LowerLeg);

        // ---- Upper Arm (P1, derived)：總臂 twist - 前臂 twist ----
        // 用同樣的 ref（手掌法線），但 axis 換上臂；得到「整段 shoulder→wrist」的 twist
        // 再扣掉已分配給前臂的，剩餘給上臂
        ChannelDebug lUpperArmDebug;
        float totalL_ArmTwist = SolveRaw(
            currentAxis: pose.L_UpperArm.Forward,
            currentRef: pose.L_PalmNormal,
            restAxis: _restL_UpperArmAxis,
            restRef: _restL_PalmNormal,
            state: _l_upperArm,
            debug: out lUpperArmDebug);
        ChannelDebug rUpperArmDebug;
        float totalR_ArmTwist = SolveRaw(
            currentAxis: pose.R_UpperArm.Forward,
            currentRef: pose.R_PalmNormal,
            restAxis: _restR_UpperArmAxis,
            restRef: _restR_PalmNormal,
            state: _r_upperArm,
            debug: out rUpperArmDebug);

        result.L_UpperArm = FinalizeDerived(totalL_ArmTwist - result.L_LowerArm,
                                            _cfg.upperArmClamp, dt, _l_upperArm,
                                            ref lUpperArmDebug);
        result.R_UpperArm = FinalizeDerived(totalR_ArmTwist - result.R_LowerArm,
                                            _cfg.upperArmClamp, dt, _r_upperArm,
                                            ref rUpperArmDebug);
        debug.L_UpperArm = lUpperArmDebug;
        debug.R_UpperArm = rUpperArmDebug;

        // ---- Thigh (P2, derived)：總腿 twist - 小腿 twist ----
        ChannelDebug lUpperLegDebug;
        float totalL_LegTwist = SolveRaw(
            currentAxis: pose.L_UpperLeg.Forward,
            currentRef: pose.L_FootPlaneNormal,
            restAxis: _restL_UpperLegAxis,
            restRef: _restL_FootNormal,
            state: _l_upperLeg,
            debug: out lUpperLegDebug);
        ChannelDebug rUpperLegDebug;
        float totalR_LegTwist = SolveRaw(
            currentAxis: pose.R_UpperLeg.Forward,
            currentRef: pose.R_FootPlaneNormal,
            restAxis: _restR_UpperLegAxis,
            restRef: _restR_FootNormal,
            state: _r_upperLeg,
            debug: out rUpperLegDebug);

        result.L_UpperLeg = FinalizeDerived(totalL_LegTwist - result.L_LowerLeg,
                                            _cfg.thighClamp, dt, _l_upperLeg,
                                            ref lUpperLegDebug);
        result.R_UpperLeg = FinalizeDerived(totalR_LegTwist - result.R_LowerLeg,
                                            _cfg.thighClamp, dt, _r_upperLeg,
                                            ref rUpperLegDebug);
        debug.L_UpperLeg = lUpperLegDebug;
        debug.R_UpperLeg = rUpperLegDebug;

        _lastDebug = debug;
        return result;
    }

    // ===== 核心解法 =====

    /// <summary>
    /// 完整流程：算 raw twist → clamp → filter → 寫回 state。
    /// </summary>
    private float SolveAndStore(Vector3 currentAxis, Vector3 currentRef,
                                 Vector3 restAxis, Vector3 restRef,
                                 float clamp, float dt, ChannelState state,
                                 out ChannelDebug debug)
    {
        float raw = SolveRaw(currentAxis, currentRef, restAxis, restRef, state, out debug);
        return FinalizeDerived(raw, clamp, dt, state, ref debug);
    }

    /// <summary>
    /// 只算原始 twist 角度（含退化檢查），不做 clamp 也不寫進 filter。
    /// 給 derived twist 用：上臂 raw - 前臂 final 後才 clamp + filter。
    /// 但會更新 state 的 prev/initialized，供退化時 hold 上一個可用值。
    /// </summary>
    private float SolveRaw(Vector3 currentAxis, Vector3 currentRef,
                           Vector3 restAxis, Vector3 restRef, ChannelState state,
                           out ChannelDebug debug)
    {
        debug = default;
        Vector3 axis = SafeNormalize(currentAxis, Vector3.right);
        Vector3 refN = SafeNormalize(currentRef, Vector3.up);

        // 平行搬運 rest reference 到 current frame
        Quaternion swingRest2Cur = Quaternion.FromToRotation(restAxis, axis);
        Vector3 expectedRef = swingRest2Cur * restRef;

        Vector3 expectedProj = Vector3.ProjectOnPlane(expectedRef, axis);
        Vector3 currentProj = Vector3.ProjectOnPlane(refN, axis);
        debug.ExpectedProjectionMagnitude = expectedProj.magnitude;
        debug.CurrentProjectionMagnitude = currentProj.magnitude;
        debug.ReferenceAxisAngleDeg = Vector3.Angle(refN, axis);

        float threshold = _cfg.degenerateProjectionThreshold;
        if (expectedProj.magnitude < threshold || currentProj.magnitude < threshold)
        {
            // 退化：reference 與 bone 軸近平行（極端屈腕、踢直腿、腳尖朝下等）
            // → hold 上一幀 raw 值；若無歷史則回 0
            float held = state.HasPrev ? state.PrevRaw : 0f;
            debug.RawDeg = held;
            debug.PreClampDeg = held;
            debug.Degenerate = true;
            debug.HeldPreviousRaw = state.HasPrev;
            return held;
        }

        float angle = Vector3.SignedAngle(expectedProj.normalized, currentProj.normalized, axis);

        state.PrevRaw = angle;
        state.HasPrev = true;
        debug.RawDeg = angle;
        debug.PreClampDeg = angle;
        return angle;
    }

    /// <summary>
    /// Clamp + 1€ filter，給最終 (forearm/shin) 或 derived (upperArm/thigh = totalRaw - distalFinal) 用。
    /// </summary>
    private float FinalizeDerived(float angle, float clamp, float dt, ChannelState state,
                                  ref ChannelDebug debug)
    {
        debug.PreClampDeg = angle;
        float clamped = Mathf.Clamp(angle, -clamp, clamp);
        debug.ClampedDeg = clamped;
        float filtered = state.Filter.Filter(clamped, dt);
        debug.FilteredDeg = filtered;
        return filtered;
    }

    private static Vector3 SafeNormalize(Vector3 v, Vector3 fallback)
    {
        if (v.sqrMagnitude > 1e-10f) return v.normalized;
        return fallback;
    }

    private class ChannelState
    {
        public OneEuroFilter1D Filter;
        public bool HasPrev;
        public float PrevRaw;

        public ChannelState(float minCutoff, float beta)
        {
            Filter = new OneEuroFilter1D(minCutoff, beta);
        }

        public void Reset()
        {
            Filter.Reset();
            HasPrev = false;
            PrevRaw = 0f;
        }
    }
}
