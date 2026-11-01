using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace TestingApp.Models;

public sealed record FolderChoice(string ModDir, string Label);

// Finds the mod's save folder (...\Geometry Dash\geode\config\abhyu.input-buffer) and remembers the choice.
public static class Locator
{
    public const string ModId = "abhyu.input-buffer";

    public static List<FolderChoice> Detect()
    {
        var roots = new List<string>();
        void AddRoot(string? r)
        {
            if (string.IsNullOrWhiteSpace(r)) return;
            try { r = System.IO.Path.GetFullPath(r); } catch { return; }
            if (!roots.Contains(r, StringComparer.OrdinalIgnoreCase) && Directory.Exists(r)) roots.Add(r);
        }
        try
        {
            AddRoot(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string);
            AddRoot(Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string);
        }
        catch { }
        AddRoot(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));

        // extra Steam libraries
        foreach (string root in roots.ToList())
        {
            string vdf = System.IO.Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) continue;
            try
            {
                foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                    AddRoot(m.Groups[1].Value.Replace("\\\\", "\\"));
            }
            catch { }
        }

        var found = new List<FolderChoice>();
        foreach (string root in roots)
        {
            string gd = System.IO.Path.Combine(root, "steamapps", "common", "Geometry Dash");
            string mod = System.IO.Path.Combine(gd, "geode", "config", ModId);
            if (Directory.Exists(mod)) found.Add(new FolderChoice(mod, "Steam: " + gd));
        }
        return found;
    }

    // the user may pick the game folder, geode, geode\config or the mod folder itself
    public static string? Normalize(string picked)
    {
        if (!Directory.Exists(picked)) return null;
        string trimmed = picked.TrimEnd('\\', '/');
        if (string.Equals(System.IO.Path.GetFileName(trimmed), ModId, StringComparison.OrdinalIgnoreCase)) return picked;
        foreach (string rel in new[] { System.IO.Path.Combine("geode", "config", ModId), System.IO.Path.Combine("config", ModId), ModId })
        {
            string c = System.IO.Path.Combine(picked, rel);
            if (Directory.Exists(c)) return c;
        }
        return null;
    }
}

public sealed class AppSettings
{
    public string Folder { get; set; } = "";

    static string FilePath => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Testing", "app.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath)) return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
        }
        catch { }
    }
}

public static class GameWatcher
{
    public static bool IsGameRunning()
    {
        try
        {
            var list = Process.GetProcessesByName("GeometryDash");
            bool any = list.Length > 0;
            foreach (var p in list) p.Dispose();
            return any;
        }
        catch { return false; }
    }

    // The pipe exists while the mod's console link is on. It is looked up by listing the pipe folder,
    // never by connecting (the mod has ONE pipe instance; a probe would steal it).
    public static bool PipeExists()
    {
        try
        {
            foreach (string path in Directory.EnumerateFiles(@"\\.\pipe\"))
                if (string.Equals(System.IO.Path.GetFileName(path), PipeClient.PipeName, StringComparison.OrdinalIgnoreCase)) return true;
        }
        catch { }
        return false;
    }
}
