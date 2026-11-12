namespace FbAuto.Keygen;

/// <summary>程序入口。逻辑都在 <see cref="Cli"/>，这里只做转发，方便测试直接调用。</summary>
public static class Program
{
    public static int Main(string[] args) => Cli.Run(args);
}
