# S11 · 头像与背景图 · 执行记录

2026-11-13 · 579 用例全绿（S10 为 553，新增 26）

## 一、这一步补的是什么

资料修改原本只做三项（签名、类别、发帖设置），界面上的「头像」「背景图」两行还是
「S5 阶段接入」的灰色占位。这一步把两项做成真的：

- **头像**：点头像区域 → 等「选择头像」菜单 → 点它 → 等「上传照片」input 进 DOM → 注入 → 保存 → 等保存完成
- **背景图**：等主页上的封面 file input → 注入（被拒就换下一张）→ 点「保存更改」→ 等保存完成

**主页名称不在这一步**：它要走 `settings?tab=profile&section=name` 的 **iframe** 取
`form[action='/settings/name_change_preview/']`、填 `input[name='pseudonymous_name']`、
再输主账号密码过 `#save_password`。这需要一个新的「frame 内操作」原语，
塞进这一步会让本步的验收变成「六项都过」，而其中一项的失败原因是权限与审核，和前五项完全不同。
单独作 S12。

## 二、证据

全部来自重扫 `_orig/FaceBook自动化V1.1.1_1/FaceBook自动化.dll` 得到的
`_analysis/u16_all.txt`（UTF-16LE 重解）。段落在 3436–3457 行，原样摘录：

```
div:has(> div[role='button']:has-text('封面照片')) > input[type='file']
: 未找到背景图 file input
: 背景图已注入:
: 点击头像区域...
[aria-label='头像操作']
: 未找到【头像操作】按钮
: 等待【选择头像】菜单出现...
div[role='menuitem']:has-text('选择头像')
: 未找到【选择头像】菜单项
: 通过【上传照片】定位 file input...
div:has(> div[aria-label='上传照片']) > input[type='file']
: 未找到头像 file input
: 头像已注入:
注入失败:
: 修改头像...
: 修改头像失败，第 N 次重试...
: 等待头像处理...
: 点击【保存】保存头像...
: 保存头像失败，第 N
: 等待头像保存...
: 修改背景图...
: 修改背景图失败，第 N
: 检测背景图尺寸提示...
: 背景图尺寸不符合要求（太大或太小），将尝试其他图片
: 点击【保存更改】保存背景图...
: 保存背景图失败，第 N
: 等待背景图保存...
: 已跳过【修改背景图】
: 已跳过【修改头像】
: 修改公共主页发帖设置...
```

从串的顺序能读出的三件事：

1. **执行顺序是 头像 → 背景图 → 发帖设置**（「修改头像...」在「修改背景图...」之前，
   「修改公共主页发帖设置...」在最后）。S8 时是 签名→类别→发帖设置，这一步按证据重排。
2. **头像的 file input 只在对话框里**：`div:has(> div[aria-label='上传照片']) > input[type='file']`
   外面套着「上传照片」对话框，所以必须先「点头像区域 → 点选择头像」把它渲染出来。
   这直接决定了重试方式 —— 见第三节。
3. **背景图有成对的保存文案与尺寸提示**，头像没有尺寸提示（头像被裁成方形，尺寸阈值对它没意义）。

**没有原版证据的地方**（已尽量少猜，但必须写明）：

- **头像被拒时页面弹什么**：串里只有封面的「请选择其他封面照片」对话框。头像目前**复用**这条
  判定信号。若真机上头像被拒弹的是别的文案，这条流程会把它当「通过」。真机验收必须确认。
- **`[aria-label='头像操作']` 与 `div[role='menuitem']:has-text('选择头像')` 的现行有效性**：
  串是 2024 年版 DLL 里的，FB 改版就会失效。仿真页只能证明「选择器能命中我自己搭的 DOM」。

## 三、设计

### 3.1 头像为什么需要一个「每轮准备」钩子

`CoverAttemptFlow`（S5 写的）原本是「打开一次 input → 循环注入候选图」。背景图适用，
**头像不适用**：被拒一次，那个「上传照片」对话框就关了，不重开菜单就注不进第二张图，
`SetInputFiles` 会以「找不到元素」超时收场，白白烧掉一个动作超时。

所以给它加了一个可选参数：

```csharp
public static Task<CoverAttemptResult> RunForAvatarAsync(
    ..., Func<CancellationToken, Task>? prepareAsync = null)
```

循环里的位置很关键，注释也写进代码了：

```csharp
// 上一张的提示框还没关干净就注新图，会立刻被判成「刚上传就被拒」，白白消耗一次尝试。
if (await page.IsCoverRejectedAsync(ct)) { ... break; }

// 顺序不能反：先确认提示已关，再重开入口。
if (prepareAsync is not null) { await prepareAsync(ct); }

await page.SetFileInputAsync(fileInputSelector, path, ct);
```

`prepareAsync` 给 null 时行为与原来**完全一致** —— 背景图那条路径不传，就不会多点任何东西。

### 3.2 两项都不做「本地尺寸预检」

背景图传 `CoverSizeRule.FacebookPageCover`（820×312，最小 400×150），头像传 `rule: null`。
头像不预检的理由写在 `RunForAvatarAsync` 上：头像被裁成方形，封面阈值套上去会把正常头像全判为不合格。

### 3.3 保存的收口用「按钮消失」而不是「等固定时长」

两项都是：等保存按钮可用 → 点 → 等它消失（`WaitHiddenAsync`，受 `SaveSettleTimeout` 约束）。
按钮一直在（超时）时**不判失败**，日志写「保存按钮在超时内仍未消失，按已提交处理」。
理由：FB 的保存是异步提交，按钮消失只是「面板关了」，不消失也不等于没保存成功；
在这里判失败会造成「明明改好了却报失败」，比漏报更烦人。**但按钮不可用（Disabled）判失败** ——
那说明改动根本没提交上去。

### 3.4 账号与图片的分配

一个图片目录、多个账号，谁用哪张？执行器是**逐任务**跑的，每次只知道一个别名。
`AccountImagePlan`（S5 写的）的设计前提是「一次拿到全部账号」，它按**别名在列表里的下标**取图，
逐任务调用时下标永远是 0 —— 所有账号会拿到同一张。**这正是本轮被用例抓出来的 bug**
（`不同账号拿到不同的第一张` 首版实现为红）。

修法：洗牌种子由别名的稳定哈希（FNV-1a）算出来，同别名恒定同一张，不同别名洗牌顺序不同。
**代价写在代码注释里**：这是「大概率不同」而不是「保证不同」，哈希会撞。
严格不重复需要一次拿到全部别名，批量入口仍用 `AccountImagePlan.Create`。

不用 `string.GetHashCode()` 的理由也写在代码里：它每进程都不同，今天跑这张、明天跑那张，
断点续跑会莫名换头像。

## 四、顺带修掉的两个缺陷

### 4.1 `.gitignore` 把整个 `Profiles/` 源码目录吃掉了（S8 遗留，最严重）

`.gitignore` 第 22 行原为：

```
profiles/
```

本意是忽略运行时 cookie / 登录态目录。但 **Windows 上 `core.ignorecase=true`，这条规则不分大小写**，
于是把源码目录 `src/**/Profiles/` 一起忽略了。

后果：**S8 的资料修改源码从来没进过仓库**。`git ls-tree HEAD` 里 `Automation/Profiles/` 是 0 个文件，
而 `ProfileEditExecutor.cs`（已跟踪）第 2 行就是 `using FbAuto.Automation.Profiles;` ——
**仓库在 HEAD 上编译不过**，而 `git status` 上一切干净，看不出任何异常。

本轮收尾时 `git status` 只显示 3 个新文件才暴露出来（AvatarEditor / CoverPhotoEditor 应该也是 `??`）。

修法：把 `profiles/` 锚定成 `/profiles/`（只匹配仓库根目录），并把同组的宽匹配 `**/Profile*/`
换成精确的 `**/Profile [0-9]*/`、`**/Profile[0-9]*/`、`**/*-profile/`
（Chrome 的用户资料子目录叫 `Profile 1`，中间**有空格**）。

修完 `git status` 多出 11 个未跟踪源码文件，全部纳入本次提交：

```
AvatarEditor.cs  CategoryEditor.cs  CoverPhotoEditor.cs  PostingSettingsEditor.cs
ProfileEditFlow.cs  ProfileFieldResult.cs  ProfileIdExtractor.cs  ProfileIdResolver.cs
SignatureEditor.cs  SignatureSource.cs  SignatureText.cs
```

已复查：`src` / `tests` / `tools` 下**除 `obj/`、`bin/` 之外的 `.cs` 文件无一被忽略**。

### 4.2 通道测试里新加的图片目录断言无法被证伪

`ChannelProfileTests` 的两条「不会读到对方通道」，注入 `BitImageDirectory → ImageDirectory`
回落时**打不红** —— 因为用例里两边的图片目录都是空串，回落了也看不出来。

修法：给这两条用例加上「对方通道图片目录非空」的反例配置（`ImageDirectory = @"D:\g\images"`
/ `BitImageDirectory = @"D:\b\images"`）。注入后立刻变红（见第五节 F）。

**这条比它看起来重要**：一条永远绿的守卫用例，价值等于零，而且会让人误以为有保护。

## 五、反向注入（7 次，全部命中）

| # | 改坏了什么 | 变红的用例 | 条数 |
|---|---|---|---|
| A | `CoverAttemptFlow` 的 `prepareAsync` 只在第一轮调用 | 头像_被拒之后重开菜单再注第二张 | 1 |
| B | 头像「保存按钮不可用」不再判失败，继续当成功走 | 头像_保存按钮一直不可用时报失败而不是当成功 | 1 |
| C | 洗牌种子由别名哈希退化成常量 0 | 不同账号不应全部拿到同一张 | 1 |
| D | 别名缺失时不再用占位键，直接传空串 | 别名缺失时仍然分到一张图而不是空 | 1 |
| E | 编排里把背景图提到头像前面 | 编排_五项顺序与原版日志一致 | 1 |
| F | 比特版 `ImageDirectory` 回落谷歌版 | 比特通道不会读到谷歌版的路径、两套参数各自独立赋值 | 2 |
| G | 删掉「没有候选图就跳过」的提前返回 | 头像_没有候选图时跳过且不碰页面、编排_五项顺序与原版日志一致 | 2 |

还原后全量 579 全绿，`grep -rn "MUTATION" src tests` 无输出。
明细见 `artifacts/S11-tests.txt`。

## 六、未完成项

1. **主页名称没做** → S12。需要 frame 内操作原语（`page.Frames` 里找含 `cquick` 的 frame，
   在 frame 内找表单与输入框），现有 9 个原语都在主 frame 上，硬做会写成「在当前页面找名字输入框」，
   永远找不到，而且失败得莫名。
2. **比特版资料修改无真机验收**：比特浏览器未安装、`127.0.0.1:54345` 无服务。
   配置层（`ForChannel`）与执行器（按 `TaskKind` 取通道）有单测，但「比特版浏览器里打开的主页
   长什么样」完全没验证过。
3. **所有页面验收都是本地仿真页**：`facebook.com:443` 不通。仿真页能证明「选择器能命中、动作能落地」，
   **证明不了「FB 现在还是这个 DOM」**。三条新选择器（`[aria-label='头像操作']`、
   `div[role='menuitem']:has-text('选择头像')`、`div:has(> div[role='button']:has-text('封面照片')) > input[type='file']`）
   必须在真机上逐条重验。
4. **头像被拒的判定信号是借来的**：见第二节末。真机上若弹的不是「请选择其他封面照片」，
   头像会被当成功。
5. **本地尺寸预检是增强，不是原版行为**：原版纯靠页面对话框。好处是省一次注定失败的上传，
   代价是「本地判不合格但其实能传」的图会被跳过 —— 阈值取自 FB 公开的封面规格，真机若有变化要跟着改。
6. **账号图片分配是「大概率不同」不是「保证不同」**：哈希会撞。要严格不重复得改成批量入口。
7. **背景图与头像共用同一张图**：原版串里没看到「头像单独一个目录」，目前两项从同一个目录取图，
   且第一张就是本账号那张。若实际业务要求两项用不同图，需要加一个 `AvatarDirectory`。

## 七、验收对照

| 原版行为（串） | 本步实现 | 证据出处 |
|---|---|---|
| 点 `[aria-label='头像操作']` | `AvatarEditor.OpenUploadDialogAsync` 第一、二步 | u16 3437 |
| 等 `div[role='menuitem']:has-text('选择头像')` | 同上，`WaitVisibleAsync` | u16 3438 |
| `div:has(> div[aria-label='上传照片']) > input[type='file']` | 同上的 `WaitExistsAsync`（不能等可见，`display:none`） | u16 3440 |
| 「头像已注入:」「修改头像...」 | `已注入图片: {文件名}` + `修改头像...` | u16 3441 / 3444 |
| 「修改头像失败，第 N 次重试...」 | `CoverAttemptFlow` 的重试日志 | u16 3445 |
| 「等待头像处理...」→「点击【保存】保存头像...」→「等待头像保存...」 | 三行日志顺序一致 | u16 3446–3449 |
| 「未找到背景图 file input」 | 同一句文案 | u16 3435 |
| 「检测背景图尺寸提示...」「尺寸不符合要求（太大或太小）」 | `CoverSizeRule` + `CoverAttemptFlow`（S5） | u16 3453–3454 |
| 「点击【保存更改】保存背景图...」→「等待背景图保存...」 | 同一顺序 | u16 3455–3457 |
| 「已跳过【修改头像】」「已跳过【修改背景图】」 | `AvatarEditor.SkipLine` / `CoverPhotoEditor.SkipLine` | u16 3465–3466 |
| 顺序：头像 → 背景图 → 发帖设置 | `ProfileEditFlow` 五项顺序 | u16 3444 / 3450 / 3467 |

## 八、文件清单

**新增**

- `src/FbAuto.Automation/Profiles/AvatarEditor.cs` — 六步 + 每轮重开菜单
- `src/FbAuto.Automation/Profiles/CoverPhotoEditor.cs` — 注入（含换图重试）+ 保存更改
- `src/FbAuto.Automation/Media/AccountImageCandidates.cs` — 别名 → 候选图（首位是本账号那张）
- `tests/FbAuto.Tests/AvatarCoverEditorTests.cs` — 16 条
- `tests/FbAuto.Tests/AccountImageCandidatesTests.cs` — 7 条
- `docs/plans/artifacts/S11-tests.txt`

**修改**

- `src/FbAuto.Automation/Selectors/FacebookUrls.cs` — 加 `ProfileHome`
- `src/FbAuto.Automation/Profiles/ProfileFieldResult.cs` — `ProfileEditOptions` 加
  `ProfileHomeUrlTemplate` / `SaveSettleTimeout` / `ImageRejectionWindow`，
  加助手 `WaitExistsAsync` / `WaitHiddenAsync`
- `src/FbAuto.Automation/Profiles/ProfileEditFlow.cs` — `ProfileEditRequest.Images`；
  五项重排为 头像→背景图→发帖→签名→类别
- `src/FbAuto.Automation/Flows/CoverAttemptFlow.cs` — `prepareAsync` 每轮注入前调用
- `src/FbAuto.Core/Configuration/AppConfig.cs` — `ImageDirectory` / `BitImageDirectory`（成对）
- `src/FbAuto.Core/Configuration/ChannelProfile.cs` — 加 `ImageDirectory`
- `src/FbAuto.Application/Execution/ProfileEditExecutor.cs` — 扫图片目录、取本账号图排首位、
  把扫描告警写进日志
- `src/FbAuto.Presentation/ViewModels/MainViewModel.cs` — 两个图片目录属性（读、存两侧）
- `src/FbAuto.Presentation/Views/MainWindow.xaml` — 资料修改页两行占位换成「图片目录（谷歌版/比特版）」
- `tests/FbAuto.Tests/ProfileEditFlowIntegrationTests.cs` — +3 条真 Chrome 用例；
  两条旧用例的 `ChangedCount` 改为按五项算
- `tests/FbAuto.Tests/Support/FacebookMimicPage.cs` — 加 `ProfileHome` 仿真页（头像菜单 + 封面 input + 两处保存）
- `tests/FbAuto.Tests/Support/FakePageInterop.cs` — 加 `OnInject` 钩子（注入时可模拟「页面立刻弹拒绝提示」）；
  批量注入也记入 `FileInjections`
- `tests/FbAuto.Tests/ChannelProfileTests.cs` / `JsonConfigServiceTests.cs` / `ProfileEditTests.cs` — 补断言
- `.gitignore` — 修 4.1 那条把源码目录吃掉的规则

## 九、口径

- 「S11」按**实施顺序**编号。计划 PPT 里的 S11 是别的内容，对照时别以为漏做了什么。
- 这一步的验收标准是「两项各自能走到保存」，**不是**「改完页面上真的换好了」。
  后者需要登录态与真站点，列为第六节第 3 条。
