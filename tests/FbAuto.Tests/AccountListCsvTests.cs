using System.Text;
using FbAuto.Core.Models;
using FbAuto.Infrastructure.Accounts;
using FbAuto.Tests.Support;

namespace FbAuto.Tests;

/// <summary>
/// 账号清单读取。清单是手工维护的，坏行是常态——读它的代码必须「尽量读出来 + 说清哪里不对」，
/// 而不是一行填错就整表拒绝。
/// </summary>
public sealed class AccountListCsvTests : IDisposable
{
    private const string Header = "别名,通道,user-data-dir,比特窗口ID,代理,主页ID,主页名称,凭证引用,状态,备注";

    private readonly TempDirectory _temp = new();
    private readonly TestLogger _logger = new();

    public void Dispose() => _temp.Dispose();

    private AccountListCsv Load(string content, bool withBom = false)
    {
        var path = _temp.Combine("accounts.csv");
        File.WriteAllText(path, content, new UTF8Encoding(withBom));
        return new AccountListCsv(path, _logger);
    }

    [Fact]
    public void 正常两行按列名读出字段()
    {
        var accounts = Load($"""
            {Header}
            acc01,google,D:\FbProfiles\acc01,,http://127.0.0.1:7890,100001,主页甲,cred://acc01,正常,谷歌版·独立Profile
            acc02,bit,,aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa,,100002,主页乙,cred://acc02,待接入,比特版·窗口ID
            """);

        Assert.Equal(2, accounts.All.Count);

        var first = accounts.All[0];
        Assert.Equal("acc01", first.Alias);
        Assert.Equal(BrowserChannelKind.Google, first.Channel);
        Assert.Equal(@"D:\FbProfiles\acc01", first.ProfileDirectory);
        Assert.Equal("http://127.0.0.1:7890", first.Proxy);
        Assert.Equal("主页甲", first.HomePageName);
        Assert.Equal("Chrome-acc01", first.BrowserDisplayName);

        var second = accounts.All[1];
        Assert.Equal(BrowserChannelKind.Bit, second.Channel);
        Assert.Equal("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", second.BitProfileId);
        Assert.Equal("Bit-acc02", second.BrowserDisplayName);
    }

    [Fact]
    public void 带BOM的文件也能认出别名列()
    {
        // Excel 另存为 CSV 时几乎一定带 BOM，不处理的话第一列名会是 "\uFEFF别名"，
        // 表现为「文件明明有别名列，程序却说缺列」。
        var accounts = Load($"""
            {Header}
            acc01,google,D:\FbProfiles\acc01,,,,,,正常,
            """, withBom: true);

        Assert.Single(accounts.All);
        Assert.Equal("acc01", accounts.All[0].Alias);
    }

    [Fact]
    public void 空行和注释行被跳过()
    {
        var accounts = Load($"""
            # 这是注释，不是账号
            {Header}

            acc01,google,D:\FbProfiles\acc01,,,,,,正常,

            # 又一条注释
            acc02,google,D:\FbProfiles\acc02,,,,,,正常,
            """);

        Assert.Equal(2, accounts.All.Count);
        Assert.DoesNotContain(_logger.Entries, e => e.Contains("没有别名"));
    }

    [Fact]
    public void 列顺序打乱仍能按列名读对()
    {
        var accounts = Load("""
            状态,备注,别名,通道,user-data-dir
            正常,倒序表,acc09,google,D:\FbProfiles\acc09
            """);

        Assert.Single(accounts.All);
        Assert.Equal("acc09", accounts.All[0].Alias);
        Assert.Equal(@"D:\FbProfiles\acc09", accounts.All[0].ProfileDirectory);
        Assert.Equal("正常", accounts.All[0].Status);
    }

    [Fact]
    public void 未知通道的行被跳过并记警告()
    {
        var accounts = Load($"""
            {Header}
            acc01,firefox,D:\FbProfiles\acc01,,,,,,正常,
            acc02,google,D:\FbProfiles\acc02,,,,,,正常,
            """);

        Assert.Single(accounts.All);
        Assert.Equal("acc02", accounts.All[0].Alias);
        Assert.Contains(_logger.Entries, e => e.Contains("firefox") && e.Contains("只认 google / bit"));
    }

    [Fact]
    public void 别名重复时保留第一次出现的()
    {
        var accounts = Load($"""
            {Header}
            acc01,google,D:\FbProfiles\第一次,,,,,,正常,
            acc01,google,D:\FbProfiles\第二次,,,,,,正常,
            """);

        Assert.Single(accounts.All);
        Assert.Equal(@"D:\FbProfiles\第一次", accounts.All[0].ProfileDirectory);
        Assert.Contains(_logger.Entries, e => e.Contains("重复"));
    }

    [Fact]
    public void 缺必需列时返回空清单并说清缺什么()
    {
        var accounts = Load("""
            名字,类型
            acc01,google
            """);

        Assert.Empty(accounts.All);
        Assert.Contains(_logger.Entries, e => e.Contains("缺少必需列"));
    }

    [Fact]
    public void 文件不存在时返回空清单而不是抛异常()
    {
        var accounts = new AccountListCsv(_temp.Combine("不存在.csv"), _logger);

        Assert.Empty(accounts.All);
        Assert.Contains(_logger.Entries, e => e.Contains("账号清单为空或不存在"));
    }

    [Fact]
    public void 引号包裹的字段里的逗号不会切错列()
    {
        var accounts = Load($"""
            {Header}
            acc01,google,D:\FbProfiles\acc01,,,,,,正常,"备注里,有逗号"
            """);

        Assert.Single(accounts.All);
        Assert.Equal("备注里,有逗号", accounts.All[0].Remark);
    }

    [Fact]
    public void 转义的双引号被还原()
    {
        // 这一行里有转义的引号（""），用普通字符串拼，避免与原始字符串的 """ 定界符打架。
        const string line = "acc01,google,D:\\FbProfiles\\acc01,,,,,,正常,\"他说\"\"可以\"\"";
        var accounts = Load($"{Header}\n{line}");

        Assert.Single(accounts.All);
        Assert.Equal("他说\"可以\"", accounts.All[0].Remark);
    }

    [Fact]
    public void 谷歌行缺目录时仍然载入但记警告()
    {
        // 不在这里丢弃：执行时会给出更具体的「目录为空」提示，比静默少一行好排障。
        var accounts = Load($"""
            {Header}
            acc01,google,,,,,,,正常,
            """);

        Assert.Single(accounts.All);
        Assert.Contains(_logger.Entries, e => e.Contains("user-data-dir 为空"));
    }

    [Fact]
    public void 按通道轮询分配账号()
    {
        var accounts = Load($"""
            {Header}
            acc01,google,D:\P1,,,,,,正常,
            acc02,google,D:\P2,,,,,,正常,
            """);

        var first = accounts.NextAvailable(BrowserChannelKind.Google);
        var second = accounts.NextAvailable(BrowserChannelKind.Google);
        var third = accounts.NextAvailable(BrowserChannelKind.Google);

        Assert.Equal("acc01", first!.Alias);
        Assert.Equal("acc02", second!.Alias);
        Assert.Equal("acc01", third!.Alias);
    }

    [Fact]
    public void 停用的账号不参与分配()
    {
        var accounts = Load($"""
            {Header}
            acc01,google,D:\P1,,,,,,停用,
            acc02,google,D:\P2,,,,,,正常,
            """);

        Assert.Equal("acc02", accounts.NextAvailable(BrowserChannelKind.Google)!.Alias);
    }

    [Fact]
    public void 某通道没有账号时返回空()
    {
        var accounts = Load($"""
            {Header}
            acc01,google,D:\P1,,,,,,正常,
            """);

        Assert.Null(accounts.NextAvailable(BrowserChannelKind.Bit));
    }

    [Fact]
    public void 别名查找不区分大小写()
    {
        var accounts = Load($"""
            {Header}
            Acc01,google,D:\P1,,,,,,正常,
            """);

        Assert.NotNull(accounts.Find("acc01"));
        Assert.NotNull(accounts.Find("ACC01"));
        Assert.Null(accounts.Find("acc99"));
    }

    [Fact]
    public void 重新加载能读到后来新增的行()
    {
        var accounts = Load($"""
            {Header}
            acc01,google,D:\P1,,,,,,正常,
            """);

        Assert.Single(accounts.All);

        File.WriteAllText(accounts.SourcePath, $"""
            {Header}
            acc01,google,D:\P1,,,,,,正常,
            acc02,google,D:\P2,,,,,,正常,
            """, new UTF8Encoding(false));

        accounts.Reload();

        Assert.Equal(2, accounts.All.Count);
    }
}
