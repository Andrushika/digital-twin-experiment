using UnityEngine;

/// <summary>
/// Swing-twist 分解 + 1€ filter（給 TwistSolver 用的數學工具）。
///
/// q = q_swing × q_twist，其中 q_twist 是沿 twistAxis 的純旋轉。
/// 推導：把 q 的虛部投影到 twistAxis，重新組合就是 twist；剩下的就是 swing。
/// </summary>
public static class SwingTwistMath
{
    /// <summary>
    /// 把 rotation 拆成 (swing, twist)，twist 沿 twistAxis。
    /// twistAxis 必須單位向量。
    /// </summary>
    public static void DecomposeSwingTwist(Quaternion rotation, Vector3 twistAxis,
                                            out Quaternion swing, out Quaternion twist)
    {
        Vector3 r = new Vector3(rotation.x, rotation.y, rotation.z);
        Vector3 proj = Vector3.Dot(r, twistAxis) * twistAxis;
        twist = new Quaternion(proj.x, proj.y, proj.z, rotation.w);

        float magSqr = twist.x * twist.x + twist.y * twist.y + twist.z * twist.z + twist.w * twist.w;
        if (magSqr < 1e-8f)
        {
            twist = Quaternion.identity;
        }
        else
        {
            float invMag = 1f / Mathf.Sqrt(magSqr);
            twist = new Quaternion(twist.x * invMag, twist.y * invMag, twist.z * invMag, twist.w * invMag);
        }

        swing = rotation * Quaternion.Inverse(twist);
    }

    /// <summary>
    /// 連續性處理：若 current 與 previous 內積為負，翻號。
    /// q 與 -q 表示同姿態，但用在內插/濾波上要保持半球連續才不會抖。
    /// </summary>
    public static Quaternion EnsureHemisphereContinuity(Quaternion previous, Quaternion current)
    {
        if (Quaternion.Dot(previous, current) < 0f)
            return new Quaternion(-current.x, -current.y, -current.z, -current.w);
        return current;
    }

    /// <summary>
    /// Angle unwrap：把 current 拉到離 previous 最近的同義角，避免 ±180° 跳變。
    /// 用 Mathf.DeltaAngle 處理 wrap，再加回去。
    /// </summary>
    public static float UnwrapAngleDeg(float current, float previous)
    {
        return previous + Mathf.DeltaAngle(previous, current);
    }
}

/// <summary>
/// 1€ filter (Casiez et al. 2012)：對 noisy 訊號做時序平滑。
/// 動作慢時：cutoff 低 → 重平滑；動作快時：cutoff 高 → 反應快。不會像 EMA 那樣慢動作也跟丟。
///
/// 用法：每個 twist channel 各持一個 filter，每幀 Filter(angle, dt)。
/// </summary>
public class OneEuroFilter1D
{
    private readonly float _minCutoff;
    private readonly float _beta;
    private readonly float _dCutoff;

    private float _xPrev;
    private float _dxPrev;
    private bool _initialized;

    public OneEuroFilter1D(float minCutoff = 1.0f, float beta = 0.02f, float dCutoff = 1.0f)
    {
        _minCutoff = Mathf.Max(1e-3f, minCutoff);
        _beta = Mathf.Max(0f, beta);
        _dCutoff = Mathf.Max(1e-3f, dCutoff);
    }

    public void Reset()
    {
        _initialized = false;
        _xPrev = 0f;
        _dxPrev = 0f;
    }

    /// <summary>
    /// 第一幀（_initialized=false）直接 pass-through，不做平滑（避免假惯性）。
    /// </summary>
    public float Filter(float x, float dt)
    {
        if (!_initialized)
        {
            _xPrev = x;
            _dxPrev = 0f;
            _initialized = true;
            return x;
        }
        if (dt <= 0f)
            return _xPrev;

        float dx = (x - _xPrev) / dt;
        float aDx = Alpha(_dCutoff, dt);
        float dxFiltered = _dxPrev + aDx * (dx - _dxPrev);

        float cutoff = _minCutoff + _beta * Mathf.Abs(dxFiltered);
        float aX = Alpha(cutoff, dt);
        float xFiltered = _xPrev + aX * (x - _xPrev);

        _xPrev = xFiltered;
        _dxPrev = dxFiltered;
        return xFiltered;
    }

    private static float Alpha(float cutoff, float dt)
    {
        float tau = 1f / (2f * Mathf.PI * cutoff);
        return 1f / (1f + tau / dt);
    }
}

