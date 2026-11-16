using FbAuto.Automation.Profiles;
using FbAuto.Automation.Selectors;
using FbAuto.Core.Configuration;
using FbAuto.Core.Models;
using FbAuto.Tests.Support;

namespace FbAuto.Tests;

/// <summary>
/// 资料修改的七项开关（原版 <c>GoogleEditXxx</c> / <c>BitEditXxx</c>）。
/// </summary>
/// <remarks>
/// 开关的语义是「这次别动它」，不是「动了再恢复」。所以判据是**页面一次都没被碰过**，
/// 而不是「结果里写了跳过」—— 后者在「跑了但半路失败」时也成立，看不出区别。
///
/// 每条「关掉就不碰」的用例都配一条「开着就会碰」的对照。没有对照的话，
/// 闸门被整段删掉、或者编辑器改了名压根没跑，用例同样会是绿的 —— 那就不叫守住了。
/// </remarks>
public sealed class ProfileEditSwitchTests
{
    private const string ProfileId = "100012345678";

    private static ProfileEditOptions Fast => new(
        ActionTimeout: TimeSpan.FromMilliseconds(120),
        SaveEnabledTimeout: TimeSpan.FromMilliseconds(120),
        PollInterval: TimeSpan.FromMilliseconds(5));

    /// <summary>
    /// 有五项各自对应一个页面地址，可以用「有没有导航到它」直接判定跑没跑。
    /// 背景图与头像共用一个页面地址，只能另测（见各自用例）。
    /// </summary>
    private static readonly (string Field, string Url, string SkipLine, Func<ProfileEditRequest, ProfileEditRequest> Off)[] UrlGates =
    [
        (
            PostingSettingsEditor.FieldName,
            FacebookUrls.PostingSettings,
            PostingSettingsEditor.SkipLine,
            r => r with { UpdatePostingSettings = false }),
        (
            FollowersContentEditor.FieldName,
            FacebookUrls.FollowersContent,
            FollowersContentEditor.SkipLine,
            r => r with { EditFollowersContent = false }),
        (
            SignatureEditor.FieldName,
            FacebookUrls.Format(FacebookUrls.ProfileIntro, ProfileId),
            SignatureEditor.SkipLine,
            r => r with { EditSignature = false }),
        (
            CategoryEditor.FieldName,
            FacebookUrls.Format(FacebookUrls.ProfileCategory, ProfileId),
            CategoryEditor.SkipLine,
            r => r with { EditCategory = false }),
        (
            PageNameEditor.FieldName,
            FacebookUrls.NameSettings,
            PageNameEditor.SkipLine,
            r => r with { EditPageName = false }),
    ];

    /// <summary>五项都给了料、开关也都开着的一份请求。</summary>
    private static ProfileEditRequest AllOn() => new(
        Signature: "签名内容",
        Category: "教育",
        UpdatePostingSettings: true,
        BlockWords: ["spam"],
        PageName: "新名字",
        PageNamePassword: "pw");

    /// <summary>
    /// 对照组：五项开关全开时，五个页面各自都会被打开。
    /// </summary>
    /// <remarks>
    /// 这条是下面「关掉就不碰」的前提。它红了，说明被测的地址或入口变了，
    /// 那几条用例的「没导航」就失去了意义（本来就不会导航）。
    /// </remarks>
    [Fact]
    public async Task 开关全开时五项各自打开自己的页面()
    {
        var page = new FakePageInterop();

        var result = await ProfileEditFlow.RunAsync(
            page,
            AllOn(),
            profileId: ProfileId,
            options: Fast);

        Assert.Equal(7, result.Fields.Count);
        foreach (var gate in UrlGates)
        {
            Assert.True(
                page.NavigatedUrls.Contains(gate.Url),
                $"{gate.Field} 开关开着，却没有打开 {gate.Url}。实际打开过：{string.Join(" | ", page.NavigatedUrls)}");
        }
    }

    /// <summary>关掉哪一项，就一次都不打开哪一项的页面，并记下原版那句跳过串。</summary>
    [Fact]
    public async Task 关掉某一项后不打开它自己的页面()
    {
        foreach (var gate in UrlGates)
        {
            var page = new FakePageInterop();

            var result = await ProfileEditFlow.RunAsync(
                page,
                gate.Off(AllOn()),
                profileId: ProfileId,
                options: Fast);

            var field = result.Fields.Single(f => f.Field == gate.Field);

            Assert.True(
                field.Status == FieldEditStatus.Skipped,
                $"{gate.Field} 关掉后应记为跳过，实际 {field.Status}（{field.Message}）");
            Assert.Contains("未勾选", field.Message);
            Assert.Contains(gate.SkipLine, result.LogLines);

            // 关键的一条：页面一次都没被打开过。
            Assert.True(
                !page.NavigatedUrls.Contains(gate.Url),
                $"{gate.Field} 已关掉，却仍然打开了 {gate.Url}");
        }
    }

    /// <summary>
    /// 七项全关时，一条页面动作都不该有，并明确说「本次不会修改任何内容」。
    /// </summary>
    /// <remarks>
    /// 这是「报成功却一项没改」的形态。原版没有这句提示（它不校验），
    /// 但使用者看到全绿会更相信任务跑过了，所以这行是我们自己加的。
    /// </remarks>
    [Fact]
    public async Task 七项全关时不碰页面并明确提示()
    {
        var page = new FakePageInterop();

        var request = new ProfileEditRequest(
            Signature: "签名内容",
            Category: "教育",
            UpdatePostingSettings: false,
            ImageCandidates: [@"C:\imgs\a.jpg"],
            BlockWords: ["spam"],
            PageName: "新名字",
            PageNamePassword: "pw",
            EditCoverImage: false,
            EditAvatar: false,
            EditFollowersContent: false,
            EditSignature: false,
            EditCategory: false,
            EditPageName: false);

        var result = await ProfileEditFlow.RunAsync(
            page,
            request,
            profileId: ProfileId,
            options: Fast);

        Assert.True(result.Success);
        Assert.Equal(7, result.SkippedCount);
        Assert.Empty(page.NavigatedUrls);
        Assert.Empty(page.Clicked);
        Assert.Empty(page.FileInjections);

        foreach (var line in new[]
                 {
                     "已跳过【修改背景图】",
                     "已跳过【修改头像】",
                     "已跳过【修改公共主页发帖设置】",
                     "已跳过【修改粉丝公开内容】",
                     "已跳过【修改个性签名】",
                     "已跳过【修改类别】",
                     "已跳过【修改主页名字】",
                 })
        {
            Assert.Contains(line, result.LogLines);
        }

        // 注意：`Assert.Contains(string, 集合)` 是「整项相等」，不是子串匹配 ——
        // 想匹配半句必须用谓词重载，否则会报一个看起来像「提示语没写进日志」的假失败。
        Assert.Contains(result.LogLines, x => x.Contains("七项都未勾选", StringComparison.Ordinal));
    }

    /// <summary>
    /// 背景图与头像共用主页地址，所以用「另一项还开着时主页照样打开」来证明两把开关是分开的。
    /// </summary>
    /// <remarks>
    /// 两把开关如果被写成同一个字段，表现会是「只关背景图，头像也没了」——
    /// 而这种错在「两个都关」的用例里完全看不出来。
    /// </remarks>
    [Fact]
    public async Task 只关背景图时头像照样打开主页()
    {
        var page = new FakePageInterop();

        var result = await ProfileEditFlow.RunAsync(
            page,
            new ProfileEditRequest(
                ImageCandidates: [@"C:\imgs\a.jpg"],
                EditCoverImage: false),
            profileId: ProfileId,
            options: Fast);

        var cover = result.Fields.Single(f => f.Field == CoverPhotoEditor.FieldName);
        Assert.Equal(FieldEditStatus.Skipped, cover.Status);
        Assert.Contains("未勾选", cover.Message);

        var avatar = result.Fields.Single(f => f.Field == AvatarEditor.FieldName);
        Assert.NotEqual(FieldEditStatus.Skipped, avatar.Status);

        Assert.Contains(FacebookUrls.Format(FacebookUrls.ProfileHome, ProfileId), page.NavigatedUrls);
    }

    /// <summary>两项都关掉时，主页一次都不打开，也不注入任何图片。</summary>
    [Fact]
    public async Task 背景图与头像都关掉时连主页都不打开()
    {
        var page = new FakePageInterop();

        var result = await ProfileEditFlow.RunAsync(
            page,
            new ProfileEditRequest(
                ImageCandidates: [@"C:\imgs\a.jpg"],
                EditCoverImage: false,
                EditAvatar: false),
            profileId: ProfileId,
            options: Fast);

        foreach (var name in new[] { CoverPhotoEditor.FieldName, AvatarEditor.FieldName })
        {
            var field = result.Fields.Single(f => f.Field == name);
            Assert.True(field.Status == FieldEditStatus.Skipped, $"{name} 应跳过，实际 {field.Status}");
            Assert.Contains("未勾选", field.Message);
        }

        Assert.DoesNotContain(FacebookUrls.Format(FacebookUrls.ProfileHome, ProfileId), page.NavigatedUrls);
        Assert.Empty(page.FileInjections);
    }

    /// <summary>
    /// 配置里的「本次要改哪几项」必须与流程真正的执行顺序一致。
    /// </summary>
    /// <remarks>
    /// 两边是各写一遍的（配置层没有执行层的依赖），所以会漂。漂了之后的症状很隐蔽：
    /// 日志说「本次要改 7 项」并按某个顺序列出来，实际改的顺序却是另一个 ——
    /// 对着日志排查时会被带偏。这条用例在漂的当场就红。
    /// </remarks>
    [Fact]
    public async Task 开关清单顺序与执行顺序一致()
    {
        var page = new FakePageInterop();

        var result = await ProfileEditFlow.RunAsync(
            page,
            new ProfileEditRequest(),
            profileId: ProfileId,
            options: Fast);

        var profile = new AppConfig().ForChannel(BrowserChannelKind.Google);

        Assert.Equal(profile.EnabledEditItems, result.Fields.Select(f => f.Field));
    }
}
