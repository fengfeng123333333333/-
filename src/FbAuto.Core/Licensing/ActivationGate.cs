namespace FbAuto.Core.Licensing;

/// <summary>
/// 「开发期跳过激活」的闸门。
///
/// 为什么需要这个类：这个开关存在 config.json 里，而 config.json 是使用者能手改的明文文件。
/// 只要发布版认这个键，任何人把它改成 true 就绕过了整套授权 —— 密钥、签名、机器绑码全部白做。
/// 所以真正的闸门必须落在**编译期**：Debug 构建允许跳过，Release 构建无论配置怎么写都不允许。
///
/// 验证方式不是单测（单测跑在 Debug 下，只能证明 Debug 分支），而是
/// 「打 Release 发布包 → 故意把 config.json 里的键写成 true → 启动后必须停在激活窗」，
/// 见 docs/DEPLOY-REMOTE.md 第 5 节。
/// </summary>
public static class ActivationGate
{
    /// <summary>当前构建是否允许「跳过激活」。Release 构建恒为 <c>false</c>。</summary>
    public static bool BuildAllowsSkip =>
#if DEBUG
        true;
#else
        false;
#endif

    /// <summary>
    /// 是否真的跳过激活。两个条件同时成立才跳：配置里写了要跳，且当前是 Debug 构建。
    /// </summary>
    public static bool ShouldSkip(bool configuredSkip) => ShouldSkip(configuredSkip, BuildAllowsSkip);

    /// <summary>
    /// 纯函数版本，把「构建是否允许」显式传进来 —— 这样 Release 的行为也能在 Debug 测试里断言，
    /// 不必靠「打一个 Release 包再看」才能覆盖。
    /// </summary>
    public static bool ShouldSkip(bool configuredSkip, bool buildAllowsSkip) =>
        configuredSkip && buildAllowsSkip;
}
