# S14 · 七项开关与资料修改结果导出

2026-11-16 · 提交见文末 · 不依赖 Chrome 的 **579 条全绿**（Chrome 集测 63 条暂缓，见 6.9）

---

## 1. 这一步做什么

S13 结束时资料修改七项都能跑了，但**每跑一次就把七项全改一遍** —— 没有「这次只改其中几项」的入口。
原版界面上有七个勾选框，还有一处「资料修改结果保存目录」。这一步把这两样补上：

1. 七对开关（谷歌版 / 比特版各七个），没勾的项不打开页面、不读对应文件。
2. 资料修改结果保存目录（谷歌版 / 比特版各一个），配上就把失败账号写成清单。

---

## 2. 证据

### 2.1 七个开关键名（`_analysis/u16_all.txt` 配置字段块）

```
EditCoverImage / EditAvatar / EditPagePostSettings / EditFollowersContent
EditSignature / EditCategory / EditPageName
Google* 前缀一份、Bit* 前缀一份
```

### 2.2 原版**不持久化**这七个开关

这一条是两步验证出来的，不是推断：

- `_orig/FaceBook自动化V1.1.1_1/settings.json`（发行包自带）
- `_runtest/settings.json`（实测跑出来的）

两份都有 `GoogleProfileEditResultExportDir` 且值为空串，但**都没有 `EditXxx` 这七个键**。
所以原版把它们当界面上的当次勾选，不写盘。

### 2.3 结果导出的文件名与行格式（2771–2795 行）

```
settings.json / Logs / FaceBookAuto_ / yyyyMMdd / .log
AccountErrors_ / ] [账号失败] 浏览器: / | assetId: / | 原因:
yyyy-MM-dd HH:mm:ss.fff
```

拼起来就是：

- 文件 `<目录>/AccountErrors_<yyyyMMdd>.txt`
- 行 `[<时间>] [账号失败] 浏览器:<X> | assetId:<Y> | 原因:<Z>`

标识符侧也能对上：`WriteAccountError` 与 `WriteAllText` 在 ascii 转储里紧挨着。

### 2.4 每项的跳过文案（3455–3495 行）

七项各有一句 `已跳过【修改X】`。这一步把这七句做成常量并真正打出来 ——
在此之前只有「没数据」才会跳过，没有「这次不打算改」这个原因。

---

## 3. 做了什么

### 3.1 配置层

`ChannelProfile` 加六个布尔 + 一个导出目录：

```csharp
bool EditCoverImage = true,   bool EditAvatar = true,   bool EditFollowersContent = true,
bool EditSignature = true,    bool EditCategory = true, bool EditPageName = true,
string ProfileEditResultExportDir = ""
```

`AppConfig` 加 `EditCoverImage` … `BitEditPageName` 与 `ProfileEditResultExportDir` / `BitProfileEditResultExportDir`，
`ForChannel` 两侧逐项映射。

**默认值一律 `true`。** 理由见 4.1。

### 3.2 编排层

`ProfileEditRequest` 加六个开关（`UpdatePostingSettings` 本来就有）。
`ProfileEditFlow` 里第七项的调用全部走同一个新方法：

```csharp
private static Task<ProfileFieldResult> RunOrSkipAsync(
    string field, bool enabled, string skipLine, Func<Task<ProfileFieldResult>> action, ...)
```

`enabled == false` 时直接返回 `Skipped(field, $"{skipLine}（未勾选）")`，**不碰页面、不读文件**。

七项全关时额外打一句 `七项都未勾选：本次不会修改任何内容` —— 这个状态下任务会「成功结束」，
没有这句就分不清是「改完了」还是「什么都没做」。

### 3.3 执行层

`ProfileEditExecutor` 开工先打印本次要改哪几项（`ChannelProfile.EnabledEditItems`），
请求改用**具名实参**传 14 个参数 —— 这里连着 6 个 bool，位置传参错位在结构上无法被发现，
具名之后错位不可能发生。

`TaskRunOptions` 加按通道查表的导出目录：

```csharp
IReadOnlyDictionary<BrowserChannelKind, string>? ExportDirectories
string? ExportDirectoryFor(BrowserChannelKind channel)
```

`TaskQueueRunner` 每轮建一个 `AccountErrorLog`，凡是判定为失败的地方（执行器返回失败 / 超时 /
浏览器启动失败 / 执行异常 / 账号找不到 / 账号停用 / 没有执行器）都记一条，轮末统一写盘。

### 3.4 界面

资料修改页顶部加两块 `UniformGrid`（每块七项，谷歌版一块、比特版一块），
下面加「资料修改结果保存目录」两行。新增 `EditCheck` 样式（`Styles/Theme.xaml`）。

---

## 4. 两个容易错的点

### 4.1 开关默认值必须是 `true`

原版不持久化这七个键。我们持久化（界面是配置页，勾了下次还在才符合预期），于是老配置
`config.json` 里没有这七个键。`JsonSerializer.Deserialize<AppConfig>` 会先执行无参构造器的初值、
再只覆盖 JSON 里出现的键 —— 缺键 = 保留初值。

如果初值写成 `false`：升级后的使用者打开程序，七个勾选框全是空的，任务跑完**报成功、一项没改**，
而且没有任何失败记录可查。这是最坏的一种失败：看起来一切正常。

仓里已有用例 `老配置缺开关键时读出七项都改` 钉住（把初值改成 `false` 会立刻变红）。

### 4.2 「未勾选」与「没有数据」是两回事

两者都跳过，但原因完全不同：

| 情况 | 日志 | 谁的问题 |
| --- | --- | --- |
| 这次不打算改 | `已跳过【修改个性签名】(未勾选)` | 使用者配的 |
| 想改但签名文件为空 | `签名文件为空…` | 使用者漏配了料 |

合成一句的话，事后看日志分不清「该改的没改」和「本来就没打算改」。三个编辑器原先缺 `SkipLine`
常量（`PostingSettingsEditor` / `SignatureEditor` / `CategoryEditor`），这一步补齐了。

---

## 5. 反向注入（证伪）

七项，逐项改坏产品代码，确认对应用例变红后还原。

| 注入 | 改法 | 命中 |
| --- | --- | --- |
| A | `RunOrSkipAsync` 里 `if (enabled)` → `if (enabled \|\| true)`，闸门整体失效 | 4 条 |
| B | `EditCoverImage` 初值改 `false`（新装默认什么都不改） | 2 条（配置层 + 反序列化层） |
| C | `EnabledEditItems` 顺序倒过来 | 1 条（精确命中顺序用例） |
| D | 比特版头像开关读成 `BitEditCoverImage`（典型回落错） | 1 条 |
| E | `ProfileEditRequest.EditSignature` 默认改 `false` | 1 条 |
| F | 删掉「七项都未勾选」那句提示 | 1 条 |
| G | 跳过原因里去掉「（未勾选）」 | 3 条 |
| H | XAML 里 `EditCategory` 写成 `EditCategoryX` | 1 条 |
| I1 | 行格式去掉毫秒、去掉空格分隔 | 2 条 |
| I2 | 写盘改成整体覆盖（不读已有内容） | 2 条 |
| I3 | `ExportDirectoryFor` 未配时返回 `""` 而不是 `null` | 4 条 |
| I4 | 执行器返回失败时不再记清单 | 2 条 |
| I5 | 比特版导出目录回落读谷歌版 | 2 条 |
| I6 | XAML 里 `ProfileEditResultExportDir` 写错一个字母 | 1 条 |
| I7 | `Record` 只判 `null` 不判空白串 | 2 条 |

全部命中，`grep -rn MUTATION src/ tests/` 无残留。

---

## 6. 未完成项（如实记录）

1. **真机验收没做**。`facebook.com:443` 在本机不通，开关只影响「开不开页面」，不影响选择器，
   但「勾掉某项后原版行为是否完全一致」没有真机证据。
2. **导出文件扩展名 `.txt` 是推断的**。原版 DLL 里没有独立的 `.txt` 字面量（只有文件对话框的
   `文本文件 (*.txt)|*.txt|...`）。按「纯文本清单」推断成 `.txt`，需真机核对。
3. **编码未验证**。写的是 UTF-8 无 BOM。原版用什么编码从字符串证据里看不出来。
4. **写盘策略与原版可能不同**。原版只暴露 `WriteAllText`（整体覆盖），没有 `AppendAllText`。
   并发下逐个「读-改-写」会互相覆盖，所以本实现先攒后写、且把当天已有内容接在前面。
   如果原版实际是「每轮覆盖」，我们的行为是「同一天累加」——**比原版多留了记录，不会丢**，
   但跟原版产出对比时行数会不一样。
5. **导出范围比键名宽**。键名叫「资料修改结果保存目录」，本实现把**所有任务类型**的失败
   都记进同一份清单（`[账号失败]` 这个标记本身不限定任务类型）。若确认只该记资料修改，
   需要在 `RecordError` 里加一层 `task.Kind` 过滤。
6. **七个 `EditXxx` 开关键名与原版不完全一致**：发帖设置那一项沿用本实现 S5 就有的
   `UpdatePostingSettings`（原版叫 `EditPagePostSettings`），改名会让已有 `config.json` 的值静默丢失。
7. **`Google` 前缀**：原版是 `GoogleProfileEditResultExportDir`，本实现按类内既有约定写成
   `ProfileEditResultExportDir`（与 `NamesFile` 对 `GoogleNamesFilePath` 的处理一致）。
8. **界面「选择...」按钮仍然全部没有命令绑定**（全项目范围内，不是这一步引入的）。
   导出目录这两行的按钮同样是空的，路径要手填。
9. **需要真 Chrome 的 7 个集成测试类暂缓**（需求方 2026-11-16 明确指示「暂时先不用测试」）。
   共 63 条，涉及 `ChromeChannelIntegrationTests` / `PageFlowIntegrationTests` /
   `PlaywrightPageInteropIntegrationTests` / `ProfileEditFlowIntegrationTests` /
   `TaskQueueRunnerIntegrationTests` / `MessageDeleteFlowIntegrationTests` / `VideoUploadFlowIntegrationTests`。
   日常跑 `build\test-nochrome.cmd`（579 条，4~6 秒），Chrome 集测恢复时仍用 `build\test.cmd`。

   暂缓的直接原因是**全量跑会偶发卡死**，已查到的实据：
   - `--blame --blame-hang-timeout 90s` 一次运行把卡住的用例点名到
     `VideoUploadFlowIntegrationTests.描述为空时不填但流程照走`，顺序文件里它是唯一
     `Completed` 不为 True 的一条，此时已跑完 549 条。
   - 另外几次卡住的页面分别停在 `upload.html`、`upload-nodesc.html`、`upload-schedule.html`，
     **都在同一个类里，但不固定是哪一条** —— 说明是这个类共用的那个浏览器实例被拖住，
     不是某一条用例的固定缺陷。
   - 单独跑该类：5 次里 4 次 9 条全绿（23~32 秒），1 次以
     `Assert.True(result.Published)` 失败（39 秒）而**不是**卡死。即同一份代码有两种表现。
   - 已排除的因素：残留测试 Chrome（跑前按进程清过）、残留临时目录（清过 54 个约 670MB）、
     上一轮 testhost（没有残留）。

   结论：这是集成测试夹具层面的不稳定，**与本步改动无关**（S14 没有碰上传流程与页面层），
   但还没定位到根因。恢复 Chrome 集测前需要先解决它，否则全量结果不可信。

---

## 7. 验收对照

| 要求 | 结果 |
| --- | --- |
| 七项各自可控（谷歌/比特分开） | ✅ 14 个绑定，`ChannelProfileTests` 逐项断言 + 非空反例 |
| 没勾的项不打开页面 | ✅ `ProfileEditSwitchTests` 断言 `NavigatedUrls` 不含该项地址 |
| 没勾的项打出原版跳过串 | ✅ 七句常量，`已跳过【修改X】(未勾选)` |
| 七项全关有明确提示 | ✅ `七项都未勾选：本次不会修改任何内容` |
| 老配置升级后行为不变 | ✅ 初值 `true` + 反序列化用例 |
| 结果目录可配、可留空 | ✅ 两行文本框 + `TaskRunOptionsTests` 三个出口 |
| 失败落盘且格式照原版 | ✅ `AccountErrorLogTests` 逐字段断言 |
| 同一天多次执行不丢记录 | ✅ 续写用例 |
| 写盘失败不影响任务结论 | ✅ 回调报错、不抛 |

---

## 8. 测试

### 8.1 结果

| 范围 | 条数 | 结果 | 耗时 |
| --- | --- | --- | --- |
| 不依赖 Chrome（`build\test-nochrome.cmd`） | 579 | 全绿，连跑两次一致 | 4~6 秒 |
| 需真 Chrome 的 7 个集成类 | 63 | 暂缓（见 6.9） | — |
| 合计 | 642 | — | — |

Chrome 集测暂缓前，S13 的全量口径是 611 条。本步新增 4 个测试类共 22 条；
另有若干条对既有用例补了断言（不增条数）。

### 8.2 本步新增用例的落点

- `ProfileEditSwitchTests`（6 条）：每条都配「开着的对照组」，否则「没打开页面」可能只是流程没跑。
- `AccountErrorLogTests`（11 条）：行格式逐字段、文件名、按目录分文件、续写、末尾无换行、
  写盘失败不抛、目录自动创建、空白目录当没配。
- `TaskRunOptionsTests`（4 条）：三个「取不到目录」的出口。
- `ProfileEditBindingTests`（S14 新增）：14 个勾选 + 2 个目录的绑定是否都能落到 ViewModel 属性。
- `ChannelProfileTests` / `JsonConfigServiceTests`：成对映射 + 非空反例 + 缺键默认值。

完整输出归档在 `docs/plans/artifacts/S14-tests.txt`。

---

## 9. 文件清单

新增：

- `src/FbAuto.Application/Execution/AccountErrorLog.cs`
- `tests/FbAuto.Tests/AccountErrorLogTests.cs`
- `tests/FbAuto.Tests/TaskRunOptionsTests.cs`
- `tests/FbAuto.Tests/ProfileEditSwitchTests.cs`（开关行为）
- `docs/plans/S14-EDIT-SWITCHES-EXPORT.md`（本文）

改动：

- `src/FbAuto.Core/Configuration/ChannelProfile.cs`（六个开关 + 导出目录 + `EnabledEditItems`）
- `src/FbAuto.Core/Configuration/AppConfig.cs`（成对映射）
- `src/FbAuto.Automation/Profiles/ProfileEditFlow.cs`（`RunOrSkipAsync` 闸门 + 全关提示）
- `src/FbAuto.Automation/Profiles/{PostingSettings,Signature,Category}Editor.cs`（补 `SkipLine`）
- `src/FbAuto.Application/Execution/TaskRunOptions.cs`（按通道查导出目录）
- `src/FbAuto.Application/Execution/TaskQueueRunner.cs`（记清单 + 轮末写盘）
- `src/FbAuto.Application/Execution/ProfileEditExecutor.cs`（打印本次改哪几项、具名实参）
- `src/FbAuto.Presentation/ViewModels/MainViewModel.cs`（14 个开关 + 2 个目录的读写）
- `src/FbAuto.Presentation/Views/MainWindow.xaml`（勾选区 + 导出目录两行）
- `src/FbAuto.Presentation/Styles/Theme.xaml`（`EditCheck` 样式）
- `tests/FbAuto.Tests/{ChannelProfile,JsonConfigService,ProfileEditBinding,ProfileEdit}Tests.cs`
