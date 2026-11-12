using FbAuto.Core.Models;

namespace FbAuto.Core.Abstractions;

/// <summary>
/// 账号清单。执行任务前用它把「别名」解析成可用的 profile 目录 / 比特窗口 ID。
/// </summary>
public interface IAccountDirectory
{
    /// <summary>清单里所有账号，按文件顺序。</summary>
    IReadOnlyList<AccountEntry> All { get; }

    /// <summary>清单文件路径，用于错误提示（告诉用户该去改哪个文件）。</summary>
    string SourcePath { get; }

    /// <summary>按别名查；找不到返回 null。</summary>
    AccountEntry? Find(string alias);

    /// <summary>
    /// 按通道分配一个可用账号。S4 策略是轮询，不检查该账号是否已被别的任务占用
    /// （并发默认 1，S4 不引入账号级锁；真并发跑同一账号属于配置错误，日志会记）。
    /// </summary>
    AccountEntry? NextAvailable(BrowserChannelKind channel);

    /// <summary>重新读盘。清单是手工维护的文件，改完不必重启程序。</summary>
    void Reload();
}
