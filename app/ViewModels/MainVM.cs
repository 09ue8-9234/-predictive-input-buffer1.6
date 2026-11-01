using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using TestingApp.Models;

namespace TestingApp.ViewModels;

public enum AppMode { NoFolder, Fatal, ReadOnly, Offline, Locked, Live }

public sealed class MainVM : Obs
{
    readonly AppSettings _app = AppSettings.Load();
    readonly PipeClient _pipe = new();
    readonly Dictionary<string, SettingVM> _vms = new();
    readonly Dictionary<string, object> _remote = new();
    readonly Dictionary<string, object> _pendingLive = new();
    readonly DispatcherTimer _watch = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };
    readonly DispatcherTimer _liveTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    GroupRules _rules = GroupRules.Load();

    string _modDir = "";
    SchemaDoc? _schema;
    ConfigStore? _store;
    bool _fatal;
    bool _wasRunning;

    AppMode _mode = AppMode.NoFolder;
    bool _gameRunning, _pipeVisible, _liveConnected, _statusBad, _toastVisible, _overlayVisible = true;
    string _banner = "", _status = "", _schemaText = "", _toast = "", _overlayTitle = "", _overlayText = "", _search = "";
    int _bannerKind;
    NavItem? _selectedNav;
    SettingsPage _searchPage = new();

    public AppMode Mode { get => _mode; private set { if (Set(ref _mode, value)) { Raise(nameof(CanEdit)); Raise(nameof(IsLive)); } } }
    public SchemaDoc? Schema => _schema;

    public ObservableCollection<NavItem> Nav { get; } = new();
    public ObservableCollection<FolderChoice> FolderChoices { get; } = new();
    public BufferPage? Buffer { get; private set; }
    public LivePage Live { get; }
    public BackupsPage Backups { get; }

    public ICommand BrowseCommand { get; }
    public ICommand PickFolderCommand { get; }
    public ICommand ConnectCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public ICommand ChangeFolderCommand { get; }
    public ICommand ResetSettingsCommand { get; }
    public ICommand ResetBindsCommand { get; }
    public ICommand ResetAllCommand { get; }

    public MainVM()
    {
        Live = new LivePage(this);
        Backups = new BackupsPage(this);
        BrowseCommand = new RelayCommand(_ => Browse());
        PickFolderCommand = new RelayCommand(p => { if (p is FolderChoice c) OpenFolder(c.ModDir); });
        ConnectCommand = new RelayCommand(async _ => await ToggleConnectionAsync(), _ => _schema != null && (LiveConnected || PipeVisible));
        OpenFolderCommand = new RelayCommand(_ => { if (Directory.Exists(_modDir)) Process.Start(new ProcessStartInfo("explorer.exe", "\"" + _modDir + "\"") { UseShellExecute = true }); });
        ChangeFolderCommand = new RelayCommand(_ => ShowFolderPicker(Locator.Detect(), "Choose the mod's folder"));
        ResetSettingsCommand = new RelayCommand(async _ => await ResetAsync("settings"), _ => CanEdit);
        ResetBindsCommand = new RelayCommand(async _ => await ResetAsync("binds"), _ => CanEdit);
        ResetAllCommand = new RelayCommand(async _ => await ResetAsync("all"), _ => CanEdit);

        _pipe.Message += e => Application.Current.Dispatcher.InvokeAsync(() => OnPipeMessage(e));
        _pipe.Closed += () => Application.Current.Dispatcher.InvokeAsync(OnPipeClosed);
        _watch.Tick += (_, _) => Tick();
        _saveTimer.Tick += (_, _) => SaveNow();
        _liveTimer.Tick += (_, _) => FlushLive();
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); ToastVisible = false; };

        Tick();
        _watch.Start();
        Start();
    }

    // ------------------------------------------------------------ bindable state

    public bool CanEdit => Mode is AppMode.Offline or AppMode.Live;
    public bool IsLive => Mode == AppMode.Live;
    public bool GameRunning { get => _gameRunning; private set { if (Set(ref _gameRunning, value)) Raise(nameof(GameText)); } }
    public bool PipeVisible { get => _pipeVisible; private set { if (Set(ref _pipeVisible, value)) { Raise(nameof(LinkText)); Raise(nameof(ConnectText)); } } }
    public bool LiveConnected { get => _liveConnected; private set { if (Set(ref _liveConnected, value)) { Raise(nameof(LinkText)); Raise(nameof(ConnectText)); } } }
    public string GameText => GameRunning ? "Game running" : "Game closed";
    public string LinkText => LiveConnected ? "Live link connected" : PipeVisible ? "Console link available" : "Console link off";
    public string ConnectText => LiveConnected ? "Disconnect" : "Connect to game";
    public string Banner { get => _banner; private set { if (Set(ref _banner, value)) Raise(nameof(ShowBanner)); } }
    public bool ShowBanner => _banner.Length > 0;
    public int BannerKind { get => _bannerKind; private set => Set(ref _bannerKind, value); }   // 0 info, 1 warning, 2 error
    public string StatusText { get => _status; private set => Set(ref _status, value); }
    public bool StatusBad { get => _statusBad; private set => Set(ref _statusBad, value); }
    public string SchemaText { get => _schemaText; private set => Set(ref _schemaText, value); }
    public string Toast { get => _toast; private set => Set(ref _toast, value); }
    public bool ToastVisible { get => _toastVisible; private set => Set(ref _toastVisible, value); }
    public bool OverlayVisible { get => _overlayVisible; private set => Set(ref _overlayVisible, value); }
    public string OverlayTitle { get => _overlayTitle; private set => Set(ref _overlayTitle, value); }
    public string OverlayText { get => _overlayText; private set => Set(ref _overlayText, value); }
    public SettingVM? ConsoleLink => _vms.TryGetValue("console-link", out var v) ? v : null;
    public bool HasConsoleLink => ConsoleLink != null;

    public NavItem? SelectedNav
    {
        get => _selectedNav;
        set { if (Set(ref _selectedNav, value)) Raise(nameof(CurrentPage)); }
    }

    public string SearchText
    {
        get => _search;
        set
        {
            if (!Set(ref _search, value)) return;
            RebuildSearch();
            Raise(nameof(CurrentPage));
        }
    }

    public object? CurrentPage => string.IsNullOrWhiteSpace(_search) ? _selectedNav?.Page : _searchPage;

    public void ShowToast(string text)
    {
        Toast = text;
        ToastVisible = true;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    void SetStatus(string text, bool bad = false)
    {
        StatusText = text;
        StatusBad = bad;
    }

    // ------------------------------------------------------------ folder and schema

    void Start()
    {
        string dir = _app.Folder;
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
        {
            var found = Locator.Detect();
            if (found.Count == 1) dir = found[0].ModDir;
            else
            {
                ShowFolderPicker(found, "Where is the mod's folder?");
                return;
            }
        }
        OpenFolder(dir);
    }

    void ShowFolderPicker(List<FolderChoice> found, string title)
    {
        FolderChoices.Clear();
        foreach (var f in found) FolderChoices.Add(f);
        OverlayTitle = title;
        OverlayText = found.Count > 0
            ? "These installs were found. Pick one, or browse to the Geometry Dash folder (or straight to geode\\config\\" + Locator.ModId + ")."
            : "Couldn't find it automatically. Browse to your Geometry Dash folder. If the mod has never run, start the game once first so it creates its folder.";
        OverlayVisible = true;
        if (_schema == null) Mode = AppMode.NoFolder;
    }

    void Browse()
    {
        var dialog = new OpenFolderDialog { Title = "Choose the Geometry Dash folder or the mod's folder" };
        if (dialog.ShowDialog() != true) return;
        string? dir = Locator.Normalize(dialog.FolderName);
        if (dir == null)
        {
            ShowToast("That folder doesn't contain geode\\config\\" + Locator.ModId + ". Start the game once with the mod, or pick another folder.");
            return;
        }
        OpenFolder(dir);
    }

    static string? ReadResource(string name)
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
        return s == null ? null : new StreamReader(s).ReadToEnd();
    }

    SchemaDoc? LoadSchema(string dir, out string note)
    {
        note = "";
        string installed = System.IO.Path.Combine(dir, "schema.json");
        if (File.Exists(installed))
        {
            try
            {
                var d = SchemaDoc.Parse(File.ReadAllText(installed), "the installed mod (schema.json)", out string err);
                if (d != null) return d;
                note = "schema.json: " + err + ". ";
            }
            catch (Exception ex) { note = "schema.json: " + ex.Message + ". "; }
        }
        string? bundled = ReadResource("schema.bundled.json");
        if (bundled != null)
        {
            var d = SchemaDoc.Parse(bundled, "bundled with the app", out string err2);
            if (d != null) return d;
            note += "bundled schema: " + err2 + ". ";
        }
        else note += "This build has no bundled schema. ";
        return null;
    }

    void OpenFolder(string dir)
    {
        string? keepPage = SelectedNav?.Name;
        _modDir = dir;
        _app.Folder = dir;
        _app.Save();
        var schema = LoadSchema(dir, out string note);
        if (schema == null)
        {
            _fatal = true;
            _schema = null;
            _store = null;
            Mode = AppMode.Fatal;
            OverlayTitle = "No settings list found";
            OverlayText = note + "Start the game once with the current mod (it writes schema.json next to config.json), or use a build of the app that includes the schema.";
            OverlayVisible = true;
            FolderChoices.Clear();
            return;
        }
        _fatal = false;
        _schema = schema;
        _store = new ConfigStore(schema, dir);
        _store.Load();
        OverlayVisible = false;
        BuildModel(keepPage);
        UpdateMode();
        RefreshBackups();
        SetStatus(_store.Editable ? "Loaded " + _store.FilePath : "Read-only", false);
    }

    // ------------------------------------------------------------ model

    void BuildModel(string? keepPage)
    {
        var schema = _schema!;
        _vms.Clear();
        _rules = GroupRules.Load();
        foreach (var sp in schema.Settings)
            if (sp.Key != "config-version") _vms[sp.Key] = new SettingVM(sp, this);

        Nav.Clear();
        Buffer = new BufferPage(this, schema, k => _vms[k]);
        Nav.Add(new NavItem { Name = "Frame buffer", Page = Buffer });

        var byGroup = new Dictionary<GroupRule, List<SettingVM>>();
        foreach (var sp in schema.Settings)
        {
            if (sp.IsBind || _rules.IsHidden(sp.Key) || _rules.IsBuffer(sp.Key)) continue;
            var g = _rules.Classify(sp.Key);
            if (!byGroup.TryGetValue(g, out var list)) byGroup[g] = list = new List<SettingVM>();
            list.Add(_vms[sp.Key]);
        }
        foreach (var kv in byGroup.OrderBy(k => k.Key.Sort))
            Nav.Add(new NavItem { Name = kv.Key.Name, Page = new SettingsPage { Title = kv.Key.Name, Blurb = kv.Key.Blurb, Items = kv.Value } });

        Nav.Add(new NavItem { Name = "Keys", Page = new KeysPage(this, schema, k => _vms.TryGetValue(k, out var v) ? v : null) });
        Nav.Add(new NavItem { Name = "Actions", Page = BuildActionsPage() });
        Nav.Add(new NavItem { Name = "Live", Page = Live });
        Nav.Add(new NavItem { Name = "Backups and reset", Page = Backups });

        SelectedNav = Nav.FirstOrDefault(n => n.Name == keepPage) ?? Nav[0];
        SyncValues();
        SchemaText = "Settings from " + schema.Origin + (schema.ModVersion.Length > 0 ? " · mod " + schema.ModVersion : "");
        Raise(nameof(ConsoleLink));
        Raise(nameof(HasConsoleLink));
        RebuildSearch();
        Raise(nameof(CurrentPage));
    }

    ActionsPage BuildActionsPage()
    {
        var rows = new List<ActionRowVM>();
        foreach (var a in _schema!.Actions)
        {
            string name = a.Name;
            string key = _vms.TryGetValue("bind." + name, out var b) ? b.TextValue : "";
            rows.Add(new ActionRowVM
            {
                Title = Fmt.Humanize(name),
                Info = a.Info,
                KeyText = key.Length > 0 ? key : "no key",
                RunCommand = new RelayCommand(async _ => await RunActionAsync(name), _ => IsLive),
            });
        }
        return new ActionsPage { Host = this, Rows = rows };
    }

    void SyncValues()
    {
        foreach (var vm in _vms.Values)
        {
            object v;
            if (LiveConnected && _remote.TryGetValue(vm.Key, out var rv)) v = rv;
            else if (_store != null) v = _store.Get(vm.Key);
            else v = vm.Spec.Default;
            vm.SetFromHost(v);
        }
    }

    void RebuildSearch()
    {
        var tokens = _search.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var items = new List<SettingVM>();
        if (tokens.Length > 0 && _schema != null)
        {
            foreach (var sp in _schema.Settings)
            {
                if (sp.IsBind || _rules.IsHidden(sp.Key) || sp.Key == "stage") continue;
                string hay = sp.Title + " " + sp.Help + " " + sp.Key;
                if (tokens.All(t => hay.Contains(t, StringComparison.OrdinalIgnoreCase))) items.Add(_vms[sp.Key]);
            }
        }
        _searchPage = new SettingsPage { Title = "Search results", Blurb = items.Count + " found for “" + _search.Trim() + "”", Items = items };
    }

    // ------------------------------------------------------------ editing

    string? BindOwner(string canon, string self)
    {
        foreach (var vm in _vms.Values)
            if (vm.Spec.IsBind && vm.Key != self && vm.TextValue == canon) return Fmt.Humanize(vm.Key);
        return null;
    }

    public bool Commit(SettingVM vm, object value, out object normalized)
    {
        normalized = value;
        if (!CanEdit || _schema == null) return false;
        string err = Rules.Validate(_schema, vm.Key, value, BindOwner, out normalized);
        if (err.Length > 0)
        {
            ShowToast(err);
            return false;
        }
        if (Mode == AppMode.Live)
        {
            _pendingLive[vm.Key] = normalized;
            _liveTimer.Stop();
            _liveTimer.Start();
            return true;
        }
        if (_store == null) return false;
        _store.Set(vm.Key, normalized);
        _saveTimer.Stop();
        _saveTimer.Start();
        SetStatus("Unsaved changes…");
        return true;
    }

    void SaveNow()
    {
        _saveTimer.Stop();
        if (_store == null || !_store.Dirty) return;
        // 1.0.1: checked fresh right before writing (the cached GameRunning is only refreshed once a second, so a game
        // that just started could otherwise get its config.json overwritten). The change stays pending and is saved
        // by Tick() once the game closes.
        bool proc = GameWatcher.IsGameRunning();
        bool pipe = GameWatcher.PipeExists() || _pipe.Connected;
        if (GameRunning || proc || pipe)
        {
            PipeVisible = pipe;
            GameRunning = true;
            _wasRunning = true;
            UpdateMode();
            SetStatus("Not saved yet: the game is running (saved when it closes)", true);
            return;
        }
        if (_store.Save(out string err))
        {
            SetStatus("All changes saved");
            RefreshBackups();
        }
        else SetStatus("Could not save: " + err, true);
    }

    public void ApplyPreset(PresetSpec preset)
    {
        if (!CanEdit) return;
        if (Mode == AppMode.Live)
        {
            _ = SendAsync("preset", ("name", preset.Name));
            return;
        }
        foreach (var kv in preset.Set)
            if (_vms.TryGetValue(kv.Key, out var vm)) vm.BoolValue = kv.Value;
        Buffer?.SelectStage(preset.Stage);
        ShowToast("Preset “" + Fmt.Humanize(preset.Name) + "” applied");
    }

    async Task ResetAsync(string what)
    {
        string label = what == "settings" ? "all settings (and the stage), keeping your keys" : what == "binds" ? "all key bindings" : "everything (settings and keys)";
        var answer = MessageBox.Show(Application.Current.MainWindow, "Reset " + label + " to the defaults?" + (Mode == AppMode.Live ? "\n\nThis changes the running game right away." : "\n\nA backup of config.json is made first."),
            "Reset", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (answer != MessageBoxResult.OK) return;
        if (Mode == AppMode.Live)
        {
            await SendAsync("reset", ("what", what));
            return;
        }
        if (_store == null) return;
        _store.ResetScope(what);
        SyncValues();
        _saveTimer.Stop();
        _saveTimer.Start();
        SetStatus("Unsaved changes…");
        ShowToast("Reset done");
    }

    // ------------------------------------------------------------ backups

    public void RefreshBackups()
    {
        Backups.Folder = _modDir;
        Backups.Backups.Clear();
        if (_store == null) return;
        Backups.FileInfo = _store.FileExists ? "config.json · version " + _store.FileVersion + " · " + new FileInfo(_store.FilePath).LastWriteTime.ToString("g") : "config.json doesn't exist yet (start the game once)";
        foreach (var f in _store.ListBackups())
        {
            string path = f.FullName;
            Backups.Backups.Add(new BackupVM
            {
                Name = f.Name,
                Detail = f.LastWriteTime.ToString("g") + " · " + (f.Length / 1024.0).ToString("0.#") + " KB",
                RestoreCommand = new RelayCommand(_ => Restore(path), _ => Mode == AppMode.Offline),
            });
        }
    }

    void Restore(string path)
    {
        if (_store == null || Mode != AppMode.Offline) return;
        var answer = MessageBox.Show(Application.Current.MainWindow, "Replace config.json with " + System.IO.Path.GetFileName(path) + "?\n\nThe current file is backed up first.",
            "Restore", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (answer != MessageBoxResult.OK) return;
        _saveTimer.Stop();
        if (_store.Restore(path, out string err))
        {
            SyncValues();
            UpdateMode();
            RefreshBackups();
            ShowToast("Restored");
        }
        else ShowToast("Couldn't restore: " + err);
    }

    // ------------------------------------------------------------ game, link, live

    void Tick()
    {
        bool proc = GameWatcher.IsGameRunning();
        bool pipe = GameWatcher.PipeExists() || _pipe.Connected;
        PipeVisible = pipe;
        GameRunning = proc || pipe;
        if (_wasRunning && !GameRunning && _schema != null)
        {
            _wasRunning = false;
            SaveNow();
            if (GameRunning) return;   // 1.0.1: SaveNow saw the game again (changes stay pending, retried next tick)
            OpenFolder(_modDir);   // the game rewrote config.json (and maybe schema.json) while it ran
            ShowToast("Game closed: settings reloaded from config.json");
            return;
        }
        _wasRunning = GameRunning;
        UpdateMode();
    }

    void UpdateMode()
    {
        AppMode m;
        if (_schema == null || _store == null) m = _fatal ? AppMode.Fatal : AppMode.NoFolder;
        else if (LiveConnected) m = AppMode.Live;
        else if (GameRunning) m = AppMode.Locked;
        else if (!_store.Editable) m = AppMode.ReadOnly;
        else m = AppMode.Offline;
        Mode = m;
        switch (m)
        {
            case AppMode.Locked:
                Banner = "Geometry Dash is running, so editing the file is locked (the game would overwrite it). " +
                         (PipeVisible ? "Press “Connect to game” (top right) to change settings live instead, or close the game."
                                      : "The console link is off: press Ctrl+F5 in the game to switch it on, or close the game to edit.");
                BannerKind = 1;
                break;
            case AppMode.Live:
                Banner = "Live: changes go straight to the running game (it saves them itself). The file isn't touched.";
                BannerKind = 0;
                break;
            case AppMode.ReadOnly:
                Banner = _store!.ReadOnlyReason;
                BannerKind = 2;
                break;
            default:
                Banner = "";
                break;
        }
        CommandManager.InvalidateRequerySuggested();
    }

    async Task ToggleConnectionAsync()
    {
        if (LiveConnected)
        {
            _pipe.Disconnect();
            return;
        }
        string? err = await _pipe.ConnectAsync();
        if (err != null)
        {
            ShowToast(err);
            return;
        }
        _remote.Clear();
        Live.Clear();
        LiveConnected = true;
        UpdateMode();
        ShowToast("Connected to the game");
    }

    void OnPipeClosed()
    {
        if (!LiveConnected) return;
        LiveConnected = false;
        _pendingLive.Clear();
        _liveTimer.Stop();
        _store?.Load();
        SyncValues();
        UpdateMode();
        ShowToast("Disconnected from the game");
    }

    void OnPipeMessage(JsonElement e)
    {
        if (!e.TryGetProperty("type", out var t)) return;
        switch (t.GetString())
        {
            case "hello":
                if (e.TryGetProperty("note", out var note) && !string.IsNullOrEmpty(note.GetString())) ShowToast(note.GetString()!);
                break;
            case "schema":
                OnLiveSchema(e.GetRawText());
                break;
            case "config":
                foreach (var p in e.EnumerateObject())
                {
                    if (p.Name == "type") continue;
                    object? v = p.Value.ValueKind switch
                    {
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        JsonValueKind.Number => p.Value.GetDouble(),
                        JsonValueKind.String => p.Value.GetString() ?? "",
                        _ => null,
                    };
                    if (v == null) continue;
                    _remote[p.Name] = v;
                    if (_vms.TryGetValue(p.Name, out var vm)) vm.SetFromHost(v);
                }
                break;
            case "state":
                Live.Update(e);
                break;
            case "log":
                Live.AddLog(e.TryGetProperty("tick", out var tk) && tk.TryGetInt64(out long tick) ? tick : 0, e.TryGetProperty("msg", out var m) ? m.GetString() ?? "" : "");
                break;
        }
    }

    // The running game's own schema is the truth. If it differs from the one the app loaded, switch to it.
    void OnLiveSchema(string json)
    {
        var live = SchemaDoc.Parse(json, "the running game", out string err);
        if (live == null)
        {
            ShowToast("The running mod's schema can't be used by this app (" + err + "). Controls follow the app's own schema.");
            return;
        }
        if (_schema != null && live.Signature() == _schema.Signature()) return;
        _schema = live;
        _store = new ConfigStore(live, _modDir);
        _store.Load();
        BuildModel(SelectedNav?.Name);
        ShowToast("The running game's settings differ from the file's schema: using the game's list.");
    }

    void FlushLive()
    {
        _liveTimer.Stop();
        var pending = _pendingLive.ToList();
        _pendingLive.Clear();
        foreach (var kv in pending) _ = SetLiveAsync(kv.Key, kv.Value);
    }

    async Task SetLiveAsync(string key, object value)
    {
        var (ok, err) = await SendAsync("set", ("key", key), ("value", value));
        if (!ok && _vms.TryGetValue(key, out var vm) && _remote.TryGetValue(key, out var old)) vm.SetFromHost(old);
        else if (ok) _remote[key] = value;
    }

    // returns the ack; shows errors
    async Task<(bool Ok, string Error)> SendAsync(string cmd, params (string Name, object Value)[] fields)
    {
        var (ok, err) = await _pipe.RequestAsync(cmd, fields);
        // switching the console link off closes the pipe itself, so there may be no ack
        if (!ok && cmd == "set" && fields.Any(f => f.Name == "key" && (string)f.Value == "console-link") && fields.Any(f => f.Name == "value" && f.Value is false)) return (true, "");
        if (!ok) ShowToast("The game said: " + err);
        return (ok, err);
    }

    public async Task RunActionAsync(string name)
    {
        if (!IsLive) return;
        await SendAsync("action", ("name", name));
    }

    // ------------------------------------------------------------ window hooks

    public bool HandleKey(KeyEventArgs e) => SelectedNav?.Page is KeysPage keys && keys.OnKey(e);

    public void Shutdown()
    {
        SaveNow();
        _pipe.Dispose();
    }
}
