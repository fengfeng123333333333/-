using FbAuto.Automation.Abstractions;

namespace FbAuto.Automation.Profiles;

/// <summary>单个资料字段的处理结论。</summary>
public enum FieldEditStatus
{
    /// <summary>改成功（或至少点完了保存）。</summary>
    Changed,

    /// <summary>不需要改（内容为空、或当前已是目标值）。不算失败。</summary>
    Skipped,

    /// <summary>失败。</summary>
    Failed,
}

/// <summary>一个字段的处理结果。</summary>
/// <param name="Field">字段名，写进汇总。</param>
/// <param name="Status">结论。</param>
/// <param name="Message">一句话说明。</param>
/// <param name="LogLines">按发生顺序的日志行。</param>
public sealed record ProfileFieldResult(
    string Field,
    FieldEditStatus Status,
    string Message,
    IReadOnlyList<string> LogLines)
{
    public bool Ok => Status != FieldEditStatus.Failed;

    public static ProfileFieldResult Skipped(string field, string message) =>
        new(field, FieldEditStatus.Skipped, message, [message]);

    /// <summary>
    /// 跳过，但保留已经走过的日志行。
    /// </summary>
    /// <remarks>
    /// 与上一个重载的区别在「半路跳过」：例如粉丝公开内容已经跳转过去了，
    /// 才发现关键词输入框不在 —— 不把「跳转到粉丝公开内容设置页...」带上，
    /// 日志里就看不到它其实动过页面。
    /// </remarks>
    public static ProfileFieldResult Skipped(string field, string message, IReadOnlyList<string> lines) =>
        new(field, FieldEditStatus.Skipped, message, lines);

    public static ProfileFieldResult Failed(string field, string message, IReadOnlyList<string> lines) =>
        new(field, FieldEditStatus.Failed, message, lines);

    public static ProfileFieldResult Changed(string field, string message, IReadOnlyList<string> lines) =>
        new(field, FieldEditStatus.Changed, message, lines);
}

/// <summary>资料修改的调参。</summary>
/// <param name="ActionTimeout">等输入框/按钮出现的最长时间。</param>
/// <param name="SaveEnabledTimeout">
/// 等保存按钮从禁用变成可用的上限。原版串里有「按钮禁用,等待 3s...」——
/// 表单刚改动时 FB 的保存按钮会短暂禁用，直接点会一直等到点击超时。
/// </param>
/// <param name="PollInterval">等待时的轮询间隔。</param>
/// <param name="IntroUrlTemplate">
/// 签名页地址模板（<c>{0}</c> 是 profile id）。默认取
/// <see cref="Selectors.FacebookUrls.ProfileIntro"/>；测试用它指向本地仿真页。
/// </param>
/// <param name="CategoryUrlTemplate">类别页地址模板，同上。</param>
/// <param name="PostingSettingsUrl">发帖设置页地址，同上（这一页不用 profile id）。</param>
/// <param name="ProfileHomeUrlTemplate">
/// 主页首页地址模板（<c>{0}</c> 是 profile id）。头像与背景图都在这一页上改。
/// </param>
/// <param name="ImageRejectionWindow">
/// 注入图片后等「尺寸不合格」提示的窗口，直接转给换图流程。
/// </param>
/// <param name="SaveSettleTimeout">
/// 点完保存后等编辑面板消失的上限。
/// </param>
/// <remarks>
/// 三个地址做成参数的理由和视频上传的 <c>ComposerUrlTemplate</c> 一样：
/// 真站点要登录态、本机 443 又不通，若地址写死，「导航」这段就只能在测试里被跳过 ——
/// 而「导航之后页面能不能操作」恰恰是最容易出错的一段。
/// </remarks>
public sealed record ProfileEditOptions(
    TimeSpan? ActionTimeout = null,
    TimeSpan? SaveEnabledTimeout = null,
    TimeSpan? PollInterval = null,
    string? IntroUrlTemplate = null,
    string? CategoryUrlTemplate = null,
    string? PostingSettingsUrl = null,
    string? ProfileHomeUrlTemplate = null,
    TimeSpan? ImageRejectionWindow = null,
    TimeSpan? SaveSettleTimeout = null,
    string? FollowersContentUrl = null,
    string? NameSettingsUrl = null)
{
    public static ProfileEditOptions Default { get; } = new();

    public TimeSpan EffectiveActionTimeout => ActionTimeout ?? TimeSpan.FromSeconds(15);

    public TimeSpan EffectiveSaveEnabledTimeout => SaveEnabledTimeout ?? TimeSpan.FromSeconds(8);

    public TimeSpan EffectivePollInterval => PollInterval ?? TimeSpan.FromMilliseconds(150);

    /// <summary>等编辑面板消失的上限（即「保存完成」）。</summary>
    public TimeSpan EffectiveSaveSettleTimeout => SaveSettleTimeout ?? TimeSpan.FromSeconds(15);

    /// <summary>注入图片后等拒绝提示的窗口，与 <c>CoverAttemptFlow</c> 的默认值保持一致。</summary>
    public TimeSpan EffectiveImageRejectionWindow => ImageRejectionWindow ?? TimeSpan.FromSeconds(5);

    /// <summary>签名页地址模板。</summary>
    public string EffectiveIntroUrlTemplate =>
        string.IsNullOrWhiteSpace(IntroUrlTemplate) ? Selectors.FacebookUrls.ProfileIntro : IntroUrlTemplate;

    /// <summary>类别页地址模板。</summary>
    public string EffectiveCategoryUrlTemplate =>
        string.IsNullOrWhiteSpace(CategoryUrlTemplate) ? Selectors.FacebookUrls.ProfileCategory : CategoryUrlTemplate;

    /// <summary>发帖设置页地址。</summary>
    public string EffectivePostingSettingsUrl =>
        string.IsNullOrWhiteSpace(PostingSettingsUrl) ? Selectors.FacebookUrls.PostingSettings : PostingSettingsUrl;

    /// <summary>主页首页地址模板。</summary>
    public string EffectiveProfileHomeUrlTemplate =>
        string.IsNullOrWhiteSpace(ProfileHomeUrlTemplate) ? Selectors.FacebookUrls.ProfileHome : ProfileHomeUrlTemplate;

    /// <summary>粉丝公开内容设置页地址。</summary>
    public string EffectiveFollowersContentUrl =>
        string.IsNullOrWhiteSpace(FollowersContentUrl)
            ? Selectors.FacebookUrls.FollowersContent
            : FollowersContentUrl;

    /// <summary>主页名字设置页地址。</summary>
    public string EffectiveNameSettingsUrl =>
        string.IsNullOrWhiteSpace(NameSettingsUrl)
            ? Selectors.FacebookUrls.NameSettings
            : NameSettingsUrl;

    /// <summary>等元素出现。</summary>
    public async Task<bool> WaitVisibleAsync(
        IPageInterop page,
        string selector,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + EffectiveActionTimeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await page.IsVisibleAsync(selector, cancellationToken).ConfigureAwait(false))
            {
                return true;
            }

            if (DateTime.UtcNow >= deadline)
            {
                return false;
            }

            await Task.Delay(EffectivePollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 等元素**存在于 DOM**（不看可见性）。
    /// </summary>
    /// <remarks>
    /// 专门给 file input 用。真实页面的 file input 一律 <c>display:none</c>，
    /// 用可见性判断永远等不到 —— 那不是「还没出现」，是「已经在但看不见」。
    /// </remarks>
    public async Task<bool> WaitExistsAsync(
        IPageInterop page,
        string selector,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + EffectiveActionTimeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await page.ExistsAsync(selector, cancellationToken).ConfigureAwait(false))
            {
                return true;
            }

            if (DateTime.UtcNow >= deadline)
            {
                return false;
            }

            await Task.Delay(EffectivePollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 等元素消失（不再可见）。
    /// </summary>
    /// <remarks>
    /// 「保存完成」的判定依据。点完保存之后编辑面板会关掉，面板上的保存按钮随之不可见 ——
    /// 这是**唯一**能从页面观察到「保存结束了」的信号。
    ///
    /// 为什么不固定 sleep 几秒：慢机器上会不够、快机器上白等。等它消失既能自适应，
    /// 又能在页面卡住时按 <see cref="EffectiveSaveSettleTimeout"/> 收口，不至于无限转。
    /// 超时**不判失败** —— 面板没关不等于没保存成功，此时判失败会把「慢」当成「错」。
    /// 调用方按「等到就记一条、没等到也记一条」处理。
    /// </remarks>
    public async Task<bool> WaitHiddenAsync(
        IPageInterop page,
        string selector,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + EffectiveSaveSettleTimeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!await page.IsVisibleAsync(selector, cancellationToken).ConfigureAwait(false))
            {
                return true;
            }

            if (DateTime.UtcNow >= deadline)
            {
                return false;
            }

            await Task.Delay(EffectivePollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 等按钮可用再点。
    /// </summary>
    /// <remarks>
    /// 不能直接点：FB 的保存按钮在表单刚变、或还在提交上一次修改时会带
    /// <c>aria-disabled='true'</c>，Playwright 的 click 会一直等到可操作——
    /// 表现为「每条任务白卡一个点击超时」，日志里只有一句英文 Timeout。
    /// 这里主动等它变可用，超时了给一句能看懂的话。
    /// </remarks>
    public async Task<bool> ClickWhenEnabledAsync(
        IPageInterop page,
        string selector,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + EffectiveSaveEnabledTimeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await page.IsEnabledAsync(selector, 0, cancellationToken).ConfigureAwait(false))
            {
                await page.ClickAsync(selector, 0, cancellationToken).ConfigureAwait(false);
                return true;
            }

            if (DateTime.UtcNow >= deadline)
            {
                return false;
            }

            await Task.Delay(EffectivePollInterval, cancellationToken).ConfigureAwait(false);
        }
    }
}
