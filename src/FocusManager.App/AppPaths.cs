using System.IO;

namespace FocusManager.App;

/// <summary>
/// 决定「用户数据放在哪里」。
///
/// 正常情况下应该放在 %LOCALAPPDATA%\ProjectFocusManager\data，
/// 但有些电脑（包括开发这台）对用户目录做了权限限制，连新建文件夹都不允许。
/// 所以这里按顺序试几个位置，用第一个真正能写的。
/// 注意比安装目录多一层 data 子目录：Velopack 安装器默认占用
/// %LOCALAPPDATA%\ProjectFocusManager 本身（放 Update.exe 和 current\），
/// 数据库不能和它混在同级。
/// </summary>
public static class AppPaths
{
    public const string AppFolderName = "ProjectFocusManager";

    /// <summary>正式数据目录在应用目录下再隔一层，避免与 Velopack 安装器文件混放。</summary>
    public const string DataSubfolderName = "data";

    private const string ProbeFileName = ".write-test";

    /// <summary>
    /// 依次尝试标准位置，返回第一个可写的目录。全部失败时返回 null。
    /// </summary>
    /// <param name="tried">已经尝试过的路径，失败时用于告诉用户具体情况。</param>
    public static string? ResolveDataDirectory(out IReadOnlyList<string> tried)
    {
        var candidates = new List<string>();

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            candidates.Add(Path.Combine(localAppData, AppFolderName, DataSubfolderName));
        }

        var roamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrWhiteSpace(roamingAppData))
        {
            candidates.Add(Path.Combine(roamingAppData, AppFolderName, DataSubfolderName));
        }

        // 最后退到程序自己所在的目录（便携版思路）。
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "data"));

        foreach (var candidate in candidates)
        {
            if (TryPrepare(candidate))
            {
                tried = candidates;
                return candidate;
            }
        }

        tried = candidates;
        return null;
    }

    /// <summary>试着建目录并真的写一个文件进去，确认不是「看着能写其实不能」。</summary>
    public static bool TryPrepare(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);

            var probe = Path.Combine(directory, ProbeFileName);
            File.WriteAllText(probe, "Project Focus Manager");

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
