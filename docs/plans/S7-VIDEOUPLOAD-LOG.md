# S7 · 视频上传（谷歌版）· 执行记录

2026-11-12 · 提交见文末 · 431 用例全绿

## 一、这一步补的是什么

到 S6 为止，五类任务全部挂在 `NotWiredTaskExecutor` 上，页面上一个动作都没做过。这一步把
**视频上传（谷歌版）**接到真流程：读视频目录 → 取 asset_id → 打开批量上传页 → 整批注入 →
填描述 → 排期 → 盯进度 → 清理失败项 → 发布。

接完之后的边界：**只有 `VideoUploadGoogle` 是真的**，另外四类仍然是「未接入」占位（明确记失败，
不伪装成功）。`ExecutorCatalogTests` 把这条边界钉住了。

## 二、证据：原版到底怎么传的

全部从 `_analysis/utf16_strings.txt`（原始 DLL 按 UTF-16LE 重扫）还原，不是推测。

| 环节 | 证据串 |
|---|---|
| 主页列表 | `https://www.facebook.com/pages/?category=your_pages` |
| asset_id | `asset_id 通过 fallback 方式提取成功`、`LEGACY_`、`_boosted_post_`、`/latest/leads_center?asset_id=` |
| 上传页 | `//business.facebook.com/latest/bulk_upload_composer?asset_id=` |
| 添加入口 | `没有找到添加视频按钮，跳过该账号`、`已点击添加视频按钮` |
| 描述框 | `[aria-label*='在对话框中输入内容']` |
| 排期 | `div[role='button']:has-text('立即发布')`、`定时`、`[placeholder='年-月-日']`、`[aria-label='小时']`、格式 `yyyy-M-d` |
| 进度 | `div[role='progressbar'][aria-valuemax='100'][aria-valuemin='0']` |
| 卡死判定 | `进度为0已超过` `轮（90秒），判定为卡住失败`、`进度在` `% 已超过` `轮，判定为卡住失败` |
| 结束判定 | `进度条全部消失，上传流程结束`、`所有进度条均为版权审核，上传已完成`、`无视频正在上传，等待最后一轮间隔后跳出判断` |
| 批次判定 | `全部成功，直接发布`、`部分成功（` `个），删除` `个失败视频后发布`、`没有视频上传成功，账号疑似被封，跳过发布` |
| 发布 | `div[role='button']:has-text('发布')`、`div[role='button']:has-text('完成')` |

**一个必须先纠正的认知**：原版**不是一条任务传一个视频**，而是一个账号一次把视频目录里所有
`*.mp4` 批量注入。计划书里「10 条视频成功率 ≥8」说的是 10 条任务，每条任务内部是整批。
描述文件是一个 `.txt`，**整体作为一段描述**，不是一行一条 —— 依据是界面只有一个「视频描述」框，
且缺文件时的提示是「将以空描述上传」，不是「描述条数不足」。

## 三、三处有意偏离原版

1. **发布按钮改用 `:text-is('发布')`**。原版是 `:has-text('发布')`，而这个是子串匹配，
   会同时命中「立即发布」「定时发布」—— 点下去可能点到排期选项上，表现是「点了没反应」。
2. **上传页地址做成参数**（`ComposerUrlTemplate`）。不是为了灵活，是因为域名写死时
   这条流程**没法在本地验证**：集成测试只能去连真实站点，而没有出口的机器上必挂。
3. **失败条目数要采信「连续两次一致」**（见下一节），不是数一次就用。

## 四、修掉的三个缺陷

### 4.1 失败条目数早了 → 带着失败条目发布（最严重）

第一次全量跑时，「有失败条目时先移除再发布」偶发失败一次，单独跑又绿。
这种偶发不能放过，查下去是固定缺陷：

> 进度条消失和失败条目渲染完之间隔着几帧。在进度条消失那一刻立刻数失败数，会数到 **0**，
> 于是判成「全部成功」→ 跳过清理 → 把失败的视频一起发布出去。

修法不是加一个固定 sleep（那只是把概率调低，机器一忙还犯），而是**连续两次采样一致才采信**：
第一次数到 0，等 200ms 再数，若变成 1 就继续等，直到两次相同。全部成功时只多花 200ms。

对应用例：`失败条目晚于进度条出现_仍然要清理后再发布`（仿真页把失败条目延迟 400ms 渲染）。

### 4.2 轮询时读文本会等 30 秒

`ReadTextsAsync` / `ReadAttributesAsync` 先判可见再读值。进度条在「判可见」和「读值」之间
到 100 消失是常态，Playwright 的 `InnerTextAsync` 默认会等元素出现，于是一轮采样卡 30 秒，
任务被判超时失败。

修法：读操作传 `Timeout = 0`（不等待），并把 `TimeoutException` 当成「元素刚好消失」跳过。
这个缺陷是集成测试直接暴露的（`描述为空时不填但流程照走` 报 30 秒超时）。

### 4.3 新测试类漏了 `[Collection("chrome")]` → 全量跑整挂

现象：**单独跑每个集成测试类都全绿，全量跑必然没有任何输出地卡死**（不是失败，是挂住）。
原因是新加的 `VideoUploadFlowIntegrationTests` 没进 `chrome` 集合，于是它和其他浏览器测试类
并行各起一个 Chrome（每个十几进程），CDP 握手和页面加载互相拖死。

补了 `[Collection("chrome")]`，并把集合显式定义成 `Support/ChromeCollection.cs`，
注释里写清「新增浏览器测试类必须加这一行」。修复后连续两次全量跑均 431 全绿、耗时 73/72 秒。

## 五、反向注入（6 次，全部命中）

| # | 改坏什么 | 命中 | 表现 |
|---|---|---|---|
| 1 | 失败条目只数一次 | 1 | `Expected: 1, Actual: 0`（跳过清理直接发布） |
| 2 | 零成功仍往下走 | 1 | `Expected: Aborted, Actual: Published` |
| 3 | 没有进度条就判完成 | 2 | 「从没出现过进度条」「没有失败条目」两条红 |
| 4 | 不认版权审核 | 2 | 单元 + 集成各一条红 |
| 5 | 进度签名改成求和 | 1 | `Expected: Waiting, Actual: Stalled` |
| 6 | 批量注入退化成循环单文件 | 1 | `Expected: 2, Actual: 1`（只剩最后一个文件） |

第 5 次值得单说：签名用求和时，`50,50` 和 `100,0` 都是 100，
「两个都停住」会被当成「没变化」而累计到卡死 —— 实际上后者是一个传完一个还没开始，处置完全不同。

## 六、验收对照

| 判据 | 结果 |
|---|---|
| 整批注入（多文件一次注入） | 集成测试断言 `__injected == 2` |
| 隐藏 file input 可直接注入 | 全部用例通过 |
| 初始无 input 时先点「添加视频」 | `已点击添加视频按钮` 出现且注入成功 |
| 进度全程 0 判卡住 | 单元测试覆盖（90 秒阈值） |
| 进度停在某值判卡住 | 单元测试覆盖，日志带「停在 45%」 |
| 版权审核判完成 | 单元 + 集成 |
| 全部消失判完成 | 单元 + 集成 |
| 零成功不发布 | 集成断言 `__published == false` |
| 部分成功先清理再发布 | 集成断言 `__removed == 1` |
| 定时发布填日期与小时 | 断言 `2026-12-1` / `9`（原版格式 `yyyy-M-d`） |
| 描述为空不阻断 | 流程照走，日志写「描述为空」 |
| 执行器注册不再落 NotWired | `ExecutorCatalogTests` 四条 |

## 七、未完成项（不许写成已完成）

1. **真实 Facebook 页面一条都没验。** 所有结论来自本地仿真页 + 单元测试。
   仿真页证明「选择器和动作在当前 Chrome 里成立」，证明不了「FB 现在还是这个 DOM」。
2. **失败条目数用「可见的移除按钮」估算，这是推断**。串里只证明原版会「删除失败视频后发布」，
   没证明失败条目一定有「移除」按钮、成功条目一定没有。若真实页面给成功条目也配了「移除」，
   这里会把成功数算少、把成功条目删掉 —— 这是本步风险最高的一条，真机验收第一件事就是确认它。
3. **多主页只取第一个 asset_id**。原版串有「个子账号开始循环处理」，说明它会遍历全部子账号；
   这里只取页面里第一个命中，多主页账号会漏传。
4. **进度阈值是经验值**。0% 超时 90 秒来自原版，但「进度不变超时 120 秒」与「总超时 15 分钟」
   是按量级估的，真机要按视频大小和网络调。
5. **发布成功的判据是「完成」按钮出现**。若真实页面发布后直接跳转、不弹「完成」，
   会被误判为发布失败（任务判失败但帖子其实发出去了）。
6. **`SkipActivationInDevelopment` 源码默认仍是 true**，发布前必须改（S9 项）。

## 八、文件清单

新增（产品）：

- `src/FbAuto.Automation/Uploads/UploadProgressMonitor.cs` — 进度状态机
- `src/FbAuto.Automation/Uploads/VideoBatchPlanner.cs` — 批次清单
- `src/FbAuto.Automation/Uploads/AssetIdExtractor.cs` — 三级提取
- `src/FbAuto.Automation/Uploads/AssetIdResolver.cs` — 导航 + 读整页
- `src/FbAuto.Automation/Uploads/UploadBatchJudge.cs` — 批次判定
- `src/FbAuto.Automation/Flows/VideoUploadFlow.cs` — 主流程
- `src/FbAuto.Application/Execution/VideoUploadExecutor.cs` — 执行器
- `src/FbAuto.Application/Execution/ExecutorCatalog.cs` — 类型 → 执行器登记表

改动：

- `IPageInterop` 加 6 个原语：`Navigate` / `Exists` / `Fill` / `ReadAttributes` / `ReadTexts` / `ReadContent`，`SetFileInputs` 支持批量
- `PlaywrightPageInterop`、`NotConnectedPageInterop`、`FakePageInterop` 同步实现
- `FacebookSelectors` 补 10 条视频上传选择器
- `PageInteropOptions` 加 `NavigationTimeout`
- `App.xaml.cs` 改用 `ExecutorCatalog.CreateAll`

新增（测试）：

- `UploadProgressMonitorTests`（14）、`VideoBatchPlannerTests`（10）、`AssetIdExtractorTests`（9）、
  `UploadBatchJudgeTests`（5）、`VideoUploadFlowIntegrationTests`（9）、`ExecutorCatalogTests`（7）
- `FacebookMimicPage.UploadComposer`、`Support/ChromeCollection.cs`

测试规模：377 → **431**（+54），全绿约 73 秒。
