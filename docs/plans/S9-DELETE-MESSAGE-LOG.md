# S9 · 删除消息 · 执行记录

2026-11-12 · 提交 `a8c5d0e` · 530 用例全绿（S8 为 497，新增 33）

## 一、这一步补的是什么

把「删除消息」这条任务接上真流程。接完之后的边界：`VideoUploadGoogle`、`ProfileEditGoogle`、
`DeleteMessage` 三个是真的，比特版两类仍是「未接入」占位（`ExecutorCatalogTests` 钉住）。

**这一条不是仿制出来的。** 原版 DLL 里「删除消息」只有任务名和三句生命周期日志，
没有任何页面地址或选择器 —— 无从仿制，只能按需求自建。这是本项目到目前为止
第一条**完全没有证据支撑的页面流程**，所以它的可信度和前八步不是一个量级，
下面第二节、第七节都围绕这件事写。

## 二、证据：原版留下了什么，又没留下什么

| 项 | 证据 | 结论 |
|---|---|---|
| 任务名 | `[删除消息]`（左栏 `TaskTypeCatalog` 已按原文录入） | 这条任务确实存在 |
| 生命周期 | `任务添加` / `任务开始` / `任务完成` | 有跑批逻辑，但只有三句 |
| 页面流程 | **无** | 进哪个页面、点哪个按钮，一概不知 |
| 专属参数 | `DeleteMsgCookieRootPath` | 登录态**只从 cookie 来**，不用浏览器配置目录 |
| ~~屏蔽词~~ | `GoogleCommentsBlockText` / `BitCommentsBlockText` | **是评论屏蔽词，与删除消息无关** |

最后一行是本步挖出来的一个命名错误：我们 `AppConfig` 里把它叫 `MessageBlockWords`，
注释写的是「删除消息的屏蔽词」。名字看着像，实际键名是 `CommentsBlockText`，指的是**评论**。
界面上「删除消息」那一页也因此摆了一个屏蔽词输入框 —— 照着字段名想当然的结果。

已改：`CommentsBlockWords`，输入框挪到「视频上传」页（评论属于视频上传那条链路），
「删除消息」页换成真正的两个参数（删除上限、Cookie 目录）加两行风险提示。
旧配置里若还有 `MessageBlockWords` 键会读不到、回落到默认值，而默认值就是原版那五个词，行为不变。

## 三、口径（需求方定的，不是猜的）

- **删什么**：Messenger 会话记录。
- **删多少**：每账号上限 N 条，默认 20；`0` 表示不限，一直删到列表空。

第二条是这一步唯一的安全阀。删掉的对话在**对方那边也会消失**，不可恢复；
一批几十个号上去全清，出一次错就没有回头路。

## 四、设计：为什么是这四个出口、为什么要逐条删

**逐条删，不做全选。** 全选删最坏情况是「整仓没了」，逐条删最坏情况是「多删了几条」。
代价是慢（每条 4 次点击），但这个代价换的是「出错时最多错一条」。

**四步序列**：点开第一条会话 → 打开会话详情 → 点「删除对话」→ 二次确认。
不走「列表行右键菜单」，因为列表行上的更多按钮**悬停才出现**，而页面原语里没有 hover；
加 hover 要动接口、真实现、假实现三处，而「进会话再删」只用到现成的点击与等待。

**五个出口**（`MessageDeletePlanner`，纯逻辑、可单测）：

| 出口 | 触发条件 | 算不算正常收尾 |
|---|---|---|
| `LimitReached` | 已删 ≥ 上限 | 是 |
| `ListEmpty` | 剩余会话 0 条 | 是 |
| `TooManyFailures` | 连续失败 3 条（成功一次清零） | 否 |
| `MaxRounds` | 轮次超过安全上限 | 否 |
| `NoProgress` | 连续 2 次「删了但数量没降」 | 否，**默认关闭** |

两条判断值得单独说：

**`NoProgress` 为什么默认关。** Messenger 的会话列表是虚拟滚动，删一条会从下面补一条进来，
渲染行数**本来就不变**。按「数量没降」判停，真机上会表现为「删两条就停」，功能等于废掉。
所以默认关掉，只在真机确认列表不是虚拟滚动后才打开。留着它是因为还有另一种场合用得上：
页面改版到「点了删除但没生效」时，数量同样不降，而连续失败抓不到（每一步都点成功了）。

**「数量没降」为什么仍要写一行日志。** 不算失败（虚拟滚动下是正常的），也不算成功
（那确实没删掉）。折中记「已执行删除，剩余 N 条（未下降：可能是虚拟滚动，也可能是没删掉）」——
出问题时能查到，不让人对着「删除完成」发愣。

## 五、注入实验拽出来的一个真缺陷

按老规矩做反向注入，第一条就出了状况：把「已达上限」那个判断去掉之后，
**测试进程直接挂死**（不是失败，是没有任何输出）。

查下去是真问题：上限判断失效 + 列表数量不降时，`LimitReached` 不触发、`TooManyFailures` 不触发
（每步都点成功了）、`NoProgress` 默认关 —— **一个出口都不剩，循环永远转下去**。
现实里的对应场景是「上限配成 0（想清空）+ 页面点了没反应」：任务永远跑不完，
而日志看起来每轮都在干活。

修法是加 `MaxRounds` 硬上限（默认「上限 × 2 + 10」，不限条数时 500），它只做刹车，不参与业务判断。
补三条用例钉住，其中一条就是「不限条数且列表数量不降时由轮次上限兜住」。

这条缺陷不是设计时想到的，是**测试挂死逼出来的** —— 也说明「注入」这一步不能省：
不注入的话，这个循环要等到真机上才暴露，而那时没人盯着测试进程。

## 六、反向注入（7 次，全部命中）

| # | 注入 | 变红用例数 |
|---|---|---|
| A | 不判「已达上限」 | 4（含 1 条集成） |
| B | 不判「列表已空」 | 3 |
| C | 连续失败阈值差一 | 2 |
| D | 停滞计数永不累加 | 1 |
| E | 默认就启用停滞检测 | 1 |
| F | 把上限 0 当成 1 | 5 |
| G | 跳过二次确认（真浏览器） | 2 |

注入 A 是第一次跑就把测试进程挂死的那个，见第五节。
G 是唯一一条在真 Chrome 里验的：删掉「点确认」这一步，页面上一条都没删掉，
`window.__deleted` 停在 0，两条集成用例同时红 —— 说明四步里最后一步确实不可省。

## 七、未完成项（不许写成已完成）

1. **四条选择器没有任何证据支撑**。`ConversationRow`、`ConversationInfoButton`、
   `DeleteConversationMenuItem`、`DeleteConversationConfirm` 全部按 FB 现行 DOM 惯例写的，
   仿真页也是照这四条搭的。所以这一整组测试证明的只是「四步序列在真浏览器里能跑通、
   退出条件能生效」，**证明不了「FB 的 Messenger 现在就是这个结构」**。
   真机第一件事：逐条确认这四个选择器，尤其是「会话详情」的 `aria-label`
   （中英两个都写了，但真实文案可能是第三种）。
2. **Cookie 注入没做**。原版这条任务靠 `DeleteMsgCookieRootPath` 起会话，
   我们沿用其他四个流程的同一套假设（浏览器配置目录已登录），cookie 目录只读出来写进日志，
   并在日志里明说「只记录、不注入」。要做 cookie 注入就得开第二条登录路线，
   要和浏览器通道一起改，不在这一步。
3. **界面没真机看过**。「删除消息」页换成了「删除上限 + Cookie 目录 + 两行风险提示」，
   编译通过、绑定编译通过，但没起界面看排版。
4. **虚拟滚动下的数量读数不准**。`WaitCountBelowAsync` 在虚拟滚动列表上会等满超时，
   日志里的「剩余 N 条」可能滞后。不影响删除本身（主判据是条数上限），但会让日志读起来别扭。
   真机确认后再决定要不要改成按会话 id 去重。

## 八、验收对照

| 验收项 | 结果 | 依据 |
|---|---|---|
| 上限生效，不多删 | 通过 | 单测 2 条 + 集成 1 条（页面上真的少 2 行） |
| 空列表不空转 | 通过 | 单测 1 条 + 集成 1 条（`window.__confirmed` 为 0） |
| 连续失败会停 | 通过 | 单测 2 条 + 集成 1 条 |
| 失败一条不中断整批 | 通过 | 单测 1 条 |
| 不限条数能清空 | 通过 | 单测 1 条 |
| 上限 0 / 负数 = 不限 | 通过 | 单测 2 条 |
| 死循环有刹车 | 通过 | 单测 3 条（注入 A 逼出来的） |
| 删完数量没降要留痕 | 通过 | 单测 1 条 + 集成 1 条 |
| 四条选择器在真 FB 上成立 | **未验收** | 本机 443 不通，需出口 |

## 九、文件清单

**产品**

- `src/FbAuto.Automation/Messages/MessageDeletePlanner.cs`（新）—— 五个出口，纯逻辑
- `src/FbAuto.Automation/Messages/MessageDeleteOptions.cs`（新）—— 调参 + 等元素 + 等列表刷新
- `src/FbAuto.Automation/Messages/ConversationDeleter.cs`（新）—— 删一条的四步
- `src/FbAuto.Automation/Messages/MessageDeleteFlow.cs`（新）—— 编排与汇总
- `src/FbAuto.Application/Execution/DeleteMessageExecutor.cs`（新）—— 配置 → 流程 → 结果
- `src/FbAuto.Automation/Selectors/FacebookSelectors.cs` —— 追加「删除消息」一组（已注明无证据）
- `src/FbAuto.Automation/Selectors/FacebookUrls.cs` —— 追加 `MessengerInbox`
- `src/FbAuto.Core/Configuration/AppConfig.cs` —— `CommentsBlockWords` 改名、`DeleteMessageLimit`、
  `DeleteMessageCookieRootPath`
- `src/FbAuto.Application/Execution/ExecutorCatalog.cs` —— 注册 `DeleteMessage`
- `src/FbAuto.Presentation/ViewModels/MainViewModel.cs`、`Views/MainWindow.xaml` —— 屏蔽词挪位、
  删除消息页重做

**测试**

- `tests/FbAuto.Tests/MessageDeleteTests.cs`（新）—— 规划器 + 编排（假页面）
- `tests/FbAuto.Tests/MessageDeleteFlowIntegrationTests.cs`（新）—— 真 Chrome
- `tests/FbAuto.Tests/Support/FacebookMimicPage.cs` —— 追加 `MessengerInbox`
- `tests/FbAuto.Tests/Support/FakePageInterop.cs` —— 加 `OnClick` 钩子、补齐取消检查
- `tests/FbAuto.Tests/ExecutorCatalogTests.cs`、`JsonConfigServiceTests.cs` —— 同步边界与字段

## 十、附：一次偶发挂死的排查记录

全量第一次跑时挂住了，blame 报出卡住的是 **S7 的用例**
`VideoUploadFlowIntegrationTests.有失败条目时先移除再发布`（`Completed="False"`），不是本步新增的。

排查顺序与结论：

1. 单独跑 `VideoUploadFlowIntegrationTests` → 9/9 全绿，21 秒。
2. 单独跑 `MessageDeleteFlowIntegrationTests` → 4/4 全绿，33 秒。
3. 复查两次挂起前的执行序列，卡住的位置**不是同一个用例**，只是都落在起浏览器的类里。
4. 查到环境里有孤儿 Chrome：上一轮排查时直接 `Stop-Process testhost`，留下 `--type=renderer`
   的渲染进程没退（父进程没了，它自己不会退）。下一次 `LaunchAsync` 起新实例时被这些进程拖住。
5. 按 PID 清掉孤儿进程（保留使用者自己开着的那些）后重跑全量 → **530/530 全绿，2.28 分钟**。

教训：**排查挂起时不要直接杀 testhost**。正确做法是让 blame 跑完（它会自己清理），
或者杀之前先记下这次启动的 Chrome PID，事后按 PID 清干净 —— 否则「杀一次脏一次」，
下一轮排查会被上一轮的残留误导，看起来像「又挂了一次」。

`docs/plans/artifacts/S9-tests.txt` 是这次全绿的完整输出（`-v n`，含每条用例耗时）。
