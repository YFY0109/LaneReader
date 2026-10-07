# 第三方代码

本项目自身以 MIT 发布（见 [`LICENSE`](LICENSE)），但其中两处排键算法来自他人项目，
版权与来源如下。

---

## 1. ReADOFAIMacro —— 拆手序算法

`LaneReader/src/LaneReader/HandPlanner.cs` 是
[ReADOFAIMacro](https://github.com/TonyLimps/ReADOFAIMacro)（作者 **Tony Limps**，
MIT 许可）按键分配算法的 C# 移植，来源文件 `src/PlayScript.cpp`
的 `PlayScript()` 构造函数与 `allocateFingers()`。

移植内容包括：BPM 归一化、按拍切窗口、窗口内指法分配、按窗口长度换手的规则。
只依赖音符时间，不依赖该项目的 C++ 运行时代码。

按 MIT 许可要求，其版权与许可声明照录如下：

```
The MIT License (MIT)
Copyright (c) 2026 Tony Limps

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

---

## 2. AdoFalling —— 原版排键算法

`LaneReader/src/LaneReader/FingeringPlanner.cs` 移植自
[AdoFalling](https://github.com/CatciSurn/AdoFalling) 的 `FingeringPlanner.cs`
（间隔小于窗口则沿轨道 ±1 走格轮指，否则两食指交替）。

该仓库当前没有附带许可证文件，因此无法照录声明；此处按来源署名致谢。
如果上游后续补充许可证，请同步更新本节。

---

## 其它

`LaneReader/tools/HandPlanCheck.cs` 是离线验证工具：它对比本项目移植版与
ReADOFAIMacro 的逐窗口决策。为了得到参考输出，本地临时克隆了该项目并打了两处
**仅用于分析**的补丁（不真的发送按键、导出每个音的时间与逐窗口决策）。
这些补丁与克隆产物**不在本仓库内**。
