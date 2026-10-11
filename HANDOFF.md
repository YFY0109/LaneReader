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
| 长按（hold）：长条被判定线慢慢吃掉、尾巴强制同轨 | ⚠️ 已实现，待实机复验 |
| 3D 轨道（针孔投影 + 抗锯齿 + 无白线） | ⚠️ 已实现，待实机复验 |
| 自定义音符 / 长按图片（长按图按拍平铺） | ⚠️ 已实现，待实机复验 |
| 补帧（叠加前后帧）/ 平滑拖尾（固定时间窗） | ⚠️ 已实现，待实机复验 |
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
- 每次编译都会顺手打 release 包：`build\LaneReader-<版本>.zip`，根目录是 `LaneReader\`
  （dll + Info.json + `lang\` + 示例图片 + 安装说明），和 UMM 的安装结构一致，
  版本号从 `Mod\Info.json` 读。`lang\*.txt` 在这里是**覆盖**打包的（安装到游戏目录时才只在缺失时复制）

---

## 4. 架构：十个文件

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

### `TimeResetPlanner.cs` — 时间重置法（第三种手法）

只看音符间隔的因果状态机，一个参数（主手重置间隔）。不读 BPM、不读角度、不做分组。
正面解决「3 分音被 `2^k` 缩放切错」的问题。详见 §18。

---

### `DoublePressMerge.cs` — 双押合并（三种手法共用）

间隔小于阈值的两颗音当作一次双押，后一颗移到和前一颗同一时刻。与手指分配无关，所以在
三种手法之后统一跑。详见 §19。

---

### `Localization.cs` — 面板多语言

中文内置（代码里的中文原文就是 key），英文在 `Mod\lang\en.txt`；复制它只翻译右列就能加语言。
详见 §20。

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

### 6.7 中旋（midspin）表示与处理（2026-10-10 晚改成"折算成正常砖"）

游戏侧的事实（反编译确认，别再猜）：

- `scrFloor.midSpin` 是**地板自己的属性**，游戏把它打在**零长度砖前面那块砖**上：
  角度数据里的 999（路径数据里的 `'!'`）会让游戏**多造一块不转角的砖**，
  然后把前一块标成 midspin（`listFloors[num3-1].midSpin = true` / `scrFloor4.midSpin = true`）。
- `CalculateFloorEntryTimes` 里，**带标记的那块（前一块）** `num4 = 0`（`if (angleMoved<=1e-6) num4 = midSpin ? 0 : 2拍`），
  所以它的时长是 0；`'!'` 那块（后一块）正常参与计算，且它的 entry/exit 同角 → 走"掉头"分支
  `num2 = 2π` = **360 度 = 2 拍**。两块砖 entryTime 相同 → 4K 时间轴上它是**一次按键**。
- **旋转算在后一块（`'!'` 那块）身上**：前一块 length 0，后一块 2 拍。
  这就是"到下一个音的间隔是 2 拍（不是 1 拍）"的原因，也是参考实现
  `trackAngleDifferenceMidSpin(前一块角度, 后一块角度)` 那个特判对上的东西。
- 中旋**是要按的键**，不是"路过不按"：按下时 `currFloor.midSpin` 为真 → 拿到无限判定窗口
  （`scrPlayer.midspinInfiniteMargin`），游戏再补一次 `Hit()` 把两块砖一起过掉。

现在的处理（`ChartReader.Build` 里的 folded 循环）：把这一对**折算成一块正常的砖**——
**留下前一块**（按下时星球所在、带标记的那块；挂在它上面的 hold 也留在它身上）、清掉
`MidSpin` 标记、删掉 `'!'` 那块，于是手序看到的就是**一颗普通砖**，窗口计数/形状/抬手规则
全部按普通谱面走，没有任何地方再特判 midspin。
**角度不能跟着前一块**：前一块的 entryangle 比中旋轨道少半圈，拿它去算拍网格会把 360 度
量成 180 度（少一拍，之后整条网格错拍）。所以折叠时把 `'!'` 那块的 entryangle 记到
`Note.GridAngle`，`ComputeBeats` 优先用它 —— 2026-10-10 修的就是这一条。
判定同一对的容差是 **1ms**（真实相邻砖至少几十毫秒），超过就当成两次真实按键，完全不碰，
而且会打日志（只有这种情况手序才会多看到一个音）。
每对都打日志（两个 seqID、时间差、幸存音符"到下一个音的转角"，中旋应 ≈360）。
**注意**：日志里那个转角以前打的是 176.9/183.1 —— 那是把游戏的**弧度**直接喂进要**角度**的
`TurnDegrees`（180 度差 π 的假象）。现在已经按 `AngleDegrees` 换算，别再被旧日志误导。

### 6.7.1 手序的"度"标尺：结论更正 + 一次被回退的实验（2026-10-10 晚）

⚠️ 这一节早先写过"移植版比参考实现大 4 倍"，**是错的，别信它**：参考实现 `Level` 构造时
`speedList` 从 floor 0 覆盖到谱尾，`processSetSpeed` 把每个音的角度乘 `baseBpm/该音当时BPM`，
所以它的度 = `180·真实秒·baseBpm/60`，和移植版从游戏 `entryTime` 算出来的**完全一致**
（`HandPlanCheck` 28 张谱 100% 逐窗口一致也不是巧合）。标尺上没有偏离宏。

真正的性质：**参考实现的归一化把"窗口 180"和四档 270/450/630/720 一起缩小了 `2^k` 倍**
（`2^k` 是把 baseBpm 抬进 (550,1100] 的倍数）。80BPM 的 test 谱 k=3 → 窗口只有 1/8 拍，
于是 ≥半拍的音全部落进"回主手"档（主手同一根手指连打），只有 ≤1/3 拍的细分音会换手，
SetSpeed 一变还会整体平移档位。3 分音正好压在 480/720/960 三档的边界上 ——
这就是"每次都是 3 分音出问题"的观感来源，但它是参考实现代码自己的标尺，不是移植 bug。

试过、**已回退**（`a719b4a` → revert `79b2d3a`）：把度换成"音自己的转角"
（`Note.Beats × 180`，1 拍 = 180°），即改成参考实现**注释**里写的规则
（窗口 = 一拍、四档 = 1.5/2.5/3.5/4 拍）。实机结果：用户报"啥都乱了" ——
窗口整体变宽 `2^k` 倍、几乎每个音都被并进多音窗口，和玩家熟悉的手感完全不同。
**这条路不要默认打开。** 真要再试：默认关的开关 + 度的来源换成**角度**
（`TurnDegrees`，60°/120° 精确）而不是 `Note.Beats`（时间是推的，谱面开头那些音还没采到
BPM，会前后不一致）。

下一步正确做法：**只对 3 分音这类细分做窄规则**，别再动全局标尺；动手前先问清用户在那里
想要什么指法（同手交替 / 一个窗口 1+2 分组 / 其他）。

### 6.7.2 `GUI.DrawTextureWithTexCoords` 的 v 是**从下往上**的

Unity 自己的例子把 `Rect(0,0,0.5,0.5)` 叫 "the **bottom left** half of the texture"，
`Graphics.DrawTexture` 的文档也写明 `sourceRect` 是 "(0,0) in the bottom-left corner"，
而 `GUI.DrawTextureWithTexCoords` 就是转发给它的。本文件的贴图坐标全部是**图左上角为原点**
（和 `GUI.DrawTexture` 画整张图一致），所以 `DrawSpriteSlice` 里必须翻转一次：
`new Rect(uv.x, 1 - (uv.y + uv.height), uv.width, uv.height)`。
**不翻的后果**：切片全部上下颠倒——对称的音符图看不出来，长按图就是玩家报的
"剪裁有问题、像是裁掉了上面"（图被镜像了，判定线吃掉的是图的上半边，
靠近线的部分看起来像卡住）。**只在这一处翻，别在每个调用点翻。**

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

### 6.14 半透明分层的两条铁律（2026-10-10 第三轮踩的）

**(1) 相邻的 alpha 混合矩形不要重叠。** 轨道体/长条/音符板都是"一叠横带拼出一个梯形"，
第一版给每条带 `height + 0.5f` 想避免缝隙。`Fill` 是 alpha 混合，重叠 0.5px 就把
那一行的 alpha 叠了第二遍：轨道底色 6% 时 0.06 → 0.116，**亮度几乎翻倍**。一屏几十条带，
于是每条带边界都是一条更亮的白线 —— 用户报的"轨道内部出现很多平行的白色细线"就是它
（他的配置：轨道底色透明度 21%，重叠处 0.213 → 0.38，非常明显）。
相邻矩形共享同一条边时 GPU 会保证每个像素只被覆盖一次，本来就没有缝，那 0.5px 纯属有害。
现在**全部精确拼接**（长按长条 45%、音符板同理），并且把共享边写成同一个表达式。

**(2) "每层是不是比描边大"这类判断要按整体算，不能按层算。** 音符描边原来是
`band.height > 2 * NoteBorderPx` 才画。带子数从 3 提到 24 以后每条带只有 2~3px 高，
这个条件永远不成立，**描边整个消失**。现在按整块板的投影高度判断一次，
每条带只负责"自己落在描边里多少"。

**顺带一条工具坑**：不要在 Windows PowerShell 里用 `Get-Content -Raw` + `Set-Content`
往返 .cs 文件。无 BOM 的 UTF-8 会被 5.1 按 GBK 解码（中文全变乱码），
`-Encoding UTF8` 又写回一个 BOM —— 一次往返就把整个文件的字符串毁了（这次真踩了，
`git checkout` 才救回来）。本仓库的文本一律用带显式编码的 .NET 调用或编辑工具写。

### 6.15 `JsonUtility` 缺键 = 类型零值，不是字段初值

`Settings.Load` 里 `JsonUtility.FromJson<Settings>` 会把**对象先清零再填**，
所以 `Settings.json` 里缺的键拿到的是 `bool=false` / `float=0`，而不是字段后面写的初值
（`Color` 尤其明显：缺键 = 全透明黑，叠加层直接看不见 —— 早期那次"颜色升级"就是为它写的特判）。
后来的版本又加过 `HoldSupport`（默认 true）、`LaneLengthPx`、`SmoothGhosts` 等键，
每一个都可能被老配置读成 0/false。现在 `Settings.FillMissingKeys` 用反射把
**文件里没提到的键一律补成 `new Settings()` 的值**，只覆盖真正缺的那些，
所以老 `Settings.json` 只会丢掉它确实没有的东西。加新字段后不需要再写特判。

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

设置面板「排键」里切换手法，**1.0.1 起默认是时间重置法**（0.3.0 起是拆手序，更早是 AdoFalling 原版）：

| 手法 | 实现 | 说明 |
|---|---|---|
| AdoFalling 原版 | `FingeringPlanner.cs` | 间隔 < 窗口 (±1 走格)，否则 F/J 交替 |
| 拆手序 | `HandPlanner.cs` | 移植 [ReADOFAIMacro](https://github.com/TonyLimps/ReADOFAIMacro)（MIT） |
| 时间重置法（默认） | `TimeResetPlanner.cs` | 只看音符间隔的因果状态机，一个参数；见 §18 |

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

- **只有一种渲染器（横带）**。曾经做过第二种"投影变换"（把整个 3D 收进一个 4x4 塞进 `GUI.matrix`，
  让显卡直接把平面视图投影成 3D：边缘会是真正的直线、没有横带、没有接缝、图片透视插值也对），
  **数学是对的**（当时用 `tools/WarpCheck` 对着 `View.Rise/Scale` 验过 19200 个点、0 误差），
  但**实机用不了：Unity 的 IMGUI 不接受投影矩阵**，3D 整个画不出来。代码已删（含那个开关和校验工具），
  `Plugin.cs` 原来 `WarpMatrix` 的位置留了一段注释记这个结论。**记住：IMGUI 只有仿射变换可用**
  （旋转可以，投影不行；别再把 `GUI.matrix` 的非仿射用法当成出路）。
- **轨道边框粗细**（`LaneEdgePx`，默认 3px，面板在「外观 / 颜色 → 轨道」里）：
  平面视图和 3D 共用，按 1080p 缩放。它同时是 3D 抗锯齿的手段——轨道体是一叠**内切**在真三角形里的
  横带（按远边宽度画，绝不外露），横带的台阶靠边线盖住，边线越粗盖得越干净，
  所以默认从写死的 2px 提到 3px（玩家反馈"调粗一点锯齿会好很多"）。
- 横带渲染器这一轮的其他修正：横带精确拼接（半透明重叠会画出亮线）、按远边宽度内切、
  删掉 1x8 柔边（那层柔边本身就是"细线"的观感来源）、小节线不再淡出
  （只裁掉糊成一片的那一小段，3D 下 2px 间距）。

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
  （`DrawEdgeLine`，从底角直达灭点）。横带的边缘每 9px 横跳 1.25px，在细白线上就是锯齿；
  旋转矩形是精确直线。旋转用 `GUIUtility.RotateAroundPivot`（反编译确认实现是
  `TRS(pivot, Euler(0,0,angle))`），GUI 的 y 向下，所以 `atan2(dy,dx)` 直接就是屏幕角度。
  **边线粗细就是「轨道边框粗细」设置**（`LaneEdgePx`，默认 3px，平面和 3D 共用）。
- **横带要按自己"远边"的宽度画**（`p = 1 - far/len`），整叠内切在真三角形里：
  用带子中点的宽度会让下半部分比真三角形宽出半个台阶（96 带时 0.6px），
  这一丝露在边线外面就是"一点点一格一格的锯齿"。内切之后横带绝不外露，
  剩下的半个台阶由边线盖住 —— **这就是为什么把边框调粗能让 3D 更干净**。
- **横带必须精确拼接，绝对不能留半像素重叠**（"轨道里的平行白线"）：
  `Fill` 是 alpha 混合，两条半透明带在重叠处会把 alpha 叠一遍 —— 轨道底色只有 6%~21%，
  重叠区就接近两倍，于是每条带边界都是一条更亮的白线，一屏几十条。
  相邻矩形共享同一条边时，GPU 的像素规则保证每个像素只被覆盖一次，本来就不会有缝。
  同样的问题也在长按长条（45% alpha）和音符板上，一并改掉了。
- 边线曾经用 1x8 的 alpha 斜坡贴图填充（`EdgeRamp`）想让直线有个柔边；实测那层柔边本身就是
  一条"细线"的观感来源，已经删掉，改回干净的实心线。
- 音符/重影/长按长条都走 `FillNotePlate` / 同一套 `View`，所以它们**一起**透视；
  贴图（自定义图片）按每个带子的高度取图片对应的横条，因此图片也跟着压扁。
  **带子数不能是常数 3**（"一张图被切成 3 块"的原因）：自定义图片被切成三段、
  每段又是各自宽度的矩形，看上去就是三块错位的图。现在按投影高度**每 2px 一条**
  （`PlateBands`，2~24），台阶降到 0.1px 级。描边按**整块板**的上下边算，
  不再按每条带子判断（带子只有 2px 高，"带子比描边粗"这个条件永远不成立，
  结果是把高音符的描边整个关掉了）。
- 小节线在 3D 下**挤到 2px 以内就不再画**（`MinBeatGap`，只裁掉糊成一片的那一小段，
  透明度不再衰减——上一版把远处拍线调淡，看起来像"小节线不见了"）。
  平面路径不做这个裁剪，保证关掉 3D 时逐像素不变。
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
- **长按尾巴那颗音符也要强制回同一条轨道**（`Plugin.ForceHoldTails`，两种手法之后统一跑）：
  手指还按着，下一块砖的那一刻就是松手那一刻，它只能在原地；两个 planner 都不知道这件事，
  拆手序只强制了自己喂进去的那个"松手事件"，没管同一时刻到达的**下一颗音符**，
  于是长条顺着开头那条轨道往上，末端却停在另一条轨道旁边。链式长按靠"上一颗的尾巴就是下一颗的开头"
  自然传递，一趟就够。
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
- **长按图片按拍平铺**（`HoldImageRepeatBeats`，默认 1 = 每拍接一次）：
  - 图片**下端 = 长按开头**（贴判定线那一端），上端 = 尾巴（下一颗音符）；
  - 一份图覆盖长条总拍数 / `HoldImageRepeatBeats` 那么多，份数**四舍五入成整数**，
    所以不会在半路截断图案，接缝永远落在图片的同一个位置；
  - 拍数用 `note.Beats + note.HoldBeats`（这块砖自己的长度 + holdLength×2），
    和下落速度无关 —— 调速度不会改变"每拍接一次"这件事；
  - 判定线从下往上吃、尾巴从上往下切，都是**裁掉**而不是把整张图压进剩下那一截：
    每份图按它自己的真实范围取样（`copyNear/copySpan`），再按可见段切；
  - 3D 下每份图再切成"每 6px 一条"的横带跟着轨道收窄；平面下每份就是一块矩形。
- `Mod/hold.png` 是**示例图**（64×256）：浅蓝管身 + 两侧深蓝边 + 四个向上的箭头。
  图案周期 64 整除高度 256，所以**首尾相接**，平铺时接缝看不出来。
  `build.ps1` 像 `note.png` 一样只在目标目录里没有这张图时才部署它。
- 长按图片的宽度仍按「长条粗细」的比例；按原样画，
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
- **套用主题会把背景一起定下来**：`Themes.Apply` 先把 `BackgroundEnabled` 置 false，
  只有纯黑底那个主题再打开它。以前它不碰这个开关，于是点过一次纯黑底之后，
  换任何别的主题都还顶着一整块黑（"回不到没有背景"）。面板「显示」里的开关仍然随时能再打开。
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

**实验性平滑下落**（`SmoothFalling` / `SmoothGhosts` / `SmoothTrailMs` / `SmoothStrength`，默认关）：

- 音符位置本身是准的，但**每帧才采一次样**：60fps + 1000px/s 一帧约 17px，快速段落看着是一格一格。
- 拖尾 = 沿轨道方向再画几层同样的板、逐层变淡。
- **窗口是固定的毫秒数（`SmoothTrailMs`，默认 40ms），层数只决定密不密**。
  第一版把重影放在「上一帧的位置」上（位移 = `now - _lastTrailTime` × 速度），
  于是拖尾的长度跟着帧时间抖：帧一慢整条拖尾被拉长、帧一快又缩回去 ——
  这就是用户报的"分层闪烁、一卡一卡"。固定时间窗后形状稳定，层数用满（10~24 层）
  时层间距只有几像素，看起来是一整条渐隐的拖影而不是条纹。
- 位置是**线性时间**推出来的（落点 = (命中时刻 - now) × 速度），所以"固定时间之前"就是
  "固定距离之前"，不需要上一帧的采样；只有时钟冻结（暂停/结算）才整个不画。
- 拖尾只画在 `laneTop` 以下、画在本体之前，不影响命中弹跳；3D 下还有一条
  「缩到 1.5px 以下就不再画」的剔除，免得灭点附近画一堆亚像素点子。

**实验性补帧**（`FrameBlendEnabled` / `FrameBlendAlpha` / `FrameBlendOffset`，默认关）：

- 一帧里把叠加层画三次：`now - offset`、`now + offset` 各按 `FrameBlendAlpha` 画一遍，
  再把当前帧画在上面 —— 用户要的"以某透明度同时叠加渲染下一帧和上一帧，共 3 帧"。
- `offset = 实测帧时间 × FrameBlendOffset`（`Plugin.FrameSeconds`，取 `MeasureFrameRate`
  那半秒平滑值，读不到时用 `Time.unscaledDeltaTime` 并夹在 1~100ms）：
  这样"一帧"不会因为帧率变化而变意思，单帧卡一下也不会把邻居甩到半屏外。
- **只重画会动的那一半**（`DrawOverlay(..., timeOnly: true)`）：轨道、判定线、判定框、
  标签、帧数在三个时刻位置相同，重画只会自己和自己混得更暗。音符、拖尾、长条、
  小节线的时间项都乘 `mix`（颜色带着走，所以贴图路径也一起淡）。
- 暂停/结算（`Chart.ClockFrozen`）不叠：时钟没走，三个时刻是同一张画。
- 代价是每帧 3 次绘制（面板里写明了），默认关。

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
- 2026-10-10 第三轮（用户实机反馈，一个功能提交 + 文档提交）：
  1. **长按尾巴同轨**：`Plugin.ForceHoldTails`，两种手法之后统一把 hold 尾那颗音符拉回开头的轨道
     （手指还按着，它只能在原地），链式长按自然传递。
  2. **长按示例图 + 按拍平铺**：新增 `Mod/hold.png`（64×256，图案周期整除高度，可首尾相接），
     `build.ps1` 像 `note.png` 一样只补缺；`HoldImageRepeatBeats` 默认 1 = 每拍接一次，
     份数四舍五入成整数，图片下端是开头、上端是尾巴，判定线/尾巴处都是裁掉不是压扁。
  3. **拖尾改成固定时间窗**（新增 `SmoothTrailMs` 默认 40ms，层数上限 10 → 24）：
     原来重影放在"上一帧的位置"，帧时间一抖整条拖尾就忽长忽短 —— 这就是分层闪烁/一卡一卡的来源。
  4. **新增补帧**（`FrameBlendEnabled` 默认关 + 透明度 + 间隔）：一帧画三次，
     上一帧和下一帧按透明度叠在下面，共 3 帧；只重画会动的一半，暂停不叠。
  5. **3D 抗锯齿与白线**：横带精确拼接（半透明重叠 = 每条边界一条亮线）、左右各内收 1~2px
     （残留锯齿）、边线改用 1x8 alpha 斜坡柔边；音符板分层从固定 3 条改成按投影高度每 2px 一条
     （自定义图片被切成 3 块的原因），描边判断改成按整块板算（否则高音符描边消失）；
     3D 下小节线越远越淡、挤到 3px 以内停止绘制。
  6. **主题背景开关**：`Themes.Apply` 先关掉全屏背景，只有「纯黑底 经典白线」再打开。
  7. `Settings.FillMissingKeys`：老配置缺的键按默认值补齐（见 §6.15）。
  8. 文档 + 验收清单同步；dll 67,072 字节。
- 2026-10-10 第四轮（用户第三次反馈 3D 仍不满意）：
  1. 试了**投影变换 3D**（整个 3D 收进一个 4x4 塞进 `GUI.matrix`）：数学正确、离线验过 19200 点，
     但**实机用不了**（IMGUI 不接受投影矩阵，3D 画不出来）。
  2. **修 `DrawTextureWithTexCoords` 的 v 方向**（§6.7.1）：切片一直是**上下颠倒**的，
     这就是玩家报的"长按图片剪裁有问题、像是裁掉上面"。
  3. **中旋折算成正常砖**（§6.7 重写）：不再"去重后消失"，而是把 midspin 那一对折成
     一颗普通砖再进手序，转角不丢，带日志。
  4. 横带渲染器顺手修：按远边宽度内切（不再露锯齿）、删掉 EdgeRamp 柔边、
     小节线不再淡出（只裁掉重叠的一小段）。
- 2026-10-10 第五轮（用户反馈"投影变换用不了，可以删了；旧算法修得还行"）：
  1. **删掉投影变换那条路**：`ThreeDWarp` 开关、`WarpMatrix`、`Field.Draw` 两套投影的分工、
     面板选择器、`tools/WarpCheck.*` 全部删除，只在原位留注释记结论
     （IMGUI 只有仿射变换可用，投影矩阵别再试）。3D 只有横带一种渲染器了。
  2. **新增「轨道边框粗细」**（`LaneEdgePx`，1~12，默认 **3px**，原来写死 2px）：
     平面和 3D 共用，按 1080p 缩放。它是 3D 抗锯齿最直接的手段——横带内切在真三角形里，
     边线盖住的台阶越多越干净，所以默认加粗 1px，锯齿明显时继续往上调。
  3. 文档 + 验收清单同步；dll 67,584 字节。
- 2026-10-11：**新增第三种手法「时间重置法」**（`TimeResetPlanner.cs`，`FingeringMode = 2`，
  `ResetMs` 默认 **300ms**）—— 只看音符间隔的因果状态机，不读谱面 BPM/角度、不做分组，
  正面解决 3 分音被 `2^k` 缩放切错、边界落在音组中间的问题；含外轮收尾扫描、长条尾音同轨
  （链式长条整段同轨）、边界 1ns 容差；`tools/TimeResetCheck.cs` 离线校验。详见 §18。
  dll 71,168 字节。
- 2026-10-11 收尾：**版本进入 1.0.0**。面板顶部写明「本 mod 旨在帮助理解谱面配置，或者提供一种
  玩法；请不要只是为了直接打出更好的成绩而使用它——某种意义上这是一种外挂」，并在「排键（手序）」
  里逐条列出三种手法各自的优势与劣势，附 [ReADOFAIMacro](https://github.com/TonyLimps/ReADOFAIMacro)
  的可点击外链。README 重写（三种手法对照表、时间重置法规则与离线实测表、默认设置表）。
  dll 72,704 字节。
- 2026-10-11（1.0.1）：**默认手法改成时间重置法**（`FingeringMode` 默认 2）、
  **`ResetMs` 默认 300 → 165ms**；`Info.json` 与安装说明升到 1.0.1，README 和 §18 的实测表
  按 R = 165ms 跑（均匀间隔分界 82/165ms；50+50+100 锁成 JKF，100+100+200 是 JFJ）。
  这一版中间改过两次默认值（150 → 200 → 165），都直接改在同一个版号里，不另发版本。
  dll 仍 72,704 字节。
- 2026-10-11（1.0.2）：**主轮那支的参照从"上上个音（那颗主手）"改成"上一个音（主轮那一下）"**
  （实机确认后发版）：一次快速双击后隔 150ms 的音现在仍换手（旧规则按"距上上个音 230ms"
  判回主手），头落在主轮的 hold 从尾音起算，长条后那颗音因此从 J/K 变成 F/D。
  复测：均匀间隔三档与 100+100+200 / 50+50+100 / 100×4 / 50+400 全部未变，
  新增用例 80+150 → JKFD、80+220 → JKJK。dll 仍 72,704 字节。
- 2026-10-11（1.0.3）：新增**双押合并**（`DoublePressMerge.cs`，`MergeRollMs` 默认 50ms，
  面板放在「排键（手序）」的**公共位置** —— 三种手法共用，不是时间重置专有）：间隔小于它的
  两颗音当作双押，后一颗移到和前一颗同一时刻；只移按键，长条尾音不动。新增
  **「刷新手序」**按钮，并且每次重算前先 `RestoreChartTimes()` 把音符时间还原成谱面自己的
  （否则重算会吃到上一次合并过的 0 间隔 —— 那在拆手序里会被读成一个巨大的窗口）。
  面板手序介绍改成只显示当前模式，设置行之间加空行（`RowGap`，加在 Toggle/Slider/
  IntSlider/ColourRow/ColourToggle 的末尾）。§19 是新的一节。dll 74,240 字节。
- 2026-10-11（1.1.0）：**面板多语言**（`Localization.cs`，§20）。中文内置 —— **key 就是代码里的
  中文原文**，所以中文不需要文件、也不会缺条目；英文 `Mod\lang\en.txt`（188 个 key 全部有译文），
  复制它只翻译右列即可加语言。面板顶部语言按钮循环 `lang\*.txt`，首次运行跟随
  `Application.systemLanguage`（非中文 → en），选择存进 `Settings.json` 的 `Language`。
  `build.ps1` 只在缺失时复制语言文件。dll 77,312 字节。

## 18. 时间重置法（`TimeResetPlanner.cs`，2026-10-11）

`FingeringMode = 2`。**只看音符间隔，一个参数**（`ResetMs`，1.0.1 起默认 165ms），不读谱面 BPM、
不读角度、不做任何分组。这是对 §6.7.1 那个「3 分音永远排不对」问题的正面处理：旧算法先把音
积累成 180° 的窗口，窗口的度被 `2^k/速度` 缩放，所以三条 1/3 拍的音只在缩放正好为 1 时才凑成
一个窗口；其余情况下边界落进组中间，还把组后面那颗音一起吞进去，整段手序从此错位一位。

### 状态机（R = ResetMs）

状态 = 上一个音用的手指（D 副轮 / F 副手 / J 主手 / K 主轮），外加 `lastMain` / `lastOff`
（最近一颗主手 / 副手音的时间）。判据**全部含等号**：

| 上次手指 | 判据 | 本次 |
|---|---|---|
| 无 / 副轮 | — | J |
| 主手 | d > R | J（慢：一根指头能一直点） |
| 主手 | R/2 < d ≤ R | F（强拍主、弱拍副） |
| 主手 | d ≤ R/2 | K（一只手来不及，同手轮指） |
| 主轮 | d > R | J（这一段过去了，回落） |
| 主轮 | d ≤ R | F（还在快速段里，换手） |
| 副手 | dOff ≤ R/2 **或** dMain ≤ R | D（副手也得轮） |
| 副手 | 否则 | J |

`d` = 本次 − **上一个音**（主轮那一行比的就是主轮那一下本身，不是它回应过的那颗主手音；
长按挡在中间时"上一个音"是尾音，手指松手才算空出来，所以头落在主轮的 hold 从尾音起算）；
`dOff` / `dMain` = 本次 − `lastOff` / `lastMain`。第二条 OR 只在 F 是**主轮分支**产生时可能生效——F 来自主手分支时
`dMain = dPrev + dOff > R/2 + dOff`，要它 ≤ R 就必须 `dOff < R/2`，第一条早已成立。

- **外轮**是收尾的一遍扫描：每个 K/D 和它**前一个按键**交换手指 ⇒ 外侧指先落（K J D F）。
  交换只在按键之间做，**松手音不算一个按键**（算进去会拆散长条）。
- **长条**：尾音永远取头的手指（"尾音在哪儿就当成在哪"），并且**参照时间推进到尾音时刻**
  —— 手指到松手才空出来。链式长条（尾即下一个的头）自动整段同一条轨道。头允许排在 K/D
  （用户选的方案），外轮换完之后再把头的最终手指同步给尾音。
- **双押合并**不在这支里：`DoublePressMerge` 是三种手法共用的后处理（§19），在状态机与外轮之后跑。
- **边界容差** `RoundOff = 1e-9` 秒：时间是 double，间隔正好等于 R 或 R/2 时
  `0.45 - 0.3 = 0.15000000000000002`，判定在边界两边逐音乱翻（离线实测 g = 150ms 打出来是
  `JKFJKFJFDJKFJKJK`，加 1ns 容差后干净地是 `JKFD`）。`HandPlanner.RoundOff` 同因同值。

### 离线实测（`tools/TimeResetCheck.cs`，R = 165ms）

均匀间隔只有三档，分界正好是 R 和 R/2（没有 R/4 档）：

| 均匀间隔 g | 周期 | 内轮 | 外轮 |
|---|---|---|---|
| g > R（>165ms） | 1 | J… | J… |
| R/2 < g ≤ R（83~165ms） | 2 | JF | JF |
| g ≤ R/2（≤82ms） | 4 | JKFD | KJDF |

三连音要锁成 **JKF**，需要**组内 ≤ R/2、组间 > R/2**（R = 165ms 时锁得住的是 50+50+100 这种）：

| 音型 (ms) | 内轮 | 外轮 |
|---|---|---|
| 50+50+100（更快） | JKF 循环 | KJF 循环 |
| 80+150 | JKFD 循环 | KJDF 循环 |
| 80+220 | JKJK 循环 | KJKJ 循环 |
| 100+100+200 | JFJ 循环 | JFJ 循环 |
| 100+100+300 | JFJ 循环 | JFJ 循环 |
| 125+125+250 | JFJ 循环 | JFJ 循环 |
| 200+200+400 | J 连点 | J 连点 |
| 100×4（等分） | JFJF | JFJF |
| 50+400 | JKJK（**副手全程不动**） | KJKJ |

最后一行要知道：一趟「快 50ms 双击、再隔 400ms」的音型，主轮之后 `d > R` 就回落主手，
所以副手一直闲着。要它换手得把 R 调大（让 400ms 落进 R 以内）。

长条实测（`ok` = 每颗尾音都跟在头的轨道上）：单个长条 内轮 `JKKFDJ` / 外轮 `KJJDFJ`；
链式长条 内轮 `JKKKF` / 外轮 `KJJJF`。（主轮改成比"上一个音"之后，长条后面那颗音
从 J/K 变成 F/D —— 刚松手 50ms，算还在快速段里。）

双押合并实测（`DoublePressMerge`，R = 165ms、merge = 50ms，写成"时刻ms+手指"）：

| 音型 | 合并后 |
|---|---|
| 50+400 | `0J 0K 450J 450K 900J 900K …`（5 处） |
| 40+40+40 | `0J 0K 80F 80D 160J 160K …`（5 处） |
| 100+100+200 | 不变（间隔 100/200ms > 50ms） |

### 验证方式

```
cd LaneReader\tools
csc /nologo /target:exe /langversion:5 /out:TimeResetCheck.exe TimeResetCheck.cs `
    ..\src\LaneReader\TimeResetPlanner.cs ..\src\LaneReader\FingeringPlanner.cs
TimeResetCheck.exe 165                 # 内置表（均匀间隔 / 音型 / 长条 / 双押合并；不填按 300）
TimeResetCheck.exe <dump> 165 [inner]  # 参考宏 dump（TSX/TS 行）整首走一遍
```

面板：「排键（手序）」→ 手法按钮点三下循环 原版 → 拆手序 → **时间重置法**；选中它有
「主手重置间隔 (毫秒)」和「轮指方向」。日志行：
`time reset: 165ms reset, inner roll, D=.. F=.. J=.. K=.., N presses (M hold tails), X hand switches, <手指序列>`。

## 19. 双押合并（`DoublePressMerge.cs`，三种手法共用，1.0.3）

间隔小于 `MergeRollMs`（默认 50ms，0 = 关）的两颗音当作一次双押：后一颗被移到和前一颗
**同一时刻**，画在同一行上。它改的是"这个谱看起来是什么样"，不是"用哪根手指"，所以三种手法
一律套用 —— `Plugin.Tick` 在 planner 之后调用，日志行
`double presses: N note(s) pulled level with the note before them (merge 50ms)`。

- **只移按键**：长条的尾音是同一个键的松手，必须留在长条末端（长条的"下一颗音"正好是它的
  尾音，所以也不会弄坏任何长条的长度）。
- **判定要带 `RoundOff`**：时间是 double，`0.95 - 0.9 = 0.050000000000000044 > 0.05` ——
  第一版没带，离线 5 处只合了 2 处。
- 实测见 §18 末尾那张表（`50+400`、`40+40+40` 各 5 处；`100+100+200` 不受影响）。

### 刷新手序 + 重算的输入（同一次提交）

「排键（手序）」里的**「刷新手序」**按钮把 `_planFingerprint` 置成 NaN，下一帧强制重算。
但重算必须从**谱面自己的时间**出发：合并已经改过 `Note.HitTime`，直接拿改过的时间再排，
0 间隔在拆手序里会被读成一个巨大的窗口（degree 0 累加不到 180，窗口一路吞下去）。所以
`Plugin.RestoreChartTimes()` 在每次重算前把时间还原 —— 原始时间按 `Chart.Generation` 缓存，
只在换谱时重新抓。日志行 `plan: put N note(s) back on the chart's own time before re-planning`。

### 面板排版

- 手序介绍改成**只显示当前模式**那一段（三种全列太长）；ReADOFAIMacro 的外链只在拆手序下出现。
- 「双押合并」滑块放在「排键（手序）」的公共位置（三种手法共用）。
- 设置行之间加空行：`RowGap`（6px）加在 `Toggle` / `Slider` / `IntSlider` / `ColourRow` /
  `ColourToggle` 的末尾，一处改动覆盖整个面板。

## 20. 面板多语言（`Localization.cs`，1.1.0）

**key 就是中文原文**：面板里每个字都走 `L10n.Tr(text)`，查到就用译文，查不到原样返回 ——
中文因此不需要任何文件（代码里的中文就是兜底），任何语言缺条目也不会变空白。

- 入口：`Plugin.L`（Label）与 `Plugin.Btn`（Button）两个包装，面板里的 `GUILayout.Label(` /
  `GUILayout.Button(` 全部换成了它们；`Section` 的标题在拼 ▶/▼ 之前先 `Tr`，
  `Toggle`/`Slider`/`IntSlider`/`ColourRow`/`ColourToggle` 里的标签走 `L(...)`。
  开关上的字（开/关、四轨单独/四轨统一）是 `GUILayout.Toggle` 的第二个参数，单独 `L10n.Tr`。
- 动态文字拆成固定前后缀再查表：`"状态: " + …`、`"当前主题: " + …`、`"轨道 N 底色"`（拆成
  `轨道` / `底色` 两条）。
- 文件：`<mod>\lang\<code>.txt`，UTF-8，一行一条，**制表符分隔** —— 原文里常带 ` = `
  （`轨道长度 (px, 0=自动到屏幕顶)`、`间隔小于窗口 = 轮指；大于 = 食指交替。`），用 `=` 会歧义；
  不含 ` = ` 的 key 也接受 ` = ` 手写。`#` / `;` 是注释，值里的 `\n` 是换行，
  **行首空格属于 key**（那是面板的缩进）。
- 选择：`Settings.Language`，留空 = 跟随 `Application.systemLanguage`（非中文 → `en`）；
  顶部按钮在 `L10n.Available`（zh + `lang\*.txt`）之间循环，每次切换重新扫目录（丢文件进去不用重启）。
- 安装：`build.ps1` **只在目标缺失时**复制 `lang\*.txt`（和示例图片同一条规矩），玩家改过的
  译文不会被覆盖。
- 校验方式：把源码里所有含中日韩字符的字面量抽出来，和 `en.txt` 的 key 求差集。
  1.1.0 时 188 个 key 全部命中（`中文` 是语言按钮自己的名字，用不到但留着无妨）。


> 长按的验收要点（本机谱面数据里如果没有 hold 砖，日志里不会有 `chart holds:` 那行）：
> 找一张有长按的谱，看长条末端是否**始终**贴住下一颗音符的顶边、拖尾是否和普通音符一样存在。

