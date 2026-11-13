namespace FbAuto.Automation.Profiles;

/// <summary>
/// 从签名文件读整段内容。
/// </summary>
/// <remarks>
/// **整段一份，不按行分配**。依据是原版界面只有一个「签名文件」选择框，
/// 日志是「签名已加载: {路径}」—— 没有「第 N 条」「条数不足」这类说法
/// （对比视频描述那条路径，它的失败提示也是「将以空描述上传」而不是按行报错）。
///
/// 读不到不算错误：签名是可选输入，跳过这一项即可，不该让整条任务失败。
/// </remarks>
public static class SignatureSource
{
    /// <summary>读签名；读不到返回空串并给出说明。</summary>
    public static (string Text, IReadOnlyList<string> LogLines) Read(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return (string.Empty, ["没有配置签名文件，签名项将跳过"]);
        }

        if (!File.Exists(path))
        {
            return (string.Empty, [$"签名文件不存在（{path}），签名项将跳过"]);
        }

        try
        {
            // 去 BOM 与首尾空白：txt 多半是记事本存的，末尾带换行；
            // 原样写进 textarea 会让签名多一个空行，页面上看得出来。
            var text = File.ReadAllText(path).Trim();
            return (text, [$"签名已加载: {path}"]);
        }
        catch (Exception ex)
        {
            return (string.Empty, [$"读取签名文件失败（{ex.Message}），签名项将跳过"]);
        }
    }
}
