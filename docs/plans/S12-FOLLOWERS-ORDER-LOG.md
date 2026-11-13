# S12 · 粉丝公开内容与顺序修正 · 执行记录

2026-11-13 · 590 用例全绿（S11 为 579，新增 11）

## 一、这一步补的是什么

两件事，都是回证据时发现的偏差：

1. **资料修改少一项**：原版有「粉丝公开内容」（屏蔽关键词），我们只做了签名 / 类别 / 发帖 / 头像 / 背景图。
2. **顺序错了**：S11 排成 头像 → 背景图 → 发帖 → 签名 → 类别，与证据不符。

顺带纠正一个更早的错：**屏蔽词被放在了视频上传页**（S10），它其实是「资料修改 → 粉丝公开内容」的参数。

## 二、证据

### 2.1 决定顺序的是「分发串」，不是字符串堆的先后

原版在同一个方法里，每一项都连续出现「主文案 / 失败 / 跳过」三连
（`_analysis/u16_all.txt` 3468–3491）：

```
: 已跳过【修改背景图】
: 已跳过【修改头像】
: 修改公共主页发帖设置...
: 公共主页发帖设置已修改为【仅限自己】
: 修改公共主页发帖设置失败(继续下一步)
: 已跳过【修改公共主页发帖设置】
: 修改粉丝公开内容...
: 修改粉丝公开内容失败，第 N
: 修改粉丝公开内容失败(继续下一步)
: 已跳过【修改粉丝公开内容】
: 修改个性签名...
: 修改签名失败，第 N
: 修改签名失败(继续下一步)
: 已跳过【修改个性签名】
: 修改类别...
: 修改类别失败，第 N
: 修改类别失败(继续下一步)
: 已跳过【修改类别】
: 修改主页名字...
: 修改主页名字失败，第 N
: 修改主页名字失败(可能无权限)
: 已跳过【修改主页名字】
: 修改完成
```

这就是执行顺序：**背景图 → 头像 → 发帖设置 → 粉丝公开内容 → 个性签名 → 类别 → 主页名字**。

**S11 错在哪**：当时看到「修改头像...」在堆里比「修改背景图...」靠前就定了顺序。
但字符串堆反映的是**方法体的书写顺序**，不是**分发顺序** —— 写方法时先写头像那把方法，
不代表运行时分发也先调它。教训记在这里：**定顺序要找「每项一条」的分组串，别拿堆里两段不相干的串比先后**。

### 2.2 屏蔽关键词的完整串（3307–3348）

```
: 跳转到粉丝公开内容设置页...            https://www.facebook.com/settings?tab=followers_and_public_content
textarea[id='CometUSFTagInputFreeformDialogTagEditor'][placeholder*='限 1000']
: 未找到关键词输入框，跳过关键词设置
: 输入屏蔽词...
.com  https  pn  dn  inbox
: 共 N 个屏蔽词:
Enter
: 已添加屏蔽词: X
: 点击保存按钮...
: 已点击保存
: 未找到保存按钮，尝试按 Escape 关闭...
: 粉丝公开内容设置完成
: 修改粉丝公开内容失败:
```

三个结论：

- **它是「输入即打标签」**：`Enter` 单独作为一条串出现，说明每敲一个词要按一次回车才变成标签。
  只填不回车，词留在输入框里，保存后一起丢 —— 而日志照样能打「已添加屏蔽词」。
- **默认那五个词与配置默认值一致**：`.com https pn dn inbox` 正是 `CommentsBlockText` 的默认值，
  这就是「屏蔽词属于这一项」的直接证据。
- **输入框找不到不是失败**：「未找到关键词输入框，跳过关键词设置」之后仍能看到「设置完成」。

### 2.3 配置键的全表（2546–2590）

这一步把原版 `settings.json` 的键全捞出来了，**资料修改每项都有一个开关，全部按通道成对**：

| 谷歌版 | 比特版 | 含义 |
|---|---|---|
| `GoogleNamesFilePath` | `BitNamesFilePath` | 主页名称文件 |
| `GooglePageNamePassword` | `BitPageNamePassword` | 改主页名称要的主账号密码 |
| `GoogleImageDir` | `BitImageDir` | 头像与背景图目录 |
| `GoogleEditCoverImage` | `BitEditCoverImage` | 是否改背景图 |
| `GoogleEditAvatar` | `BitEditAvatar` | 是否改头像 |
| `GoogleEditPagePostSettings` | `BitEditPagePostSettings` | 是否改发帖设置 |
| `GoogleEditFollowersContent` | `BitEditFollowersContent` | 是否改粉丝公开内容 |
| `GoogleEditSignature` | `BitEditSignature` | 是否改个性签名 |
| `GoogleEditCategory` | `BitEditCategory` | 是否改类别 |
| `GoogleEditPageName` | `BitEditPageName` | 是否改主页名称 |
| `GoogleProfileEditResultExportDir` | `BitProfileEditResultExportDir` | 资料修改结果导出目录 |

**七项开关我们一个都没做**（只有发帖设置有一个布尔）。已列进未完成项。

## 三、设计

### 3.1 新增一个原语：`PressKeyAsync`

现有 9 个原语只有 `PressEscapeAsync`（页面级、不针对元素）。屏蔽词需要「聚焦输入框 → 按回车」，
拿页面级按键顶替会打在没有焦点的页面上，什么都不发生。

因此加 `PressKeyAsync(selector, key, index, ct)`，真实现用 `Locator.PressAsync`（它自己会先聚焦）。
刻意不复用 `FillAsync`：只填不回车，整项等于没配。

### 3.2 `FollowersContentEditor`

```
去空白去重 → 空则 Skipped（已跳过【修改粉丝公开内容】）
→ 导航 → 等关键词框
  ├─ 没有：记「未找到关键词输入框，跳过关键词设置」，返回 Skipped（不判失败）
  └─ 有：逐词 Fill + Enter，每词一行「已添加屏蔽词: x」
        → 「共 N 个屏蔽词: ...」
        → 有保存按钮：点它，「已点击保存」
        → 没有：按 Escape，「未找到保存按钮，尝试按 Escape 关闭...」
        → 「粉丝公开内容设置完成」，Changed
```

**保存按钮缺失不判失败**，依据是串的顺序（它后面紧跟着「设置完成」）。标签是即时生效的，保存只关面板。

### 3.3 顺序重排

`ProfileEditFlow` 改成 背景图 → 头像 → 发帖设置 → 粉丝公开内容 → 个性签名 → 类别（名字待 S13）。
顺序本身不影响结果（每项自己导航、自己保存），照它排是为了拿原版日志逐行对照时不用先换算。

### 3.4 屏蔽词的界面归位

输入框从「视频上传」页挪到「资料修改」页，标签改成「屏蔽关键词（粉丝公开内容，逗号分隔）」，
下面那行提示也改成「这一项是『资料修改 → 粉丝公开内容』，不是视频上传用的」。
`AppConfig.CommentsBlockWords` 的注释重写，把两次搞错的过程留在代码里，避免第三次。

## 四、反向注入（5 次，全部命中）

| # | 改坏了什么 | 变红的用例 | 条数 |
|---|---|---|---|
| A | 去掉 `PressKeyAsync("Enter")`，只填不回车 | 每个词都填一次并按一次回车（纯逻辑，靠 Timeline 交错）；屏蔽关键词逐词回车后变成标签（真 Chrome，靠页面 `__tags`） | 2 |
| B | 顺序改回旧版（头像 → 背景图） | 编排_六项顺序与原版分发串一致；全部跳过时整体算成功 | 2 |
| C | 输入框找不到时判 Failed | 找不到关键词输入框时跳过而不是失败；粉丝公开内容页没有关键词框时跳过 | 2 |
| D | 保存按钮缺失时不按 Escape | 没有保存按钮时按Escape并仍算成功 | 1 |
| E | 去掉屏蔽词去重 | 重复词只敲一次 | 1 |

A 是这一步最该守的：漏掉回车时**日志一行不少**，只有页面上的标签数能证明。
为了能守住「先填后按」这个先后关系，给 `FakePageInterop` 加了 `Timeline`
（`fill:x` / `press:Enter` 交错记录）—— 原来的 `Filled` 与 `PressedKeys` 两张表长度一样，查不出顺序反了。

## 五、未完成项

1. **主页名字没做** → S13。要走 iframe（找含 `cquick` 的 frame）→
   `form[action='/settings/name_change_preview/']` → `input[name='pseudonymous_name']` →
   `#save_password` 输主账号密码 → `button[value='1'].layerConfirm`。
   现有原语都在主 frame 上，必须先加「frame 内操作」能力。
2. **七项 `EditXxx` 开关没做**（见 2.3 表）。原版每项可单独开关，我们现在是「有值就改」。
3. **`ProfileEditResultExportDir` 没做**：原版会把资料修改结果导出成一个文件（推测每账号一行），
   我们只在任务日志里写。
4. **粉丝公开内容的「开关」部分没做**：同一段串里有 `input[role='switch'][aria-checked]` 的碎片，
   被 UTF-16 拆得读不出完整选择器，无法确认它控制哪几个开关、目标值是什么。只做了关键词。
5. **`FollowersKeywordInput` 与 `SaveButton` 未真机验证**：`id` 里的 `Comet` 是 FB 内部代号，
   改版最容易变；仿真页只能证明「选择器能命中我自己搭的 DOM」。
6. **词重复检测只在本批内做**：页面上已有的标签读不到（没有原版选择器证据），
   所以「这个词本来就在页面上」这种情况会被重复添加一次。

## 六、验收对照

| 原版行为（串） | 本步实现 | 证据行 |
|---|---|---|
| 「跳转到粉丝公开内容设置页...」+ `followers_and_public_content` | `NavigationAsync(FollowersContent)` + 同文案 | 3307 |
| `textarea[id='CometUSFTagInputFreeformDialogTagEditor'][placeholder*='限 1000']` | `FacebookSelectors.FollowersKeywordInput` 一字不差 | 3325 |
| 「未找到关键词输入框，跳过关键词设置」 | `NoInputLine` + Skipped | 3326 |
| 「输入屏蔽词...」→ 「已添加屏蔽词: X」 | 同两句，逐词一行 | 3327 / 3332 |
| `Enter` | `PressKeyAsync(..., "Enter")` | 3330 |
| 「共 N 个屏蔽词:」 | 同一句 | 3329 |
| 「点击保存按钮...」「已点击保存」 | 同两句 | 3333 / 3334 |
| 「未找到保存按钮，尝试按 Escape 关闭...」 | 同句 + `PressEscapeAsync` | 3335 |
| 「粉丝公开内容设置完成」 | 同句 | 3336 |
| 「已跳过【修改粉丝公开内容】」 | `FollowersContentEditor.SkipLine` | 3478 |
| 顺序：背景图 → 头像 → 发帖 → 粉丝 → 签名 → 类别 → 名字 | `ProfileEditFlow` 六项（名字待 S13） | 3468–3491 |

## 七、文件清单

**新增**

- `src/FbAuto.Automation/Profiles/FollowersContentEditor.cs`
- `tests/FbAuto.Tests/FollowersContentEditorTests.cs` — 9 条
- `tests/FbAuto.Tests/Support/FollowersMimicPage.cs` — 关键词框 + 保存按钮，Enter 才成标签
- `docs/plans/artifacts/S12-tests.txt`

**修改**

- `src/FbAuto.Automation/Abstractions/IPageInterop.cs` — 加 `PressKeyAsync`
- `src/FbAuto.Automation/Pages/PlaywrightPageInterop.cs` — 实现（`Locator.PressAsync`）
- `src/FbAuto.Automation/Selectors/FacebookUrls.cs` — 加 `FollowersContent`
- `src/FbAuto.Automation/Selectors/FacebookSelectors.cs` — 加 `FollowersKeywordInput`
- `src/FbAuto.Automation/Profiles/ProfileFieldResult.cs` — 加 `Skipped(field, msg, lines)` 重载；
  `ProfileEditOptions` 加 `FollowersContentUrl`
- `src/FbAuto.Automation/Profiles/ProfileEditFlow.cs` — 请求加 `BlockWords`；六项重排；接入粉丝公开内容
- `src/FbAuto.Application/Execution/ProfileEditExecutor.cs` — 传 `profile.CommentsBlockWords`
- `src/FbAuto.Core/Configuration/AppConfig.cs` / `ChannelProfile.cs` — 注释纠正（它是粉丝公开内容的屏蔽关键词）
- `src/FbAuto.Presentation/Views/MainWindow.xaml` — 屏蔽词输入框从视频上传页挪到资料修改页
- `tests/FbAuto.Tests/Support/FakePageInterop.cs` — `PressKeyAsync`、`PressedKeys`、`OnPressKey`、`Timeline`
- `tests/FbAuto.Tests/AvatarCoverEditorTests.cs` / `ProfileEditTests.cs` — 顺序与跳过数改为六项
- `tests/FbAuto.Tests/ProfileEditFlowIntegrationTests.cs` — +2 条真 Chrome 用例

## 八、口径

- 这一步不在原计划里。原计划 S12 是「主页名称」，但回证据时发现顺序错、少一项，
  修这两件比继续往上加功能更要紧（先把已做的做对），所以插了这一步，主页名称顺延到 S13。
- 资料修改现在**六项**，原版是**七项**（差主页名字）。
