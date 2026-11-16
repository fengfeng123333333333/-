# S16 · 并发风控补全（同账号串行、截图不重名）· 以及「原版这一步本来没什么可抄」

2026-11-16 · 提交 `fa8af9d` · 不依赖 Chrome 的 **598 条全绿**（Chrome 集测 63 条按指示暂缓）

---

## 1. 这一步做什么

原计划的下一阶段叫「并发与风控」。动手前先回证据，结论是：

**原版的风控手段只有两个数** —— 并发数（默认 1）和浏览器启动间隔（默认 3 秒）。
这两样在本项目里 S4 就已经实现，并且有 5 条用例钉着
（并发 1 不并行、并发 2 真并行、启动间隔够长、取消落终态、单条超时）。

所以这一步如果按「照原版补」，是空的。真正要做的是**并发下会出事的那些事**，
它们不是仿制需求，是缺陷：

1. **同账号并行会互相把对方打死。** 两条同账号任务并行时各启一个 Chrome 指同一个
   `user-data-dir`，Chrome 会把第二个实例的启动请求转发给第一个然后自己退出；
   而启动前那句「清掉残留的 DevToolsActivePort」删的是**第一个实例正在用的**那个文件。
   结果：先开的那条读不到端口（`SessionClosed`），后开的那条报「启动后立即退出」，两条一起废。
   原版那句错误提示「该 profile 已被另一个实例占用」说的就是这个场景，但它只在报错文案里，
   调度层没有挡。
2. **失败截图会互相覆盖。** 文件名精确到秒，并发下同一秒失败两条就只剩一张图，
   而三条任务的结果列都写着「有截图」。
3. **别名里有非法字符时截图整个丢。** 别名来自手工维护的 CSV，写个 `acc/01` 就把路径变成子目录，
   `Directory` 不存在 → 抛异常 → 被 `catch` 吞掉 → 使用者只看到「没截图」，日志里也只有一句警告。

顺带修了第四件小事：并发数填 5 但只有 2 条任务时，日志原来写「并发 5」，
事后对账会以为调度器私吞了 3 条。

---

## 2. 证据

### 2.1 原版没有额外的风控机制

在 `_analysis/u16_all.txt`（原版 DLL 的 UTF-16LE 重扫全文）里按词计数：

| 词 | 命中 | 说明 |
| --- | --- | --- |
| 风控 | 0 | — |
| 间隔 | 0 | 界面标签之外没有这个词 |
| 延迟 | 0 | — |
| 冷却 | 0 | — |
| 频繁 | 0 | — |
| 验证 | 0 | 没有验证码 / checkpoint 检测 |
| 重试 | 5 | **全是页面级**：等弹窗、等元素，见第 3168 / 3229 / 3231 / 3415 / 3452 行 |
| 并发 | 1 | 第 2854 行，日志串「个任务，并发数:」 |
| 随机 | 3 | 只有「图片目录随机选取」与两行 JS 注释，与请求节奏无关 |

那 5 处「重试」的原文是「…，准备重试...」「重试」「: 1.5 秒后重试...」
「次未检测到弹窗，重试...」「次重试...」，都是**页面内等某个元素出现**的轮询，
不是任务失败后重跑账号。所以 V1 的「任务级不重试」口径与原版一致。

结论：原版没有任何形式的随机延迟、请求节流、失败退避、封号检测。
「风控」在原版里就是「并发数配小 + 启动间隔拉长」这一件事。

### 2.2 并发与间隔的默认值确实是原版值

`AppConfig.ConcurrentCount = 1`、`BrowserOpenIntervalSeconds = 3`，
与 S1 从原版 `settings.json` 实测的默认一致，`JsonConfigServiceTests` 钉着。

### 2.3 缺陷 1 在代码里是能直接读出来的

`ChromeChannel.LaunchAsync` 的顺序是：

```
Directory.CreateDirectory(profile)
TryDelete(profile/DevToolsActivePort)     ← 删除的是「别人正在用的」端口文件
Process.Start(chrome --user-data-dir=profile --remote-debugging-port=0 ...)
轮询读 DevToolsActivePort → 拿到端口就返回
```

而它自己在轮询里写的错误文案是：
「Chrome 启动后立即退出（退出码 N）。常见原因：该 profile 已被另一个实例占用…」

也就是说：**代码作者知道 Chrome 会拒绝同 profile 的第二个实例，但没有在调度层挡住**。

---

## 3. 做了什么

### 3.1 同账号串行（核心）

`TaskQueueRunner` 每轮开始时按本轮待执行任务的账号别名建一张闸门表：

```csharp
var accountGates = pending
    .Select(t => t.AccountAlias)
    .Where(a => !string.IsNullOrWhiteSpace(a))
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToDictionary(a => a, _ => new SemaphoreSlim(1, 1), StringComparer.OrdinalIgnoreCase);
```

每个任务在拿并发槽**之前**先拿自己的账号闸门，拿不到就等，并打一行日志说明为什么在等。

三个设计选择，都有原因：

- **按别名而不是按 profile 目录。** 别名是账号清单的主键，一个别名只对应一行。
  按目录加锁会在比特通道上出事：比特账号的 `ProfileDirectory` 全是空串，
  按目录锁等于把所有比特账号串成一条队，把并发废掉。
- **账号闸门在并发闸门之前。** 反过来（先进并发槽再等账号锁）会让一个并发槽被占着干等，
  并发数 5 时可能 4 个槽都在等同账号的锁，别的账号反而跑不动。
- **两层锁不会死锁。** 持账号锁的一方随后去抢并发槽，而并发槽的持有者一定会跑完退出，
  不存在环。
- 只影响同账号：`并发数为二时确实并行`（acc01 + acc02）这条旧用例仍然绿。

### 3.2 日志写实际并发

```csharp
var effectiveConcurrency = Math.Min(concurrency, pending.Count);
```

配的并发大于待执行条数时，日志改成「并发数配的是 5，实际并发 2」。
`SemaphoreSlim` 的槽数仍然是配的值（不影响语义），只是不再让日志说谎。

### 3.3 失败截图唯一 + 别名清洗

```csharp
var name = $"{DateTime.Now:yyyyMMdd_HHmmssfff}_{Task.Kind}_{SafeFileName(Task.AccountAlias)}_{Task.Id.ToString("N")[..6]}.png";
```

- 从 `HHmmss` 加到 `HHmmssfff`：同秒两次失败不再互相覆盖。
- 再拼 `Task.Id` 前 6 位：并发被账号锁串行之后，同账号同类型不会同时失败，
  但「两个别名指向同一个 profile 目录」这类配置错误仍然可能，留个兜底。
- `SafeFileName` 把 `Path.GetInvalidFileNameChars()` 里的字符换成 `_`，空别名给 `unknown`。

---

## 4. 四个容易错的点

1. **「拿到了没有」不能用「引用非空」判断。**
   `accountGate.WaitAsync(0, token)` 在令牌已取消时会直接抛 `OperationCanceledException`，
   此时 `accountGate` 已经被 `TryGetValue` 赋了值却**没拿到锁**。
   只看非空就 `Release()`，会把信号量放到超上限并抛 `SemaphoreFullException`，
   而且这条异常是从 `finally` 冒出去的 —— 整轮 `Task.WhenAll` 直接挂掉。
   必须单独记一个 `heldAccountGate`。
   这个 bug 是我第一版代码里真实存在的，被新写的 `取消时卡在账号锁上的任务也落到中断态` 抓住，
   后面注入 M5 复现了同一异常。

2. **同账号排队必须留痕。**
   没有那行「排队等待：同账号已有任务在跑…」的日志，使用者看到第二条任务迟迟不开始，
   会以为程序卡死并去点「停止」。但也不能无条件打印 —— 直接进去的那条任务不该也来一句，
   所以用 `WaitAsync(0)` 的结果区分「直接进」和「要排队」，用例钉住**只出现一条**。

3. **账号锁挡不住「两个别名配了同一个目录」。**
   这是配置错误，不是并发缺陷。用例和文档都不假装挡住它；
   它的表现会落在 `ChromeChannel` 那句「该 profile 已被另一个实例占用」上，
   有错误提示可查。写进第 6 节未完成项。

4. **截图失败是「吞异常」的，所以它坏了没人知道。**
   设计上截图失败不该让「任务失败」变成「程序崩溃」，代价是这一块没有报错。
   所以它必须单独有测试，断言要落到磁盘（真写文件、数文件个数），
   不能只断言「返回路径不为 null」。

---

## 5. 反向注入（证伪）

每项都是「改坏产品代码 → 重新 `build FbAuto.slnx` → 跑对应用例 → 确认变红 → 还原」。
6 项全部命中后才还原。

| 注入 | 改坏什么 | 结果 | 命中的用例与报错 |
| --- | --- | --- | --- |
| M1 | 建完闸门表后 `accountGates.Clear()`（等于没有账号锁） | 红 | `并发数为二时同一账号的两条任务仍然串行`：`Assert.Equal() Failure: Expected: 1, Actual: 2` |
| M2 | 日志改回固定写 `并发 {concurrency}` | 红 | `并发数大于任务数时日志写实际并发`：`Assert.Contains() Failure: Filter not matched in collection` |
| M3 | 文件名退回 `yyyyMMdd_HHmmss_Kind_别名`（去掉毫秒与 Id） | 红 | `同一秒内的两次失败截图不会互相覆盖`：`Assert.NotEqual() Failure: Strings are equal` |
| M4 | 文件名去掉 `SafeFileName`，直接拼别名 | 红 3 条 | `别名含非法文件名字符时截图仍然能保存`（`acc/01` / `acc:01` / `acc*01` 全部 `Assert.NotNull() Failure: Value is null`）+ `Assert.DoesNotContain() Failure` |
| M5 | 释放条件改回 `heldAccountGate \|\| accountGate is not null` | 红 | `取消时卡在账号锁上的任务也落到中断态`：`System.Threading.SemaphoreFullException: Adding the specified count to the semaphore would cause it to exceed its maximum count` |
| M6 | 「排队等待」日志移到 `WaitAsync(0)` 之前，无条件打印 | 红 | `同账号排队时写一行说明原因的日志`：`Assert.Single() Failure: The collection contained 2 matching items` |

M5 是这次最值钱的一条：它证明第 4 节第 1 点不是理论担忧，而是真的会把整轮执行打挂。

全部还原后 `grep -rn MUTATION src/ tests/` 无输出。

---

## 6. 未完成项（如实记录）

1. **真机验收没做。** `facebook.com:443` 在这台机器上不通。本步改的是调度层，
   不涉及页面选择器，但仍属于「没有在真 FB 上跑过」。
2. **同账号串行是在进程内做的。** 两台机器 / 两个进程同时跑同一个账号，
   这里挡不住，仍然会撞在 Chrome 的 profile 独占上。
   要挡需要文件锁或比特 API 的占用查询，不在本步范围。
3. **「两个别名配了同一个 user-data-dir」挡不住**（见第 4 节第 3 点）。
   要不要在账号清单加载时做一次同目录查重并报错，需要需求方定
   —— 它会改变「清单能直接跑」的现有行为。
4. **并发数没有上限。** 填 999 时实际并发是 `min(999, 待执行条数)`，不会开 999 个浏览器，
   所以没有崩溃风险。但也没有「超过 N 个提醒风控风险」的提示。
   原版同样没有，暂按原版。
5. **失败不重试、不冷却。** 与原版一致（证据 2.1：原版的「重试」全是页面级轮询）。
   如果想要「同账号第一条失败就跳过后续」，那会改行为语义，等需求方定。
6. **任务清单仍然不持久化。** 见 S15 第 6 节第 3 点，等需求方定。
7. **Chrome 集成测试的偶发卡死未定位。** 按指示暂缓，恢复前必须先查。

---

## 7. 验收对照

| 要求 | 结果 |
| --- | --- |
| 并发 > 1 时同账号不并行 | ✅ 账号闸门，用例断言 peak = 1 且两条都执行完、各关一次浏览器 |
| 不同账号仍然并行 | ✅ 旧用例 `并发数为二时确实并行` 仍绿 |
| 并发 > 1 时不会两个 Chrome 抢同一 profile | ✅ 同上（这是第 7 行要求的落地形式） |
| 排队时使用者能看到原因 | ✅ 一行「排队等待：同账号已有任务在跑…」，且只打一条 |
| 取消时不卡死、不泄漏信号量 | ✅ 两条都落中断态；注入 M5 证明原写法会 `SemaphoreFullException` |
| 失败截图不互相覆盖 | ✅ 文件名带毫秒 + Id，用例数磁盘文件 |
| 别名含非法字符照样出截图 | ✅ `SafeFileName`，三个非法字符形态各一条用例 |
| 每步有测试且能证伪 | ✅ 新增 9 条，注入 6 项全部命中后还原 |

---

## 8. 测试

不依赖 Chrome 的全量：**598 条全绿（耗时 4 / 6 秒两轮）**。
S15 是 589 条，本步净增 9 条：

| 用例类 | 条数 | 覆盖什么 |
| --- | --- | --- |
| `TaskQueueRunnerTests`（扩容） | +4 | 同账号两条并发 2 仍串行（peak=1、两条都跑完、启停各 2 次）、排队日志只打一条、并发数大于任务数时日志写实际并发、取消时卡在账号锁上也落中断态且启停配对 |
| `TaskExecutionContextScreenshotTests`（新） | 5 | 同秒两次失败落两个文件、三种非法别名（`acc/01` / `acc:01` / `acc*01`）仍能保存且文件名无非法字符、空别名用 `unknown` 占位 |

需真 Chrome 的 63 条按指示暂缓，日常 `build\test-nochrome.cmd`，全量 `build\test.cmd`。

---

## 9. 文件清单

**产品**

- `src/FbAuto.Application/Execution/TaskQueueRunner.cs`
  （账号闸门表 + `RunOneGuardedAsync` 加锁与 `heldAccountGate` + 实际并发日志 + 类注释六件事）
- `src/FbAuto.Application/Execution/ITaskExecutor.cs`
  （截图文件名带毫秒与 Id + `SafeFileName`）

**测试**

- `tests/FbAuto.Tests/TaskQueueRunnerTests.cs`（+4 条）
- `tests/FbAuto.Tests/TaskExecutionContextScreenshotTests.cs`（新，5 条）

**文档**

- `docs/plans/S16-CONCURRENCY-AND-RISK.md`（本文）
- `docs/plans/artifacts/S16-tests.txt`
- `docs/PITFALLS.md`（补「拿到锁要用独立标志判断」与「截图失败是静默的，必须落盘断言」）
