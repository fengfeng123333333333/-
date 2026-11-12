# S10 · 比特版差异覆写 · 执行记录

2026-11-12 · 提交 `0b0e928` · 553 用例全绿（S9 为 530，新增 23）

## 一、这一步补的是什么

把两个比特版任务（`[视频上传] - 比特版`、`[资料修改] - 比特版`）接上真流程。

接完之后 **五类任务全部有真执行器**，`NotWiredTaskExecutor` 仍在代码里（给将来新增类型用），
但登记表不再返回它 —— `ExecutorCatalogTests` 钉住这条。

## 二、证据：差异到底在哪

原版 `settings.json` 里，业务路径**全部是成对的**：

| 用途 | 谷歌版 | 比特版 |
|---|---|---|
| 视频目录 | `GoogleVideoPath` | `BitVideoPath` |
| 描述文件 | `GoogleDescriptionFilePath` | `BitDescriptionFilePath` |
| 名字文件 | `GoogleNamesFilePath` | `BitNamesFilePath` |
| 评论屏蔽词 | `GoogleCommentsBlockText` | `BitCommentsBlockText` |
| 签名文件 | `GoogleSignatureFilePath` | `BitSignatureFilePath` |
| 主页名称密码 | `GooglePageNamePassword` | `BitPageNamePassword` |
| 图片目录 | `GoogleImageDir` | `BitImageDir` |
| 结果导出目录 | `GoogleProfileEditResultExportDir` | `BitProfileEditResultExportDir` |

共享的只有四项：`CookieRootPath`、`DeleteMsgCookieRootPath`、`BrowserOpenInterval`、`ConcurrentTaskCount`。

**页面流程没有任何差异**：`_analysis/utf16_strings.txt` 里搜「比特」只命中服务层
（`比特服务不可用`、`未启动比特浏览器，请先启动浏览器再运行脚本`、`正在获取比特浏览器列表...`），
没有任何「比特版特有」的页面日志或选择器。

所以这一步的实质是：**流程复用 + 配置分通道 + 通道选择正确**，不是重写一遍流程。

## 三、设计

**配置层**：新增 `ChannelProfile`（一条通道的全部业务参数）与 `AppConfig.ForChannel(channel)`。
执行器只调 `ForChannel`，不直接读字段 —— 「比特版读成谷歌版」只可能错一次，且能被单测钉住。

**执行器层**：`VideoUploadExecutor` / `ProfileEditExecutor` 加 `TaskKind kind` 参数，
`Kind` 与 `Profile` 都暴露出来。比特版 = 同一个类 + 另一个 Kind。

为什么 `Profile` 要 public：光断言 `Kind` 正确证明不了「读到了对的那套配置」——
两边指向同一份配置时任务照样跑，但会拿错目录传错视频，日志里看不出异常。

**通道选择**：`TaskTypeCatalog.ChannelFor(descriptor, global)`。

## 四、顺带修掉的两个缺陷

### 4.1 「删除消息」被写死成谷歌版（S9 的假设错误）

S9 把 `[删除消息]` 的通道定成 `Google`，理由是它没有「- 谷歌版 / - 比特版」后缀。
但证据里有 `正在获取比特浏览器列表（删除消息）...` —— **它确实会在比特浏览器上跑**，
说明它跟随界面顶部的「浏览器类型」下拉，而不是写死某一通道。

后果很具体：比特版账号永远删不了消息，而界面上会显示「账号清单里没有可用账号」，
看起来像账号清单填错了。

修法：`TaskTypeDescriptor` 加 `FollowsBrowserType`，只有删除消息为 true；
挑选账号的通道走 `ChannelFor`。抽成纯函数是为了能测 —— 视图模型 `AddTask` 本身没有测试覆盖。

### 4.2 「上传进度从未出现」会无限等下去（S7 遗留，本步被测试挂死逼出来）

全量跑两次都**挂死**（零输出、不是失败），blame 指到 `VideoUploadFlowIntegrationTests`。
查下去是真缺陷，与比特版无关，但既然撞上了就这一步修掉：

`UploadProgressMonitor` 里「从没出现过进度条」那条分支直接 `return Waiting`，
而总超时的检查写在它**后面** —— 永远执行不到。于是注入没生效时监控会一直等。

真机上的表现：任务卡十几分钟，日志停在「尚未出现进度条」不再更新。
测试上的表现更糟：**进程挂死、没有任何输出**，很容易被当成环境问题（我第一次就是这么误判的，
先去查了孤儿 Chrome 进程）。

修法两条：
1. 该分支自带 `NoBarTimeout`（默认 90 秒），超时判失败，日志写明「注入没有生效（文件可能没被选中、被页面拒绝，或上传区选择器已改版）」。
2. 该分支同时受总超时约束，哪个先到按哪个判。

**「完成」判定仍然优先于任何超时**：传完了却因为超时报失败，会让人去查一个不存在的问题。
第一版修法把总超时提到了所有分支之前，被已有的用例 `进度条消失优先于总超时` 直接打红 —— 那条用例是对的。

## 五、反向注入（4 次，全部命中）

| # | 注入 | 变红用例数 |
|---|---|---|
| A | `ForChannel` 比特分支读谷歌字段 | 4 |
| B | 登记表把比特版传成谷歌版 Kind | 4 |
| C | `ChannelFor` 忽略「跟随全局」标记 | 1 |
| D | 取消「无进度条」超时 | 1 |

D 是 4.2 那个缺陷的守卫：去掉超时后 `从没出现过进度条超过容忍时长判失败` 立刻红。

## 六、未完成项（不许写成已完成）

1. **比特浏览器没装，整条链路没真机验过**。`127.0.0.1:54345` 无服务，比特通道目前只有
   S2 用裸 `TcpListener` 桩验证过。本步能证明的是「配置分对了、执行器接对了」，
   证明不了「比特浏览器能起、能拿到调试端口、能在上面跑完一次上传」。
   补上的条件：装比特浏览器 + 建一个窗口 + 拿到 id。
2. **资料修改页的输入项仍是占位**（`S5 阶段接入`），比特版那四个字段（签名文件、类别、
   发帖设置、评论屏蔽词）目前只能手改 `config.json`。界面统一重做放在交付阶段，
   不在这一步开半成品。
3. **界面新增的两个绑定没有测试**。`BitVideoDirectory` / `BitDescriptionFile` 加在了
   视频上传页，编译通过、绑定名与 VM 属性一致，但没起界面看，也没有自动化校验
   （测试项目没引用 Presentation，引进来要带 WPF）。
4. **`Bit*` 其余字段未接**：`BitImageDir`、`BitPageNamePassword`、`BitProfileEditResultExportDir`
   对应的功能（头像/背景图、主页名称、结果导出）本身还没做，字段没加。

## 七、验收对照

| 验收项 | 结果 | 依据 |
|---|---|---|
| 比特版任务有真执行器 | 通过 | 登记表 5 条 Theory 全过 |
| 比特版读比特版配置 | 通过 | `比特版执行器读比特版配置` + 注入 A/B |
| 谷歌版读谷歌版配置 | 通过 | `谷歌版执行器读谷歌版配置` |
| 两套配置互不覆盖 | 通过 | 配置往返用例 + 注入 A |
| 删除消息跟随全局浏览器类型 | 通过 | 2 条 Theory + 注入 C |
| 带版本后缀的任务不被全局带偏 | 通过 | 4 条 Theory |
| 上传进度不会无限等 | 通过 | 2 条新用例 + 注入 D；全量不再挂死 |
| 比特浏览器真机跑通 | **未验收** | 未安装，见第六节 |

## 八、文件清单

**产品**

- `src/FbAuto.Core/Configuration/ChannelProfile.cs`（新）—— 一条通道的全部业务参数
- `src/FbAuto.Core/Configuration/AppConfig.cs` —— 6 个 `Bit*` 字段 + `ForChannel`
- `src/FbAuto.Core/Models/TaskTypeCatalog.cs` —— `FollowsBrowserType` + `ChannelFor` + `ChannelOf`
- `src/FbAuto.Application/Execution/VideoUploadExecutor.cs` —— Kind 参数化 + `Profile`
- `src/FbAuto.Application/Execution/ProfileEditExecutor.cs` —— 同上，并接上 `options` 注入
- `src/FbAuto.Application/Execution/ExecutorCatalog.cs` —— 注册五类
- `src/FbAuto.Automation/Uploads/UploadProgressMonitor.cs` —— `NoBarTimeout` + 分支重排（4.2）
- `src/FbAuto.Presentation/ViewModels/MainViewModel.cs` —— `GlobalChannel`、比特版两字段、通道选择
- `src/FbAuto.Presentation/Views/MainWindow.xaml` —— 视频上传页加比特版两行

**测试**

- `tests/FbAuto.Tests/ChannelProfileTests.cs`（新，8 条）
- `tests/FbAuto.Tests/TaskTypeCatalogTests.cs`（新，8 条）
- `tests/FbAuto.Tests/UploadProgressMonitorTests.cs` —— 加 2 条、改 1 条标题
- `tests/FbAuto.Tests/ExecutorCatalogTests.cs` —— 重写（边界从「谁没接」变成「接对没接对」）
- `tests/FbAuto.Tests/JsonConfigServiceTests.cs` —— 比特版默认值与互不覆盖
