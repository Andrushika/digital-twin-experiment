# Motion Retargeting System - Modular Architecture

## 📋 系統概覽

一套模組化的即時動作捕捉 → Unity Avatar 骨架映射系統，從原始3D座標點直接驅動人物角色。

```
Python (mocap data)
    ↓ CSV/UDP/TCP
┌─────────────────────────────────────────┐
│  Module A: DataReceiver                 │ ← 接收原始 joints +時間戳
├─────────────────────────────────────────┤
│  Module B: PoseInterpreter              │ ← 解釋人體結構 (pelvis/chest/head/limbs)
├─────────────────────────────────────────┤
│  Module C: RetargetSolver               │ ← 計算並套用 bone rotations
├─────────────────────────────────────────┤
│  MotionController                       │ ← 協調三個模組，管理 Phase
└─────────────────────────────────────────┘
    ↓
Unity Avatar (animated)
```

---

## 🏗️ 四個核心模組

### **Module A: DataReceiver** (`DataReceiver.cs`)
**職責:** 原始資料接收與幀管理

- 支援多種輸入：CSV 檔案、UDP Socket、TCP Socket、直接注入
- 維持環形 buffer (預設 300 幀 = 10 秒 @ 30fps)
- 附加時間戳與幀編號
- 發出事件供下層訂閱

**公開方法:**
```csharp
public void ReceiveFrame(HumanPoseData pose)           // 注入一幀
public void ReceiveCSVLine(string csvLine)              // 從 CSV 字串解析
public HumanPoseData GetLatestFrame()                   // 取最新幀
public bool TryGetFrame(int frameIndex, out HumanPoseData) // 查詢特定幀
```

---

### **Module B: PoseInterpreter** (`PoseInterpreter.cs`)
**職責:** 從 raw joints 解釋人體姿態結構

- 輸入：17 個 COCO/OpenPose 標準骨點
- 輸出：結構化的 `InterpretedPose`，包含：
  - 11 個主要部位的位置、旋轉、座標軸 (Pelvis/Chest/Head/4個肢段)
  - 各部位的信心度

**公開方法:**
```csharp
public InterpretedPose Interpret(HumanPoseData rawPose) // 解釋一幀
```

**InterpretedPose 結構:**
```
├─ Pelvis      (髖部中點)
├─ Chest       (肩膀中點)
├─ Head        (鼻尖位置)
├─ L_UpperArm  (左肩到左肘)
├─ L_LowerArm  (左肘到左腕)
├─ R_UpperArm
├─ R_LowerArm
├─ L_UpperLeg
├─ L_LowerLeg
├─ R_UpperLeg
└─ R_LowerLeg
```

---

### **Module C: RetargetSolver** (`RetargetSolver.cs`)
**職責:** 人體姿態 → Avatar 骨架旋轉映射

- 從 InterpretedPose 計算每根 bone 的目標旋轉
- **改用絕對旋轉** (`localRotation =`) 而非累積旋轉 (`.Rotate()`)
- 處理座標系轉換、縮放差異
- 快取所有必要的 bone Transform

**公開方法:**
```csharp
public void ApplyPose(PoseInterpreter.InterpretedPose pose) // 套用一幀
public void SetTargetAvatar(Animator animator)              // 設定目標
```

---

### **MotionController** (`MotionController.cs`)
**職責:** 協調三個模組 + Phase 管理

- 訂閱 DataReceiver 的新幀事件
- 根據當前 Phase 調用相應的邏輯
- 提供外部測試介面

**支援的 Phase:**
- **Phase 0:** 待命
- **Phase 1:** 打通資料流 + Debug 視覺化 (紅點/綠點/黃線)
- **Phase 2:** 軀幹控制 (Spine/Chest/Head)
- **Phase 3:** 四肢控制 (加入 Limbs)
- **Phase 4:** 完整旋轉驅動
- **Phase 5:** IK 與約束 (待實作)
- **Phase 6:** 穩定化 (smoothing/limits，待實作)

---

## 🚀 快速開始

### 1. 場景設置
```
Scene
├─ Avatar (with Animator)
└─ MotionController (empty GameObject)
   ├─ MotionController.cs (script)
   ├─ DataReceiver.cs (auto-added)
   ├─ RetargetSolver.cs (auto-added)
   ├─ Phase1_DebugVisualizer.cs (auto-added)
   └─ Phase1Test.cs (manual add for testing)
```

### 2. 在 Editor 中測試
```csharp
// 方式 A: 使用 Phase1Test 的 GUI 按鈕
// - 在 Scene 掛上 Phase1Test.cs
// - 按下 "Phase 1: Data Flow + Debug"
// - 按下 "Inject Test Frame (T-Pose)"

// 方式 B: 程式呼叫
MotionController mc = GetComponent<MotionController>();
mc.StartPhase(MotionController.Phase.Phase1_DataFlow);
mc.InjectCSVFrame("0,1.7,0,  -0.1,1.65,0,  ...");  // 51 個浮點數
```

### 3. 從 Python 傳資料 (Phase 1 完成後示範)
```python
# Python 端 (pseudo-code)
import socket

sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
sock.sendto(b"0,1.7,0, -0.1,1.65,0, ...", ("127.0.0.1", 5555))
```

---

## 📊 資料格式

### HumanPoseData
```csharp
public Vector3[] Joints;        // 17 個骨點位置
public float[] Confidence;      // 各骨點的信心度 (0-1)
public int FrameIndex;          // 幀編號
public double Timestamp;        // 秒或毫秒
public bool IsValid;            // 有效性標誌
```

### CSV 格式 (17 個骨點 × 3 座標)
```
x1,y1,z1,x2,y2,z2,...,x17,y17,z17
1.0,1.7,0.0,-0.1,1.65,0.0,...
```

### Joint 索引 (0-16)
```
0: Nose        5: L_Shoulder  10: R_Wrist    15: L_Ankle
1: L_Eye       6: R_Shoulder  11: L_Hip      16: R_Ankle
2: R_Eye       7: L_Elbow     12: R_Hip
3: L_Ear       8: R_Elbow     13: L_Knee
4: R_Ear       9: L_Wrist     14: R_Knee
```

---

## ✅ Phase 實作進度

### Phase 1: 打通資料流 ✅ 完成
- [x] DataReceiver 接收 raw joints
- [x] PoseInterpreter 解釋人體結構
- [x] Phase1_DebugVisualizer 視覺化 (紅/綠/黃)
- [x] GUI 測試介面

### Phase 2: 軀幹控制 ⚙️ 就位
- [x] Pelvis frame 建立
- [x] Chest frame 建立
- [x] Head frame 建立
- [ ] 測試與微調

### Phase 3: 四肢控制 ⚙️ 就位
- [x] UpperArm/LowerArm frame
- [x] UpperLeg/LowerLeg frame
- [ ] 測試與微調

### Phase 4: 完整旋轉驅動 ⏳ 待測
- [x] 改用 `localRotation =` (絕對旋轉)
- [ ] 座標系校正
- [ ] 長時間穩定性測試

### Phase 5: IK 與約束 📝 待做
- [ ] Foot IK (ground constraint)
- [ ] Hand target adjustment
- [ ] Pole hint 支援

### Phase 6: 穩定化 📝 待做
- [ ] Temporal smoothing (低通濾波)
- [ ] Joint limits
- [ ] Velocity constraints

---

## 🔧 使用方式

### 方式 1: 直接注入 CSV
```csharp
motionController.InjectCSVFrame(
    "0,1.7,0, -0.1,1.65,0, 0.1,1.65,0, ... "
);
```

### 方式 2: 注入 HumanPoseData
```csharp
var pose = new HumanPoseData();
pose.SetJoint(HumanPoseData.JointType.Nose, new Vector3(0, 1.7f, 0));
pose.SetJoint(HumanPoseData.JointType.L_Hip, new Vector3(-0.2f, 1f, 0));
// ... 設定其他關節
motionController.InjectPoseData(pose);
```

### 方式 3: UDP 即時流 (Phase 1 後擴展)
```csharp
// DataReceiver 設定為 UDPSocket mode
// Python 持續推送資料到 127.0.0.1:5555
```

---

## 🐛 調試

### 啟用 Debug 視覺化
```csharp
// Scene view 會顯示：
// - 紅色小球: 原始 17 個 joints
// - 綠色大球: 解釋後的 11 個部位
// - 黃線: 骨骼連接線
// - RGB 軸: 座標系 (optional)

motionController.SetDebugVisualizationEnabled(true);
```

### 監控日誌
```
[DataReceiver] Initialized
[MotionController] Frame 0 received at 0.00s
[MotionController] Frame 30 received at 1.00s
...
```

---

## 📝 檔案結構

```
new_controller_module/
├─ HumanPoseData.cs              # 資料結構 + 17 個 joint 定義
├─ DataReceiver.cs              # Module A
├─ PoseInterpreter.cs           # Module B
├─ RetargetSolver.cs            # Module C
├─ MotionController.cs          # 主協調器
├─ Phase1_DebugVisualizer.cs    # Debug 工具
├─ Phase1Test.cs                # 測試腳本 (optional)
└─ README.md                     # 本文件
```

---

## 🎯 下一步

1. **Phase 1 驗證** → 確認資料流完整，視覺化正確
2. **座標系校正** → 根據你的 mocap 系統調整 axis mapping
3. **Phase 2 微調** → 軀幹旋轉的精確度
4. **Phase 3 測試** → 四肢控制
5. **Phase 4 驗證** → Avatar 完整跟隨動作
6. **Phase 5-6 延伸** → IK、smoothing 等

---

## ❓ 常見問題

**Q: 我的 CSV 格式不同？**  
A: 在 `DataReceiver.ReceiveCSVLine()` 中修改解析邏輯，或在 `HumanPoseData.SetJointsFromArray()` 前做軸映射。

**Q: Avatar 骨架跟我的 mocap 系統對不上？**  
A: 在 `RetargetSolver` 中調整 `axisScale`, `flipX/Y/Z`, `scaleFactor` 參數。

**Q: 怎麼連接 Python 的實時流？**  
A: Phase 1 完成後，擴展 `DataReceiver.InitializeReceiver()` 裡的 UDP/TCP 邏輯。

---

## 📞 支援

有問題時，檢查：
1. Console 的錯誤訊息
2. MotionController 是否正確掛載
3. Target Avatar 是否設定
4. Debug Visualizer 的視覺反饋
