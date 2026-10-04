using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using ServerManager.Core;
using static ServerManager.Core.UiText;

namespace ServerManager.App;

public enum ServerRunState { Checking, Stopped, Starting, Managed, External, Unknown }

public sealed class ServerRow(ServerProfile profile) : INotifyPropertyChanged
{
    public ServerProfile Profile { get; } = profile;
    public string Folder => Profile.Folder;
    public ServerRunState State { get; private set; } = ServerRunState.Checking;
    public int[] ProcessIds { get; private set; } = [];
    public bool IsRunning => State is ServerRunState.Starting or ServerRunState.Managed or ServerRunState.External;
    public string StatusLabel => T(State switch
    {
        ServerRunState.Stopped => "Остановлен", ServerRunState.Starting => "Запускается…",
        ServerRunState.Managed => "Запущен", ServerRunState.External => "Запущен вне менеджера",
        ServerRunState.Unknown => "Статус недоступен", _ => "Проверяю статус…"
    });
    public string StatusColor => State switch
    {
        ServerRunState.Starting => "#E9B46D", ServerRunState.Managed or ServerRunState.External => "#75D9AA",
        ServerRunState.Unknown => "#E9B46D", _ => "#747D8D"
    };
    public string StatusHint => StatusLabel + (ProcessIds.Length > 0 ? " · PID " + string.Join(", ", ProcessIds) : "")
        + (State == ServerRunState.External ? T("\nПроцесс запущен отдельно. Остановите его в исходном окне или менеджере.") : "")
        + (State == ServerRunState.Unknown ? T("\nНе удалось прочитать путь одного из процессов сервера.") : "");
    public event PropertyChangedEventHandler? PropertyChanged;
    public void SetStatus(ServerRunState state, int[] ids)
    {
        if (State == state && ProcessIds.SequenceEqual(ids)) return;
        State = state; ProcessIds = ids; RefreshLabels();
    }
    public void RefreshLabels() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
}

public sealed record ServerProcessSnapshot(IReadOnlyDictionary<string, int[]> ByFolder, bool HasUnreadablePaths)
{
    public static ServerProcessSnapshot Read()
    {
        var found = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        var unreadable = false;
        foreach (var process in Process.GetProcessesByName("AssettoServer"))
        {
            using (process)
            {
                try
                {
                    if (process.HasExited) continue;
                    var executable = process.MainModule?.FileName;
                    if (executable == null) { unreadable = true; continue; }
                    var folder = Path.GetDirectoryName(Path.GetFullPath(executable))!;
                    if (!found.TryGetValue(folder, out var ids)) found[folder] = ids = [];
                    ids.Add(process.Id);
                }
                catch (Win32Exception) { unreadable = true; }
                catch (InvalidOperationException) { /* Process exited during enumeration. */ }
            }
        }
        return new(found.ToDictionary(p => p.Key, p => p.Value.Order().ToArray(), StringComparer.OrdinalIgnoreCase), unreadable);
    }
}
