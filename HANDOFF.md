# LaneReader — 项目交接文档

> 一个 ADOFAI（冰与火之舞）的 **4K 下落式辅助读谱 mod**。
> 读取游戏内存里已构建的谱面，渲染成 4 条下落轨道，并用 AdoFalling 的算法自动排键。

---

## 1. 当前状态

**功能已基本完成，正在做细节打磨。**

| 功能 | 状态 |
|---|---|
| 谱面读取（游戏内存，无需文件） | ✅ |
| 4K 轨道 / 判定线 / 判定框 | ✅ |
| 小节线（每拍细线 / 每 N 拍粗线） | ⚠️ 新增，未实机验证 |
| 音符边框（加深色描边） | ⚠️ 新增，未实机验证 |
| 叠加层坐标偏移 / 轨道背景透明度 | ⚠️ 新增，未实机验证 |
| 手序排键（AdoFalling 原版算法） | ✅ |
| 手序排键（拆手序，移植 ReADOFAIMacro） | ✅ 离线逐窗口验证通过（28 谱 / 17558 窗口 100% 一致）；**未实机验证** |
| 静态框 / 音符两道显示门控 | ⚠️ 已实现，未实机验证 |
| 键位标签 D F J K | ✅ |
| 中旋识别 + 去重 | ✅ |
| 打击弹跳效果 | ✅ |
| 长按 = 普通按键（相关代码已全删） | ✅ |
| UMM 内置设置面板（手写 GUI） | ✅ |
| 死亡清除 / 检查点跳过 / 只在打歌时显示 | ✅ |
| 诊断日志（可关） | ✅ |

---

## 2. 路径

```
工作区       D:\Techs\workplaces\ADOFAI-4k-transformer
mod 源码     LaneReader\src\LaneReader\        (4 个 .cs)
mod 清单     LaneReader\Mod\Info.json
构建脚本     LaneReader\build.ps1              (-Install 部署到游戏)
构建产物     LaneReader\build\LaneReader.dll
游戏目录     D:\Steam\steamapps\common\A Dance of Fire and Ice
部署位置     <游戏>\Mods\LaneReader\           (LaneReader.dll + Info.json)
游戏日志     <游戏>\A Dance of Fire and Ice_Data\Managed\UnityModManager\Log.txt
```

版本：**ADOFAI 3.3.1**，Unity 6000.3.10f1，**Mono**（非 IL2CPP），加载器 **UnityModManager 0.32.4**

---

## 3. 构建与部署

```powershell
Set-Location 'D:\Techs\workplaces\ADOFAI-4k-transformer\LaneReader'
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Install
```

- 用 `csc.exe`（.NET Framework 4.0.30319）编译，`/langversion:5`（**不能用 C# 6+ 语法**）
- 引用：`netstandard.dll` + 全部 `UnityEngine*.dll` + `Assembly-CSharp.dll` + `UnityModManager\UnityModManager.dll` + `UnityModManager\0Harmony.dll`
- **部署前必须完全退出游戏**，否则 DLL 被占用（`user-mapped section open`）

---

## 4. 架构：六个文件

### `Plugin.cs` — 入口 + 渲染 + 设置面板

- `public static bool Load(ModEntry)` — UMM 入口
- Harmony 补丁挂两个游戏方法（见 `HarmonyHooks.cs`）
- `Tick()` — 每帧逻辑：读谱、排键、判定状态
- `Draw()` — 全屏渲染
- `OnGui()` — **手写 GUILayout 设置面板**（关键，见第 6 节）

### `ChartReader.cs` — 谱面读取（核心）

通过反射读游戏对象。**所有反射成员名都经过 Cecil dump 验证**：

| 游戏成员 | 用途 |
|---|---|
| `scrLevelMaker.instance` | 静态属性，单例 |
| `scrLevelMaker.listFloors` | `List<scrFloor>`，全部地块 |
| `scrFloor.seqID` / `entryTime` / `entryangle` / `midSpin` / `isFake` | 单个音符数据 |
| `scrConductor.instance` | 静态属性 |
| `scrConductor.songposition_minusi` | **属性**（时间轴基准） |
| `scrConductor.hasSongStarted` | 公开字段 |
| `scrController.instance` / `paused` / `currentState` / `currentSeqID` | 运行状态 |
| `scnEditor.instance` / `playMode` / `pausedInPlayMode` | 编辑器播放态 |

### `FingeringPlanner.cs` — 排键算法

**逐行移植 AdoFalling 原版**（`AdoFalling\src\AdoFalling\FingeringPlanner.cs`，94 行）。单窗口模型：

| 间隔 | 处理 |
|---|---|
| `< RollWindowMs`（默认 180ms） | **轮指**：轨道沿当前方向 ±1 走格，每段新的快速段翻转方向 |
| `>= RollWindowMs` | **食指交替**：在轨道 1(F)、2(J) 之间来回 |

初始 `lane = 1`，`direction = 1`。保证不连续两音同轨。

### `Settings.cs` — 配置存储

纯数据类（基类 `System.Object`），用 `JsonUtility` 存 `<游戏>\Mods\LaneReader\Settings.json`。

> 历史遗留：早期版本用 `XmlSerializer` 存过 `Settings.xml`，已删除。改配置字段名时旧键会被 JsonUtility 忽略（无害）。
> 当前 `Settings.json` 里可能还留着已删除的 `ShowHolds` 键，保存一次就会消失。

### `HarmonyHooks.cs` — 两个 Harmony 补丁

只挂 `AsyncInputManager` 的 `Update`→`Plugin.Tick()` 和 `OnGUI`→`Plugin.Draw()`（原因见 6.3）。
两个补丁都是 postfix，只做转发，内部异常不会影响游戏自己的输入处理。

---

### `HandPlanner.cs` — 拆手序（可选手法）

移植 ReADOFAIMacro（MIT）的按键分配算法，只依赖音符时间，不碰 Unity。详见 §14。

---

## 5. 键位模型

玩家用 **DFJK**，轨道从左到右：

```
轨道 0    1    2    3
      D    F    J    K
      左中  左食  右食  右中
```

```csharp
public enum Finger { LeftMiddle=0, LeftIndex=1, RightIndex=2, RightMiddle=3 }
public static Hand HandOf(int lane) => lane <= 1 ? Hand.Left : Hand.Right;
```

---

## 6. 关键踩坑记录（重要，省下重复试错）

### 6.1 UMM 没有自动设置面板

**UMM 不存在"自动生成设置面板"的机制。** 我花了大量轮次去调 `UI.DrawFields` / `[Draw]` 特性，全是空面板。

正确做法（照抄 JipperResourcePack / BetterCalibration / JALib）：**自己设 `modEntry.OnGUI` 并手写 `GUILayout`**。

参考 `JAMod.SetupEventMain` 的 IL：它就是把委托挂到 `ModEntry.OnGUI` 上。

### 6.2 字体：不要用 CJK 动态字体

`Font.CreateDynamicFontFromOSFont` 在这台 Unity 上会给乱码（整面板全是 "180"）或裁掉字形下半截。

**本 mod 的解法：屏幕上所有文字都是 ASCII**（键位标签 D F J K）。设置面板用中文，因为那是游戏自己的字体渲染。

### 6.3 全屏渲染必须钩游戏自己的 OnGUI

`ModEntry.OnGUI` 只在设置窗口打开时触发，**画不了全屏**。

解法：Harmony 补丁挂在 `AsyncInputManager`——它是常驻单例，且**同时有 `Update()` 和 `OnGUI()`**：

```csharp
[HarmonyPatch(typeof(AsyncInputManager), "Update")]  → Plugin.Tick()
[HarmonyPatch(typeof(AsyncInputManager), "OnGUI")]   → Plugin.Draw()
```

### 6.4 `scrFloor.grade` 不能当"未判定"哨兵

**未判定的初始值是 `TooEarly`(0)，不是 `None`(-1)。** 用它判断会让**全部音符瞬间消失**。

本 mod 现在**完全不读 grade**（打击效果改成纯时间驱动）。

### 6.5 `songposition_minusi` 是属性，字段叫 `_songposition_minusi`

带下划线！读错字段会得到 null → 兜底 0 → **音符静止不动**。

### 6.6 不要给时间轴加偏移或平滑

试过锚定(`offset = 首音 - 提前量 - 时钟`)和指数平滑，**都会让音符与音乐脱节**。

正确做法：`DrawTime()` 直接返回原始音频时钟。

### 6.7 中旋（midspin）表示与处理

- `scrFloor.midSpin` 是**地板自己的属性**，**中旋是要按的按键**
- 中旋在数据里是**成对的两个砖块、同一时刻** → 必须**去重**（否则每个中旋出现两次）
- 去重**只针对中旋**（`note.MidSpin || prev.MidSpin` 且时间相同才合并）

### 6.8 "绘制边界"曾经完全写反

用 `_judgeCursor` 同时当下界和上界 → 只剩 1 个音符可绘制（日志 `drawable=1/1133`）。

**游戏会预生成全部地块**，所以无法用光标推断"哪些已生成"。现在改成简单的时间判断。

### 6.9 起点跳过：必须用"锁定的起点"，不能用"游戏当前位置"

用 `currentSeqID`（每帧推进）做过滤 → 起点块**先渲染再消失**，看起来像"多一个音符"。

正确做法：**进入打歌时锁定一次**起点，用 `<=`（起点本身也跳过）：

```csharp
private int _runStartSeqId = -1;   // 锁定值，运行期间不变
if (_runStartSeqId > 0 && _notes[index].SeqId <= _runStartSeqId) return false;
```

### 6.10 重开状态重置：用"状态跳变"，不能用"谱面重建"

**死亡复活不重建地块**（游戏复用对象），所以 `Build()` 不触发。

正确做法：检测状态离开 `Fail`/`Fail2`/`Won`：

```csharp
bool restarted = _sawRunEnd && !ended;
```

### 6.11 下落与弹跳的基准必须一致

下落时底边在 `judgeY`，弹跳时底边在 `judgeY + noteH/2` → 差 22px → **音符到线时"回闪"**。

现在两者都用 `boxBottom = judgeY + noteH/2`。

### 6.12 PowerShell 脚本必须存成带 BOM 的 UTF-8

无 BOM 的 `.ps1` 会被 PowerShell 按 GBK 读 → 中文注释截断字符串 → 语法错误。

`.vbs` 必须存成 **UTF-16LE**。

### 6.13 「按任意键开始」不能用 `hasSongStarted` 判断

**现象**：关卡加载完、还没按键，音符已经在下落。

**原因**：旧的门控用 `scrConductor.hasSongStarted` 区分「按任意键」和「321 倒计时」，
但那个标志在同一局游戏的第二个关卡时已经是 true，区分不出来 → 那一屏也在滚谱。

**正确做法：用时钟本身。** 那一屏的 `songposition_minusi` 要么停在上一局的位置，要么
还在往 0 之前预滚 —— 两种情况都落在「时钟没在往正方向走」或「时钟 < 0」里。
`ChartReader.TimelineLive()` 就是这两条的合取：`_clockMoved`（在 `AdvanceRenderClock`
里比较前后帧）为真，且 `SongPosition() >= 0`。冻结、时钟为负、谱面重建都会清掉它。

诊断日志为此加了字段：`state=` / `song=` / `live=` / `frame=` / `notes=` —— 下次再出
这类问题，先看这五个值，不要猜。

---

## 7. 显示门控：两道门

渲染拆成两半，各有各的门（`Plugin.Draw` / `Plugin.NotesVisible`）：

| 半 | 门 |
|---|---|
| 静态框（轨道 / 判定线 / 判定框 / 键位标签） | `ChartReader.LevelStanding()` |
| 音符 | `LevelStanding() && Playing() && TimelineLive()` |

`Playing()` —— 音符「在打歌状态里」：

```csharp
1. ctrl.paused                          → 关
2. scnEditor.instance.playMode          → 开（编辑器测试播放）
3. state ∈ {Fail, Fail2, Won}           → 关（死亡/通关）
4. state ∈ {PlayerControl, Checkpoint}  → 开
5. state == Countdown && hasSongStarted → 开（倒计时 321）
6. 其他                                  → 关
```

`LevelStanding()` —— 框「关卡立着」：`IsFrozen()` → 关；编辑器播放 → 开；
state ∈ {Start, Countdown, PlayerControl, Checkpoint} → 开；其他 → 关。

`TimelineLive()` —— 音符「时间轴真的在走」：见 6.13。

| 场景 | 框 | 音符 |
|---|---|---|
| 主菜单 / 编辑器编辑中 | 关 | 关 |
| 「按任意键开始」 | **开** | **关**（时钟停在上一局位置 / 还没到 0） |
| 倒计时 321 | 开 | 开 |
| 打歌中 | 开 | 开 |
| 暂停 / 死亡 / 通关 | 关 | 关 |
| 编辑器测试播放 | 开 | 开 |

> 旧版这里写的是「按任意键开始 → 关」，靠 `hasSongStarted`。已作废，原因见 6.13。

---

## 8. 打击效果

**纯时间驱动，不读游戏判定。**

音符到达判定线后（`age = now - HitTime >= 0`）：

```csharp
t = age / 0.30                      // 0..1
scale: t<0.25 时 1 → 1.10（快速放大）
       t>=0.25 时 1.10 → 0.82（持续缩小）
alpha: 1 - t*t                     // 渐透明
```

**底边固定在判定框底边**（`boxBottom = judgeY + noteH/2`），向上生长：
`Fill(new Rect(x + (laneW-w2)*0.5f, boxBottom - h2, w2, h2), ...)`

判定框：每条轨道一个与音符同尺寸的矩形框，**框的中轴线 = 判定线**。

---

## 9. 环境坑

| 坑 | 说明 |
|---|---|
| **没有 `pwsh`** | 只有 Windows PowerShell 5.1，路径 `C:\windows\System32\WindowsPowerShell\v1.0\powershell.exe` |
| **不要碰 `doorstop_config.ini`** | UMM 和 BepInEx 抢这个文件的 `target_assembly`。用户环境是 **UMM**，此文件必须指向 `A Dance of Fire and Ice_Data\Managed\UnityModManager\UnityModManager.dll` |
| **BepInEx 已移除** | 之前装 BepInEx 装 AdoFalling 时覆盖了 `doorstop_config.ini`，导致用户 5 个 mod 消失。已还原 |
| **别直接开 Steam 启动** | 桌面上有两个快捷方式（`launch-adofai.ps1` + `.vbs`）会切换加载器 |

桌面上还有两个遗留快捷方式（「原版 Mods」/「4K 下落式」），后者依赖已删除的 BepInEx，**可以删掉**。

---

## 10. 未使用但保留的代码

- `ChartReader.GradeOf()` / `CurrentSeqId()` / `IsFake()` / `SongRunning()` — 当前无人调用
- `Note.Direction` — 从 `entryangle` 算的，手序算法不用
- `Note.Floor` — 保留给未来的判定读取

想要干净可以删，但留着无害。

---

## 11. 下一步可能的改进

1. **真正的双窗口手序**（用户曾提出但最终选择 AdoFalling 原版）
   - 小窗口：中指轮指
   - 大窗口：换手食指
   - 约束：不允许同一只手连续两个中指 → 需两手交替 `J→K→F→D→J...`

### 已完成（2026-10-06 收尾清理）

- 设置项标签「打击反馈（游戏判定）」→「**打击弹跳**」（`Plugin.cs` 的 `OnGui`）
- 删除遗留诊断行 `chart head (s): ...`（`ChartReader.Build`）
- 二者已构建并部署（`LaneReader.dll` 26,112 字节），源码与部署哈希一致

> ⚠️ **当前部署的构建尚未实机验证。** 上一次游戏会话（日志 22:59:43 结束）跑的是
> **删除长按代码之前**的旧 DLL；「长按 = 普通按键」那批改动加上本次清理都没进过游戏。
> 下一次交接的第一件事应当是：跑一局，确认 `chart:` / `fingering:` / `frame:` 三行正常。

---

## 12. 调试方法

用户反馈现象后：

1. **先读日志**，不要先改代码：
   ```
   Select-String -Path "<游戏>\A Dance of Fire and Ice_Data\Managed\UnityModManager\Log.txt" -Pattern 'LaneReader|frame:'
   ```
2. 关键日志行：
   - `chart: N notes, first X.XXXs, last X.XXXs, zero-time N, non-monotonic N`
   - `fingering: N notes -> roll R, tap T, window Wms, lanes D=.. F=.. J=.. K=..`
   - `frame: t=..s raw=..s win=..s onscreen=N #I in X.XXXs frozen=B | seq=.. drawable=N/M`
3. `onscreen` 正常但屏幕没音符 → 渲染/坐标问题
4. `drawable` 远小于总数 → 绘制门控问题
5. 需要新增诊断时，往 `FrameDiag()` 里加字段

**用户明确要求：少烧 token。** 不要重复全文件重写、不要重复列目录、不要读已确认过的文件。

---

## 13. 版本控制

本目录是一个 git 仓库（分支 `main`）。

| 内容 | 是否纳入 |
|---|---|
| `LaneReader/`（6 个 `.cs`、`Mod/Info.json`、`build.ps1`、`tools/HandPlanCheck.cs`） | ✅ |
| `HANDOFF.md` + 根目录的 `install-adofalling.ps1` / `uninstall-adofalling.ps1` / `launch-adofai*.ps1` / `.vbs` | ✅ |
| `LaneReader/build/`（`LaneReader.dll`、`csc.log`） | ❌ 构建产物，`build.ps1` 会重建 |
| `AdoFalling/` | ❌ **独立仓库**：`CatciSurn/AdoFalling` 的克隆，自带 `.git`，必须在它自己的目录里提交 |

- `.gitattributes` 里是 `* -text`：全仓库关闭行尾转换，保证 `*.ps1` 的 UTF-8 BOM 和
  `launch-adofai-hidden.vbs` 的 UTF-16LE 原样进出（原因见 6.12）。
- 没有用 submodule 收编 AdoFalling：它工作区里有未提交的本地改动（`ControlPanel.cs`），
  submodule 只记录 commit，那处改动会脱离版本控制。
- 部署（`build.ps1 -Install`）**不在**仓库里：DLL 产物被忽略，改动源码后要自己重新构建部署。

---

## 14. 拆手序（`HandPlanner.cs`）

设置面板「排键」里切换手法，**默认仍是 AdoFalling 原版**：

| 手法 | 实现 | 说明 |
|---|---|---|
| AdoFalling 原版 | `FingeringPlanner.cs` | 间隔 < 窗口 (±1 走格)，否则 F/J 交替 |
| 拆手序 | `HandPlanner.cs` | 移植 [ReADOFAIMacro](https://github.com/TonyLimps/ReADOFAIMacro)（MIT） |

### 它的模型

1. **BPM 归一化**：`while (bpm <= 550) bpm *= 2; while (bpm > 1100) bpm /= 2;`
   再 `if (bpm > 最大硬抗BPM) bpm /= 2;` —— 这就是「窗口 BPM 不断翻倍」的实现。
   「最大硬抗 BPM」是用户可感知的旋钮（建议 800~1100，**越低越偏轮指**）。
2. **窗口 = 归一化后的一拍**：累积音符时长直到 `degree ≥ 180`，窗口内的音符数就是 `count`。
3. **`count` 决定指法**，**窗口长度（degree）决定下一窗换不换手**：
   `≤270` 换 / `≤450` 不换 / `≤630` 换 / `<720` 不换 / `≥720` 回主手。
   慢音（1 拍以上）因此变成**单指重复**，而 1 拍的音才是两手交替 —— 和「档1 单指、档2 主副手交替、
   档3 轮指」的说法完全对上。

**两个关键结论**：

- **只需要音符时间。** 参考实现解析文件里的转角 / twirl / pause / 变速，全都只是为了**重建时间**；
  游戏里每个音已经有 `entryTime`，所以这些一个都不用复刻。
- **`degree ≡ 3 × Δt(秒) × 归一化BPM`**，也就是「窗口时长（归一化拍）×180」。

### 4K 适配

参考实现每只手 4 根手指（手8/手16 布局），DFJK 每只手只有 2 根，所以指法表按**手容量**参数化
（`HandPlanner.Assign(..., handKeys, outerRoll)`）：

| count | 参考实现 | 4K（handKeys=2） |
|---|---|---|
| 1 | `p1` | `J`（下一窗换手 → `F`） |
| 2 | `p2 p1` | `K J`（外轮；`OuterRoll=false` → `J K` 内轮） |
| 3 | `p3 p2 p1` | 递归：`p1` + `n2 n1` = `J D F` ← **待用户拍板** |
| 4 | `p4 p3 p2 p1` | 正好一个完整外轮周期 `K J D F` |
| >手容量 | 递归对半分手 | 同左 |

工坊 39 张谱 17558 个窗口的 count 实测分布：**count1 85.1% / count2 14.0% / count3 0.9% / 无 ≥4**。

### 验证方式（离线 oracle，可复用）

`LaneReader/tools/HandPlanCheck.cs` + 临时克隆的 ReADOFAIMacro（打了「只分析不发按键 + 导出
每个音时间与逐窗口决策」的补丁）：

1. 宏导出 `LEVELBPM / BASEBPM / MAXBPM / TSX`（17 位有效数字）+ 逐窗口 `WIN` 决策；
2. `HandPlanCheck <dump> 4` 用同样时间跑移植版，打印同格式决策；
3. 逐窗口比对 count / degree / pref 与指法符号。

**结果：28 张谱面、17558 个窗口，决策与指法 100% 逐窗口一致。**

踩过的坑（都别重犯）：

- 宏的 `WIN` 行默认只有 6 位有效数字 → 必须 `setprecision(17)` 导出，否则度差 0.25%；
- `TS` 是取整毫秒，而它的分组用的是 double 角度 → 要用 `TSX`（精确时间）对比；
- **末尾哨兵**：宏会把最后一个角度复制一份，所以**最后一个音的度数是前一段间隔**，不是 0；
- **绝对不要给阈值加容差或贴网格**：谱面真的会产生 719.99982 这种略低于阈值的窗口，
  抹平它会错误地重置整首手序。忠实做法是原值比较 + 1e-9 容差。

### 还没做的

- ⚠️ **关卡 BPM 的实时来源未验证**：现在读 `scrLevelMaker.bpm_forUnityUseOnly`
  （`ChartReader.LevelBpm()`），日志会打印 `hand split: N notes, level bpm X`。
  如果它不是谱面基础 BPM，窗口大小会整体错一档；
- `count=3` 在 4K 上的指法（上表）是按递归推的，需要用户确认；
- 实机验证：面板切「拆手序」，看日志 `hand split:` 行与画面。

---

## 15. 显示项（新增）

设置面板里新增的部分：

| 设置 | 说明 |
|---|---|
| 水平偏移 / 垂直偏移 (px) | 整个叠加层的位移，按 1080p 基准像素缩放，范围 ±1200 / ±800。垂直偏移移动判定线，音符/判定框/小节线都跟着走 |
| 轨道背景透明度 | 原来是硬编码 `0.06`；0 = 只留轨道两侧的边线 |
| **背景明暗** | 外观里切换。所有中性元素（轨道、判定线、判定框、键位标签、小节线）走 `Plugin.Ink()`：暗背景画白、亮背景画黑 |
| 音符边框 + 边框粗细 | 边框用**与背景相反**的 `Ink()` 色（暗背景 → 白框，亮背景 → 黑框），先铺满边框色再把原色内缩填进去 —— 外框尺寸与原来完全一致 |
| 小节线（可折叠） | 见下 |

> 边框颜色最初写的是「原色 ×0.35 加深」，后来按需求改成与背景相反的中性色：加深色在同色系音符密集时不够分辨，
> 中性反色才真正解决可读性。

### 小节线

**拍网格按 TBPM（真实 BPM）铺，随变速自动变化，且锚在音符上。**

**TBPM 的来源是抄 JipperResourcePack 的**（`OverlayContents/Overlay.cs` 的 `UpdateBpm`）：

```csharp
// JRP 原文
float bpm  = (float)(conductor.bpm * conductor.song.pitch * VersionSafe.GetPlanetSpeed(scrController.instance));
float cbpm = floor.nextfloor ? (float)(60.0 / (floor.nextfloor.entryTime - floor.entryTime) * conductor.song.pitch) : bpm;
```

- **TBPM = `scrConductor.bpm × song.pitch × 行星速度`**，其中行星速度在
  `VersionSafe.cs` 里是 `controller.playerOne.planetarySystem.speed`（新版本）
  或 `controller.speed`（旧版本）。我们的 `ChartReader.PlanetSpeed()` 两条都试。
- 每块砖的拍数 = **`Δt × TBPM ÷ 60`**。对正常 180° 砖这就是 1 拍；twirl 是 2 拍；
  伪双押/半拍砖会得到 <1 拍 —— 这也正是 JRP 那个 `checkPesudo`（伪双押检查）在做的事。
- **TBPM 只有实时才知道**（游戏在变速事件到来时才更新它），所以 `ChartReader.SampleBeatTempo`
  边打边学：已经走过的砖记录自己的 TBPM，**还没走到的砖先借用最近一次的 TBPM**，
  行星一到就换成自己的。学到新值就重算拍线。
- **回落链**：TBPM → 几何转角（`entryangle` 差 / 180，180°=1 拍）→ 一砖一拍。
  几何那条是**实测验证过**的（工坊 `2118022119`，170 BPM：180° 砖 = 352.94 ms = 正好 1 拍，
  300° 砖 = 1.667 拍，60° 砖 = 0.333 拍），所以在 TBPM 读不到时网格依然是对的。
- **细线 = 每拍，粗线 = 每 `每小节拍数` 拍**（3=3/4，4=4/4，可 2~8）；
  「小节起点偏移」按**整拍**挪动粗线，用来对付弱起。
- 与音符同一基准（`boxBottom`），所以线正好在自己的拍子上压过判定线；
  一张砖跨多拍时会在砖内部**插值**出中间的线。
- 只画在轨道区，或「横跨全屏」；透明度 / 粗细可调。
- 门控与音符相同（`drawNotes`），所以「按任意键开始」那屏不会出现停住不动的线。

> 这条路一次解掉两个坑：小节线不再需要 `bpm_forUnityUseOnly`（那个值在本机读不到，
> 曾让小节线**静默不画**），也不再受「变速段落网格对不上」的限制。
> 每次读谱打一行（含来源与采样进度）：
> ```
> beats: 816 tiles, 1024.5 beats, per tile 0.333..2.000 (avg 1.255), 1024 lines |
> tbpm 150.0, laid out from tbpm 700 / angles 0 tiles, sampled 700/816, radians=False, angles unreadable=0
> ```
> **自检方法**：`from tbpm` 占多数 → TBPM 读到了；`tbpm 0.0` → 三个字段都没读到，网格会
> 退到几何或一砖一拍。`sampled k/N` 会随打歌推进增长（这是「边打边学」的进度）。
