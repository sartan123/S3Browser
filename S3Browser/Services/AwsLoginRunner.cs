using System.ComponentModel;
using System.Diagnostics;

namespace S3Browser.Services;

/// <summary>
/// `aws login` コマンドを起動して一時認証情報をキャッシュに書き込ませる。
/// CLI はブラウザ経由で対話的にサインインを求めるため、コンソールウィンドウを表示する。
/// </summary>
public static class AwsLoginRunner
{
    public static async Task<int> RunAsync(string? profile = null, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "aws",
            UseShellExecute = true,
            CreateNoWindow = false,
            WindowStyle = ProcessWindowStyle.Normal,
        };
        psi.ArgumentList.Add("login");
        if (!string.IsNullOrWhiteSpace(profile))
        {
            psi.ArgumentList.Add("--profile");
            psi.ArgumentList.Add(profile);
        }

        Process? proc;
        try
        {
            proc = Process.Start(psi);
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException(
                "aws CLI を起動できませんでした。AWS CLI v2 がインストールされ、PATH に存在することを確認してください。", ex);
        }

        if (proc is null)
            throw new InvalidOperationException("aws プロセスを開始できませんでした。");

        try
        {
            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
            return proc.ExitCode;
        }
        finally
        {
            proc.Dispose();
        }
    }
}
