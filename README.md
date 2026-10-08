# LaneReader

> **A Dance of Fire and Ice（冰与火之舞）的 4K 下落式辅助读谱 mod。**
> 直接读取游戏内存里已经构建好的谱面，渲染成 4 条下落轨道，并用两套算法自动排键。

面向 ADOFAI 3.3.1 / Unity 6000.3.10f1 / Mono / UnityModManager 0.32.4。

---

## 功能

| 功能 | 说明 |
|---|---|
| 实时读谱 | 从游戏内存读已构建的谱面，**不需要谱面文件**，兼容任意自制谱 |
| 4K 视图 | 4 条轨道 / 判定线 / 判定框 / 键位标签 `D F J K` |
| 打击弹跳 | 音符到线时的弹跳反馈，纯时间驱动，不读游戏判定 |
| 暂停保留 | 暂停时叠加层留在屏幕上、音符停在原地；「按任意键开始」那屏只有静态框 |
| 小节线 | 每拍细线、每小节粗线；节拍按 TBPM 铺，跟着谱面的加速/减速变化 |
| 外观可调 | 明暗背景、音符边框、轨道背景透明度、整体坐标偏移、配色 |
| 自定义音符图片 | 用 png/jpg（含 alpha）替换音符色块；可保持宽高比、可按左右手着色、可热重载 |
| 整体旋转 | 0~360°，整个叠加层绕判定线中点旋转 |
| 两套手序 | 「AdoFalling 原版」与「拆手序」可切换 |
| 诊断日志 | 可关闭；排键、拍网格、每帧门控都有一行可对照的日志 |

---

## 自定义音符图片

把图片（png / jpg）放到 `Mods\LaneReader\`，面板「显示 → 音符 → 用自定义图片」打开，
路径默认 `note.png`（相对路径基于 mod 目录，也可以填绝对路径）。
安装目录里带了一张演示图 `note.png`（圆角胶囊、半透明填充），可以直接开来试。

- png 的 alpha 生效；jpg 没有 alpha
- 「保持宽高比」默认开（contain 居中）；关掉就拉伸填满音符框
- 「用左右手颜色着色」默认关，图片自带颜色时不用开
- 换图不用重启：最多 1 秒后自动重读，面板上还有「重新加载」按钮
- 面板会显示状态（`已加载 256x64` / `找不到文件` / `解码失败`），日志里对应 `note image: ...`

---

## 安装

UnityModManager 已就位的前提下：

1. 把 `LaneReader.dll` 和 `Info.json` 放进 `<游戏目录>\Mods\LaneReader\`
2. 启动游戏，UMM 的 mod 列表里会出现 `LaneReader (4K view)`

设置面板在 UMM 的 mod 设置里（全部手写 `GUILayout`，**不依赖 UMM 的自动绘制**
—— 那套在这台安装上对自绘面板的 mod 无效）。

---

## 构建

```powershell
Set-Location LaneReader
powershell -ExecutionPolicy Bypass -File .\build.ps1            # 只编译
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Install   # 编译并部署到游戏
```

- 用 `csc.exe`（.NET Framework 4.0.30319）编译，`/langversion:5`（**不能用 C# 6+ 语法**）
- **部署前必须完全退出游戏**，否则 DLL 被占用（`user-mapped section open`）
- 构建产物 `LaneReader/build/` 不入库

---

## 默认设置（v0.2.0）

| 设置 | 默认 | 说明 |
|---|---|---|
| 流速 | `930 px/s` | 1080p 基准像素，实际按屏幕高度缩放 |
| 手法 | **拆手序** | 可切回「AdoFalling 原版」 |
| 最大硬抗 BPM | `506` | 手序的标定旋钮，见下 |
| 轮指方向 | 外轮 `K J D F` | 可切内轮 `J K F D` |
| 音符边框 / 小节线 | 开 | 可关 |

> 已经存过 `Settings.json` 的机器会继续使用自己保存的值，不会套用新默认。

---

## 手序（排键）说明

**AdoFalling 原版**：相邻音间隔小于「轮指窗口」就轮指（轨道沿当前方向 ±1 走格），
否则在两根食指之间交替。

**拆手序**（默认，移植自 [ReADOFAIMacro](https://github.com/TonyLimps/ReADOFAIMacro)）：

1. 把谱面 BPM 归一化（翻倍到 >550、减半到 ≤1100、超过硬抗上限再减半一次）
2. 按归一化的一拍把音符切成「窗口」：窗口内音符数越多，说明这拍越挤
3. 窗口内的音符数决定指法（1 个 = 单指，2 个 = 同一只手两根手指＝外轮，更多则对半分给两手）
4. **窗口长度决定下一个窗口换不换手** —— 这一条是"拆手"的核心：
   一拍左右换手、更长则不换、长音回主手单指重复

完整的推导、4K 适配表、离线对比验证方法，以及**实测调好的那套标定**（为什么
`ApplyHandSplit` 里那一行 BPM 来源不能改）都写在 [`HANDOFF.md`](HANDOFF.md) §14。

---

## 开发与验证

- [`HANDOFF.md`](HANDOFF.md)：完整技术交接文档 —— 架构、每个踩过的坑、调试方法、门控规则
- [`LaneReader/tools/HandPlanCheck.cs`](LaneReader/tools/HandPlanCheck.cs)：
  离线验证工具，把移植版与参考实现**逐窗口**比对（28 张谱面 / 17558 个窗口，
  决策与指法符号完全一致）

---

## 许可

本项目以 MIT 发布，见 [`LICENSE`](LICENSE)。

排键算法移植自他人项目，版权与来源见 [`THIRD-PARTY.md`](THIRD-PARTY.md)。
