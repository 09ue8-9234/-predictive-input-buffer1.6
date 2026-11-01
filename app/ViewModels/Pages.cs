using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows.Input;
using TestingApp.Models;

namespace TestingApp.ViewModels;

public sealed class NavItem
{
    public string Name { get; init; } = "";
    public object Page { get; init; } = null!;
}

public sealed class SettingsPage
{
    public string Title { get; init; } = "";
    public string Blurb { get; init; } = "";
    public List<SettingVM> Items { get; init; } = new();
    public bool IsEmpty => Items.Count == 0;
}

// ---------------------------------------------------------------- frame buffer

public sealed class PresetVM
{
    public string Name { get; init; } = "";
    public string Label => Fmt.Humanize(Name);
    public string Info { get; init; } = "";
    public ICommand Command { get; init; } = null!;
}

public sealed class SlotVM : Obs
{
    public SlotSpec Spec { get; }
    public SettingVM Ticks { get; }
    public SettingVM Wave { get; }
    public SettingVM Name { get; }
    public ICommand UseCommand { get; }

    public SlotVM(SlotSpec spec, SettingVM ticks, SettingVM wave, SettingVM name, Action<int> use)
    {
        Spec = spec;
        Ticks = ticks;
        Wave = wave;
        Name = name;
        UseCommand = new RelayCommand(_ => use(spec.Stage));
        ticks.PropertyChanged += (_, _) => Raise(nameof(TicksLabel));
        wave.PropertyChanged += (_, _) => Raise(nameof(WaveLabel));
    }

    public string Letter => Spec.Letter;
    public string TicksLabel => Fmt.Frames(Ticks.NumValue) + "  ·  " + Fmt.Num(Ticks.NumValue) + " ticks";
    public string WaveLabel => Wave.NumValue <= 0 ? "wave: off" : "wave " + Fmt.Frames(Wave.NumValue) + "  ·  " + Fmt.Num(Wave.NumValue) + " ticks";
}

public sealed class BufferPage : Obs
{
    readonly MainVM _host;
    int _lastOnStage;

    public SchemaDoc Schema { get; }
    public SettingVM StageVM { get; }
    public List<SlotVM> Slots { get; } = new();
    public List<PresetVM> Presets { get; } = new();
    public int Version { get; private set; }   // changes whenever the ruler must be redrawn

    public BufferPage(MainVM host, SchemaDoc schema, Func<string, SettingVM> vm)
    {
        _host = host;
        Schema = schema;
        StageVM = vm("stage");
        _lastOnStage = (int)(schema.ByKey["stage"].Default is double d ? d : 1);
        if (_lastOnStage >= schema.StageMax) _lastOnStage = 1;
        foreach (var s in schema.Slots)
        {
            var slot = new SlotVM(s, vm(s.TicksKey), vm(s.WaveKey), vm(s.NameKey), SelectStage);
            slot.Ticks.PropertyChanged += (_, _) => Touch();
            Slots.Add(slot);
        }
        foreach (var p in schema.Presets)
        {
            var preset = p;
            Presets.Add(new PresetVM { Name = p.Name, Info = p.Info, Command = new RelayCommand(_ => host.ApplyPreset(preset), _ => host.CanEdit) });
        }
        StageVM.PropertyChanged += (_, _) => Touch();
    }

    void Touch()
    {
        Version++;
        Raise(nameof(Version));
        Raise(nameof(Stage));
        Raise(nameof(BufferOn));
        Raise(nameof(Headline));
        Raise(nameof(Detail));
    }

    public bool CanEdit => _host.CanEdit;
    public int Stage => (int)StageVM.NumValue;
    public bool IsOff => Stage >= Schema.StageMax;

    public bool BufferOn
    {
        get => !IsOff;
        set
        {
            if (value == !IsOff) return;
            if (!value)
            {
                _lastOnStage = Stage;
                SelectStage(Schema.StageMax);
            }
            else SelectStage(_lastOnStage);
        }
    }

    public void SelectStage(int stage) => StageVM.NumValue = Math.Clamp(stage, 0, Schema.StageMax);

    public StageSpec? SpecOf(int stage) => Schema.Stages.FirstOrDefault(s => s.Stage == stage);
    public SlotVM? SlotOfStage(int stage) => Slots.FirstOrDefault(s => s.Spec.Stage == stage);

    public double TicksOf(int stage)
    {
        var sp = SpecOf(stage);
        if (sp == null || !sp.Enabled) return 0;
        return sp.Custom ? (SlotOfStage(stage)?.Ticks.NumValue ?? 0) : sp.Ticks;
    }

    public double WaveOf(int stage)
    {
        var sp = SpecOf(stage);
        if (sp == null || !sp.Enabled) return 0;
        return sp.Custom ? (SlotOfStage(stage)?.Wave.NumValue ?? 0) : sp.WaveTicks;
    }

    public void SetSlotTicks(SlotVM slot, int ticks) => slot.Ticks.NumValue = Math.Clamp(ticks, 1, Schema.CustomMaxTicks);

    public string Headline
    {
        get
        {
            if (IsOff) return "Buffer off";
            var slot = SlotOfStage(Stage);
            string name = slot != null ? "Custom " + slot.Letter + (slot.Name.TextValue.Length > 0 ? " (" + slot.Name.TextValue + ")" : "") : "";
            return Fmt.Frames(TicksOf(Stage)) + (name.Length > 0 ? "  ·  " + name : "");
        }
    }

    public string Detail
    {
        get
        {
            if (IsOff) return "Inputs go to the game unchanged.";
            double t = TicksOf(Stage), w = WaveOf(Stage);
            return Fmt.Num(t) + " ticks of delay at most" + (w > 0 && Math.Abs(w - t) > 0.01 ? "  ·  wave " + Fmt.Frames(w) + " (" + Fmt.Num(w) + " ticks)" : "") +
                   "  ·  1 frame = 4 ticks";
        }
    }
}

// ---------------------------------------------------------------- keys and actions

public sealed class KeyRowVM : Obs
{
    readonly KeysPage _page;
    public SettingVM Bind { get; }
    public string Title { get; init; } = "";
    public string Info { get; init; } = "";
    public ICommand CaptureCommand { get; }
    public ICommand ClearCommand { get; }
    bool _capturing;

    public KeyRowVM(KeysPage page, SettingVM bind)
    {
        _page = page;
        Bind = bind;
        CaptureCommand = new RelayCommand(_ => page.StartCapture(this), _ => bind.CanEdit);
        ClearCommand = new RelayCommand(_ => bind.TextValue = "", _ => bind.CanEdit && bind.TextValue.Length > 0);
        bind.PropertyChanged += (_, _) => { Raise(nameof(DisplayText)); Raise(nameof(HasKey)); Raise(nameof(CanEdit)); };
    }

    public bool HasKey => Bind.TextValue.Length > 0;
    public bool IsCapturing { get => _capturing; set { if (Set(ref _capturing, value)) Raise(nameof(DisplayText)); } }
    public string DisplayText => IsCapturing ? "Press a key…" : HasKey ? Bind.TextValue : "not set";
    public bool CanEdit => Bind.CanEdit;
}

public sealed class KeysPage : Obs
{
    readonly MainVM _host;
    KeyRowVM? _capturing;

    public List<KeyRowVM> Actions { get; } = new();
    public List<KeyRowVM> Toggles { get; } = new();

    public KeysPage(MainVM host, SchemaDoc schema, Func<string, SettingVM?> vm)
    {
        _host = host;
        var actionNames = schema.Actions.ToDictionary(a => a.Name, a => a.Info);
        foreach (var sp in schema.Settings.Where(s => s.IsBind))
        {
            var bind = vm(sp.Key);
            if (bind == null) continue;
            string target = sp.Key["bind.".Length..];
            if (target.StartsWith("toggle:", StringComparison.Ordinal))
            {
                string setting = target["toggle:".Length..];
                string title = schema.ByKey.TryGetValue(setting, out var ts) ? ts.Title : Fmt.Humanize(setting);
                Toggles.Add(new KeyRowVM(this, bind) { Title = title, Info = "Switches “" + title + "” on or off" });
            }
            else
            {
                string info = actionNames.TryGetValue(target, out var ai) ? ai : sp.Info;
                Actions.Add(new KeyRowVM(this, bind) { Title = Fmt.Humanize(target), Info = info });
            }
        }
    }

    public void StartCapture(KeyRowVM row)
    {
        if (_capturing != null) _capturing.IsCapturing = false;
        _capturing = row;
        row.IsCapturing = true;
        _host.ShowToast("Press the new key (Esc cancels, Backspace clears)");
    }

    public bool IsCapturing => _capturing != null;

    // returns true if the key press was consumed
    public bool OnKey(System.Windows.Input.KeyEventArgs e)
    {
        if (_capturing == null) return false;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (KeyNames.IsModifierKey(key)) return true;
        var row = _capturing;
        var mods = Keyboard.Modifiers;
        if (mods == ModifierKeys.None && key == Key.Escape)
        {
            Stop();
            return true;
        }
        if (mods == ModifierKeys.None && (key == Key.Back || key == Key.Delete))
        {
            Stop();
            row.Bind.TextValue = "";
            return true;
        }
        string? text = KeyNames.FromWpf(key, mods);
        Stop();
        if (text == null) _host.ShowToast("That key can't be used for a binding.");
        else row.Bind.TextValue = text;
        return true;
    }

    void Stop()
    {
        if (_capturing != null) _capturing.IsCapturing = false;
        _capturing = null;
    }
}

public sealed class ActionRowVM
{
    public string Title { get; init; } = "";
    public string Info { get; init; } = "";
    public string KeyText { get; init; } = "";
    public ICommand RunCommand { get; init; } = null!;
}

public sealed class ActionsPage
{
    public MainVM Host { get; init; } = null!;
    public List<ActionRowVM> Rows { get; init; } = new();
}

// ---------------------------------------------------------------- live view

public sealed class StatRow : Obs
{
    string _value = "";
    public string Label { get; init; } = "";
    public string Value { get => _value; set => Set(ref _value, value); }
}

public sealed class LivePage : Obs
{
    readonly Dictionary<string, StatRow> _rows = new();
    public ObservableCollection<StatRow> Rows { get; } = new();
    public ObservableCollection<string> Log { get; } = new();
    public MainVM Host { get; }

    public LivePage(MainVM host) { Host = host; }

    public void Clear()
    {
        Rows.Clear();
        _rows.Clear();
        Log.Clear();
    }

    public void Update(JsonElement state)
    {
        foreach (var p in state.EnumerateObject())
        {
            if (p.Name == "type") continue;
            string text = p.Value.ValueKind switch
            {
                JsonValueKind.String => p.Value.GetString() ?? "",
                JsonValueKind.True => "yes",
                JsonValueKind.False => "no",
                JsonValueKind.Number => Fmt.Num(p.Value.GetDouble()),
                _ => p.Value.ToString(),
            };
            if (text.Length == 0) text = "—";
            if (_rows.TryGetValue(p.Name, out var row)) row.Value = text;
            else
            {
                row = new StatRow { Label = Fmt.HumanizeCamel(p.Name), Value = text };
                _rows[p.Name] = row;
                Rows.Add(row);
            }
        }
    }

    public void AddLog(long tick, string msg)
    {
        Log.Add("[" + tick + "] " + msg);
        while (Log.Count > 500) Log.RemoveAt(0);
    }
}

// ---------------------------------------------------------------- backups and reset

public sealed class BackupVM
{
    public string Name { get; init; } = "";
    public string Detail { get; init; } = "";
    public ICommand RestoreCommand { get; init; } = null!;
}

public sealed class BackupsPage : Obs
{
    public MainVM Host { get; }
    public ObservableCollection<BackupVM> Backups { get; } = new();
    string _folder = "";
    string _fileInfo = "";

    public BackupsPage(MainVM host) { Host = host; }

    public string Folder { get => _folder; set => Set(ref _folder, value); }
    public string FileInfo { get => _fileInfo; set => Set(ref _fileInfo, value); }
    public ICommand OpenFolderCommand => Host.OpenFolderCommand;
    public ICommand ChangeFolderCommand => Host.ChangeFolderCommand;
    public ICommand ResetSettingsCommand => Host.ResetSettingsCommand;
    public ICommand ResetBindsCommand => Host.ResetBindsCommand;
    public ICommand ResetAllCommand => Host.ResetAllCommand;
}
