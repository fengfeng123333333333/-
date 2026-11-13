# S13 · 主页名字（iframe + 密码二次确认）

2026-11-13 · 提交见文末 · 611 用例全绿（S12 为 590，新增 21）

---

## 1. 这一步为什么单开

资料修改在原版里有七项，S12 结束时我们做了六项，差的这一项是**主页名字**。它不是「再写一个编辑器」那么简单，有两处结构性差异：

1. **表单在 iframe 里**。前面所有原语（可见/存在/点击/填文本…）都只作用于主 frame，对 iframe 里的元素一律返回「找不到」。
2. **需要主账号密码二次确认**。填完名字只是第一步，还要提交预览、输密码、点「申请更改」。

所以这一步分两块：先给页面层补一组 **frame 内操作原语**，再用它写编辑器。

---

## 2. 证据

全部取自 `_analysis/u16_all.txt`（原版 DLL 按 UTF-16LE 重扫），不是猜的。

### 2.1 操作串（3350–3372 行）

```
: 新名字：「
: 跳转至主页名字设置页...        https://www.facebook.com/settings?tab=profile&section=name&view
: 当前共有           个 frame:
:   frame[           ] url=
: 未找到包含 cquick 的 frame
: 使用 frame 查找表单...          form[action='/settings/name_change_preview/']
: 未找到表单
: 找到表单，在表单内查找名字输入框...   input[name='pseudonymous_name']
: 在表单内未找到 input[name='pseudonymous_name']
: 名字已填入
button[type='submit']
: 未找到【预览修改】按钮
: 已点击【预览修改】
#save_password
: 未找到密码输入框
: 输入密码...
: 未配置密码，跳过
button[value='1'].layerConfirm
: 未找到【申请更改】按钮
: 已点击【申请更改】
: 修改主页名字失败:
```

这段把执行顺序钉死了：**先填名字 → 点预览（表单提交）→ 密码框才出现 → 输密码 → 点申请更改**。密码在预览之后，不是之前。

### 2.2 配置键（2545 / 2579 / 2594 行）

`NamesFilePath`、`PageNamePassword`、`GoogleNamesFilePath`、`BitNamesFilePath`、`GooglePageNamePassword`、`BitPageNamePassword`——**又是成对的**（第 9、10 对），与 S10 定的「配置按通道成对」一致。

### 2.3 界面串（2670–2674 行）

`请选择主页名称文件` / `主页名称文件:` / `请先选择主页名称文件`——证明界面只有**一个名称文件选择框**，且它是加入任务前的必填校验项。

### 2.4 分发顺序里的位置（3486–3490 行）

`修改主页名字...` / `修改主页名字失败，第 N` / `修改主页名字失败(可能无权限)` / `已跳过【修改主页名字】`——排在类别之后，是七项的最后一项。

---

## 3. 做了什么

### 3.1 新增四个 frame 原语（`IPageInterop`）

| 原语 | 用途 |
| --- | --- |
| `GetFramesAsync` | 读 frame 清单（序号 + URL），原版日志就按这个格式打印 |
| `FrameExistsAsync` | 指定 frame 里元素是否存在（**不看可见性**，与主 frame 同口径） |
| `FrameFillAsync` | 指定 frame 里填文本 |
| `FrameClickAsync` | 指定 frame 里按可见序号点击 |

**定位方式统一用「frame 的 URL 含某个片段」**，不用序号或 name：序号随加载顺序变，name 在 FB 上基本为空，而 `cquick` 是原版自己用的口径。

`PlaywrightPageInterop` 的实现要点：

- `FindVisibleAsync` 重构为接收 **已解析的 `ILocator`**（原来收选择器字符串）。frame 内点击复用同一套「按可见序号定位」，否则主 frame 与 iframe 的序号语义迟早分叉。
- `WaitFrameElementAsync` 同时等两个条件：frame 建出来 + 元素进 DOM。FB 的设置页是先出壳再挂 iframe，分开等会让「frame 在、元素还没到」白等一个超时。
- frame 遍历时逐个 `try` 读 URL：导航途中 frame 被移除会抛，一个 frame 掉了不该让整份清单读不出来。

### 3.2 `PageNameEditor`

按证据的顺序实现，每个出口一句原版文案。几处刻意的取舍：

| 情形 | 处置 | 理由 |
| --- | --- | --- |
| 没配名字 | `Skipped` + `已跳过【修改主页名字】` | 与原版一致，不算失败 |
| 没有 cquick frame | `Failed` + 打印完整 frame 清单 | 只有一句「未找到」时分不清「iframe 没挂上」和「地址变了」 |
| 没有表单 / 没有名字框 | `Failed` | 做不下去 |
| 没有【预览修改】按钮 | `Failed`，**不再往下点确认** | 表单没提交就不可能有确认框；硬点只会得到「未找到【申请更改】按钮」，把真实原因藏起来 |
| 没有密码框 | 记一行，继续点确认 | 有两种可能（这页不要求密码 / 预览没真提交），页面区分不出来，判失败会把「不需要密码的账号」全判错 |
| 未配密码 | 记「未配置密码，跳过」，继续点确认，**结论带「需人工确认」** | 原版语义就是跳过；但没有密码 FB 侧多半不会真改，不能报成干净的成功 |
| 没有【申请更改】按钮 | `Failed` | 最后一步没做成 |

### 3.3 名字从哪来：`NameSource`

- 名称文件 **一行一个名字**（与签名「整段一份」不同），空行与 `#` 开头的行忽略。
- **挑哪一行由别名决定**（`StableHash`，FNV-1a 取低 31 位）。逐任务执行时只有当前账号一个别名，按行号取会永远是第一行——表现是「所有账号改成同一个名字」，单账号试跑完全看不出来。
- 去 BOM 用 `Replace("\uFEFF", "")` 而不是 `TrimStart`：BOM 可能写在空白之后，`TrimStart` 只吃紧贴行首的那个，残留的空格会被写进页面上的名字。

`StableHash` 从 `AccountImageCandidates` 里抽出来共用：头像和名字都按别名分配，两处各写一份 FNV-1a，迟早在某次修改后算出不同的值，表现为「同一账号的头像与名字对不上人」。

### 3.4 配置与界面

- `AppConfig`：`NamesFile` / `BitNamesFile` / `PageNamePassword` / `BitPageNamePassword`（原版键名 `Google/BitNamesFilePath`、`Google/BitPageNamePassword`）。
- `ChannelProfile`：加 `NamesFile`、`PageNamePassword` 两个**带默认值**的位置参数，老配置缺键落到空串正好是「没配就不改」。
- 执行器：挑名字 → 写日志 → 没配密码而配了名字时 `Warn` 一句 → 传进请求。
- 界面：资料修改页原本两个占位行（「主页名称」「密码」）换成四行真绑定（谷歌/比特各一对），顺带把放错位置的签名文件也补成两行（`SignatureFile` / `BitSignatureFile`）。

---

## 4. 顺带修掉的两个缺陷

### 4.1 守卫用例又一次不可证伪（第二次踩）

给 `ChannelProfileTests` 补了「比特版不读谷歌版名称文件」的断言，但**没给谷歌版填非空反例**——两边都是空串，注入「比特版回落谷歌版」时用例照样绿。

这是同一条规矩的第二次违反（S11 记过一次）。已补 `NamesFile = @"D:\g\names.txt"` / `PageNamePassword = "g-pw"` 等反例，重新注入后 2 条立刻变红。

**为什么总犯**：加断言是「写检查」，加反例是「让检查有机会失败」，后者在动手时没有存在感。**做法改成：写完守卫用例先跑注入了再说自己写完了。**

### 4.2 xunit 的 `DoesNotContain(string, string)` 会忽略 U+FEFF

`Assert.DoesNotContain("\uFEFF", name)` 对一个**完全干净**的字符串报「在位置 0 找到」。原因是该重载走**文化相关**比较，而 U+FEFF（零宽不换行空格）在文化比较里是零权重字符，会被忽略——于是它对任何字符串都成立。

改成 `Assert.False(name.Contains('\uFEFF'))`（char 重载，序数比较）后正常。

**这个坑很值钱**：它的表现是「断言红了但数据没问题」，第一反应会去查产品代码，实际是断言选错了比较方式。以后凡是断言「不包含零宽字符」，一律用 char 重载。

---

## 5. 反向注入（证伪）

逐项改坏产品代码，确认目标用例真的变红，再还原。**7 项，第一次有 1 项没变红（见 4.1），补完反例后重注入才命中。**

| # | 注入内容 | 变红用例数 | 命中的是 |
| --- | --- | --- | --- |
| A | 不填名字，只打「名字已填入」 | 2 | 纯逻辑成功路径 + 真 Chrome 全链路 |
| B | 密码框改用 `FrameFillAsync`（错 frame） | 2 | 同上 |
| C | 只读一次 frame 清单，不等 iframe 挂上来 | 1 | `frame后挂上来时等一下仍能成功` |
| D | 没有预览按钮也继续往下点确认 | 1 | `没有预览按钮时失败且不再点确认` |
| E | 名字永远取文件第一行 | 1 | `不同账号不会全挑到第一行` |
| F | 比特版回落谷歌版名称文件 | 0 → **2** | 补反例后命中两条成对断言 |
| G | 不按 URL 片段挑 frame | 2 | 纯逻辑「没有 cquick」+ 真 Chrome「没有 cquick」 |

C 项只有 1 条命中，是因为「等 frame」这段在真 Chrome 里通常第一个采样就能命中（iframe 挂得很快），纯逻辑用例才是它的守卫。这一点写在用例注释里。

还原后 `grep MUTATION` 无残留。

---

## 6. 未完成项（如实记录）

1. **真机验收没做**：`facebook.com:443` 不通。七条选择器（cquick / 表单 / 名字框 / 预览 / 密码 / 确认）全部来自原版串，但**原版串里只有选择器片段，没有「它现在仍然有效」的证据**。
   真机第一件事：确认 `settings?tab=profile&section=name&view` 打开后 iframe 的 URL 仍含 `cquick`；不再含的话改 `FacebookSelectors.NameFrameUrlFragment` 一个常量即可。
2. **`NamePreviewButton` 只有 `button[type='submit']`**：没有 aria-label 可依，这一页若有多个 submit，会按可见序号点第一个。
3. **URL 末尾的 `&view` 是残缺片段**：原版串在那里被切断，没找到后半截，先按原样用。
4. **密码是明文存 config.json 的**（原版也这样）。要改成 `cred://` 引用得先有凭据库，不在这一步。
5. **七对 `EditXxx` 开关仍未做**：现在还是「有值就改」。这是资料修改最后一块功能缺口。
6. **`ProfileEditResultExportDir`（结果导出目录）未做**。
7. **界面上的「选择...」按钮都没有命令绑定**，只能手工填路径。这是全项目统一的缺口，不是这一项特有。
8. **主页名字提交后 FB 侧可能进入审核**，程序无法知道结果；结论里只写「已提交申请」。

---

## 7. 验收对照

| 原版行为 | 本实现 | 证明方式 |
| --- | --- | --- |
| 打印 frame 清单 | `当前共有 N 个 frame:` + 每条 url | 纯逻辑 + 真 Chrome 断言日志 |
| 按 cquick 挑 frame | `FrameRef.Matches` + `FindFrame` | 注入 G |
| frame 内填名字 | `FrameFillAsync` | 真 Chrome 断言预览页读到的名字 |
| 预览 → 密码 → 申请更改 | 三步顺序固定 | 真 Chrome 断言密码值 + 已确认标记 |
| 无权限时失败 | 汇总里记一项失败 | 纯逻辑（各出口） |
| 没配名字就跳过 | `已跳过【修改主页名字】` | 纯逻辑 |
| 名称文件按账号分配 | `NameSource` + StableHash | 注入 E |

---

## 8. 测试

- 新增 21 条：`PageNameEditorTests` 12、`NameSourceTests` 7、真 Chrome 集成 2。
- 更新 2 条既有断言：七项顺序（`AvatarCoverEditorTests`）、七项全跳过的计数（`ProfileEditTests`）。
- 全量 **611 条全绿**。

**跑挂起的排查记录（值得记一笔）**：本轮有两次全量跑卡住（8 分钟无输出）。查进程发现测试 Chrome 还活着、页面停在 `upload-schedule.html`、testhost 空转；但那条用例单独跑 2 秒通过，视频上传类单独跑 9 条全绿。

原因是我在前一轮把 testhost 强杀了（命令超时被工具 kill），留下的测试 Chrome 拖住了下一轮——每轮启动新 Chrome 时都要和残留实例抢资源。按 `user-data-dir` 匹配清掉测试 Chrome 后，一次性跑 **611 条 2m58s 全绿**。

教训：**跑挂起的第一嫌疑是上一轮的残留，而不是用例本身**；排查顺序是「先清测试 Chrome → 重跑」，不要边跑边杀 testhost。

---

## 9. 文件清单

**产品（新增）**
- `src/FbAuto.Automation/Profiles/PageNameEditor.cs`
- `src/FbAuto.Automation/Profiles/NameSource.cs`
- `src/FbAuto.Automation/Support/StableHash.cs`

**产品（改动）**
- `src/FbAuto.Automation/Abstractions/IPageInterop.cs` — 四个 frame 原语 + `FrameRef` + 占位实现
- `src/FbAuto.Automation/Pages/PlaywrightPageInterop.cs` — frame 实现；`FindVisibleAsync` 改为收 locator
- `src/FbAuto.Automation/Selectors/FacebookSelectors.cs` — 主页名字一组（六个常量）
- `src/FbAuto.Automation/Profiles/ProfileFieldResult.cs` — `NameSettingsUrl` 选项
- `src/FbAuto.Automation/Profiles/ProfileEditFlow.cs` — 第七项 + 请求加 `PageName` / `PageNamePassword`
- `src/FbAuto.Automation/Media/AccountImageCandidates.cs` — 改用 `StableHash`
- `src/FbAuto.Core/Configuration/AppConfig.cs` — 两对配置 + `ForChannel` 映射
- `src/FbAuto.Core/Configuration/ChannelProfile.cs` — 两个新字段
- `src/FbAuto.Application/Execution/ProfileEditExecutor.cs` — 挑名字、缺密码告警、传参
- `src/FbAuto.Presentation/ViewModels/MainViewModel.cs` — 六个属性（读 + 存）
- `src/FbAuto.Presentation/Views/MainWindow.xaml` — 资料修改页四行真绑定 + 签名两行

**测试**
- `tests/FbAuto.Tests/PageNameEditorTests.cs`（新）
- `tests/FbAuto.Tests/NameSourceTests.cs`（新）
- `tests/FbAuto.Tests/ProfileEditFlowIntegrationTests.cs` — 两条真 Chrome
- `tests/FbAuto.Tests/Support/FakePageInterop.cs` — frame 记录 + `FramesProvider`
- `tests/FbAuto.Tests/Support/FacebookMimicPage.cs` — 主页 / iframe / 预览三份仿真页
- `tests/FbAuto.Tests/AvatarCoverEditorTests.cs`、`ProfileEditTests.cs` — 七项顺序与计数
- `tests/FbAuto.Tests/ChannelProfileTests.cs`、`JsonConfigServiceTests.cs` — 成对断言 + 反例
- `tests/FbAuto.Tests/Support/FakePageInterop.cs` — 取消令牌一致的 frame 实现

**文档**
- `docs/plans/artifacts/S13-tests.txt`
