# 快速啟動指南 - MediaPipe 動作驅動

## 🚀 5 分鐘啟用系統

### 方法 A: 用你的 CSV 檔案（推薦）

#### 1️⃣ 準備 CSV 檔案
```
Assets/
└─ Motion Data/keypoints_data/
   └─ your_pose_data.csv  (MediaPipe 33 點格式)
```

**CSV 格式：**
```
x1,y1,z1,x2,y2,z2,...,x33,y33,z33
1.5,0.8,2.1,1.4,0.8,2.0,...
1.5,0.8,2.1,1.4,0.8,2.0,...
```

或含信心度 (confidence):
```
x1,y1,z1,c1,x2,y2,z2,c2,...,x33,y33,z33,c33
```

#### 2️⃣ 在 Unity 中設置

**在 Avatar GameObject 上添加 QuickStart.cs：**

```
你的 Avatar GameObject
├─ Animator (已有)
├─ QuickStart.cs (新增)      ← 掛這個
└─ MotionController.cs (自動建立)
   ├─ DataReceiver.cs
   ├─ RetargetSolver.cs
   └─ Phase1_DebugVisualizer.cs
```

#### 3️⃣ Inspector 設置

在 Inspector 中設定 **QuickStart** 的參數：

| 參數 | 值 | 說明 |
|------|-----|------|
| **CSV File Name** | `your_pose_data.csv` | 檔案名稱（不含路徑） |
| **Play On Start** | ✓ | 自動播放 |
| **Target Phase** | Phase4_RotationDriven | 完整骨架驅動 |
| **Playback Speed** | 1.0 | 播放速度倍數 |

#### 4️⃣ 按 Play

Avatar 會自動開始跟著你的 CSV 動作驅動！

---

## 🧪 方法 B: 快速測試（無 CSV）

1. 建立 empty GameObject，掛上 **MotionController.cs**
2. 再掛上 **Phase1Test_MediaPipe.cs**
3. 在 Scene view 按 GUI 按鈕：
   - "Phase 1: Data Flow + Debug" - 看紅綠點
   - "Inject T-Pose" - 測試 T-Pose
   - "Inject Walking" - 測試行走

---

## 📊 MediaPipe 33 點定義

系統支援的 MediaPipe 格式：

```
0: Nose (鼻子)
1-6: 眼睛區域
7-8: 耳朵
9-10: 嘴巴
11-16: 上身（肩、肘、腕）
17-20: 手指
23-24: 髖部
25-28: 腿部
29-30: 腳跟
31-32: 腳尖
```

---

## 🎮 控制界面

### QuickStart GUI 按鈕
Scene 中會自動出現控制面板：

- **Start** - 開始播放
- **Pause** - 暫停
- **Resume** - 繼續
- **Stop** - 停止

### 程式控制（C# 代碼）
```csharp
QuickStart quickStart = GetComponent<QuickStart>();

// 啟動
quickStart.StartMotion();

// 暫停 / 恢復 / 停止
quickStart.Pause();
quickStart.Resume();
quickStart.Stop();
```

---

## ⚙️ 進階設置

### 調整播放速度
```csharp
// 在 DataReceiver 中
csvPlaybackSpeed = 2f;  // 2 倍速
```

### 改變目標 Phase
```csharp
// 在 QuickStart Inspector 設定
targetPhase = MotionController.Phase.Phase2_TorsoControl;  // 只驅動軀幹
```

### Debug 視覺化
```csharp
// 在 MotionController 中
motionController.SetDebugVisualizationEnabled(true);
```

Scene 中會看到：
- 🔴 紅點：原始 MediaPipe 33 個座標點
- 🟢 綠點：解釋後的 11 個人體部位
- 🟡 黃線：骨骼連接線

---

## 🔧 常見問題

### Q: CSV 檔找不到
A: 確認檔案位置：
```
Assets/Motion Data/your_file.csv
```
Python 導出時使用相同路徑。

### Q: Avatar 沒有動作
A: 檢查：
1. Avatar 的 Animator 已正確設置（Humanoid）
2. QuickStart 的 Target Phase 不是 Phase0_Standby
3. Console 有無錯誤訊息

### Q: 動作看起來扭曲
A: 檢查座標系：
- MediaPipe 預設是 (X, Y, Z) 其中 Z 是深度
- 如果需要調整，在 DataReceiver 中改 `csvPlaybackSpeed` 測試
- 或在 RetargetSolver 中調整 `scaleFactor`

### Q: 想即時接收 Python 資料
A: 改 DataReceiver 的 InputMode：
```csharp
inputMode = InputMode.UDPSocket;  // 或 TCPSocket
udpPort = 5555;
```
（實作細節見 Module A 文檔）

---

## 📁 檔案結構

```
new_controller_module/
├─ HumanPoseData.cs              # 33 點資料容器 + 軸映射
├─ DataReceiver.cs              # CSV 讀取 + 幀管理
├─ PoseInterpreter.cs           # 33點 → 11 部位 frame
├─ RetargetSolver.cs            # Frame → Avatar bone rotation
├─ MotionController.cs          # 主協調+Phase管理
├─ QuickStart.cs                # ✨ 快速啟動（推薦用這個）
├─ Phase1_DebugVisualizer.cs    # Debug 工具
├─ Phase1Test_MediaPipe.cs      # 測試腳本
└─ README.md                     # 詳細文檔
```

---

## 🎯 流程圖

```
CSV 檔
  ↓
DataReceiver (Module A)
讀每一行，解成 33 個座標點
  ↓
PoseInterpreter (Module B)
33 點 → 11 個部位 frame（含完整旋轉信息）
  ↓
RetargetSolver (Module C)
計算 Avatar 每根骨頭應該怎麼轉
  ↓
MotionController
套用旋轉到 Avatar
  ↓
Avatar 動起來 ✨
```

---

## 💡 下一步

- **Phase 1 完成後** → 確認視覺化正確
- **Phase 2-4 測試** → 調整座標系轉換參數
- **Phase 5** → 加入 IK (手腳修正)
- **Phase 6** → 加入 smoothing (穩定化)

---

## 📞 支援

有問題時檢查：
1. Console 中的 log 訊息 (關鍵字: `[DataReceiver]`, `[MotionController]`, `[QuickStart]`)
2. MotionController 的 Phase 狀態
3. CSV 檔格式是否正確 (99 或 132 個浮點數一行)
