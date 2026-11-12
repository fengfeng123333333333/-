# S6 · 页面原语接通真实浏览器 · 执行记录

- 日期：2026-11-12
- 前置：S5（`8460c70`，四件套纯逻辑，357 用例）
- 本步提交：见 `git log`（标题 `feat(S6)`）
- 测试：**377 全绿**（+20 条集成用例），明细见 `docs/plans/artifacts/S6-tests.txt`

---

## 一、这一步补的是哪个缺口

S5 交付了四件套的判断逻辑，但有一个必须写明的事实：**一条真机验收都没有**。
所有结论都来自单测和反编译串，页面动作一行没跑过。本步补两个缺口：

1. `IPageInterop` 有了真实现 —— `FbAuto.Automation/Pages/PlaywrightPageInterop.cs`；
2. 四件套第一次在**真 Chrome** 里跑完整流程 —— 用本地仿真页，不依赖 facebook.com。

第 2 点很关键：本机到 `facebook.com:443` 不通，但「选择器能不能命中」「隐藏 file input
能不能注入」「序号会不会点到隐藏元素」这些问题是**跟域名无关**的。只要 DOM 结构与属性
与真页面一致，本地页就能把它们全测出来。

---

## 二、选择器不是猜的

全部来自原版 DLL 按 UTF-16LE 重扫（`_analysis/utf16_strings.txt`，2026-11-12 复核）。
`docs/evidence/extracted/*.cn.txt` 里的中文是坏的（`慦嬀栀爀攀昀`），在里面 grep 无效。

| 能力 | 选择器（原文） | 备注 |
| :-- | :-- | :-- |
| 封面 file input | `div:has(> div[role='button']:has-text('封面照片')) > input[type='file']` | input 是 button 的**兄弟**，层级不能改 |
| 头像入口 | `[aria-label='头像操作']` | 点开才出现菜单项 |
| 选择头像菜单 | `div[role='menuitem']:has-text('选择头像')` | |
| 头像 file input | `div:has(> div[aria-label='上传照片']) > input[type='file']` | |
| 视频 file input | `input[type='file']` | 另有一份 JS 写法 |
| 类别下拉 | `input[role='combobox'][aria-label='类别']` | |
| 上传进度条 | `div[role='progressbar'][aria-valuemax='100'][aria-valuemin='0']` | |
| 移除按钮 | `div[role='button']:has-text('移除')` | `:has-text` 是子串匹配，会连带命中「移除视频」 |
| 移除视频选项 | `div[role='radio'] span:has-text('移除视频')` | |
| 继续按钮 | `div[role='button'] span:has-text('继续')` | 原版是 `div[aria-label='[object Object]']`，见 S5 记录 |

这些串集中放在 `FbAuto.Automation/Selectors/FacebookSelectors.cs`，**不复制到测试里**，
测试直接从同一个常量取值 —— 否则「测试通过」只说明测试和测试自己一致。

---

## 三、接口动了：点击与可用性加了序号

S5 的接口有六方法，但**无法执行按序号点击**，而草稿清理天生是按位置的：

- 原版串：`第 {n} 个继续按钮未找到，跳过` / `已点击第 {n} 个继续按钮`
- `DraftRemovalConfirmPlanner.Decide` 返回的就是 `OptionIndex` / `ConfirmIndex`

S5 只把这个下标算出来了，没人能执行它。本步给 `ClickAsync` / `IsEnabledAsync` 加 0 起的
`index`，并把 `CountAsync` 的语义定成**可见匹配数**：

| 语义 | 规则 | 理由 |
| :-- | :-- | :-- |
| `CountAsync` | 只数可见 | 关掉的对话框会留下隐藏的同名按钮 |
| `index` | 按可见匹配顺序 | 否则「第 2 个继续按钮」会点到隐藏的那个，Playwright 一直等到超时 |

这条差异是被测试逼出来的：仿真页在最前面放了一条**隐藏**的移除按钮，
若按全部匹配数取序号，第 1 条任务就会卡在「点不到隐藏元素」上。

---

## 四、Playwright 实现里四个刻意的选择

### 4.1 可见性探测不等待

`Locator.IsVisibleAsync()` 是即时求值，元素不存在返回 false 而不抛异常。
决策器每轮都靠它做分支，一等待就会把「没有弹窗」拖成 15 秒一轮。

### 4.2 可用性自己判，不依赖 `IsEnabledAsync`

Facebook 的按钮是 `div[role='button']`，禁用态靠 `aria-disabled='true'` 表达。
**实测 Playwright 1.41 的 click 可操作性认 `aria-disabled`**（会一直等「可用」直到超时），
而 `IsEnabledAsync` 是面向表单控件的。所以这里直接读 `disabled` 属性与 `aria-disabled`，
目的是**与 click 的可操作性保持一致** —— 不一致时决策器会挑一个点不动的按钮，
每条任务白卡一个点击超时。

### 4.3 隐藏的 file input 直接注入

真实页面的 file input 都是 `display:none`（靠外层按钮触发）。先等可见必然超时，
日志会明确打出 `locator resolved to hidden <input type="file">`。
`SetInputFilesAsync` 本身不需要可见。

### 4.4 异常必须转中文，且必须同时 catch `TimeoutException`

**Playwright 的「动作超时」抛的是 `System.TimeoutException`，不是 `PlaywrightException`。**
只 catch 后者，点击超时会把一句英文的 `Timeout 30000ms exceeded` 原样写进任务日志。
现在统一走 `IsPageFailure` + `WrapActionFailure`，超时单独给一句能定位原因的话
（被遮挡 / 动画未结束 / `aria-disabled` / 页面已切走）。

---

## 五、三个流程

### 5.1 弹窗清理：观测 → 决策 → 执行 → **确认消失**

一次只决策一步，因为点掉一个弹窗后页面的可见集合、序号全变了。
额外的关键检查：点完要确认弹窗真的消失，否则下一轮会对着同一个弹窗再点一次，
每轮再叠加一个点击超时，最后任务失败在与真正原因无关的地方。

### 5.2 封面换图：本地预检**不占**上传次数

两个入口刻意分开，而不是一个 `rule: null` 参数：

```csharp
CoverAttemptFlow.RunForCoverAsync(...)   // 默认 CoverSizeRule.FacebookPageCover
CoverAttemptFlow.RunForAvatarAsync(...)  // 不做本地预检
```

原因是第一版把 `rule` 的默认值写成 `null`，含义是「跳过预检」。测试里漏传一个参数，
100×100 的图就被当成封面直接上传了 —— 而且日志上一句异常都没有。这种**静默失效**比报错危险得多。

### 5.3 失败草稿：倒序执行 + 可见序号 + 二次确认可能不存在

- 倒序：移除一条后条目整体前移，正序会删错（由 `FailedDraftCleanupPlanner` 保证）；
- 可见序号：探测与点击都用可见顺序；
- 二次确认：有些路径点「移除」直接生效，两种按钮都数不到时按「已移除」处理，不记假错误。

---

## 六、本地仿真页能证明什么、不能证明什么

**能证明**：选择器在当前 Chrome 版本里真能命中；隐藏 input 能注入；序号语义正确；
倒序移除在真实 DOM 位移下不会删错；没有弹窗时确实没有按 Escape。

**不能证明**：Facebook 现在的 DOM 还是这个样子。仿真页是按反编译串搭的，
两者一旦分叉，仿真页全绿而真机全红 —— 这种情况只能靠真机验收发现。

---

## 七、验收

### 7.1 测试规模

| 阶段 | 用例数 |
| :-- | --: |
| S5 | 357 |
| S6 | **377** |

新增 20 条：`PlaywrightPageInteropIntegrationTests` 10 条（原语行为）、
`PageFlowIntegrationTests` 10 条（三个流程）。全部真启 Chrome，约 40 秒。

### 7.2 反向注入（测试必须能被证伪）

逐项改坏产品代码，确认对应用例真的变红，再还原。

| # | 注入的缺陷 | 变红用例 | 红了几条 |
| --: | :-- | :-- | --: |
| 1 | 找序号时不再跳过隐藏元素 | 序号按可见匹配数、草稿倒序移除 | 2 |
| 2 | 可用性判定忽略 `disabled` / `aria-disabled` | 可用性认 aria_disabled、弹窗禁用改 Escape | 2 |
| 3 | 点击只 catch `PlaywrightException`（漏超时） | 禁用的元素点不动 | 1 |
| 4 | 没有弹窗也按 Escape（日志不露痕迹） | 没有弹窗时绝不按 Escape | 1 |
| 5 | 草稿按位置正序执行 | 草稿倒序移除三条全清 | 1 |
| 6 | 本地预检永远放行 | 本地挡掉的图不会真的上传 | 1 |
| 7 | 注入前先等 file input 可见 | 隐藏的 file input 能直接注入 | 1 |

7 次全部命中，还原后全绿，`grep MUTATION` 无残留。

第 4 次值得单说：它是「日志照旧写『未发现弹窗』，但偷偷按了 Escape」。
这类缺陷单看日志完全正常，只有断言页面状态（`__escaped === false` + 编辑框仍开着 + 输入值未丢）
才能抓到。

### 7.3 过程中发现并修掉的缺陷

1. **`rule` 默认 null 让本地预检静默失效**（集测第一条就红：100×100 被当封面直接上传）
   → 拆成 `RunForCoverAsync` / `RunForAvatarAsync`，核心方法改成 private，不给默认值。
2. **Playwright 动作超时抛 `TimeoutException` 而非 `PlaywrightException`**
   → 英文超时会原样冒到任务日志；改为 `IsPageFailure` 统一判定 + 中文超时文案。
3. **接口缺序号语义**（S5 遗留）→ 决策器算出的下标根本无处执行；补 `index` 并把
   `CountAsync` 定为可见数。

### 7.4 踩的坑

1. **`aria-disabled` 会让 Playwright 的 click 一直等**。第一次写用例时想「点一下禁用按钮看看」，
   结果卡满 5 秒超时，日志是 `element is not enabled - waiting...`。这不是缺陷，
   而是它把「不可用」和「能点」对齐了 —— 我们的判定必须跟着这个口径。
2. **`:has-text()` 是子串匹配且祖先也命中**。`div[role='button']:has-text('移除')`
   会同时命中包含移除按钮的外层 div，也会命中「移除视频」。真机上出现两种文案并存时序号会错位，
   已写进 `FacebookSelectors` 的注释，真机验收时必须先确认。
3. **本地页的 file input 也必须 `display:none`**。第一版为了省事写成可见，
   「隐藏 input 能注入」这条用例就等于没测。

### 7.5 明确没做的事（不写成已完成）

| 项 | 现状 | 补上的条件 |
| :-- | :-- | :-- |
| 真实 Facebook 页面验收 | 一条都没有 | 境外出口 + 已登录 profile |
| 头像被拒时的提示文案 | 未知，暂时复用封面那条选择器 | 真机看到文案后改 `CoverAttemptFlow` 的判定信号 |
| 「关闭」按钮规则的分区 | 默认规则是 `div[aria-label='关闭']`，**不分区域**（原版行为） | 真机确认资料编辑对话框是否也带这个 aria-label；若带，需加对话框范围限定 |
| `rejectionWindow`（等拒绝提示的窗口） | 默认 5 秒，是经验值 | 真机按实际网络与转码延迟调参 |
| 五类任务的页面动作 | 仍是 `NotWiredTaskExecutor` | 下一步 |

**头像那个缺口要特别记住**：如果真机上头像被拒时弹的不是「请选择其他封面照片」，
流程会把它当「通过」，表现为「头像换了但没生效，日志却说成功」。

### 7.6 与计划口径的差异

计划里视频上传才是 S4 的业务主线。本步按依赖顺序推进：先把「页面原语 + 四件套」接通并验证，
再谈具体任务的页面动作。理由和 S5 一样 —— 顺序倒过来时，一处尺寸判断出错，
要连带怀疑是页面没加载、选择器变了、还是逻辑写错了。
