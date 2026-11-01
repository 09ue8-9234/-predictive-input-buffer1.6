using System.Text.Json;

namespace TestingApp.Models;

public sealed class SettingSpec
{
    public string Key { get; init; } = "";
    public char Kind { get; init; }
    public object Default { get; init; } = false;   // bool, double or string
    public double Min { get; init; }
    public double Max { get; init; }
    public string Info { get; init; } = "";
    public string Title { get; set; } = "";
    public string Help { get; set; } = "";
    public bool IsBind => Key.StartsWith("bind.", StringComparison.Ordinal);
    public bool IsBool => Kind == 'b';
    public bool IsInt => Kind == 'i';
    public bool IsNumber => Kind == 'i' || Kind == 'f';
    public bool IsText => Kind == 's';
}

public sealed record ActionSpec(string Name, string Info);
public sealed record StageSpec(int Stage, bool Enabled, int Ticks, int WaveTicks, bool Custom, int Slot);
public sealed record SlotSpec(int Slot, string Letter, int Stage, string TicksKey, string WaveKey, string NameKey);
public sealed record PresetSpec(string Name, int Stage, string Info, IReadOnlyDictionary<string, bool> Set);

// Everything the app knows about the mod's settings. It comes from the mod's own schema (schema.json written by the mod,
// or the copy generated from src/cfg.hpp at build time, or the schema message of a running game): no setting is listed in the app.
public sealed class SchemaDoc
{
    public List<SettingSpec> Settings { get; } = new();
    public Dictionary<string, SettingSpec> ByKey { get; } = new();
    public List<ActionSpec> Actions { get; } = new();
    public List<StageSpec> Stages { get; } = new();
    public List<SlotSpec> Slots { get; } = new();
    public List<PresetSpec> Presets { get; } = new();
    public HashSet<string> PerLevel { get; } = new();
    public int ConfigVersion { get; private set; }
    public int StageMax { get; private set; } = 11;
    public int CustomMaxTicks { get; private set; } = 128;
    public int CustomMaxWaveTicks { get; private set; } = 64;
    public string ModVersion { get; private set; } = "";
    public string Origin { get; private set; } = "";

    // changes when any setting, limit or default changes (used to compare with the running game's schema)
    public string Signature()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var s in Settings) sb.Append(s.Key).Append('|').Append(s.Kind).Append('|').Append(s.Default).Append('|').Append(s.Min).Append('|').Append(s.Max).Append(';');
        return sb.ToString();
    }

    static int Int(JsonElement e, string name, int fallback) => e.TryGetProperty(name, out var v) && v.TryGetInt32(out int i) ? i : fallback;

    public static SchemaDoc? Parse(string json, string origin, out string error)
    {
        error = "";
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object || !r.TryGetProperty("settings", out var settings)) { error = "no settings list"; return null; }
            var s = new SchemaDoc { Origin = origin };
            foreach (var e in settings.EnumerateArray())
            {
                char kind = e.GetProperty("kind").GetString()![0];
                string key = e.GetProperty("key").GetString()!;
                var def = e.GetProperty("default");
                object value = kind switch { 'b' => def.GetBoolean(), 's' => def.GetString() ?? "", _ => def.GetDouble() };
                var spec = new SettingSpec
                {
                    Key = key, Kind = kind, Default = value, Info = e.GetProperty("info").GetString() ?? "",
                    Min = kind is 'i' or 'f' ? e.GetProperty("min").GetDouble() : 0,
                    Max = kind is 'i' or 'f' ? e.GetProperty("max").GetDouble() : 0,
                };
                Split(spec);
                s.Settings.Add(spec);
                s.ByKey[key] = spec;
            }
            if (r.TryGetProperty("actions", out var actions))
                foreach (var a in actions.EnumerateArray()) s.Actions.Add(new ActionSpec(a.GetProperty("name").GetString()!, a.GetProperty("info").GetString() ?? ""));
            s.ConfigVersion = Int(r, "configVersion", 0);
            s.StageMax = Int(r, "stageMax", 11);
            s.CustomMaxTicks = Int(r, "customMaxTicks", 128);
            s.CustomMaxWaveTicks = Int(r, "customMaxWaveTicks", 64);
            if (r.TryGetProperty("modVersion", out var mv)) s.ModVersion = mv.GetString() ?? "";
            if (r.TryGetProperty("stages", out var stages))
                foreach (var e in stages.EnumerateArray())
                    s.Stages.Add(new StageSpec(Int(e, "stage", 0), e.GetProperty("enabled").GetBoolean(), Int(e, "ticks", 0), Int(e, "waveTicks", 0), e.GetProperty("custom").GetBoolean(), Int(e, "slot", -1)));
            if (r.TryGetProperty("customSlots", out var slots))
                foreach (var e in slots.EnumerateArray())
                    s.Slots.Add(new SlotSpec(Int(e, "slot", 0), e.GetProperty("letter").GetString() ?? "?", Int(e, "stage", 0),
                        e.GetProperty("ticksKey").GetString()!, e.GetProperty("waveKey").GetString()!, e.GetProperty("nameKey").GetString()!));
            if (r.TryGetProperty("presets", out var presets))
                foreach (var e in presets.EnumerateArray())
                {
                    var set = new Dictionary<string, bool>();
                    foreach (var p in e.GetProperty("set").EnumerateObject()) set[p.Name] = p.Value.GetBoolean();
                    s.Presets.Add(new PresetSpec(e.GetProperty("name").GetString()!, Int(e, "stage", 0), e.GetProperty("info").GetString() ?? "", set));
                }
            if (r.TryGetProperty("perLevel", out var pl))
                foreach (var e in pl.EnumerateArray()) s.PerLevel.Add(e.GetString()!);

            if (s.ConfigVersion == 0 || s.Stages.Count == 0 || s.Slots.Count == 0)
            {
                error = "this schema has no stage table (it is from a mod older than v2.87.3)";
                return null;
            }
            foreach (var need in new[] { "stage", "config-version" })
                if (!s.ByKey.ContainsKey(need)) { error = "the schema has no '" + need + "' setting"; return null; }
            foreach (var slot in s.Slots)
                foreach (var k in new[] { slot.TicksKey, slot.WaveKey, slot.NameKey })
                    if (!s.ByKey.ContainsKey(k)) { error = "the schema has no '" + k + "' setting"; return null; }
            return s;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }

    static readonly System.Text.RegularExpressions.Regex OnOff = new(@": (ON|OFF|On|Off)$", System.Text.RegularExpressions.RegexOptions.Compiled);
    static readonly System.Text.RegularExpressions.Regex Parenthetical = new(@"\s*\([^)]*\)$", System.Text.RegularExpressions.RegexOptions.Compiled);

    // The mod's text is "Title: description". Titles can contain ": " themselves ("Custom slot A: max delay (ticks): ..."),
    // so the title ends at the last ": " (within 52 characters, before the first sentence ends, not one that belongs to "ON:" / "Off:").
    // Texts that start with a sentence instead ("Path view (in-game drawing, off by default). Draws ...") use that sentence.
    static void Split(SettingSpec s)
    {
        string info = s.Info;
        var cands = new List<int>();
        for (int i = info.IndexOf(": ", StringComparison.Ordinal); i >= 0 && i <= 80; i = info.IndexOf(": ", i + 2, StringComparison.Ordinal))
        {
            if (info.IndexOf(". ", 0, i, StringComparison.Ordinal) >= 0) break;
            if (OnOff.IsMatch(info[..i])) continue;
            cands.Add(i);
        }
        int cut = cands.Where(c => c <= 52).DefaultIfEmpty(cands.Count > 0 ? cands[0] : -1).Last();
        int dot = info.IndexOf(". ", StringComparison.Ordinal);
        if (!s.IsBind && cut > 0)
        {
            s.Title = info[..cut];
            s.Help = info[(cut + 2)..];
        }
        else if (!s.IsBind && dot > 0 && dot <= 70)
        {
            s.Title = Parenthetical.Replace(info[..dot], "");
            s.Help = info[(dot + 2)..];
        }
        else
        {
            s.Title = Fmt.Humanize(s.Key);
            s.Help = info;
        }
    }
}
