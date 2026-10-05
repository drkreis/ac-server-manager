using static ServerManager.Core.UiText;

namespace ServerManager.Core;

public static class ServerRemoval
{
    // Validate before the UI asks Windows to move the selected directory to the Recycle Bin.
    public static string Validate(string serversRoot, string serverPath, string gamePath)
    {
        if (string.IsNullOrWhiteSpace(serversRoot)) throw new InvalidOperationException(T("Сначала выберите папку серверов."));
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(serversRoot));
        var target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(serverPath));
        if (target.StartsWith(@"\\", StringComparison.Ordinal) || new DriveInfo(Path.GetPathRoot(target)!).DriveType == DriveType.Network)
            throw new InvalidOperationException(T("Удаление в корзину доступно только для локальных серверов. Сетевые папки удаляйте вручную."));
        if (target.Equals(Path.GetPathRoot(target), StringComparison.OrdinalIgnoreCase)
            || (!target.Equals(root, StringComparison.OrdinalIgnoreCase) && !target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(T("Можно удалить только выбранный сервер в папке серверов."));
        if (!Directory.Exists(target) || !File.Exists(Path.Combine(target, "cfg", "server_cfg.ini"))
            || !File.Exists(Path.Combine(target, "cfg", "entry_list.ini")) || !File.Exists(Path.Combine(target, "cfg", "extra_cfg.yml")))
            throw new InvalidOperationException(T("Папка не содержит полный конфиг сервера."));
        if (!string.IsNullOrWhiteSpace(gamePath))
        {
            var game = Path.TrimEndingDirectorySeparator(Path.GetFullPath(gamePath));
            if (target.Equals(game, StringComparison.OrdinalIgnoreCase) || target.StartsWith(game + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || game.StartsWith(target + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(T("Нельзя удалить папку игры или сервер внутри неё."));
        }
        // A linked directory could resolve outside the user's selected location.
        for (var dir = new DirectoryInfo(target); dir != null; dir = dir.Parent)
            if (dir.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new InvalidOperationException(T("Удаление серверов через ссылки и junction-папки не поддерживается."));
        var pending = new Stack<string>(); pending.Push(target);
        while (pending.TryPop(out var folder))
            foreach (var child in Directory.EnumerateDirectories(folder))
            {
                if (File.GetAttributes(child).HasFlag(FileAttributes.ReparsePoint)) throw new InvalidOperationException(T("Удаление серверов через ссылки и junction-папки не поддерживается."));
                if (File.Exists(Path.Combine(child, "cfg", "server_cfg.ini")) || File.Exists(Path.Combine(child, "AssettoServer.exe")))
                    throw new InvalidOperationException(T("Внутри выбранной папки есть другой сервер. Удаляйте серверы по отдельности."));
                pending.Push(child);
            }
        return target;
    }
}
