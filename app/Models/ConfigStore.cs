using System.Text;
using System.Text.Json;

namespace TestingApp.Models;

public enum Sk { None, Bool, Num, Str }

public sealed class Scalar
{
    public Sk Kind { get; init; }
    public bool B { get; init; }
    public double N { get; init; }
    public string S { get; init; } = "";

    public static Scalar Of(object v) => v switch
    {
        bool b => new Scalar { Kind = Sk.Bool, B = b },
        double d => new Scalar { Kind = Sk.Num, N = d },
        int i => new Scalar { Kind = Sk.Num, N = i },
        string s => new Scalar { Kind = Sk.Str, S = s },
        _ => new Scalar { Kind = Sk.None },
    };

    public object? ToObject() => Kind switch { Sk.Bool => (object)B, Sk.Num => (object)N, Sk.Str => (object)S, _ => null };

    public string ToJson() => Kind switch
    {
        Sk.Bool => B ? "true" : "false",
        Sk.Num => Fmt.Json(N),
        Sk.Str => JsonSerializer.Serialize(S),
        _ => "null",
    };
}

// config.json of the mod: a flat JSON object, one setting per line, "config-version" first.
// The store keeps what is in the file (including keys it does not know), plus the changes the user made since the last save.
// Saving re-reads the file first, applies only those changes, writes a temp file and swaps it in, so nothing else is lost.
public sealed class ConfigStore
{
    readonly SchemaDoc _schema;
    List<(string Key, Scalar Val)> _entries = new();
    readonly Dictionary<string, Scalar> _overlay = new();
    bool _backedUp;

    public string Dir { get; }
    public string FilePath => System.IO.Path.Combine(Dir, "config.json");
    public bool FileExists { get; private set; }
    public string Problem { get; private set; } = "";
    public int FileVersion { get; private set; } = 1;
    public bool Dirty => _overlay.Count > 0;
    public bool Editable => Problem.Length == 0 && (!FileExists || FileVersion == _schema.ConfigVersion);

    public string ReadOnlyReason =>
        Problem.Length > 0
            ? "config.json can't be read (" + Problem + "). Start the game once: the mod keeps a copy as config.corrupt.json and writes a fresh file."
            : "config.json is version " + FileVersion + " but this app and the mod expect version " + _schema.ConfigVersion +
              ". Start the game once with the current mod so it upgrades the file.";

    public ConfigStore(SchemaDoc schema, string dir)
    {
        _schema = schema;
        Dir = dir;
    }

    public void Load()
    {
        _overlay.Clear();
        _entries = ReadEntries(FilePath, out string problem, out bool exists, out int version);
        Problem = problem;
        FileExists = exists;
        FileVersion = version;
    }

    static List<(string Key, Scalar Val)> ReadEntries(string path, out string problem, out bool exists, out int version)
    {
        var list = new List<(string Key, Scalar Val)>();
        problem = "";
        version = 1;
        exists = File.Exists(path);
        if (!exists) return list;
        try
        {
            string text = File.ReadAllText(path, Encoding.UTF8);
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                problem = "not a JSON object";
                return list;
            }
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                Scalar s;
                switch (p.Value.ValueKind)
                {
                    case JsonValueKind.True: s = Scalar.Of(true); break;
                    case JsonValueKind.False: s = Scalar.Of(false); break;
                    case JsonValueKind.Number: s = Scalar.Of(p.Value.GetDouble()); break;
                    case JsonValueKind.String: s = Scalar.Of(p.Value.GetString() ?? ""); break;
                    case JsonValueKind.Null: s = new Scalar { Kind = Sk.None }; break;
                    default:
                        problem = "nested value in \"" + p.Name + "\"";
                        return new List<(string Key, Scalar Val)>();
                }
                list.Add((p.Name, s));
                if (p.Name == "config-version" && s.Kind == Sk.Num && s.N >= 1 && s.N <= 999) version = (int)s.N;
            }
        }
        catch (Exception ex)
        {
            problem = ex.Message;
            list.Clear();
        }
        return list;
    }

    // value as the mod would see it: pending change, else the file (if valid), else the default
    public object Get(string key)
    {
        var spec = _schema.ByKey[key];
        if (_overlay.TryGetValue(key, out var o)) return o.ToObject() ?? spec.Default;
        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            if (_entries[i].Key != key) continue;
            return FromFile(spec, _entries[i].Val) ?? spec.Default;
        }
        return spec.Default;
    }

    static object? FromFile(SettingSpec sp, Scalar s)
    {
        switch (sp.Kind)
        {
            case 'b':
                return s.Kind == Sk.Bool ? (object)s.B : null;
            case 'i':
            case 'f':
            {
                if (s.Kind != Sk.Num) return null;
                double v = s.N;
                if (sp.IsInt)
                {
                    if (Math.Abs(v - Math.Round(v)) > 1e-9) return null;
                    v = Math.Round(v);
                }
                return v < sp.Min || v > sp.Max ? null : (object)v;
            }
            default:
            {
                if (s.Kind != Sk.Str) return null;
                if (sp.IsBind) return s.S.Length == 0 ? "" : KeyNames.TryNormalize(s.S, out var canon) ? canon : null;
                string clean = Rules.CleanName(s.S);
                return clean.Length == 0 ? null : clean;
            }
        }
    }

    // value must already be validated (Rules.Validate)
    public void Set(string key, object value) => _overlay[key] = Scalar.Of(value);

    // scope: "settings" (everything but key binds, including the stage), "binds", "all"
    public void ResetScope(string scope)
    {
        foreach (var sp in _schema.Settings)
        {
            if (sp.Key == "config-version") continue;
            bool hit = scope == "all" || (scope == "binds" && sp.IsBind) || (scope == "settings" && !sp.IsBind);
            if (hit) _overlay[sp.Key] = Scalar.Of(sp.Default);
        }
    }

    static string Serialize(List<(string Key, Scalar Val)> list)
    {
        var sb = new StringBuilder("{");
        for (int i = 0; i < list.Count; i++)
        {
            if (i > 0) sb.Append(",\n ");
            sb.Append(JsonSerializer.Serialize(list[i].Key)).Append(':').Append(list[i].Val.ToJson());
        }
        return sb.Append("}\n").ToString();
    }

    public bool Save(out string error)
    {
        error = "";
        if (_overlay.Count == 0) return true;
        try
        {
            Directory.CreateDirectory(Dir);
            var fresh = ReadEntries(FilePath, out string problem, out bool exists, out int version);
            if (exists && problem.Length > 0) { error = "config.json is unreadable right now (" + problem + ")"; return false; }
            if (exists && version != _schema.ConfigVersion) { error = "config.json changed to version " + version + " (start the game once, then reopen the app)"; return false; }
            if (exists && !_backedUp)
            {
                BackupNow();
                _backedUp = true;
            }
            foreach (var kv in _overlay)
            {
                int idx = fresh.FindIndex(e => e.Key == kv.Key);
                if (idx >= 0) fresh[idx] = (kv.Key, kv.Value);
                else fresh.Add((kv.Key, kv.Value));
            }
            if (!exists) fresh.Insert(0, ("config-version", Scalar.Of((double)_schema.ConfigVersion)));
            string tmp = FilePath + ".app-tmp";
            File.WriteAllText(tmp, Serialize(fresh), new UTF8Encoding(false));
            if (exists) File.Replace(tmp, FilePath, null);
            else File.Move(tmp, FilePath);
            _entries = fresh;
            FileExists = true;
            FileVersion = _schema.ConfigVersion;
            Problem = "";
            _overlay.Clear();
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    // a copy of config.json before the app's first save in this session (the mod's own config.bak.json is overwritten at every game start)
    void BackupNow()
    {
        if (!File.Exists(FilePath)) return;
        string name = "config.app-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json";
        File.Copy(FilePath, System.IO.Path.Combine(Dir, name), true);
        var old = new DirectoryInfo(Dir).GetFiles("config.app-backup-*.json").OrderByDescending(f => f.Name).Skip(12);
        foreach (var f in old)
        {
            try { f.Delete(); } catch { }
        }
    }

    public List<FileInfo> ListBackups()
    {
        var list = new List<FileInfo>();
        if (!Directory.Exists(Dir)) return list;
        var di = new DirectoryInfo(Dir);
        foreach (string pattern in new[] { "config.app-backup-*.json", "config.bak.json", "config.v*.json", "config.corrupt*.json" })
            list.AddRange(di.GetFiles(pattern));
        return list.OrderByDescending(f => f.LastWriteTime).ToList();
    }

    public bool Restore(string path, out string error)
    {
        error = "";
        try
        {
            ReadEntries(path, out string problem, out _, out _);
            if (problem.Length > 0) { error = "that file isn't a readable config (" + problem + ")"; return false; }
            if (File.Exists(FilePath)) BackupNow();
            string tmp = FilePath + ".app-tmp";
            File.Copy(path, tmp, true);
            if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
            else File.Move(tmp, FilePath);
            Load();
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
