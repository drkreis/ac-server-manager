using System.Text.RegularExpressions;
using static ServerManager.Core.UiText;

namespace ServerManager.Core;

public static class ServerRename
{
    public static string Validate(string serversRoot, string source, string name, string gamePath)
    {
        var path = ServerRemoval.Validate(serversRoot, source, gamePath);
        if (string.IsNullOrWhiteSpace(name) || name != name.Trim() || name.EndsWith('.') || name.Length > 120
            || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name is "." or ".."
            || Regex.IsMatch(name, @"^(CON|PRN|AUX|NUL|COM[1-9¹²³]|LPT[1-9¹²³])(?:\.|$)", RegexOptions.IgnoreCase))
            throw new InvalidOperationException(T("Введите допустимое имя папки без слешей, точек и пробелов в конце."));
        var destination = Path.Combine(Path.GetDirectoryName(path)!, name);
        if (name == Path.GetFileName(path)) return path;
        if (destination.Equals(path, StringComparison.OrdinalIgnoreCase)) return destination;
        if (Directory.Exists(destination) || File.Exists(destination)) throw new InvalidOperationException(T("Папка или файл с таким именем уже существует."));
        return destination;
    }
    public static string Move(string serversRoot, string source, string name, string gamePath)
    {
        var destination = Validate(serversRoot, source, name, gamePath);
        var origin = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
        if (destination == origin) return destination;
        if (destination.Equals(origin, StringComparison.OrdinalIgnoreCase))
        {
            var temporary = Path.Combine(Path.GetDirectoryName(origin)!, ".acsm-rename-" + Guid.NewGuid().ToString("N"));
            if (Directory.Exists(temporary) || File.Exists(temporary)) throw new IOException("Temporary rename destination already exists.");
            Directory.Move(origin, temporary);
            try { Directory.Move(temporary, destination); }
            catch { Directory.Move(temporary, origin); throw; }
        }
        else Directory.Move(origin, destination);
        return destination;
    }
}
