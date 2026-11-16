using System.Diagnostics;
using FbAuto.Browser.Abstractions;
using FbAuto.Core.Abstractions;
using FbAuto.Core.Models;

namespace FbAuto.Application.Execution;

/// <summary>
/// 队列执行器。职责就六件：挑待执行的、按并发与间隔跑、**同账号串行**、把状态写回去、异常转失败、**无论如何关掉浏览器**。
///
/// 刻意不做的事：
/// - 不碰 UI 线程（只发事件，界面自己 marshal）；
/// - 不重试（V1 口径：任务级失败不重跑，重跑只会把账号风险放大一倍）；
/// - 不持久化队列（原版也不持久化）；
/// - 不排队等待暂停态（原版的 WaitIfPausedAsync 属执行细节，S4 没有暂停入口）。
/// </summary>
public sealed class TaskQueueRunner
{
    private readonly TaskExecutorRegistry _executors;
    private readonly IBrowserChannelProvider _channels;
    private readonly IAccountDirectory _accounts;
    private readonly IAppLogger _logger;
    private readonly TimeProvider _time;

    /// <summary>浏览器启动闸门：保证两次 Launch 之间至少隔配置的秒数。</summary>
    private readonly SemaphoreSlim _launchGate = new(1, 1);

    private DateTimeOffset _lastLaunchAt = DateTimeOffset.MinValue;

    public TaskQueueRunner(
        TaskExecutorRegistry executors,
        IBrowserChannelProvider channels,
        IAccountDirectory accounts,
        IAppLogger logger,
        TimeProvider? timeProvider = null)
    {
        _executors = executors ?? throw new ArgumentNullException(nameof(executors));
        _channels = channels ?? throw new ArgumentNullException(nameof(channels));
        _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>状态变化通知，可能在任意线程触发。</summary>
    public event EventHandler<TaskStatusChangedEventArgs>? StatusChanged;

    /// <summary>是否有一轮执行正在进行。界面据此禁用「开始任务」。</summary>
    public bool IsRunning { get; private set; }

    /// <summary>
    /// 执行一轮。只挑 <see cref="TaskRunStatus.Pending"/> 的任务；已成功/失败/中断的一律不动。
    /// 本方法不抛异常（取消也走正常返回），调用方不需要 try。
    /// </summary>
    public async Task<TaskRunSummary> RunAsync(
        IReadOnlyList<AutomatedTask> tasks,
        TaskRunOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(options);

        if (IsRunning)
        {
            _logger.Warn("已有一轮执行在进行中，本次「开始任务」被忽略");
            return TaskRunSummary.Empty;
        }

        var pending = tasks.Where(t => t.Status == TaskRunStatus.Pending).ToList();
        if (pending.Count == 0)
        {
            _logger.Warn("队列里没有待执行的任务（已成功/已失败的不再执行，V1 不做自动重试）");
            return TaskRunSummary.Empty;
        }

        IsRunning = true;
        _lastLaunchAt = DateTimeOffset.MinValue;

        var concurrency = Math.Max(1, options.ConcurrentCount);

        // 并发槽最多只有待执行条数那么多。日志写用户填的 5 而实际只跑 2 条，事后对账会以为丢任务。
        var effectiveConcurrency = Math.Min(concurrency, pending.Count);
        _logger.Info(
            concurrency > pending.Count
                ? $"开始执行：待执行 {pending.Count} 条，并发数配的是 {concurrency}，实际并发 {effectiveConcurrency}，启动间隔 {options.LaunchIntervalSeconds} 秒"
                : $"开始执行：待执行 {pending.Count} 条，并发 {concurrency}，启动间隔 {options.LaunchIntervalSeconds} 秒");

        var gate = new SemaphoreSlim(concurrency, concurrency);
        var counters = new Counters();

        // 账号级闸门：同一个别名同时只跑一条。**这是并发下最容易踩的坑**——
        // 两条同账号任务并行会各自启动一个 Chrome 指向同一个 user-data-dir，
        // Chrome 会把第二个实例的启动请求转发给第一个然后自己退出，
        // 而启动前那句「清掉残留的 DevToolsActivePort」删的是**第一个实例正在用的**端口文件，
        // 结果是两条一起废（一条报「启动后立即退出」，另一条读不到端口）。
        // 按别名建表而不是按目录：别名是账号清单的主键，一个别名只对应一个账号。
        var accountGates = pending
            .Select(t => t.AccountAlias)
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(a => a, _ => new SemaphoreSlim(1, 1), StringComparer.OrdinalIgnoreCase);

        // 一轮一份失败清单：并发任务各自往里记，本轮结束一次性写盘。
        // 逐个任务读-改-写会互相覆盖（原版只暴露 WriteAllText，没有追加写）。
        var errorLog = new AccountErrorLog();

        try
        {
            var jobs = pending
                .Select(task => RunOneGuardedAsync(task, options, gate, accountGates, counters, errorLog, cancellationToken))
                .ToList();
            await Task.WhenAll(jobs);
        }
        finally
        {
            gate.Dispose();

            // 账号闸门不 Dispose：每轮都是新建的，规模等于本轮的账号数，
            // 让 GC 收比在这里赌「确定没有等待者」更安全。

            // 写盘放在 finally：本轮被取消时已经记下的失败仍然该留档。
            foreach (var path in errorLog.Flush(DateTime.Now, _logger.Warn))
            {
                _logger.Info($"账号失败清单已写入 {path}");
            }

            IsRunning = false;
        }

        var summary = new TaskRunSummary(pending.Count, counters.Succeeded, counters.Failed, counters.Interrupted);
        _logger.Info($"执行结束：共 {summary.Started} 条，成功 {summary.Succeeded}，失败 {summary.Failed}，中断 {summary.Interrupted}");
        return summary;
    }

    private async Task RunOneGuardedAsync(
        AutomatedTask task,
        TaskRunOptions options,
        SemaphoreSlim gate,
        IReadOnlyDictionary<string, SemaphoreSlim> accountGates,
        Counters counters,
        AccountErrorLog errorLog,
        CancellationToken cancellationToken)
    {
        var acquired = false;
        var accountGate = default(SemaphoreSlim);

        // 必须单独记「真拿到了」：WaitAsync(0, token) 在令牌已取消时会直接抛 OCE，
        // 此时 accountGate 已经被 TryGetValue 赋了值却没拿到锁，
        // 只看非空就去 Release，会把信号量放到超上限并抛 SemaphoreFullException，
        // 那条异常还会从 finally 冒出去把整轮 WhenAll 打挂。
        var heldAccountGate = false;
        try
        {
            // 账号闸门在并发闸门**之前**：同账号第二条任务应当在这里排队，
            // 不占着并发槽干等（占槽等会让别的账号也跑不动，并发形同虚设）。
            // 两层锁不会死锁：持账号锁的一方随后去抢并发槽，而并发槽的持有者一定会跑完退出。
            if (!string.IsNullOrWhiteSpace(task.AccountAlias)
                && accountGates.TryGetValue(task.AccountAlias, out accountGate))
            {
                // WaitAsync(0) 只为区分「直接进」和「要排队」——只有排队时才值得写一行日志，
                // 否则每条任务都多一行「排队等待」，日志里全是噪音。
                if (!await accountGate.WaitAsync(0, cancellationToken))
                {
                    _logger.Info(
                        $"{task.DisplayName}（{task.AccountAlias}）排队等待：同账号已有任务在跑，同时开两个浏览器会抢同一个配置目录");
                    await accountGate.WaitAsync(cancellationToken);
                }

                heldAccountGate = true;
            }

            // 并发闸门。停止时这里会抛 OCE —— 任务根本没开始，记中断而不是失败。
            await gate.WaitAsync(cancellationToken);
            acquired = true;
            await ExecuteOneAsync(task, options, counters, errorLog, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!acquired)
            {
                SetStatus(task, TaskRunStatus.Interrupted, "已停止（尚未开始执行）");
                Interlocked.Increment(ref counters.Interrupted);
                _logger.Info($"{task.DisplayName} 未开始即被停止");
            }
            // acquired 之后被取消的，由 ExecuteOneAsync 内部处理，这里不再重复记账。
        }
        finally
        {
            if (acquired)
            {
                gate.Release();
            }

            // 账号锁最后放：先放并发槽再放账号锁，别的同账号任务拿到锁时槽已经腾出来了。
            if (heldAccountGate)
            {
                accountGate!.Release();
            }
        }
    }

    private async Task ExecuteOneAsync(
        AutomatedTask task,
        TaskRunOptions options,
        Counters counters,
        AccountErrorLog errorLog,
        CancellationToken cancellationToken)
    {
        // 1. 账号与执行器先校验。这两样缺一样就不必启浏览器——省一次启动，省一次风控暴露。
        var account = _accounts.Find(task.AccountAlias);
        if (account is null)
        {
            Fail(task, options, counters, errorLog, null,
                $"账号清单里没有别名「{task.AccountAlias}」。请检查 {_accounts.SourcePath} 的「别名」列");
            return;
        }

        if (!account.IsUsable)
        {
            Fail(task, options, counters, errorLog, account, $"账号 {account.Alias} 状态为「{account.Status}」，已跳过不执行");
            return;
        }

        var executor = _executors.Find(task.Kind);
        if (executor is null)
        {
            Fail(task, options, counters, errorLog, account, $"{task.DisplayName} 没有对应的执行器（程序缺陷，请上报）");
            return;
        }

        var channel = _channels.Create(task.Channel);
        BrowserSession? session = null;
        var stopwatch = Stopwatch.StartNew();

        SetStatus(task, TaskRunStatus.Running, "执行中…");

        // 单条超时兜底。超时与「用户停止」都表现为 OCE，靠这个标志区分。
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(options.TaskTimeout);
        var token = timeoutCts.Token;

        try
        {
            await WaitForLaunchSlotAsync(options.LaunchIntervalSeconds, token);

            var request = new BrowserLaunchRequest
            {
                ProfileDirectory = account.ProfileDirectory,
                BitProfileId = string.IsNullOrWhiteSpace(account.BitProfileId) ? null : account.BitProfileId,
                Proxy = string.IsNullOrWhiteSpace(account.Proxy) ? null : account.Proxy,
                Headless = options.Headless,
            };

            session = await channel.LaunchAsync(request, token);
            var browser = await channel.ConnectAsync(session, token);
            var page = await channel.NewPageAsync(browser, token);

            var context = new TaskExecutionContext
            {
                Task = task,
                Account = account,
                Channel = channel,
                Session = session,
                Page = page,
                Logger = _logger,
                ScreenshotDirectory = options.ScreenshotDirectory,
            };

            var result = await executor.ExecuteAsync(context, token);
            stopwatch.Stop();

            if (result.Success)
            {
                SetStatus(task, TaskRunStatus.Success,
                    $"{result.Message}（耗时 {stopwatch.Elapsed.TotalSeconds:F1} 秒）", result.ScreenshotPath);
                Interlocked.Increment(ref counters.Succeeded);
                _logger.Info($"{task.DisplayName} 成功：{result.Message}");
            }
            else
            {
                var text = $"{result.Message}（耗时 {stopwatch.Elapsed.TotalSeconds:F1} 秒）";
                SetStatus(task, TaskRunStatus.Failed, text, result.ScreenshotPath);
                Interlocked.Increment(ref counters.Failed);
                _logger.Warn($"{task.DisplayName} 失败：{result.Message}｜页面 {result.PageUrl ?? "（无）"}");
                RecordError(errorLog, options, task, account, result.Message);
            }
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            if (cancellationToken.IsCancellationRequested)
            {
                SetStatus(task, TaskRunStatus.Interrupted, "已停止");
                Interlocked.Increment(ref counters.Interrupted);
                // 中断必须留日志。少了这一行，事后只看到「状态是中断」，
                // 分不清是人按了停止、还是浏览器自己崩了。
                _logger.Info($"{task.DisplayName} 已停止（已运行 {stopwatch.Elapsed.TotalSeconds:F1} 秒）");
            }
            else
            {
                // 不是用户停的，是单条超时。
                var text = $"执行超时（超过 {options.TaskTimeout.TotalMinutes:F0} 分钟），已中止";
                SetStatus(task, TaskRunStatus.Failed, text);
                Interlocked.Increment(ref counters.Failed);
                RecordError(errorLog, options, task, account, text);
            }
        }
        catch (BrowserChannelException ex)
        {
            stopwatch.Stop();
            Fail(task, options, counters, errorLog, account, $"浏览器启动失败：{ex.Message}");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Fail(task, options, counters, errorLog, account, $"执行异常（{ex.GetType().Name}）：{ex.Message}");
        }
        finally
        {
            // 关键：不管成功、失败、取消、超时，都要关掉自己开的那一个浏览器。
            // 用 CancellationToken.None —— 取消时更需要把进程收干净。
            if (session is not null)
            {
                try
                {
                    await channel.CloseAsync(session, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.Warn($"关闭浏览器失败（{ex.Message}），可能残留进程，请手工检查 PID {session.ProcessId}");
                }
            }
        }
    }

    /// <summary>
    /// 浏览器启动闸门。并发大于 1 时若不加这个锁，两个任务会同时开浏览器，
    /// 「启动间隔 3 秒」等于没配——而这正是风控上最敏感的一瞬间。
    /// </summary>
    private async Task WaitForLaunchSlotAsync(int intervalSeconds, CancellationToken cancellationToken)
    {
        if (intervalSeconds <= 0)
        {
            return;
        }

        await _launchGate.WaitAsync(cancellationToken);
        try
        {
            var now = _time.GetUtcNow();
            if (_lastLaunchAt != DateTimeOffset.MinValue)
            {
                var remaining = TimeSpan.FromSeconds(intervalSeconds) - (now - _lastLaunchAt);
                if (remaining > TimeSpan.Zero)
                {
                    await Task.Delay(remaining, cancellationToken);
                }
            }

            _lastLaunchAt = _time.GetUtcNow();
        }
        finally
        {
            _launchGate.Release();
        }
    }

    private void Fail(
        AutomatedTask task,
        TaskRunOptions options,
        Counters counters,
        AccountErrorLog errorLog,
        AccountEntry? account,
        string message)
    {
        SetStatus(task, TaskRunStatus.Failed, message);
        Interlocked.Increment(ref counters.Failed);
        _logger.Warn($"{task.DisplayName} 失败：{message}");
        RecordError(errorLog, options, task, account, message);
    }

    /// <summary>
    /// 记一条账号失败。<c>assetId</c> 取比特窗口 ID —— 原版字段名就叫 assetId，
    /// 谷歌通道没有这个东西，留空而不是编一个。
    /// </summary>
    private static void RecordError(
        AccountErrorLog errorLog,
        TaskRunOptions options,
        AutomatedTask task,
        AccountEntry? account,
        string reason) =>
        errorLog.Record(
            options.ExportDirectoryFor(task.Channel),
            DateTime.Now,
            task.BrowserName,
            account?.BitProfileId ?? string.Empty,
            reason);

    private void SetStatus(AutomatedTask task, TaskRunStatus status, string result, string? screenshotPath = null)
    {
        task.Status = status;
        task.Result = result;

        if (screenshotPath is not null)
        {
            task.ScreenshotPath = screenshotPath;
        }

        switch (status)
        {
            case TaskRunStatus.Running:
                task.StartedAt = DateTime.Now;
                break;
            case TaskRunStatus.Success:
            case TaskRunStatus.Failed:
            case TaskRunStatus.Interrupted:
                task.FinishedAt = DateTime.Now;
                break;
        }

        StatusChanged?.Invoke(this, new TaskStatusChangedEventArgs
        {
            TaskId = task.Id,
            Status = status,
            Result = result,
            ScreenshotPath = task.ScreenshotPath,
        });
    }

    /// <summary>计数放在一个对象里，多个并发任务各自加，避免分别传 ref 参数。</summary>
    private sealed class Counters
    {
        public int Succeeded;
        public int Failed;
        public int Interrupted;
    }
}
