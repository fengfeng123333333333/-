using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FbAuto.Licensing;

namespace FbAuto.Keygen;

/// <summary>
/// 命令行入口。刻意不引 System.CommandLine：四个子命令，手写解析比学一个框架便宜。
/// </summary>
public static class Cli
{
    /// <summary>口令来源。放在环境变量里，避免出现在命令行历史与进程列表。</summary>
    public const string PassphraseVariable = "FBAUTO_KEY_PASS";

    private const string PrivateKeyFileName = "license_private.key";
    private const string PublicKeyFileName = "license_pubkey.b64";

    public static int Run(string[] args)
    {
        TrySetUtf8Console();

        if (args.Length == 0)
        {
            PrintHelp();
            return 1;
        }

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "initkey" => InitKey(args[1..]),
                "gen" => Gen(args[1..]),
                "verify" => Verify(args[1..]),
                "emit" => Emit(args[1..]),
                "help" or "-h" or "--help" => PrintHelpAndOk(),
                _ => Unknown(args[0]),
            };
        }
        catch (CryptographicException ex)
        {
            Console.Error.WriteLine($"[错误] {ex.Message}");
            return 2;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"[错误] 文件操作失败：{ex.Message}");
            return 2;
        }
    }

    /// <summary>默认密钥目录：用户主目录下的 FbAutoSecrets，不在仓库里。</summary>
    public static string DefaultSecretDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "FbAutoSecrets");

    public static string DefaultPrivateKeyPath => Path.Combine(DefaultSecretDirectory, PrivateKeyFileName);

    public static string DefaultPublicKeyPath => Path.Combine(DefaultSecretDirectory, PublicKeyFileName);

    private static int InitKey(string[] args)
    {
        var keyPath = GetOption(args, "--key") ?? DefaultPrivateKeyPath;
        var pubPath = GetOption(args, "--pub") ?? DefaultPublicKeyPath;
        var force = HasFlag(args, "--force");

        if (File.Exists(keyPath) && !force)
        {
            Console.Error.WriteLine($"[拒绝] 私钥已存在：{keyPath}");
            Console.Error.WriteLine("换一份新私钥会让之前发出的所有授权码立即失效。确需重建请加 --force。");
            return 3;
        }

        var passphrase = RequirePassphrase();

        using var key = PrivateKeyVault.CreateNew();
        PrivateKeyVault.Save(keyPath, key, passphrase);
        var spki = PrivateKeyVault.ExportPublicKey(key);
        File.WriteAllText(pubPath, Convert.ToBase64String(spki));

        Console.WriteLine("密钥对已生成。");
        Console.WriteLine($"  私钥（口令加密，务必备份）：{keyPath}");
        Console.WriteLine($"  公钥（可公开，用于内置客户端）：{pubPath}");
        Console.WriteLine();
        Console.WriteLine("把下面这一行填进 src/FbAuto.Licensing/LicensePublicKey.cs 的 SpkiBase64：");
        Console.WriteLine();
        Console.WriteLine($"    private const string SpkiBase64 = \"{Convert.ToBase64String(spki)}\";");
        return 0;
    }

    private static int Gen(string[] args)
    {
        var count = GetIntOption(args, "--count", 10);
        var days = GetIntOption(args, "--days", 30);
        var keyPath = GetOption(args, "--key") ?? DefaultPrivateKeyPath;
        var outPath = GetOption(args, "--out");
        var issuedText = GetOption(args, "--issued");

        if (count <= 0)
        {
            Console.Error.WriteLine("[错误] --count 必须大于 0");
            return 1;
        }

        if (days <= 0)
        {
            Console.Error.WriteLine("[错误] --days 必须大于 0");
            return 1;
        }

        var issuedOn = issuedText is null
            ? DateOnly.FromDateTime(DateTime.Today)
            : DateOnly.ParseExact(issuedText, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        if (!File.Exists(keyPath))
        {
            Console.Error.WriteLine($"[错误] 找不到私钥：{keyPath}");
            Console.Error.WriteLine("先运行 fbauto-keygen initkey 生成密钥对。");
            return 3;
        }

        var passphrase = RequirePassphrase();
        using var key = PrivateKeyVault.Load(keyPath, passphrase);

        var codes = CodeIssuer.IssueBatch(key, count, issuedOn, days);

        var builder = new StringBuilder();
        builder.AppendLine($"# FbAuto 授权码批次");
        builder.AppendLine($"# 签发日：{issuedOn:yyyy-MM-dd}");
        builder.AppendLine($"# 有效期：激活后 {days} 天（不是从签发日算）");
        builder.AppendLine($"# 数量：{count}");
        builder.AppendLine($"# 生成时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine();

        for (var i = 0; i < codes.Count; i++)
        {
            builder.AppendLine($"{i + 1:D3}\t{LicenseCodeCodec.Encode(codes[i])}");
        }

        var text = builder.ToString();

        if (outPath is null)
        {
            var defaultOut = Path.Combine(
                DefaultSecretDirectory, $"codes_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            Directory.CreateDirectory(DefaultSecretDirectory);
            File.WriteAllText(defaultOut, text, new UTF8Encoding(false));
            Console.WriteLine($"已生成 {count} 张授权码，有效期 {days} 天（自首次激活起）。");
            Console.WriteLine($"  保存到：{defaultOut}");
            Console.WriteLine();
            Console.WriteLine("前 3 张预览：");
            foreach (var line in text.Split('\n').Where(l => l.Length > 0 && !l.StartsWith('#')).Take(3))
            {
                Console.WriteLine("  " + line.TrimEnd('\r'));
            }
        }
        else
        {
            File.WriteAllText(outPath, text, new UTF8Encoding(false));
            Console.WriteLine($"已生成 {count} 张授权码，写到 {outPath}");
        }

        return 0;
    }

    private static int Verify(string[] args)
    {
        if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
        {
            Console.Error.WriteLine("用法：fbauto-keygen verify <授权码>");
            return 1;
        }

        var keyPath = GetOption(args, "--key") ?? DefaultPrivateKeyPath;
        if (!File.Exists(keyPath))
        {
            Console.Error.WriteLine($"[错误] 找不到私钥：{keyPath}");
            return 3;
        }

        var passphrase = RequirePassphrase();
        using var key = PrivateKeyVault.Load(keyPath, passphrase);
        var spki = PrivateKeyVault.ExportPublicKey(key);

        LicenseCode code;
        try
        {
            code = LicenseCodeCodec.Decode(args[0]);
        }
        catch (FormatException ex)
        {
            Console.Error.WriteLine($"[格式错] {ex.Message}");
            return 1;
        }

        var ok = LicenseVerifier.Verify(code, spki);
        Console.WriteLine($"授权码：{code}");
        Console.WriteLine($"验签：{(ok ? "通过（确实由本私钥签发）" : "失败（不是本密钥签的，或内容被改过）")}");

        // 额外演示：改动任意一位必须验签失败
        var tamperedPayload = code.ToPayloadBytes();
        tamperedPayload[0] ^= 0x01;
        using var probe = ECDsa.Create();
        probe.ImportSubjectPublicKeyInfo(spki, out _);
        var tamperStillOk = probe.VerifyData(
            tamperedPayload, code.Signature, HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        Console.WriteLine($"把版本号改一位后验签：{(tamperStillOk ? "通过（异常！）" : "失败（符合预期）")}");

        return ok ? 0 : 4;
    }

    private static int Emit(string[] args)
    {
        var pubPath = GetOption(args, "--pub") ?? DefaultPublicKeyPath;
        if (!File.Exists(pubPath))
        {
            Console.Error.WriteLine($"[错误] 找不到公钥：{pubPath}");
            return 3;
        }

        var base64 = File.ReadAllText(pubPath).Trim();
        Console.WriteLine("把下面这一行填进 src/FbAuto.Licensing/LicensePublicKey.cs：");
        Console.WriteLine();
        Console.WriteLine($"    private const string SpkiBase64 = \"{base64}\";");
        return 0;
    }

    /// <summary>读口令：优先环境变量，其次交互输入（隐藏回显）。</summary>
    public static string RequirePassphrase()
    {
        var fromEnv = Environment.GetEnvironmentVariable(PassphraseVariable);
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return fromEnv;
        }

        if (Console.IsInputRedirected)
        {
            throw new InvalidOperationException(
                $"当前环境无法交互输入口令，请设置环境变量 {PassphraseVariable} 后重试");
        }

        Console.Write("请输入私钥口令：");
        var buffer = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                break;
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (buffer.Length > 0)
                {
                    buffer.Length--;
                }

                continue;
            }

            buffer.Append(key.KeyChar);
        }

        return buffer.ToString();
    }

    private static string? GetOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static int GetIntOption(string[] args, string name, int fallback)
    {
        var text = GetOption(args, name);
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;
    }

    private static bool HasFlag(string[] args, string name) =>
        args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"[错误] 不认识的命令：{command}");
        PrintHelp();
        return 1;
    }

    private static int PrintHelpAndOk()
    {
        PrintHelp();
        return 0;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            fbauto-keygen —— FbAuto 离线授权码签发工具

            用法:
              fbauto-keygen initkey [--key 私钥路径] [--pub 公钥路径] [--force]
                  生成一对 P-256 密钥。私钥用口令加密保存，公钥写入 b64 文件。
                  口令从环境变量 FBAUTO_KEY_PASS 读取，未设置时交互输入。

              fbauto-keygen gen --count 50 --days 30 [--out 文件] [--issued 2026-11-12]
                  批量签发授权码。默认写到 ~/FbAutoSecrets/codes_<时间>.txt。

              fbauto-keygen verify "<授权码>"
                  用私钥对应公钥验一张码，并顺带演示改一位必须失败。

              fbauto-keygen emit [--pub 公钥路径]
                  输出要粘贴进 LicensePublicKey.cs 的代码行。

            注意:
              * 私钥一旦丢失，已发出的所有授权码无法续期，也无法再签新码。
              * 换新私钥会让旧码全部失效，客户端也要换公钥重新发布。
            """);
    }

    private static void TrySetUtf8Console()
    {
        try
        {
            Console.OutputEncoding = new UTF8Encoding(false);
        }
        catch (IOException)
        {
            // 输出被重定向到不支持设置编码的目标时忽略，不影响功能
        }
    }
}
