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

## 4. 架构：七个文件

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

### `NoteImage.cs` — 自定义音符贴图

从磁盘读 png/jpg 当音符画，带热重载与失败状态显示。详见 §16。

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
| 音符 | `LevelStanding() && TimelineLive() && SongRunning()` |

> **音符那一道门为什么是这三项**（2026-10-07 实机两次踩坑的结论）：
> - `LevelStanding()`：关卡立着（死亡/通关/菜单/编辑器编辑 → 关）
> - `TimelineLive()`：时间轴动过（挡掉"时钟还停在上一局位置"的新谱面）
> - `SongRunning()`：**音乐真的开始了** —— 这是最后加的一项，也是关键的一项
>
> 用 `Playing()`（状态表）判过两次，都不行：暂停时游戏把状态报成 `Start`，
> 而"按任意键开始"那屏**也是** `Start`，状态表分不开这两者 ✗。日志把它们并排摆出来了：
>
> ```
> 暂停：        state=Start song=1 live=1 -> 要画（音符停在原地）
> 按任意键开始：state=Start song=0 live=1 -> 不能画
> ```
> `song`（= `scrConductor.hasSongStarted`）一分就开。`Playing()` 现在只剩日志用途。

`LevelStanding()` —— 框「关卡立着」：`RunEnded()` → 关（死亡/通关）；编辑器播放 → 开；
state ∈ {Start, Countdown, PlayerControl, Checkpoint} → 开；其他 → 关。
**注意它不再看 `IsFrozen()`**：暂停时框要留着（`IsFrozen` 仍然喂给时钟冻结那一路）。

`TimelineLive()` —— 音符「时间轴真的在走」：见 6.13。

| 场景 | 框 | 音符 |
|---|---|---|
| 主菜单 / 编辑器编辑中 | 关 | 关 |
| 「按任意键开始」 | **开** | **关**（song=0） |
| 倒计时 321 | 开 | 开 |
| 打歌中 | 开 | 开 |
| 暂停 | **开** | **开**（音符停在原地） |
| 死亡 / 通关 | 关 | 关 |
| 编辑器测试播放 | 开 | 开 |

> **历史**：6.13 那次得出的结论是「不能用 `hasSongStarted` 区分开局提示」，于是这一屏改由
> 时钟（`TimelineLive`）来挡。后来为了让暂停保留音符，把 `Playing()` 从门控里去掉，
> 结果重试时的开局提示又把音符放了出来 —— 因为 `TimelineLive` 在**重试**时不会清零
> （没有重建谱面）。最后成型的判据是三项合取，`song` 只负责"音乐开没开始"这一件事，
> 正好把暂停和开局提示分开（见上表）。

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

### 已知良好状态（2026-10-07 实机确认）

| 项 | 值 |
|---|---|
| 提交 | `21c0ccb` |
| 手法 | 拆手序（`Settings.json` 里 `FingeringMode = 1`） |
| 最大硬抗 BPM | 506 |
| 轮指方向 | 外轮 `OuterRoll = true` |
| 实机日志 | `hand split: 1123 notes, level bpm 0.0 (unreadable!), windows 598, count1=184 count2=311 count3=95 count4+=8, base 400.00bpm, outer roll, rolls 414, taps 184 hand switches 448` |

**换手率 448 / 598 窗口 = 75%** ← 这就是"手感对了"的那一版。

> ⚠️ **上表是 `v0.2.0` 的标定，默认值后来被用户要求改过**（`v0.3.0`）：
> 流速 `930 → 1000`、最大硬抗 BPM `506 → 850`。
> **`850` 意味着兜底的 800 不再被减半**（`800 > 850` 不成立），所以 base = **800** 而不是 400，
> 每拍的度数翻倍、窗口里的音符数减半 → 轮指和换手都会明显变少。
> 这是用户明确要的默认值；上面 base 400 那条记录仍然有效，它证明的是
> 「兜底 100 → 归一化 800 → 按上限减半」这条链子怎么工作，不是「默认必须 506」。

> **里程碑：这一版定为 `v0.2.0`**（git tag `v0.2.0`）。同一次改动还改了默认值：
> 默认手法 = 拆手序、默认流速 `930 px/s`（原 620 的 1.5 倍）、默认硬抗上限 `506`（实测值），
> `Mod/Info.json` 的 Version 同步为 `0.2.0`。
> 注意：这些是**新装默认**；已经存过 `Settings.json` 的机器会继续用它自己保存的值。

> ⚠️ **`ApplyHandSplit` 里那行 `double bpm = Chart.LevelBpm();` 是故意保留的，别"修"它。**
> 本机读不到 BPM → 传 0 给 `HandPlanner` → 落到它自己的兜底 100 BPM → 归一化 **800** →
> 被滑条（506）减半成 **400** → 每个音的度数落进「≤270 换手」档。
>
> 我曾在 `9fc7c54` 把它换成「从音符间隔估计 BPM」，结果 base 变成 667、
> 换手率从 75% 掉到 19%，用户直接报「不换手轮指」。**动这一行之前先读这段。**
>
> 副产品：这个滑条在恢复后的规则下**只有两档**（<800 → 400；≥800 → 800），
> 因为参考实现只做一次减半。要细调只能另加一个明确命名的系数（默认 1.0 = 纯参考实现）。

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
| 水平偏移 (px) | 整个叠加层的横向位移，按 1080p 基准像素缩放，范围 ±1200 |
| ~~垂直偏移 (px)~~ | **已删除**：它按像素移动判定线，而「判定线高度」按屏高比例移动同一条线 —— 两个控件是同一个功能做了两遍（用户报的重复）。现在**纵向只有「判定线高度」**，旋转轴心也跟着它走。旧 `Settings.json` 里的 `OffsetYPx` 会被 `JsonUtility` 忽略，无害 |
| 轨道背景透明度 | 原来是硬编码 `0.06`；0 = 只留轨道两侧的边线 |
| **轨道长度 (px)** | 见下。默认 `900`，`0` 恢复旧行为（一直画到屏幕顶端） |
| **背景明暗** | 外观里切换。所有中性元素（轨道、判定线、判定框、键位标签、小节线）走 `Plugin.Ink()`：暗背景画白、亮背景画黑 |
| 音符边框 + 边框粗细 | 边框用**与背景相反**的 `Ink()` 色（暗背景 → 白框，亮背景 → 黑框），先铺满边框色再把原色内缩填进去 —— 外框尺寸与原来完全一致 |
| 小节线（可折叠） | 见下 |
| **显示帧数** | 默认**关**。打开后在**判定线下方**（有键位标签时在其下一行）居中画 `143 fps 7.0 ms`，走 `Ink()` 所以跟随明暗背景、也跟随整体旋转。默认关是因为它是诊断信息，不是playfield 的一部分 |

> 边框颜色最初写的是「原色 ×0.35 加深」，后来按需求改成与背景相反的中性色：加深色在同色系音符密集时不够分辨，
> 中性反色才真正解决可读性。

### 轨道长度（不要再让它自动算）

轨道原来是从 **y=0（屏幕顶端）画到判定线**，也就是长度 = `judgeY`。这等于把长度绑死在
**判定线高度** 上：用户一调判定线，轨道就"自己变长变短"，看上去像 bug（他报的就是这个）。
`GUIUtility.RotateAroundPivot` 之后更明显 —— 轨道顶端永远贴着屏幕边，一转就伸到画面外。

现在：

```csharp
float laneLen = Cfg.LaneLengthPx > 0f ? Cfg.LaneLengthPx * refScale : judgeY;   // 0 = 旧行为
float laneTop = judgeY - laneLen;                                              // 从判定线往上量
```

- 长度是**自己的设置**（`Settings.LaneLengthPx`，默认 `900`），改判定线只是让整条轨道**平移**；
- `0` 是显式的「自动到屏幕顶端」，保留旧观感（面板滑条上写明了）；
- 默认 900 ≈ 1080p 下判定线 0.18 时的旧长度（885.6），所以默认观感几乎没变。

**`laneTop` 同时是「可见区域的上边界」，音符和小节线都在这里出现**（用户要求）：

- 音符：`if (y < laneTop) continue;` —— 整颗音符一出现在 `laneTop` 下方就完整画出来。
  以前是按屏幕裁的（`y + noteH < 0`），`laneTop` 在屏幕上方（默认值就是这种情况）时，
  两种写法看起来一样；把轨道调短，音符就会出现得晚一些，反应时间 = `(judgeY - laneTop) / speed`。
- 小节线：扫描上界从「屏幕顶端的时间」改成 `(boxBottom - laneTop) / speed`，
  并且 `if (y < laneTop) break;`（线按时间递增走，y 单调递减，所以碰到第一条越界的就能停）。
- 判定框、判定线、键位标签不受影响（它们都贴着判定线）。

### 帧数怎么来的（`Plugin.MeasureFrameRate`）

- 计数器放在 **`Tick()` 开头**（`AsyncInputManager.Update` 的 postfix），所以数的就是**游戏自己的帧**；
  叠加层没有自己的循环，不存在"叠加层的 FPS"这回事。
- 每 **0.5 秒**取一次平均：`fps = 帧数 / Δ时间`，`ms = Δ时间×1000 / 帧数`。
  取平均是为了不跳数；`Time.unscaledDeltaTime` 不受游戏内暂停/慢放影响。
- 屏上文字由 `FrameRateText()` 出（`"-"` = 还没采到样），日志 `frame:` 行也会带上同一串，
  所以日志里能直接对着看：`frame: 143 fps  7.0 ms | state=...`。
- 位置：`judgeY + (有键位标签 ? 34 : 6) px`，水平居中，宽度铺满屏幕。
  音符和打击弹跳都在 `boxBottom` 以上，所以文字不会被音符盖住。

### 实验性：3D / 长按 / 平滑下落 / 娱乐（2026-10-09，2026-10-10 两轮重写）

**3D 轨道**（`ThreeDLanes` + `ThreeDStrength`，默认关）：灭点在**轨道顶端**（判定线上方「轨道长度」那么远）。
整个下落用**一台针孔相机**投影（`Plugin.View` / `Plugin.Field`）：

- `rise(d) = L·(u-eye)/u`、`scale(d) = eye/u`，`u = d + eye`，`eye = L / 透视强度`（默认 1.0，范围 0.3~3）；
  d = 该点在判定线上方的**下落距离**（像素），L = 轨道长度。`u` 有下限 `eye*0.05`，
  防止相机平面（d = -eye）把投影打成无穷。
- 于是 `scale = 1 - rise/L`：离开轨道组中线的距离 ∝ 离判定线的高度 —— 这就是灭点性质本身。
  轨道边缘因此是**直线**，四条轨道共用一个顶点。
- **`rise` 故意不截断 d<0**：音符是躺在轨道上的**板**（`FillNotePlate`），近边在
  「下落到近边距离」处、远边在远边处，所以在过线前的最后半个音符高里，近边真的落在判定线
  下方。这就是"真的 3D 变换"和"缩小一个矩形"的区别：板的高度是两条边的投影距离，
  越往上沿轨道方向被压得越扁，而宽度只随轨道收窄。
- 轨道体是一叠 96 条横带（IMGUI 只能填轴对齐矩形），**两条边缘各是一个旋转矩形**
  （`DrawEdgeLine`，从底角直达灭点）。这是第二版修的锯齿：横带的边缘每 9px 横跳 1.25px，
  在 2px 白线上就是锯齿；旋转矩形是精确直线，带子的台阶被压在它底下（最坏 0.6px）。
  旋转用 `GUIUtility.RotateAroundPivot`（反编译确认实现是 `TRS(pivot, Euler(0,0,angle))`），
  GUI 的 y 向下，所以 `atan2(dy,dx)` 直接就是屏幕角度。
- 音符/重影/长按长条都走 `FillNotePlate` / 同一套 `View`，所以它们**一起**透视；
  贴图（自定义图片）按每个带子的高度取图片对应的横条，因此图片也跟着压扁。
- 判定线/判定框/标签都在 d=0 那一端，不受影响。
- 3D 关掉时 `View.Flat` 让 rise/scale 退化成恒等，`FillNotePlate` 的带子数变成 1，
  于是平面路径**逐像素**还是原来的矩形。
- 音符在 3D 下的消失条件从「到轨道顶端」改成**投影尺寸**（`noteH·scale < 1.5px`），
  因为轨道远端是灭点、无限远，不能再用顶端当边界。
- **第一版（已废弃，别再退回）**：只缩宽度、y 全线性 —— 轨道收敛了但沿着轨道下落的东西
  没有，看起来像四条线在空中交叉，而且音符只是被缩小。
- 顺带对齐修正：小节线原来在拍点时刻位于 `boxBottom`（判定线下方半个音符高），
  现在和音符中心一样正好在拍点过线。

**长按（hold）**（`HoldSupport`，**默认开**）：

- `ChartReader` 反射读 `scrFloor.holdLength`（**int，-1 = 不是 hold，>=0 才是**），
  1 个单位 = 2 拍（`scrLevelMaker` 里入口时间加的是 `holdLength*2*timeBetweenAngles`）。
- **`HoldSeconds` = 到下一颗音符的整个间隔**。这是查游戏实现得出的：
  `scrHoldRenderer.Start/CreateMesh` 里 `tailFloor = startFloor.nextfloor`，游戏自己画的长条
  就是从这块砖连到下一块砖。所以长条末端和下一颗音符的顶边永远精确重合。
  **不要再按 hold/travel 拆间隔**（那样每根长条短一格，末端悬空）。
- **长按不随开头音符消失**：开头过线后长条仍然画，近端夹在判定线上（线以下算在线后面），
  于是判定线从下往上慢慢吃掉它，直到尾巴（下一颗）到达才消失。
  拖尾重影同理：音符过线后重影继续下落，每个重影在判定线处裁掉下半截。
- **开头的打击弹跳不缩回**：长按的弹跳涨到打击尺寸后保持不变（`popAge = age - HoldSeconds < 0`），
  直到尾巴到达才开始缩小淡出，衔接处尺寸连续。
- 渲染顺序必须是 **长条 → 拖尾重影 → 音符本体**：长条从音符顶边往上长，拖尾的重影也在那一段，
  长条画在后面会把整条拖尾盖掉（长按音符看起来没有拖尾）。
- `ChartReader.ShouldDraw` 对长按音符放行到「过线 + 整个长按时长 + 弹跳窗口」，
  否则长条会在中途被裁掉。
- 宽度 = 轨道宽 × 「线条粗细」比例，3D 下逐带投影；颜色/alpha 单独设置（可四轨统一或单独）。
- 拆手序：每颗长按往手序输入**多喂一个"松手"事件**（时间 = 开头 + 整个间隔 = 下一颗的时刻），
  `HandPlanner.Assign` 多一个 `int[] holdHead` 参数，整首规划完后把松手强制成开头的列。
  松手仍参与它所在窗口的计数/形状 → 会"影响之后的手序"；手规则读窗口长度不读列，所以覆盖不会破坏状态机。
  松手不画成音符，长条本身就是它的位置。`HoldSupport` 在手法重算指纹里，切换会重排。
- 参考程序不支持 hold：它不读 `holdLength`，时间轴只由转角推出，长按谱在它那里连时间都不对。

**娱乐小分类**（默认全关，纯好玩）：

| 功能 | 做法 |
|---|---|
| 轨道乱飞 | 每轨、每轴一个 Perlin 噪声路径（`unscaledTime`），轨道本体/边线/判定框/标签/音符一起漂；幅度、速度可调 |
| 轨道持续旋转 | 在 `Tick` 里积分角度（不是直接读时间，改速度不会瞬移），叠加在「整体旋转」上，绕判定线中点 |
| 四花瓣 | `LaneX()` 让每条轨道都画在正中间（底边落在判定线上），再各自绕**判定线中点**转 90°×i：0 上 1 右 2 下 3 左；轨道几何/标签/判定框都在各自旋转里画，音符改成**按轨道分趟**画（pass=轨道号），所以音符沿自己的花瓣往外落。四个底边中心重合在同一个点，**中间没有方块**（旧版绕的是中心小方块四条边的中点，方块空着像个洞，已删）。关掉时 pass 只跑一次、不旋转、不过滤，等同旧行为 |

> `Field.Centre()` / `Field.SlotCentre()` 都基于 `LaneX()`，所以 3D 和花瓣可以叠加。

### 自定义音符图片 / 长按图片（`NoteImage`，两个槽位）

- `NoteImage` 现在有两个槽位：`NoteImage.Note`（音符）和 `NoteImage.Hold`（长按线条），
  各自独立加载、各自的状态行和日志前缀（`note image:` / `hold image:`）。单例挂在
  `NoteImage.Instance` 上（原来是静态类，两个槽位后改成实例）。
- **有自定义图片时，音符就只是那张图**：不画音符色块，也不画边框（边框曾经改成"绕图一圈"，
  仍然是在替玩家决定图片外面该有什么，所以整个删掉了）。只把音符自己的 alpha 乘上去。
  「透明像素一度显示成白色」的根就在这儿：图片底下曾经垫着一整块描边色矩形。
- 图片按**平面**音符框做 contain，再把那个框投影出去（`FillNotePlate`）：按投影后的板去
  contain 会让同一张图在每个高度被压成不同比例，"保持宽高比"就没意义了。
- 拖尾重影用同一张图（每个带子取图片对应的横条），所以补帧的拖尾和音符长得一样。
- 长按图片沿长条方向拉伸铺满，宽度仍按「长条粗细」的比例；按原样画，
  **长按的颜色和透明度不再乘上去**（面板里写了）。
- 被判定线吃掉时，图片按「音符/长条的真实范围」取样：`FillNotePlate` 的 `cut` 和
  `DrawHoldBar` 的 `headTop/visible` 是分开的两个量，否则会看到整张图被挤进剩下那一截。
- png/jpg 从磁盘读，最多每秒检查一次时间戳，可热替换；解码走反射（Unity 6 的
  `ReadOnlySpan<byte>` 重载在 C# 5 下报 CS0012）。

### 布局、背景、判定线、主题、外链（2026-10-10）

- **默认值**：轨道长度 `900 → 1620`（= 1.5 屏，见 `Settings.LaneLengthPx` 的注释），
  `HoldSupport` 默认**开**，透视强度 `1.2 → 1.0`。已经有 `Settings.json` 的机器不会自动套用：
  面板「其它 → 恢复默认 + 保存」或删掉 `Mods\LaneReader\Settings.json`。
- **判定线**：「显示判定线」的开关从「显示」搬到「布局」，紧跟在「判定线高度」下面 ——
  玩家报过"找不到关闭判定线的功能"。同处新增「判定线横跨全屏」（默认开）和
  「判定线长度 (% 屏宽)」（不横跨全屏时用它，以**叠加层中线** `cx` 为中心缩短，不是屏幕中心）。
- **全屏纯色背景**（`BackgroundEnabled` / `BackgroundColour`，默认关、默认黑色 50%）：
  在 `Draw()` 里、整体旋转**之前**用屏幕坐标画一整屏矩形 —— 它要盖住整个屏幕，
  旋转轨道时不能露出关卡的角。颜色的 A 就是透明度，A=1 是全不透明。面板放在「显示」区帧数开关后面。
- **主题**多了一个「纯黑底 经典白线」（放最后，不动已有主题的下标）：套用时把背景打开并设成
  纯黑不透明，其余按经典配色。**主题的硬规则**：轨道色和音符色绝不能相同，至少亮度/色相要拉开
  （写在 `Themes` 类注释里）—— 极简纯白原来是两个纯白，把轨道底色透明度拉满就分不清了，
  现在轨道底色是 0.62 灰、四轨彩虹的轨道色降到约一半亮度。
- **外链**：`Mod/Info.json` 加了 `HomePage`。UMM 的 mod 列表会给带 `HomePage` 的 mod 画一个
  Textures.WWW 按钮，点了 `Application.OpenURL`（反编译 UnityModManager 确认）——
  这就是"像其它 mod 一样可点击的 GitHub 入口"。面板「其它」区另有一个按钮和地址文字，
  老版本 UMM 不画那个按钮时也还有路。`Plugin.HomePage` 是两处共用的常量。

### 面板与颜色（2026-10-08 晚）

**面板**：7 个可折叠分区（显示 / 布局 / 外观·颜色 / 小节线 / 排键 / 对齐 / 实验性 / 其它），
`Plugin.Section(title, ref open)` 是一个 **▶/▼ 按钮**（不是 Toggle：Toggle 会画皮肤的小圆圈，
空心看起来像「整个功能被关掉了」），正文包在 `using (Plugin.Indent())` 里，每层右移 16px，
所以「外观·颜色」下面的 轨道/音符/判定线/小节线/长按 和「实验性」下面的「娱乐」都有可见的层级缩进。
`Indent()` 返回 `IDisposable`（不是 Begin/End 成对调用）是为了正文抛异常时也能把 IMGUI 的 layout group 收干净。
`_indentLevel` 同时用来收窄标题按钮，防止嵌套后顶出窗口。

**每个滑块可以直接填数字**（`Plugin.Slider` / `IntSlider` 右边那个小框）：

- 关键点：**只在提交时解析**（回车 / Tab / 失焦），编辑中把文本存在 `_typed[label]` 里。
  逐键解析会和用户抢输入框 —— 半截的减号、半截的小数点都不是数字，而按当前值回写又会把光标顶回去。
- 提交后把输入框刷成**真正被采纳的值**，越界输入不会看起来被接受。
- 键就是 label，所以 **label 必须唯一**（同名滑块会互相抢编辑状态）。

**颜色**（`Settings` 里的一堆 `Color` + `Themes.cs`）：

| 元素 | 字段 | 备注 |
|---|---|---|
| 轨道底色 | `LaneColour` / `LaneColours[4]` + `LaneSeparate` | 再乘 `LaneOpacity`（总透明度 0~1） |
| 轨道边框 | `LaneEdgeColour` / `LaneEdgeColours[4]` + `LaneEdgeSeparate` | 默认 A=0.28 |
| 音符 | `NoteColour` / `NoteColours[4]` + `NoteSeparate` | 默认 `Separate = true` 且 1/2 轨同色、3/4 轨同色 = 原来的左右手配色 |
| 音符边框 | `NoteEdgeColour` / `NoteEdgeColours[4]` + `NoteEdgeSeparate` | 跟着音符一起淡出（打击弹跳） |
| 判定线 / 判定框 / 键位标签 / 帧数 | `JudgeLineColour` / `JudgeBoxColour` / `LabelColour` / `FpsColour` | |
| 细线（每拍）/ 粗线（每小节） | `BeatLineColour` / `BarLineColour` | 再乘 `BeatLineAlpha`，粗线另外乘 1.8 |

- `Plugin.Ink()` 和 `Plugin.Palette` 都**删掉了**：旧的「背景明暗」就是主题里的一项
  （`Themes.LightLevel`），旧的左右手配色就是音符的四轨颜色。所有绘制直接读设置里的颜色。
- 颜色行 `ColourRow` = 4 个 0~1 滑条 + 一个 `RRGGBBAA` 输入框，走同一套「回车/失焦才提交」规则。
- 主题：`Themes.Names` 六个，`Themes.Apply(cfg, i)` 批量赋值；套完仍可逐项改。
- **升级旧配置**：老 `Settings.json` 里没有 `"LaneColour"` 键，而 `JsonUtility` 对缺失字段
  保留类型默认值 —— `Color` 的默认是**全透明黑**，会直接看不见。所以 `Settings.Load` 里检查
  这个键，缺了就给套上经典主题（只会在升级那一次触发）。

**实验性平滑下落**（`SmoothFalling` / `SmoothGhosts` / `SmoothStrength`，默认关）：

- 音符位置本身是准的，但**每帧才采一次样**：60fps + 1000px/s 一帧约 17px，快速段落看着是一格一格。
- 拖尾 = 按前几帧的位置再画几层、逐层变淡。
- 位移量取 **`now - _lastTrailTime`（渲染时钟实际推进） × 速度**，不是 `Time.deltaTime`：
  所以暂停/结算（时钟冻结）时位移为 0，不会把站着不动的音符拖糊；换关/跳变时位移过大（> 半屏）也跳过。
- 拖尾只画在 `laneTop` 以下、画在本体之前，不影响命中弹跳。

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

## 16. 自定义音符图片 + 整体旋转（新增）

### 16.1 自定义音符图片（`NoteImage.cs`）

| 设置 | 说明 |
|---|---|
| 用自定义图片 | 总开关，默认**关**（关着时与旧版完全一致） |
| 图片路径 | 默认 `note.png`。**相对路径基于 mod 目录**（`Mods\LaneReader\`），也接受绝对路径；带引号会自动去掉 |
| 保持宽高比 | 默认开。按「contain」缩放并居中；关掉则拉伸到音符框（`轨道宽 × 音符高`，默认 110×26 @1080p，很扁，方形图会被压扁） |
| ~~用左右手颜色着色~~ | **已删除**（2026-10-10，用户要求）。图片本身是什么颜色就画什么颜色，只乘音符自己的 alpha；`Settings.NoteImageTint` 不再存在 |
| 状态行 | 显示「已加载 宽x高 / 找不到文件 / 解码失败」 |

实现要点：

- 画法是 `FillNote()` 里把内层色块**换成 `GUI.DrawTexture`**，边框改成**绕图片画一圈四条**
  （2026-10-10 修的 bug：原来是一整块实心矩形垫在图片底下，图片的透明像素透出来的就是边框色，
  默认主题的边框色是白色，于是「透明显示成白色」）。打击弹跳（放大缩小 + 淡出）照旧生效。
- **alpha 一定参与**：`GUI.color` 乘上音符自己的 alpha（`new Color(1,1,1,c.a)`），
  否则打击弹跳的淡出对图片失效；RGB 强制留白，图片不会被染色。
- 图片自带的 alpha 由贴图给出，`Format32bppArgb` 的 png 直接生效；jpg 无 alpha。
- **热重载**：最多每 1 秒检查一次，且只在文件修改时间变化时重读 —— 换图不用重启游戏，
  文件不存在时也不会每帧刷日志。面板上的「重新加载」按钮强制重读。
- 贴图 `hideFlags = HideAndDontSave`，替换时 `Object.Destroy` 旧的。

> 演示图：安装目录里的 `note.png`（256×64 圆角胶囊，填充 alpha 190 → 圆角外全透明），
> 直接开「用自定义图片」就能验 alpha。

### 16.2 整体旋转（`RotationDegrees`，0~360）

- 面板「布局」里的「整体旋转 (度)」滑块，0~360，默认 0。
- 实现是 `GUIUtility.RotateAroundPivot(角度, Pivot())` 包住整个 `DrawOverlay()`，
  在 `finally` 里恢复 `GUI.matrix`，**设置面板本身不受影响**。
- **轴心 = 判定线中点**（`屏幕中线 + 水平偏移`，`判定线高度 + 垂直偏移`）。
  整个叠加层本来就锚在判定线上，绕它转时判定线留在原地。
- 旋转是 Unity 的屏幕坐标方向（y 向下），所以正角度看起来是**顺时针**。

### 16.3 踩坑：Unity 6 的 `ImageConversion.LoadImage` 编不过（CS0012）

```csharp
ImageConversion.LoadImage(texture, bytes);   // error CS0012
// 类型"System.ReadOnlySpan<T0>"在未被引用的程序集中定义…
```

Unity 6 给 `UnityEngine.ImageConversion` 加了一个 `LoadImage(Texture2D, ReadOnlySpan<byte>)`
重载。重载决议要先解析 `ReadOnlySpan<T>`，而它的实现在 `netstandard.dll` 后面 ——
本机这台 **C# 5 版 csc** 解析不了，于是**连 `byte[]` 那个重载都编不过**。

试过但**没用**的办法：`/reference:...\System.Memory.dll`（仍然是 CS0012），
换成静态调用 `ImageConversion.LoadImage(...)`（一样），把 `byte[]` 强转（没得转）。
试过**有效**的办法：`NoteImage.Decode()` 里**反射找那个 2 参数 `byte[]` 重载**并缓存 `MethodInfo`。

- 先 `type.GetMethod("LoadImage", new Type[]{ typeof(Texture2D), typeof(byte[]) })`；
- Unity 里这条**可能**直接抛 `TypeLoadException`（本机 .NET Framework 下实测就抛），
  所以外面要 `try/catch`，抛了就走扫描：`GetMethods()` 逐个看名字 + 参数表，
  **每个 `GetParameters()` 单独 try/catch**（本机实测 4 个重载里正好 2 个读参数表就抛）。
- 别再用「按名字取第一个」的写法：顺序不保证。

## 17. 变更记录（本节之后的东西）

- 2026-10-07：加自定义音符图片与整体旋转（§16）；dll 41,984 字节。
- 2026-10-08：默认值改为 **流速 1000 / 最大硬抗 BPM 850**；**轨道长度独立成设置**（默认 900，`0`=旧行为）；
  **删除「垂直偏移 (px)」**（与判定线高度重复）；**音符与小节线改为从轨道顶端出现**；
  **新增「显示帧数」**（默认关，判定线下方，日志 `frame:` 行也带）。
- 2026-10-08 晚：**逐项可调的颜色 + 主题预设**（`Themes.cs`，删掉 `LightBackground`/`Palette`/`Ink()`）；
  面板重排成可折叠分区、**每个滑块可填数字**、颜色支持 `RRGGBBAA` 输入；
  **轨道底色总透明度放到 0~100%**；**实验性平滑下落**（重影拖尾，默认关）。
  部署目录里的 `Settings.json` 已按用户要求**删除回默认**（旧文件备份在 `%TEMP%\LaneReader-settings-20261008-235355\`）。
- 2026-10-09：四个实验性功能各自一个提交 —— **3D 轨道**（灭点在轨道顶端）、
  **长按 hold**（读 holdLength、画长条、拆手序把松手强制同轨）、
  **娱乐小分类**（轨道乱飞 / 轨道持续旋转 / 四花瓣）；dll 57,344 字节。
- 2026-10-10（用户实机反馈后的六个提交）：
  1. **自定义音符图片**：透明像素显示成白色（边框那一整块实心矩形从图片底下透出来）→ 边框改成绕图片画一圈；
     并按用户要求**删掉图片着色开关**（`Settings.NoteImageTint` 不存在了）。
  2. **面板**：折叠标题从 Toggle 改成 ▶/▼ 按钮（圆圈会被误读成功能开关），并加上层级缩进
     （`Indent()` + `_indentLevel`）；删掉一直没人用的 `Fold()`。
  3. **四花瓣**：删掉中心小方块，四条轨道改为绕判定线中点转，底边中心重合于一点。
  4. **3D 灭点全部重写**：改成 `View`/`Field` 的单台针孔相机投影（`rise`/`scale` 一对面数），
     轨道体变三角形堆（96 带）、音符贴着轨道边下滑、`depth` 语义的去留见上；
     透视强度默认 1.2 → 1.0；顺带修正小节线过线晚半个音符高的问题。
  5. **长按**：`HoldSeconds` 改成整个间隔（游戏 `scrHoldRenderer.tailFloor = nextfloor`），
     长条末端精确接上下一颗音符；渲染顺序改成 长条 → 拖尾 → 音符（原来长条把拖尾整段盖掉）；
     `holdLength` 语义改对（int，-1 非 hold，1 单位 = 2 拍）；`HoldSupport` 加进重算指纹。
  6. 本文档 + README 同步；dll 59,904 字节。
- 2026-10-10 第二轮（用户实机反馈后的十个提交）：
  1. **长按不再随开头音符消失**：长条/拖尾被判定线慢慢吃掉（近端夹在线上），
     开头音符的打击弹跳一直顶到尾巴音符到达才缩小淡出；`ShouldDraw` 对长按放行到「过线 + 整个长按时长 + 弹跳窗口」。
  2. **3D 重写第二版**：轨道边缘改成旋转矩形（精确直线，消灭锯齿），
     音符改成 `FillNotePlate` 的「板」投影（真的 3D 变换，不是缩放）；`View.Rise` 不再截断 d<0。
  3. **主题**：加「纯黑底 经典白线」；极简纯白/四轨彩虹的轨道色与音符色拉开亮度。
  4. **自定义图片**：有图时只画图（不画音符和边框）、拖尾用同一张图、长按可另设一张图
     （`NoteImage` 改成两个槽位）。
  5. **默认值**：长按支持默认开。
  6. **默认值**：轨道长度 900 → 1620（1.5 屏）。
  7. **判定线**：开关挪到「布局」并改名「显示判定线」，加「判定线横跨全屏」+ 长度百分比。
  8. **全屏纯色背景**：默认关，默认黑色 50%，颜色和 A 可调。
  9. **mod 目录 GitHub 链接**：`Info.json` 的 `HomePage` + 面板按钮。
  10. 文档 + 验收清单同步；dll 63,488 字节。

> 长按的验收要点（本机谱面数据里如果没有 hold 砖，日志里不会有 `chart holds:` 那行）：
> 找一张有长按的谱，看长条末端是否**始终**贴住下一颗音符的顶边、拖尾是否和普通音符一样存在。

