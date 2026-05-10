using System.Net.Http;
using System.Windows;
using NotionPad.Services;

namespace NotionPad;

public partial class App : Application
{
    public static LocalStore Store { get; private set; } = null!;
    public static NotionClient Notion { get; private set; } = null!;
    public static SyncQueue Sync { get; private set; } = null!;
    public static HttpClient Http { get; } = new HttpClient();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Store = new LocalStore();
        Store.Load();
        Notion = new NotionClient(Http, () => Store.Data.Settings);
        Sync = new SyncQueue(Store, Notion);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Store.FlushNow();
        base.OnExit(e);
    }
}
