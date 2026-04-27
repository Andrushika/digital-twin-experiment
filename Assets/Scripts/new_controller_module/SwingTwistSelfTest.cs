using UnityEngine;

/// <summary>
/// SwingTwistMath 自我測試。掛在任意 GameObject，右鍵組件選 "Run Self Tests"。
/// 會印 PASS/FAIL 到 Console。
/// </summary>
public class SwingTwistSelfTest : MonoBehaviour
{
    [ContextMenu("Run Self Tests")]
    public void Run()
    {
        int passed = 0;
        int failed = 0;
        Random.InitState(12345);

        // Test 1: 純 swing → twist 應為 identity
        {
            Vector3 axis = Vector3.up;
            Quaternion pureSwing = Quaternion.AngleAxis(45f, Vector3.right);
            SwingTwistMath.DecomposeSwingTwist(pureSwing, axis, out Quaternion swing, out Quaternion twist);
            float twistAngle = Quaternion.Angle(twist, Quaternion.identity);
            bool ok = twistAngle < 1e-3f;
            Report("純 swing → twist≈identity", ok, $"twistAngle={twistAngle:F4}°");
            if (ok) passed++; else failed++;
        }

        // Test 2: 純 twist → swing 應為 identity
        {
            Vector3 axis = new Vector3(1f, 1f, 0f).normalized;
            Quaternion pureTwist = Quaternion.AngleAxis(60f, axis);
            SwingTwistMath.DecomposeSwingTwist(pureTwist, axis, out Quaternion swing, out Quaternion twist);
            float swingAngle = Quaternion.Angle(swing, Quaternion.identity);
            float twistAngleErr = Quaternion.Angle(twist, pureTwist);
            bool ok = swingAngle < 1e-3f && twistAngleErr < 1e-3f;
            Report("純 twist → swing≈identity, twist≈原值", ok,
                $"swingAngle={swingAngle:F4}°, twistAngleErr={twistAngleErr:F4}°");
            if (ok) passed++; else failed++;
        }

        // Test 3: 隨機 rotation → swing × twist 重組
        {
            int subPassed = 0;
            int subTotal = 50;
            float worst = 0f;
            for (int i = 0; i < subTotal; i++)
            {
                Quaternion q = Random.rotationUniform;
                Vector3 axis = Random.onUnitSphere;
                SwingTwistMath.DecomposeSwingTwist(q, axis, out Quaternion swing, out Quaternion twist);
                Quaternion recombined = swing * twist;
                float err = Quaternion.Angle(q, recombined);
                worst = Mathf.Max(worst, err);
                if (err < 1e-2f) subPassed++;
            }
            bool ok = subPassed == subTotal;
            Report($"隨機 rotation 重組 ({subPassed}/{subTotal})", ok, $"worst err={worst:F4}°");
            if (ok) passed++; else failed++;
        }

        // Test 4: OneEuroFilter 第一幀 pass-through
        {
            OneEuroFilter1D f = new OneEuroFilter1D();
            float y = f.Filter(42f, 1f / 30f);
            bool ok = Mathf.Abs(y - 42f) < 1e-5f;
            Report("OneEuroFilter 第一幀 pass-through", ok, $"out={y}");
            if (ok) passed++; else failed++;
        }

        // Test 5: OneEuroFilter reset 後再走
        {
            OneEuroFilter1D f = new OneEuroFilter1D(minCutoff: 1f, beta: 0.02f);
            for (int i = 0; i < 30; i++) f.Filter(0f, 1f / 30f);
            f.Reset();
            float y = f.Filter(99f, 1f / 30f);
            bool ok = Mathf.Abs(y - 99f) < 1e-5f;
            Report("OneEuroFilter reset 後第一幀 pass-through", ok, $"out={y}");
            if (ok) passed++; else failed++;
        }

        // Test 6: UnwrapAngleDeg
        {
            float a = SwingTwistMath.UnwrapAngleDeg(-170f, 170f);
            bool ok = Mathf.Abs(a - 190f) < 1e-3f;
            Report("UnwrapAngleDeg(-170, prev=170) → 190", ok, $"got {a}");
            if (ok) passed++; else failed++;
        }

        Debug.Log($"[SwingTwistSelfTest] {passed} passed, {failed} failed");
    }

    private static void Report(string name, bool ok, string detail)
    {
        if (ok) Debug.Log($"[SwingTwistSelfTest] PASS {name}  ({detail})");
        else Debug.LogError($"[SwingTwistSelfTest] FAIL {name}  ({detail})");
    }
}
