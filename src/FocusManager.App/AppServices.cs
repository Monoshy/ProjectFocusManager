using System.IO;
using FocusManager.Core.Data;
using FocusManager.Core.Services;

namespace FocusManager.App;

/// <summary>
/// 程序里所有「服务」的集中入口。
///
/// 这里没有引入依赖注入框架：只有一个程序，几个服务，
/// 手写一个简单的容器比引入一整套框架更容易看懂、也更容易维护。
/// </summary>
public sealed class AppServices
{
    private AppServices(string dataDirectory, DatabaseService database)
    {
        DataDirectory = dataDirectory;
        Database = database;
        Projects = new ProjectService(database);
        Sessions = new SessionService(database);
        Statistics = new StatisticsService(database);
        Applications = new ApplicationService(database);
        ProjectApplications = new ProjectApplicationService(database);
        Settings = new SettingsService(database);
        Ideas = new IdeaNoteService(database);
    }

    public string DataDirectory { get; }

    public DatabaseService Database { get; }

    public ProjectService Projects { get; }

    public SessionService Sessions { get; }

    public StatisticsService Statistics { get; }

    public ApplicationService Applications { get; }

    /// <summary>项目 ↔ 辅助程序的挂载关系。</summary>
    public ProjectApplicationService ProjectApplications { get; }

    public SettingsService Settings { get; }

    /// <summary>随手记。</summary>
    public IdeaNoteService Ideas { get; }

    public static AppServices? Current { get; private set; }

    /// <summary>启动时调用一次：建库、建表、补齐预置应用。</summary>
    public static AppServices Initialize(string dataDirectory)
    {
        var databasePath = Path.Combine(dataDirectory, "focus.db");
        var backupDirectory = Path.Combine(dataDirectory, "backups");

        var database = new DatabaseService(databasePath, backupDirectory);
        database.Initialize();

        var services = new AppServices(dataDirectory, database);
        services.Applications.EnsureDefaults();

        Current = services;
        return services;
    }
}
