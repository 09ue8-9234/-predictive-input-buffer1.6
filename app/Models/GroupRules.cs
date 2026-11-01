using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TestingApp.Models;

public sealed class GroupRule
{
    public string Name { get; init; } = "";
    public string Blurb { get; init; } = "";
    public int Sort { get; init; }
    public List<Regex> Match { get; init; } = new();
}

// Assets/groups.json: key patterns that decide the page a setting lands on. Rules, not a list of settings.
public sealed class GroupRules
{
    public List<GroupRule> Groups { get; } = new();
    public GroupRule Fallback { get; private set; } = new() { Name = "More settings", Sort = 60 };
    public List<Regex> Hidden { get; } = new();
    public List<Regex> Buffer { get; } = new();

    static List<Regex> Compile(JsonElement arr) => arr.EnumerateArray().Select(e => new Regex(e.GetString()!, RegexOptions.Compiled)).ToList();

    public static GroupRules Load()
    {
        var rules = new GroupRules();
        rules.Hidden.Add(new Regex("^config-version$"));
        rules.Buffer.Add(new Regex("^stage$"));
        rules.Buffer.Add(new Regex("^custom-"));
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("groups.json");
            if (stream == null) return rules;
            using var doc = JsonDocument.Parse(new StreamReader(stream).ReadToEnd());
            var r = doc.RootElement;
            foreach (var g in r.GetProperty("groups").EnumerateArray())
                rules.Groups.Add(new GroupRule
                {
                    Name = g.GetProperty("name").GetString()!,
                    Blurb = g.TryGetProperty("blurb", out var b) ? b.GetString() ?? "" : "",
                    Sort = g.TryGetProperty("sort", out var so) ? so.GetInt32() : 50,
                    Match = Compile(g.GetProperty("match")),
                });
            if (r.TryGetProperty("fallback", out var f))
                rules.Fallback = new GroupRule { Name = f.GetProperty("name").GetString()!, Blurb = f.TryGetProperty("blurb", out var fb) ? fb.GetString() ?? "" : "", Sort = f.TryGetProperty("sort", out var fs) ? fs.GetInt32() : 60 };
            if (r.TryGetProperty("hidden", out var h)) { rules.Hidden.Clear(); rules.Hidden.AddRange(Compile(h)); }
            if (r.TryGetProperty("buffer", out var bf)) { rules.Buffer.Clear(); rules.Buffer.AddRange(Compile(bf)); }
        }
        catch { }
        return rules;
    }

    public bool IsHidden(string key) => Hidden.Any(r => r.IsMatch(key));
    public bool IsBuffer(string key) => Buffer.Any(r => r.IsMatch(key));
    public GroupRule Classify(string key) => Groups.FirstOrDefault(g => g.Match.Any(r => r.IsMatch(key))) ?? Fallback;
}
