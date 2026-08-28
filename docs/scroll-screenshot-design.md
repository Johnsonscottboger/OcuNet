# OcuNet 滚动截图（Scroll Screenshot）实现设计方案

> 状态：**已实施**（v4 设计全部落地：`ScrollStitcher` / `ScrollScreenshotRunner` / `ScrollScreenshotContext` /
> `ScrollScreenshotResult` / 独立 `ScrollScreenshot` 静态类（`CaptureAsync` + 两个 `SaveAsync` 重载，
> **参数全部放在滚动截屏函数上，driver 不携带任何滚动参数**）；36 个新增测试全部通过，
> 全量 182 个测试通过；README 已增补 API 文档）
> 目标版本：OcuNet 主库新增 API，供 MyAgent RPA 自动化脚本调用。
> 本文档只做设计；实现另立任务。

---

## 1. 背景与目标

窗口内容超过一屏时，单次 `SaveWindowScreenshotAsync` 只能截到当前视口。
本方案在 OcuNet 内新增"滚动截图"能力：自动滚动目标窗口，逐帧截取，
重叠检测后拼接成一张完整长图。

核心要解决的两个问题（即需求中的两点）：

1. **滚动与拼接**：成熟的通用做法（Puppeteer fullPage / Playwright fullPage /
   AShot / 各滚动截图工具）——滚动 + 相邻帧重叠区域拼接。OcuNet 的滚轮步长是
   "tick"（1 tick = 一次 `WHEEL_DELTA=120` 事件），实际滚动像素数由目标应用决定、
   不可预知，所以本方案采用**实测重叠偏移**而非固定偏移拼接（详见 §3.5）。
2. **何时停止**：开放异步委托 `Func<ScrollScreenshotContext, Task<bool>>` 作为
   condition 参数——"某个元素可见 / 消失 / 任意自定义规则"全部由用户自己写；
   不传时默认滚动到底部，底部判定采用开源成熟的**帧差异 + 阈值**方案，
   对状态栏时间等"同位置轻微差异"**自动容错，无需任何外部参数**（详见 §3.3）。

范围：仅**向下**滚动截图（向上滚动是后续一行改动——`ScrollUpAsync` 已存在）；
仅"窗口级"截图（与现有 `SaveWindowScreenshotAsync` 对齐）；不做横向拼接、
不做 PrintWindow 捕获（沿用现有 `CopyFromScreen`，要求窗口可见不被遮挡，
文档注明限制）。

---

## 2. 现状盘点（可复用的基础能力）

| 现有能力 | 位置 | 说明 |
|---|---|---|
| `ScrollDownAsync(int scrollTicks)` / `ScrollUpAsync` | `OcuNetDriver` | 每 tick = 一次滚轮事件 + 100ms 延时；滚轮消息由系统路由到光标所在/焦点窗口 |
| `SaveWindowScreenshotAsync` / 私有 `GetWindowScreenshotAsync` | `OcuNetDriver` | monitor 截图（`CopyFromScreen`）→ 按窗口 bounds（**绝对屏幕坐标**）裁剪到 monitor 相对坐标并 clamp |
| `MonitorService.GetScreenshot` | `MonitorService` | 截图缓存默认 100ms；两次截图间隔超过缓存时长即拿到新帧 |
| `AttachedWindow.Bounds` | `AttachedWindow` | 每次访问实时刷新窗口矩形（绝对屏幕坐标） |
| `IsVisibleAsync(element, waitFor, searchRect)` | `OcuNetDriverExtensions` | 元素识别（OCR/图像模板），`NoSingleResultBehavior.Ignore` 版不抛异常；searchRect 为绝对屏幕坐标 |
| `MoveToAsync(x, y)` | `OcuNetDriver` | 绝对屏幕坐标移动鼠标（滚轮事件需要光标在目标内容上） |
| `MouseInterop.MouseWheelEventDown` | `MouseInterop` | `WHEEL_DELTA = 120`，每 tick 一次 |

关键事实：

- **坐标体系**：窗口 bounds、searchRect、鼠标坐标全部是**绝对屏幕坐标**；
  monitor 截图裁剪时统一转 monitor 相对坐标（`left = max(0, x - monitor.Left)` 并 clamp）。
- **滚轮路由**：Windows 的 `WM_MOUSEWHEEL` 发给焦点窗口，现代应用大多按光标位置转发。
  为保证滚到目标窗口，滚动前需要 `ActivateWindowAsync` + 把鼠标移到内容区锚点。
- **测试设施**：`OcuNetDriver` 内部构造器可注入 `IMonitorService` / `IMouseController` /
  `IElementRecognizer` 等 fake；现有 `FakeMonitorService` 只返回静态图，
  滚动截图测试需要新增"可推进画面"的 fake（见 §6）。
- **兼容性**：库支持 netstandard2.0 / 2.1 / net6；新代码沿用现有语言特性
  （record / init 已有先例），不引入新依赖。

---

## 3. 总体设计

### 3.1 新增 API（独立 `ScrollScreenshot` 静态类，极简签名）

滚动截图是**独立于 driver 的函数**：driver 只做输入/识别/截图等基础能力，
**所有滚动参数直接放在滚动截屏函数上**（不在 driver 上）。新增三个公开方法：

```csharp
// 拼接完成后返回长图 + 元信息（推荐，便于检查 StopReason）
public static Task<ScrollScreenshotResult> CaptureAsync(
    OcuNetDriver driver,
    Func<ScrollScreenshotContext, Task<bool>>? stopCondition = null,
    int wheelDelta = 60,
    int wheelTickIntervalMs = 120,
    int scrollToCaptureDelayMs = 0,
    ScrollMethod scrollMethod = ScrollMethod.Wheel,
    Func<Task>? customScrollTick = null,
    CancellationToken cancellationToken = default);

// 与现有 SaveWindowScreenshotAsync 对称的文件/流重载（参数集与 CaptureAsync 相同）
public static Task SaveAsync(OcuNetDriver driver, string destinationPath, ...);
public static Task SaveAsync(OcuNetDriver driver, Stream destinationStream, ...);
```

设计决策：

- **不引入 options 类，不暴露任何调优/条带参数**——仓库惯例是方法级直接参数
  （`AttachWindowAsync` 5 个参数、`ScrollDownUntilVisibleAsync` 4 个参数）；
  调优参数（`wheelDelta` / `wheelTickIntervalMs` / `scrollToCaptureDelayMs` /
  `scrollMethod` / `customScrollTick`）**直接放在滚动截屏函数上**，driver 上一律不放。
- **状态栏/粘性头部条带不需要外部传入**：重叠偏移算法对固定条带天然稳健
  （§3.5 匹配行计数 argmax），条带由多帧统计自动识别（§3.3），
  不需要用户知道自家 APP 状态栏多高。
- **演进策略**：将来出现真实需求（进度回调、自定义步长、出图裁剪、条带覆盖等）
  以**追加可选参数 / 重载**方式扩展，向后兼容；方法级参数确实超过 5~6 个时
  再引入 options 类——那是重构决策，不是 v1 决策。
- 未绑定窗口时抛 `WindowNotFoundException`（复用现有消息）。
- 实现为**薄适配层**：参数校验 + 窗口检查 + 激活 + 鼠标锚点管理，
  核心循环委托给内部 `ScrollScreenshotRunner`（§4），保证循环可脱离真实屏幕单测。

### 3.2 停止条件（condition 参数）——需求点 2 的前半

**不预置任何条件类型**，直接开放异步委托：

```csharp
public sealed record ScrollScreenshotContext
{
    public OcuNetDriver Driver { get; }          // 谓词内可调用 IsVisibleAsync 等任何 driver 方法
    public int ScrollStep { get; }                // 已完成滚动步数（0 = 尚未滚动）
    public TimeSpan Elapsed { get; }
    public Rectangle CaptureRect { get; }         // 绝对屏幕坐标（= 窗口 bounds）
    public CancellationToken CancellationToken { get; }
}
```

设计要点：

1. **`stopCondition == null` 即默认滚动到底部**（§3.3）。
   底部判定是"跨帧状态机"（连续无进展计数），不适合塞进单步求值的谓词，
   所以它是 runner 的**默认模式**，而不是一个条件。
2. **谓词语义**：滚动前先调用一次（元素可能本来就在屏上，此时单帧返回）；
   之后每滚一步、截一帧后调用一次，**返回 true 时该帧纳入拼接**
   （画面里正好包含满足条件的元素）。谓词是异步委托，内部可直接 `await`
   driver 的异步接口：
   - 元素可见：`async ctx => await ctx.Driver.IsVisibleAsync(element, TimeSpan.Zero, ctx.CaptureRect)`
   - 元素消失：`async ctx => !await ctx.Driver.IsVisibleAsync(...)`
   - 组合/任意逻辑：谓词内自由编排（且/或/超时/步数上限……）。
3. **自定义条件与"滚到底"的关系**：条件模式下，runner 检测到"连续无新内容"
   （页面不再滚动）也会提前停止并返回 `StopReason.NoProgress`——
   即"条件没等到但已经到底了"，调用方据此判断。无需用户显式表达"或滚到底"。
   **与底部模式相同的容忍计数**（`NoProgressTolerance`=2）：单步重叠测量失败
   （真实页面的空白行平台、瞬时动画等）不会立即放弃——只有连续 2 次无进展才停，
   避免偶发失败导致"只有一屏"的结果。

### 3.3 滚动到底部判定（默认行为）——需求点 2 的后半

桌面应用拿不到 DOM 内容高度，只能靠**帧差异 + 阈值**判定——这正是开源长截图
实现的通用做法（Android 长截屏、deepin 长截图、各类滚动截图工具），
其中"同位置有轻微差异"（APP 顶部状态栏的时间/电池/信号每帧变化、光标闪烁、
加载动画等）已有成熟处理。本方案做到**零参数自动容错**：

#### 采纳的成熟方案（详见 §9 参考）

| 成熟做法 | 来源 | 我们怎么用 |
|---|---|---|
| 帧间 SAD（像素差绝对值之和）与阈值比较判断"两帧相同"→ 判定滚动到底 | 手机长截屏专利 [CN118113192A](https://patents.google.com/patent/CN118113192A/zh)、[Android 长截屏实现原理](https://blog.csdn.net/qq_25804863/article/details/48698943) | 信号①的基础；改为**块级差异统计**（对"局部动态"更稳） |
| 状态栏/固定区域单独处理（裁剪或排除，避免时间等动态内容干扰） | Android 长截屏实现（状态栏识别裁剪）、各截图工具的"排除区域"设置 | **自动识别**：多帧统计同位置静态行 → 条带（§3.3"条带自动识别"），无需外部传入 |
| 连续 N 帧"相同"才确认到底（抗偶发抖动） | 长截屏工具普遍做法 | `NoProgressTolerance`（内部常量 2） |
| 重叠区自动识别（不依赖固定步长） | [自动识别重复截图区域](https://www.cnblogs.com/shengxingwang/articles/22161306)、[华为长截屏专利（按重复区域定拼接位置）](http://www.xjishu.com/zhuanli/55/202110482108.html)、[DuckCapture 原理](https://my.oschina.net/emacs_7995031/blog/19562718) | 即 §3.5 的 `FindOverlap`（行匹配计数 argmax）；同时复用其"偏移 ≈ 帧高 → 无新内容"信号 |

#### 三个停止信号

| 信号 | 判定 | 作用 |
|---|---|---|
| **① 帧内容静止**（主信号） | 滚动一步后，新帧与上一帧"基本相同"（**块级差异统计**：比较区分成 32px 块，块"变化" = 块内差异像素占比 > 10%；帧"相同" = 变化块占比 < 1%） | 页面不再变化 → 已到底。时钟/光标只影响 1~2 个块，在阈值内；整页内容移动则几乎所有块都变化 |
| **② 无新内容**（辅助信号） | 同位置变化行占比 ≤ 12%（`CountChangedRows`，行级近似：滚动百分比浮层/时钟/光标等局部动态只改几行，真实滚动改大部分行） | 覆盖"内容在动但页面没滚"的场景（轮播图、加载动画、整屏闪烁）——信号①会误判，信号②不会 |
| **③ 容忍计数 + 上限** | 连续 `NoProgressTolerance`（常量 2）次无进展 → 判定到底；`MaxScrollSteps`（常量 200）兜底 | 抗一次性的抖动（焦点丢失、动画停顿）；绝对防死循环 |

判定逻辑（伪代码，与 §4 循环合一）：

```
same = IsSameContent(prev, frame)                                        # 信号①（块级，无需条带知识）
overlap = same ? frame.Height : FindOverlap(prev, frame, ...)            # 信号②（行匹配计数 argmax，对条带天然稳健）
noNewContent = same || overlap == null || overlap >= frame.Height - MinNewContentPixels

if noNewContent: noProgress++ else noProgress = 0
if noProgress >= NoProgressTolerance:
    stop(StopReason.BottomReached)      # 最后一帧（无新内容）不纳入拼接
```

#### 对"同位置轻微差异"的自动容错（零参数）

1. **重叠偏移对条带天然稳健（关键设计）**：
   早期版本用"全带平均相似度 ≥ 0.90"接受偏移，状态栏条带会把正确偏移处的
   平均分拉低（数值例：帧高 800、状态栏 80px、滚动 400px 时，正确偏移处
   相似度只有 320/400 = 0.80 < 0.90 → 误判），所以才需要外部传条带高度。
   **v4 改为"匹配行计数 argmax"**：对每个候选偏移统计"匹配行数"
   （该行 ≥90% 采样像素一致即匹配），取匹配行数最大的偏移——状态栏/粘性头部
   那些行在正确偏移处只是不匹配、不计数，**不影响 argmax 找对位置**，
   条带知识完全不必要（算法细节见 §3.5）。
2. **条带自动识别（多帧统计）**：滚动结束后，对全部相邻帧对做"同位置行相似度"
   统计：某行在**多数帧对**中同位置相似度 ≥ 行阈值（0.90）→ 该行是固定行
   （状态栏背景/粘性头部恒定；内容行每帧滚动位置不同，只有到底后最后 1~2 帧
   静态，远低于多数）。顶部/底部连续固定行构成条带，写入结果
   （`TopBandRows` / `BottomBandRows`）——**信息/调试用途**，也作为将来
   "条带覆盖参数"（`int? ignoreTopRows = null`，null=自动）的默认值来源。
   注意：到底后所有行都会变静态，所以用"多数帧对"而非"最近帧对"做统计，
   避免到底后的静止帧把整帧误判为条带。
3. **块级差异统计（默认即容错）**：即使不依赖条带知识，时钟只改变少数几个块，
   变化块占比远低于阈值 → 信号①判定"相同"，不会误判成"内容还在动"。
   大块动态内容（广告轮播）导致块级判定失效时，由信号②兜底：
   内容没滚但画面在变，匹配行计数 argmax 仍给出 d ≈ H。
4. **感知哈希可选加速**（实现时评估）：aHash/dHash 的 Hamming 距离做"相同"预筛，
   v1 不做。

补充说明：

- **粘性头部/页脚不影响底部判定**：它们每帧恒定，不干扰"帧相同/无新内容"信号。
- **无限瀑布流页面**（永远有新内容）：信号①②都不触发，最终由
  `MaxScrollSteps` 截断，返回部分长图 + `StopReason.MaxStepsReached`（文档写明）。
- **为什么容忍 2 次**：第一次"无进展"可能是滚动瞬间的抖动，第二次确认
  （长截屏工具的普遍做法是"连续两帧相同才停止"）。

### 3.4 滚动执行细节（内部行为，非参数）

- **窗口激活**：滚动前 `AttachedWindow.ActivateAsync()`（恒执行），
  保证滚轮消息到达目标窗口（`WM_MOUSEWHEEL` 走焦点窗口）。
- **鼠标锚点**：滚轮事件需要光标位于内容区。滚动前
  `MoveToAsync(captureRect.Center)`（绝对屏幕坐标），
  **结束时在 `finally` 恢复原鼠标位置**。
- **步长**：每步 `ScrollDownAsync(3)`（内部常量：3 tick ≈ 100~150px，由应用决定）。
  步长只影响速度与帧数，**不影响拼接正确性**（重叠偏移是实测的）。
  常量值远小于常见视口高度，不会滚过一屏。
- **双线程连续采样（边走边截）**：**滚动线程**连续发送滚轮事件（每 tick 内置 ~100ms 间隔），
  目标应用的平滑滚动动画把它们合并成**连续线性滚动**（"一次几像素"由动画插值自然达成，
  wheel 保持标准 delta 保证兼容）；**截图线程**独立以 150ms 间隔采样——
  **滚动永不因截图停顿**（动作线性顺畅），帧是动画中间位置的快照，内容全覆盖不漏屏。
  停止时（无进展 ×3 / 谓词满足 / tick 上限）滚动线程被取消，等 300ms 动画收尾后
  补截最终帧。
- **截图**：每帧走与 `GetWindowScreenshotAsync` 相同的裁剪逻辑
  （monitor 相对坐标 + clamp），窗口 bounds 每次重新获取（窗口可能移动）。

### 3.5 拼接算法（ScrollStitcher，纯像素计算、无 driver 依赖）

**两阶段架构（采集 → 合并，成熟方案共识——deepin 长截图"采集-执行-合成"闭环、
Android 长截屏先收集全部截图再统一拼接）**：

```csharp
internal static class ScrollStitcher
{
    // —— 采集阶段（每次滚动一步后调用，只判"画面是否还在变"，不做拼接测量）——
    public static bool IsSameContent(Bitmap a, Bitmap b);            // 信号①：块级
    public static int CountChangedRows(Bitmap a, Bitmap b);          // 信号②：同位置变化行数（≤12% 视为无进展）

    // —— 合并阶段（拿到全部帧后统一分析，一次性拼接）——
    public static (int TopRows, int BottomRows) DetectStaticBands(IReadOnlyList<Bitmap> frames);  // 全帧统计固定条带
    public static int? FindOverlap(Bitmap upper, Bitmap lower, int? expectedOverlap = null, int ignoreTopRows = 0, int ignoreBottomRows = 0);
    public static Bitmap StitchAll(IReadOnlyList<Bitmap> frames, int topBandRows, int bottomBandRows);  // 测量+修正+拼接
    public static Bitmap Stitch(IReadOnlyList<Bitmap> frames, IReadOnlyList<int> overlaps, int ignoreTopRows = 0);
}
```

**FindOverlap 算法（匹配行计数 argmax，对固定条带天然稳健）**：

- 输入：`upper`（旧帧，在上）、`lower`（新帧，在下），帧高 H。
- **行匹配**：行 `u`（upper 中）与行 `l`（lower 中）"匹配" = 采样像素
  （每 8 列取 1）中通道差之和 ≤ 30 的比例 ≥ 0.90。
- **候选偏移** `d ∈ [minOverlapPixels, H]`：**全量扫描（步长 1）**——
  行匹配峰是单点（偏移差 1 行即全部失配），任何子采样都可能错过真实偏移；
  行比对"差异累积超阈值即提前结束该行"剪枝，1920×1080 帧对单次 <1s。
- **匹配口径 = 唯一匹配行（关键，真实页面鲁棒性）**：先统计每个 lower 行在
  多少个候选偏移匹配；**只在恰好 1 个偏移匹配的行**（内容行）参与计数——
  空白行/纯色行/相似行在多个偏移匹配（平台效应），不携带对齐信息，直接排除。
  真实页面（大量空白与相似行）由此获得与合成内容一致的尖锐峰值。
- **取 `d* = argmax 唯一匹配行数`**；接受条件：
  `唯一匹配数(d*) ≥ max(8, 唯一行总数 × 0.2)`（相对"有信息行"的分数，
  空白平台无法抬高它；错误偏移处唯一匹配数 ≈ 0）且 `d* ≥ 次优 × 1.25`；
  只有"几乎没有唯一匹配"（内容没动/无重叠）才返回 null（无进展）。
- **固定条带排除**：窗口标题栏/粘性头部等固定行在"同位置"（d=H）唯一匹配，
  会与内容对齐竞争。每步先用 `EstimateStaticBands`（相邻帧同位置连续相同行）
  实时估计顶部/底部条带，从匹配循环中排除（`ignoreTopRows`/`ignoreBottomRows`）——
  标题栏行完全不参与计数，内容对齐稳定胜出。
- **歧义处理**：重复内容导致的多个近等候选不再放弃（宁轻微错位，不出一屏结果）——
  有 `expectedOverlap`（上一步实测值）时选最强候选中最接近它的，否则选最强候选。
- **周期内容防混淆**（完全重复的行布局）：无唯一匹配行 → 返回 null（保守无进展，
  文档写明该限制；现实页面内容不会完全周期重复）。
- 可选升级：**零均值归一化互相关（NCC）+ Hann 窗**
  （[C# 窗口滚动截屏](https://ebr.clicksun.cn/mis/bbs/showapp.asp?id=39530)、
  [桌面端拼接算法](https://blog.csdn.net/liulun/article/details/147900009)），
  抗亮度差异更稳；v1 先用像素差比例，接口预留。

**IsSameContent 算法**（块级差异统计，见 §3.3）：帧分成 32×32 块；
块"变化" = 块内差异像素占比 > 10%；帧"相同" = 变化块占比 < 1%。
（比全帧 SAD 更抗局部动态；比全等更抗光标/抗锯齿。）

**DetectStaticBands 算法**（多帧统计，结果信息用途）：
对全部相邻帧对 `(F_{i-1}, F_i)` 计算同位置行相似度 `sim_i(y)`；
行 `y` 为固定行 = 多数帧对（≥ 50%）中 `sim_i(y) ≥ 0.90`；
`TopRows` = 从 y=0 起最长连续固定行数，`BottomRows` = 从 y=H-1 起最长连续固定行数。
（到底后的静止帧对会让内容行也变"同位置相似"，但只占末尾少数帧对，
  低于"多数"阈值——所以不会把整帧误判为条带。）

**StitchAll（合并阶段，全局分析）**：`F0..Fn` 全部到位后——
1. `DetectStaticBands` 全帧统计顶部/底部固定条带（标题栏/粘性头部 majority；
   滚动百分比浮层等"位置固定但内容变化"的行不静态，不会混入条带）；
2. 逐对 `FindOverlap`（唯一匹配行口径 + 排除条带行）测偏移，
   用上一步实测值做 `expectedOverlap` 消歧；
3. **全局一致性修正**：滚动量序列（每步 = 帧高 − 偏移）应接近常数——
   测量失败（null）用中位数补；**被两侧邻居夹住的孤立尖峰**用邻居均值平滑；
   **边界值保留实测**（最后一步可能被内容底部合法截断）；
4. `Stitch`：`F0` 整帧绘制（标题栏出现一次），后续帧从 `TopBandRows` 以下绘制——
   帧 i 的标题栏区域保持前一帧内容，接缝无缝、不重复。
右侧工具栏等列向固定区域不特殊处理（重叠重绘内容相同，视觉无缝）。
PNG 保存时用 `ImageFormat.Png`。

内存提示：默认步长 ~150px 时，100 步 ≈ 15000px 高 × 窗口宽 × 4B
（1920 宽约 115MB 原始位图）。`MaxScrollSteps` 常量 200 是上限。

### 3.6 内部常量与演进策略

**为什么不需要 options 类 / 条带参数**：见 §3.1（仓库惯例、真正必要的旋钮只有
`stopCondition`、以可选参数/重载演进）。条带自动识别后连 `ignoreTopRows` 也不进签名。

内部常量表（集中在 `ScrollScreenshotRunner` / `ScrollStitcher` 一处，
便于评审与调整）：

| 常量 | 值 | 说明 |
|---|---|---|
| `ScrollTicksPerCapture` | —（已移除） | 双线程架构：滚动线程连续发送，截图线程 150ms 独立采样 |
| `MaxScrollTicks` | 400 | 滚动线程 tick 上限（~40s，防无限滚动死循环） |
| `CaptureInterval` | 150ms | 截图线程采样间隔 |
| `FinalSettleDelay` | 300ms | 停止后动画收尾等待（补截最终帧） |
| `NoProgressTolerance` | 2 | 连续无进展次数，判定到底 |
| `MinNewContentPixels` | 8 | `d ≥ H - 8` 视为无新内容 |
| `MinOverlapPixels` | 24 | 重叠查找最小候选偏移 |
| `RowMatchThreshold` | 0.90 | 一行 ≥90% 采样像素一致 → 匹配行 |
| `MinMatchFraction` | 0.20 | 接受偏移：唯一匹配行数 ≥ max(8, 唯一行总数 × 0.2) |
| `MatchDominance` | 1.25 | 最优 ≥ 次优 × 1.25（唯一匹配口径下错误偏移≈0，通常自然满足） |
| `BlockSize` | 32 | 块级差异统计的块边长 |
| `BlockChangeThreshold` | 10% | 块内差异像素占比 > 10% → 块变化 |
| `ChangedBlockRatio` | 1% | 变化块占比 < 1% → 帧"相同" |
| `StaticBandMajority` | 50% | 行在 ≥50% 帧对中同位置相似 → 固定行 |
| 方向 | Down | v1 仅向下；向上是后续一行改动 |
| 窗口激活 / 鼠标锚点 | 恒执行 / CaptureRect 中心 | — |

### 3.7 结果与错误模型

```csharp
public enum ScrollScreenshotStopReason
{
    ConditionMet,      // 停止条件（用户谓词）满足（含首帧即满足）
    BottomReached,     // 默认模式：判定已滚动到底
    NoProgress,        // 条件模式：页面不再产生新内容而条件未满足
    MaxStepsReached,   // 步数上限（无限滚动页等）；返回部分长图
}

public sealed class ScrollScreenshotResult : IDisposable
{
    public Bitmap Image { get; }                    // 拼接长图（Save* 重载内部释放）
    public int FrameCount { get; }
    public int ScrollSteps { get; }
    public ScrollScreenshotStopReason StopReason { get; }
    public TimeSpan Elapsed { get; }
    public Rectangle CaptureRect { get; }
    public int TopBandRows { get; }                 // 自动识别的顶部固定条带行数（状态栏/粘性头部；信息/调试用途）
    public int BottomBandRows { get; }              // 自动识别的底部固定条带行数（同上）
}
```

**不抛异常（已确认"截到哪算哪"）**：`NoProgress` / `MaxStepsReached` 都返回
结果 + 部分长图 + 明确原因，RPA 脚本自行判断。只有窗口未绑定、参数非法等
编程错误抛异常（沿用现有消息资源模式）。

---

## 4. 核心循环（ScrollScreenshotRunner 伪代码）

```csharp
internal sealed class ScrollScreenshotRunner
{
    // 构造注入（全部可 fake）：
    //   stopCondition, capture: Func<Task<Bitmap>>, scrollStep: Func<Task>,
    //   makeContext: Func<int, ScrollScreenshotContext>, ct

    public async Task<ScrollScreenshotResult> RunAsync()
    {
        var condition = stopCondition;                     // null = 底部模式
        var frames = new List<Bitmap>();
        var overlaps = new List<int>();
        var sw = Stopwatch.StartNew();

        var prev = await capture();                        // 首帧
        frames.Add(prev);

        // 滚动前先求值：元素本来就在屏上 → 单帧结果
        if (condition != null && await condition(makeContext(0)))
            return Result(frames, ScrollSteps: 0, StopReason.ConditionMet, ...);

        var noProgress = 0;
        for (var step = 1; step <= MaxScrollSteps; step++)
        {
            ct.ThrowIfCancellationRequested();
            await scrollStep();                            // ScrollDownAsync(3)
            await this.CaptureStableFrameAsync();            // 稳定轮询：等平滑滚动动画结束（越过截图缓存）
            var frame = await capture();
            ct.ThrowIfCancellationRequested();

            var same = ScrollStitcher.IsSameContent(prev, frame);
            var overlap = same ? frame.Height : ScrollStitcher.FindOverlap(prev, frame, Constants);
            var noNewContent = same || overlap == null
                || overlap >= frame.Height - MinNewContentPixels;

            if (condition == null)                         // —— 底部模式 ——
            {
                noProgress = noNewContent ? noProgress + 1 : 0;
                if (noProgress >= NoProgressTolerance)
                    return Result(frames, step - 1, StopReason.BottomReached, ...);
            }
            else                                           // —— 条件模式 ——
            {
                if (noNewContent)
                    return Result(frames, step - 1, StopReason.NoProgress, ...);
                if (await condition(makeContext(step)))
                {
                    frames.Add(frame);                     // 满足条件的帧纳入拼接
                    return Result(frames, step, StopReason.ConditionMet, ...);
                }
            }

            frames.Add(frame);
            overlaps.Add(overlap ?? frame.Height - EstimateStep(frames));
            prev = frame;
        }

        // 条带识别（信息用途；全部帧对统计，见 §3.5）
        var (topBand, bottomBand) = ScrollStitcher.DetectStaticBands(frames);
        return Result(frames, MaxScrollSteps, StopReason.MaxStepsReached,
                      topBand, bottomBand, ...);
    }
}
```

> `overlap == null` 的兜底：正常流程中无重叠已在上面按"无进展"处理，
> 此处防御性回退到估计步长，保证拼接不中断。

**OcuNetDriver 适配层**（公开方法内部）：

```
1. _attachedWindow == null → throw WindowNotFoundException
2. captureRect = _attachedWindow.Bounds（绝对屏幕坐标）
3. _attachedWindow.ActivateAsync()
4. 记录原鼠标位置；MoveToAsync(captureRect.Center)
   finally { MoveToAsync(原位置) }
5. capture = 每帧：monitor = GetCurrentMonitorAsync()；
           windowRect 转 monitor 相对坐标 + clamp（同 GetWindowScreenshotAsync）；
           GetScreenshot(monitor).Crop(rect)
6. scrollStep = () => ScrollDownAsync(3)
7. makeContext = step => new ScrollScreenshotContext(this, step, elapsed, captureRect, ct)
8. return await new ScrollScreenshotRunner(...).RunAsync()
```

---

## 5. 文件规划与命名

遵循仓库现有扁平结构 + 单类单文件：

```
src/OcuNet/
├── ScrollScreenshotContext.cs        # 上下文 record（传给用户谓词）
├── ScrollScreenshotResult.cs         # 结果类（IDisposable）+ StopReason 枚举
├── ScrollStitcher.cs                 # internal：IsSameContent / FindOverlap / DetectStaticBands / Stitch（纯像素算法 + 内部常量）
├── ScrollScreenshotRunner.cs         # internal：滚动-截图-判定循环
├── OcuNetDriver.cs                   # +3 公开方法（薄适配层）
└── Messages.resx                     # 新增校验/异常消息（沿用 Designer 生成模式）

src/OcuNet.Tests/
├── ScrollStitcherTests.cs            # 纯算法单测
├── ScrollScreenshotRunnerTests.cs    # 循环单测（fake capture/scroll/谓词）
└── OcuNetDriverTests_ScrollScreenshot.cs  # driver 级 + 集成（真实可滚动窗口）

README.md                             # API reference 增补
samples/OcuNet.ConsoleApp/             # 可选：滚动截图示例
```

兼容性：netstandard2.0 需注意（record 已有先例、`CancellationToken` 可用、
不引入新 NuGet 依赖；`Task.Delay(TimeSpan, ct)` 在 netstandard2.0 有重载）。

---

## 6. 测试方案

### 6.1 ScrollStitcherTests（纯算法，无依赖）

1. **合成长画布**（渐变 + 文本行，高 = 3.5 屏，宽 = 窗口宽），
   按已知偏移序列裁出连续帧（含 ~20% 重叠）：
   - `FindOverlap` 返回值与真实偏移误差 ≤ 1px；
   - `Stitch` 结果与原始长画布逐像素一致（容差 ±1）。
2. **无重叠**：偏移 > 帧高 → 返回 null。
3. **动画干扰**：帧中加入少量噪声行（模拟加载动画/光标闪烁）→ 偏移仍正确。
4. **状态栏时钟模拟**（零参数自动容错，核心用例）：画布顶部 80px 为"状态栏"，
   每帧该条带内的时间文本/图标不同（其余内容正常滚动）：
   - **不传任何参数** → `FindOverlap` 偏移正确（匹配行计数 argmax 对条带稳健）；
   - `DetectStaticBands` 识别出 `TopRows ≈ 80`；
   - 底部模式全流程 `StopReason.BottomReached`、拼接 == 长画布。
5. **粘性头部**：每帧顶部 80px 恒定（模拟固定 header）+ 内容滚动 →
   零参数下偏移正确、`TopRows ≈ 80`。
6. **条带识别抗"到底静止"**：序列末尾附加 2 帧静止对（模拟到底）→
   `DetectStaticBands` 不把内容行误判为条带（多数帧对阈值生效）。
7. **相同帧**：`FindOverlap` 返回 H，`IsSameContent` 返回 true。
8. **性能冒烟**：1920×1080 帧对，`FindOverlap` 单次 < 1s（CI 上给宽松阈值）。

### 6.2 ScrollScreenshotRunnerTests（循环逻辑，fake 输入）

- **底部模式**：fake capture 按固定步长推进"画面"（共享状态），fake scrollStep
  推进偏移 → 断言 `StopReason.BottomReached`、拼接结果 == 长画布、步数正确。
- **底部模式 + 状态栏时钟**：画面推进 + 顶部条带每帧变化 → 零参数正确判定到底。
- **底部模式 + 大块动画**：大区域每帧变化（块级判定失效）→ 信号②（d≈H）兜底判定到底。
- **条件模式（异步谓词）**：`stopCondition: async ctx => ctx.ScrollStep >= N` →
  `ConditionMet`、帧数正确、满足条件的帧已纳入。
- **条件模式 + 页面到底但条件未满足** → `StopReason.NoProgress`。
- **MaxSteps**：画面永不停止（无限画布）→ `MaxStepsReached` + 部分长图。
- **首帧即满足条件** → 单帧结果、0 步滚动。
- **谓词可用性**：`async ctx => await ctx.Driver.IsVisibleAsync(...)`（fake driver：
  `FakeMonitorService` + `FakeElementRecognizer` 注入，不依赖真实窗口）验证
  "元素可见/消失"的手写谓词写法与 await 语义。

### 6.3 Driver 级测试

- **新 fake**：`FakeScrollableMonitorService`（持有长画布 + 滚动偏移，共享
  `FakeScrollState`，`GetScreenshot` 返回当前偏移下的窗口裁剪帧）+ 扩展
  `FakeMouseController`（`WheelDown` 推进共享状态偏移）——整个
  `ScrollScreenshot.CaptureAsync` 流程可在不碰真实屏幕的前提下跑通：
  断言拼接 == 长画布、鼠标位置已恢复、StopReason。
- **集成测试**（对齐现有 `WindowAttachIntegrationTests` 模式）：
  真实 `TestWindow` 内放可滚动内容（AutoScroll Panel / 多行 TextBox），
  真实滚轮跑完整流程，断言长图高度 > 视口高度、首尾内容正确。
  CI 环境不稳定时按仓库现有惯例标注/跳过。

---

## 7. 边界情况与限制（文档需写明）

| 场景 | 行为 |
|---|---|
| 状态栏时间/电池/信号等固定位置动态内容 | **自动容错**：重叠偏移用匹配行计数 argmax（条带不计数、不影响找偏移）；帧相同用块级容差；条带识别结果写入 `TopBandRows/BottomBandRows` 供调试 |
| 固定条带接近半屏（极端） | 匹配行数优势变小，自动识别可能退化（按无进展保守处理）；届时按演进策略加 `int? ignoreTopRows = null` 可选覆盖参数（null=自动） |
| 周期性重复内容（长列表/同布局行） | 可能混淆偏移；取最接近上一步实测偏移的候选，仍无法区分则按无进展保守处理 |
| 窗口被遮挡 / 最小化 | 无法可靠截图（`CopyFromScreen` 局限，与现有截图一致）；`AttachWindowAsync` 的 activate 仅保证前台 |
| 滚动期间窗口移动/缩放 | `captureRect` 固定于起始位置，可能错位；罕见，文档注明 |
| 平滑滚动动画长 / 常驻动画 | 稳定轮询超时（默认 2s）兜底取最后帧；动画极长时可后续加参数调大 `MaxSettleTime`；必要时提高 `NoProgressTolerance` |
| 无限瀑布流 | 永远有新内容 → `MaxStepsReached` 截断，返回部分长图（已确认"截到哪算哪"） |
| 高 DPI / 多显示器 | 沿用现有 monitor 相对裁剪 + clamp；跨屏窗口只截当前 monitor 部分 |
| 内容恰好每帧布局相同（罕见） | 可能误判到底；`NoProgressTolerance` 提高可缓解 |
| 需要向上滚动 / 进度回调 / 自定义步长 / 出图裁剪 | v1 不做（内部常量）；以可选参数/重载演进，向后兼容 |

---

## 8. 与 MyAgent RPA 的集成示例

MyAgent 自动化脚本（编译型 C#）直接调用；停止条件全部由用户谓词手写（异步），
状态栏等条带**无需任何参数**（算法自动识别）：

```csharp
using var driver = OcuNetDriver.Create();

await driver.AttachWindowAsync("某某管理后台");

// 例 1：滚动截全页。APP 模拟器顶部状态栏（时间每帧变化）由算法自动容错
var result = await ScrollScreenshot.CaptureAsync(driver);
Console.WriteLine($"条带: 顶 {result.TopBandRows}px / 底 {result.BottomBandRows}px");   // 调试用
result.Image.Save(@"C:\reports\page-full.png", ImageFormat.Png);

// 例 2：滚到"提交订单"按钮出现即停（谓词自己写"元素可见"）
var submit = MyLibrary.Instance.Pages.Order.SubmitButton;
var result2 = await ScrollScreenshot.CaptureAsync(
    driver,
    async ctx => await ctx.Driver.IsVisibleAsync(submit, TimeSpan.Zero, ctx.CaptureRect));
if (result2.StopReason == ScrollScreenshotStopReason.ConditionMet)
    result2.Image.Save(@"C:\reports\order-form.png", ImageFormat.Png);
else
    Console.WriteLine($"未找到按钮：{result2.StopReason}，已截 {result2.FrameCount} 帧");

// 例 3：滚动加载中"加载中"文案消失即停（谓词自己写"元素消失" + 自定义组合）
var result3 = await ScrollScreenshot.CaptureAsync(
    driver,
    async ctx => !await ctx.Driver.IsVisibleAsync("加载中", TimeSpan.Zero, ctx.CaptureRect)
                 && ctx.ScrollStep >= 2);
```

后续可选：`AutomationTaskToolHarness` 增加 `scroll_screenshot` 工具，
让对话式 RPA 直接生成滚动截图（本方案之外的独立小任务）。

---

## 9. 开源参考与借鉴点

| 项目/资料 | 借鉴点 | 差异 |
|---|---|---|
| [手机长截屏专利 CN118113192A](https://patents.google.com/patent/CN118113192A/zh) | 帧间 **SAD（像素差绝对值之和）+ 阈值**判断两帧相同 → 滚动到底判定 | 专利为全帧 SAD；我们升级为块级差异统计（对局部动态更稳），并叠加无新内容信号 |
| [Android 长截屏(滚动截屏)实现原理](https://blog.csdn.net/qq_25804863/article/details/48698943) | 滚动截屏整体流程；**状态栏等固定区域的单独处理**；连续帧相同判定 | Android 侧可识别/控制滚动条；桌面滚轮只能像素判定。状态栏处理我们改为**自动识别**（多帧统计） |
| [deepin 长截图原理（官方技术分享）](https://deepin.hashnode.dev/technical-sharing-screen-capture-principles-of-long-screenshots) | 滚动截图的感知-执行-合成闭环、重叠区处理 | 同为桌面环境，最接近的参考 |
| [自动识别重复截图区域（长图拼接）](https://www.cnblogs.com/shengxingwang/articles/22161306) | **重叠区自动识别**（行匹配定位重复区域），即我们的 `FindOverlap` 思路 | 它面向静止长图拼接；我们面向滚动实时帧，且用匹配行计数 argmax 抗固定条带 |
| [华为长截屏专利（按重复区域定拼接位置）](http://www.xjishu.com/zhuanli/55/202110482108.html) | 根据重复区域确定两帧拼接位置 | 同上 |
| [DuckCapture 滚动截图技术原理](https://my.oschina.net/emacs_7995031/blog/19562718) | 滚动截图 + 长网页拼接算法解析 | — |
| [C# 从零实现窗口滚动截屏](https://ebr.clicksun.cn/mis/bbs/showapp.asp?id=39530) | 重叠匹配用**零均值归一化互相关（NCC）+ Hann 窗**，抗亮度/边缘差异 | 我们 v1 用行匹配像素差比例（更快），接口预留 NCC 升级 |
| [桌面端截长图：图像融合拼接关键算法](https://blog.csdn.net/liulun/article/details/147900009) | 桌面端滚动截图拼接算法综述 | — |
| [wayscrollshot（Wayland 滚动截图开源工具）](https://github.com/jswysnemc/wayscrollshot) / [roll_screenshot（Python）](https://github.com/fandesfyf/roll_screenshot) / [easy-capture](https://github.com/xinhecuican/easy-capture) | 开源实现参考：滚动节奏、重叠、停止判定 | 各自平台绑定；无 condition 概念 |
| Puppeteer / Playwright `fullPage` | 视口滚动 + 重叠拼接整体思路；截前等待稳定 | 浏览器可读 DOM 内容高度；桌面应用只能像素判定 |

一句话总结借鉴思路：**"小步滚动 + 重叠区自动识别拼接 + 帧差异阈值判定到底
（块级容差 + 连续 N 帧确认）"** 是开源成熟骨架；
OcuNet 特有的三处改编是——① 用**实测重叠**替代固定偏移（滚轮步长不可知）；
② 停止语义开放为**用户委托**（条件任意自写），默认行为复用成熟像素判定；
③ 固定条带（状态栏/粘性头部）**零参数自动容错**（匹配行计数 argmax + 多帧统计）。

---

## 10. 实施里程碑

| 里程碑 | 内容 | 验收 |
|---|---|---|
| M1 | `ScrollStitcher`（IsSameContent 块级 / FindOverlap 行匹配 argmax / DetectStaticBands / Stitch）+ 纯算法单测 | 偏移误差 ≤1px；拼接与长画布一致；**状态栏时钟、粘性头部用例在零参数下通过**；条带识别正确（含"到底静止"抗误判用例） |
| M2 | `ScrollScreenshotContext` + runner 循环（底部模式 + 异步谓词模式）+ 单测 | 四类 StopReason 全路径覆盖；异步谓词可用性验证 |
| M3 | `ScrollScreenshotResult`（含条带信息字段）+ `OcuNetDriver` 公开方法 + 消息资源 + driver 级测试 | fake 屏全流程通过 |
| M4 | 真实窗口集成测试 + README API 增补 + 示例 | 集成测试通过、文档齐全 |

每个里程碑独立可评审、可回退；M1 不依赖任何驱动状态，可最先落地验证算法正确性。
