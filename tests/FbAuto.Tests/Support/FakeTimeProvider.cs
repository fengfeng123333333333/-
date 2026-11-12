namespace FbAuto.Tests.Support;

/// <summary>
/// 可控时钟。到期、回拨这类逻辑没法靠等真实时间验证，只能把时间捏在手里。
/// </summary>
public sealed class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _now;

    public FakeTimeProvider(DateTimeOffset start) => _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan delta) => _now += delta;

    /// <summary>直接把时钟拨到某个时刻，用于模拟用户回拨系统时间。</summary>
    public void SetUtcNow(DateTimeOffset value) => _now = value;
}
