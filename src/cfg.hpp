// cfg.hpp - all settings of the mod live here (JSON file in the mod's save folder), not in Geode.
// Pure C++ (no Geode / Cocos / Windows headers) so it can be unit-tested on its own.
// Flat JSON only: {"key": true|number|"text", ...}. Thread-safe.
#pragma once
#include <algorithm>
#include <cctype>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <filesystem>
#include <fstream>
#include <map>
#include <mutex>
#include <set>
#include <sstream>
#include <string>
#include <unordered_map>
#include <vector>

namespace cfg {

    // Limits shared with main.cpp (which static_asserts them against its STAGES table).
    // 1 = before the rename (no "stage" / "config-version" keys); 2 = v2.87.0 stage numbering (0.5f = 0, off = 10);
    // 3 = v2.87.1 (0.25f stage added: every stage number is one higher, off = 11)
    constexpr int CONFIG_VERSION = 3;
    constexpr int STAGE_MAX = 11;          // stages 0..6 presets (0.25f..8f), 7..10 custom slots A-D, 11 = off
    constexpr int STAGE_DEFAULT = 1;       // 0.5f (a fresh install starts here)
    constexpr int CUSTOM_MAX_TICKS = 128;  // 32 frames at 60 fps
    constexpr int CUSTOM_MAX_WAVE_TICKS = 64;

    // v2.88.3: data tables the companion app reads (through schema()). main.cpp uses these or checks its own
    // tables against them at compile time, so the app's schema cannot drift from the mod.
    struct StageInfo { bool enabled; int ticksAll; int ticksWave; bool custom; int slot; };
    inline constexpr StageInfo STAGE_TABLE[STAGE_MAX + 1] = {
        { true, 1, 1, false, -1 },    // 0: 0.25f
        { true, 2, 2, false, -1 },    // 1: 0.5f
        { true, 4, 4, false, -1 },    // 2: 1f
        { true, 8, 8, false, -1 },    // 3: 2f
        { true, 16, 8, false, -1 },   // 4: 4f, wave 2f
        { true, 24, 12, false, -1 },  // 5: 6f, wave 3f
        { true, 32, 16, false, -1 },  // 6: 8f, wave 4f
        { true, 0, 0, true, 0 },      // 7: custom A
        { true, 0, 0, true, 1 },      // 8: custom B
        { true, 0, 0, true, 2 },      // 9: custom C
        { true, 0, 0, true, 3 },      // 10: custom D
        { false, 0, 0, false, -1 },   // 11: off
    };
    inline constexpr int CUSTOM_SLOT_COUNT = 4;
    inline constexpr char const* CUSTOM_TICKS_KEYS[CUSTOM_SLOT_COUNT] = { "custom-ticks", "custom-b-ticks", "custom-c-ticks", "custom-d-ticks" };
    inline constexpr char const* CUSTOM_WAVE_KEYS[CUSTOM_SLOT_COUNT] = { "custom-wave-ticks", "custom-b-wave-ticks", "custom-c-wave-ticks", "custom-d-wave-ticks" };
    inline constexpr char const* CUSTOM_NAME_KEYS[CUSTOM_SLOT_COUNT] = { "custom-name", "custom-b-name", "custom-c-name", "custom-d-name" };
    // Settings that are remembered per level when 'per-level-settings' is on (the values live in the game's own save, not in config.json)
    inline constexpr char const* PROFILE_KEYS[] = { "robust-saves", "orb-align", "orb-timing", "level-memory",
        "more-time-death-spots", "ship-buffer", "ufo-smart", "ball-smart", "swing-smart", "cbf-sync", "predict-moving-objects",
        "training-mode" };
    struct PresetSet { char const* key; bool value; };
    struct PresetDef { char const* name; int stage; char const* info; PresetSet sets[4]; };
    inline constexpr int PRESET_COUNT = 3;
    inline constexpr PresetDef PRESETS[PRESET_COUNT] = {
        { "safe", 3, "2f window. Robust saves and wave margin on, MORE TIME off, low latency off.",
          { { "robust-saves", true }, { "low-latency", false }, { "wave-safety-margin", true }, { "more-time-death-spots", false } } },
        { "balanced", 4, "4f window (wave 2f). Robust saves, wave margin and MORE TIME on, low latency off.",
          { { "robust-saves", true }, { "low-latency", false }, { "wave-safety-margin", true }, { "more-time-death-spots", true } } },
        { "aggressive", 6, "8f window (wave 4f). Robust saves and MORE TIME on, wave margin off, low latency off.",
          { { "robust-saves", true }, { "low-latency", false }, { "wave-safety-margin", false }, { "more-time-death-spots", true } } },
    };

    // ---------- tiny flat-JSON reader / writer ----------
    struct Val {
        enum T { None, Bool, Num, Str } t = None;
        bool b = false;
        double n = 0;
        std::string s;
        static Val boolean(bool v) { Val x; x.t = Bool; x.b = v; return x; }
        static Val number(double v) { Val x; x.t = Num; x.n = v; return x; }
        static Val text(std::string v) { Val x; x.t = Str; x.s = std::move(v); return x; }
    };
    using Obj = std::map<std::string, Val>;

    inline std::string jstr(std::string const& in) {
        std::string o = "\"";
        for (unsigned char c : in) {
            switch (c) {
                case '"': o += "\\\""; break;
                case '\\': o += "\\\\"; break;
                case '\n': o += "\\n"; break;
                case '\r': o += "\\r"; break;
                case '\t': o += "\\t"; break;
                default:
                    if (c < 0x20) { char b[8]; std::snprintf(b, sizeof b, "\\u%04x", c); o += b; }
                    else o += static_cast<char>(c);
            }
        }
        return o + "\"";
    }

    inline std::string jnum(double v) {
        char b[64];
        if (std::floor(v) == v && std::fabs(v) < 1e15) std::snprintf(b, sizeof b, "%lld", static_cast<long long>(v));
        else std::snprintf(b, sizeof b, "%.10g", v);
        return b;
    }

    inline std::string jval(Val const& v) {
        switch (v.t) {
            case Val::Bool: return v.b ? "true" : "false";
            case Val::Num: return jnum(v.n);
            case Val::Str: return jstr(v.s);
            default: return "null";
        }
    }

    namespace detail {
        inline void ws(std::string const& s, size_t& i) { while (i < s.size() && std::isspace(static_cast<unsigned char>(s[i]))) ++i; }
        inline void utf8(std::string& o, unsigned cp) {
            if (cp < 0x80) o += static_cast<char>(cp);
            else if (cp < 0x800) { o += static_cast<char>(0xC0 | (cp >> 6)); o += static_cast<char>(0x80 | (cp & 63)); }
            else { o += static_cast<char>(0xE0 | (cp >> 12)); o += static_cast<char>(0x80 | ((cp >> 6) & 63)); o += static_cast<char>(0x80 | (cp & 63)); }
        }
        inline bool str(std::string const& s, size_t& i, std::string& out) {
            if (i >= s.size() || s[i] != '"') return false;
            ++i;
            out.clear();
            while (i < s.size()) {
                char c = s[i++];
                if (c == '"') return true;
                if (c != '\\') { out += c; continue; }
                if (i >= s.size()) return false;
                char e = s[i++];
                switch (e) {
                    case '"': out += '"'; break;
                    case '\\': out += '\\'; break;
                    case '/': out += '/'; break;
                    case 'n': out += '\n'; break;
                    case 'r': out += '\r'; break;
                    case 't': out += '\t'; break;
                    case 'b': out += '\b'; break;
                    case 'f': out += '\f'; break;
                    case 'u': {
                        if (i + 4 > s.size()) return false;
                        unsigned cp = 0;
                        for (int k = 0; k < 4; ++k) {
                            char h = s[i++];
                            cp <<= 4;
                            if (h >= '0' && h <= '9') cp |= h - '0';
                            else if (h >= 'a' && h <= 'f') cp |= h - 'a' + 10;
                            else if (h >= 'A' && h <= 'F') cp |= h - 'A' + 10;
                            else return false;
                        }
                        utf8(out, cp);
                        break;
                    }
                    default: return false;
                }
            }
            return false;
        }
        inline bool value(std::string const& s, size_t& i, Val& v) {
            ws(s, i);
            if (i >= s.size()) return false;
            if (s[i] == '"') { std::string t; if (!str(s, i, t)) return false; v = Val::text(t); return true; }
            if (s.compare(i, 4, "true") == 0) { i += 4; v = Val::boolean(true); return true; }
            if (s.compare(i, 5, "false") == 0) { i += 5; v = Val::boolean(false); return true; }
            if (s.compare(i, 4, "null") == 0) { i += 4; v = Val(); return true; }
            size_t j = i;
            while (j < s.size() && (std::isdigit(static_cast<unsigned char>(s[j])) || s[j] == '-' || s[j] == '+' || s[j] == '.' || s[j] == 'e' || s[j] == 'E')) ++j;
            if (j == i) return false;
            char* end = nullptr;
            std::string num = s.substr(i, j - i);
            double d = std::strtod(num.c_str(), &end);
            if (!end || *end != '\0' || !std::isfinite(d)) return false;
            i = j;
            v = Val::number(d);
            return true;
        }
    }

    // Parses one flat object. Nested objects / arrays are rejected on purpose.
    inline bool parseFlat(std::string const& s, Obj& out, std::string* err = nullptr) {
        auto fail = [&](char const* m) { if (err) *err = m; return false; };
        out.clear();
        size_t i = 0;
        detail::ws(s, i);
        if (i >= s.size() || s[i] != '{') return fail("not an object");
        ++i;
        detail::ws(s, i);
        if (i < s.size() && s[i] == '}') { ++i; detail::ws(s, i); return i == s.size() ? true : fail("trailing text"); }
        for (;;) {
            detail::ws(s, i);
            std::string key;
            if (!detail::str(s, i, key)) return fail("bad key");
            detail::ws(s, i);
            if (i >= s.size() || s[i] != ':') return fail("missing ':'");
            ++i;
            Val v;
            if (!detail::value(s, i, v)) return fail("bad value (only flat objects are accepted)");
            out[key] = v;
            detail::ws(s, i);
            if (i >= s.size()) return fail("unterminated object");
            if (s[i] == ',') { ++i; continue; }
            if (s[i] == '}') { ++i; break; }
            return fail("expected ',' or '}'");
        }
        detail::ws(s, i);
        return i == s.size() ? true : fail("trailing text");
    }

    // ---------- keys (hotkeys are plain strings like "F8", "Shift+F6", "Ctrl+F1") ----------
    enum Mod : int { CTRL = 1, SHIFT = 2, ALT = 4 };

    inline std::string lower(std::string s) { for (auto& c : s) c = static_cast<char>(std::tolower(static_cast<unsigned char>(c))); return s; }

    inline bool keyTable(std::string const& name, int& vk) {
        std::string n = lower(name);
        if (n.size() >= 2 && n[0] == 'f' && std::isdigit(static_cast<unsigned char>(n[1]))) {
            int f = std::atoi(n.c_str() + 1);
            if (f >= 1 && f <= 24) { vk = 0x70 + f - 1; return true; }
            return false;
        }
        if (n.size() == 1 && n[0] >= 'a' && n[0] <= 'z') { vk = 'A' + (n[0] - 'a'); return true; }
        if (n.size() == 1 && n[0] >= '0' && n[0] <= '9') { vk = n[0]; return true; }
        if (n.size() == 4 && n.compare(0, 3, "num") == 0 && n[3] >= '0' && n[3] <= '9') { vk = 0x60 + (n[3] - '0'); return true; }
        static const std::map<std::string, int> names = {
            {"space", 0x20}, {"tab", 0x09}, {"enter", 0x0D}, {"esc", 0x1B}, {"backspace", 0x08}, {"insert", 0x2D},
            {"delete", 0x2E}, {"home", 0x24}, {"end", 0x23}, {"pageup", 0x21}, {"pagedown", 0x22}, {"left", 0x25},
            {"up", 0x26}, {"right", 0x27}, {"down", 0x28}, {"pause", 0x13}, {"scrolllock", 0x91}, {"backquote", 0xC0},
            {"minus", 0xBD}, {"equals", 0xBB}, {"comma", 0xBC}, {"period", 0xBE}, {"slash", 0xBF} };
        auto it = names.find(n);
        if (it == names.end()) return false;
        vk = it->second;
        return true;
    }

    inline bool parseKey(std::string const& text, int& vk, int& mods) {
        vk = 0; mods = 0;
        std::string cur;
        std::vector<std::string> parts;
        for (char c : text) { if (c == '+') { parts.push_back(cur); cur.clear(); } else if (c != ' ') cur += c; }
        parts.push_back(cur);
        bool haveKey = false;
        for (size_t i = 0; i < parts.size(); ++i) {
            std::string p = lower(parts[i]);
            if (p == "ctrl" || p == "control") mods |= CTRL;
            else if (p == "shift") mods |= SHIFT;
            else if (p == "alt") mods |= ALT;
            else if (i + 1 == parts.size() && !p.empty() && keyTable(p, vk)) haveKey = true;
            else return false;
        }
        return haveKey;
    }

    inline std::string keyName(int vk, int mods) {
        std::string s;
        if (mods & CTRL) s += "Ctrl+";
        if (mods & SHIFT) s += "Shift+";
        if (mods & ALT) s += "Alt+";
        if (vk >= 0x70 && vk <= 0x87) return s + "F" + std::to_string(vk - 0x70 + 1);
        if (vk >= 0x60 && vk <= 0x69) return s + "Num" + std::to_string(vk - 0x60);
        if ((vk >= '0' && vk <= '9') || (vk >= 'A' && vk <= 'Z')) return s + static_cast<char>(vk);
        static const std::map<int, char const*> names = {
            {0x20, "Space"}, {0x09, "Tab"}, {0x0D, "Enter"}, {0x1B, "Esc"}, {0x08, "Backspace"}, {0x2D, "Insert"}, {0x2E, "Delete"},
            {0x24, "Home"}, {0x23, "End"}, {0x21, "PageUp"}, {0x22, "PageDown"}, {0x25, "Left"}, {0x26, "Up"}, {0x27, "Right"},
            {0x28, "Down"}, {0x13, "Pause"}, {0x91, "ScrollLock"}, {0xC0, "Backquote"}, {0xBD, "Minus"}, {0xBB, "Equals"},
            {0xBC, "Comma"}, {0xBE, "Period"}, {0xBF, "Slash"} };
        auto it = names.find(vk);
        return s + (it != names.end() ? it->second : "?");
    }

    // ---------- safe file writing ----------
    // Writes `text` to <target>.tmp, checks every step, then moves it over <target>. Returns false (and
    // says why in *err) if anything failed; the old file is then still there. If rename() refuses to
    // replace an existing file (not expected, but not verified on every Windows runtime), the content is
    // copied over the target instead.
    inline bool writeFileAtomic(std::filesystem::path const& target, std::string const& text, std::string* err = nullptr) {
        namespace fs = std::filesystem;
        auto fail = [&](std::string m) {
            if (err) *err = std::move(m);
            return false;
        };
        fs::path tmp = target;
        tmp += ".tmp";
        {
            std::ofstream out(tmp, std::ios::binary | std::ios::trunc);
            if (!out) return fail("cannot open " + tmp.filename().string());
            out.write(text.data(), static_cast<std::streamsize>(text.size()));
            out.flush();
            if (!out.good()) {
                out.close();
                std::error_code rc;
                fs::remove(tmp, rc);
                return fail("write to " + tmp.filename().string() + " failed (disk full?)");
            }
        }
        std::error_code ec;
        fs::rename(tmp, target, ec);
        if (ec) {
            std::error_code ec2;
            fs::copy_file(tmp, target, fs::copy_options::overwrite_existing, ec2);
            std::error_code rc;
            fs::remove(tmp, rc);
            if (ec2) return fail("rename failed: " + ec.message());
        }
        return true;
    }

    // ---------- the settings table ----------
    enum class Kind : char { Bool = 'b', Int = 'i', Float = 'f', Str = 's' };

    struct Spec {
        std::string key;
        Kind kind;
        double def;
        double lo, hi;
        std::string sdef;
        std::string info;
    };

    // Actions a key can run. "toggle:<setting>" exists for every bool setting.
    struct ActionDef { char const* name; char const* defKey; char const* info; };
    inline const std::vector<ActionDef>& actions() {
        static const std::vector<ActionDef> a = {
            {"stage", "F6", "Cycle the buffer stage (forward)"},
            {"stage-back", "Shift+F6", "Cycle the buffer stage (backward)"},
            {"export", "F8", "Copy the log to the clipboard and save console-export.txt"},
            {"run-check", "Shift+F8", "Run check: replay your best recorded run from the start of the next attempt (compares P1 only; triggers and moving objects are not replayed; a long run can freeze the game for a moment)"},
            {"stats", "F12", "Print session stats to the log"},
            {"path", "F10", "Toggle the path view"},
            {"forget", "F11", "Forget this level's memory"},
            {"preset-safe", "Ctrl+F1", "Preset Safe (2f)"},
            {"preset-balanced", "Ctrl+F2", "Preset Balanced (4f, wave 2f)"},
            {"preset-aggressive", "Ctrl+F3", "Preset Aggressive (8f, wave 4f)"},
            {"custom-down", "Ctrl+F11", "Custom slot: 1 frame (4 ticks) less delay, also for wave. Only while a custom slot is the current stage"},
            {"custom-up", "Ctrl+F12", "Custom slot: 1 frame (4 ticks) more delay, also for wave. Only while a custom slot is the current stage"},
            {"slot-a", "Alt+1", "Switch to custom slot A"},
            {"slot-b", "Alt+2", "Switch to custom slot B"},
            {"slot-c", "Alt+3", "Switch to custom slot C"},
            {"slot-d", "Alt+4", "Switch to custom slot D"},
        };
        return a;
    }
    inline char const* defaultToggleKey(std::string const& setting) {
        if (setting == "low-latency") return "F7";
        if (setting == "performance-mode") return "Shift+F7";
        if (setting == "training-mode") return "F9";
        if (setting == "wave-auto-release") return "Shift+F9";
        if (setting == "death-heatmap") return "Shift+F11";
        if (setting == "save-ding") return "Shift+F12";
        if (setting == "show-hint-text") return "Ctrl+F6";
        if (setting == "show-auto-tag") return "Ctrl+F7";
        if (setting == "show-tps-text") return "Ctrl+F8";
        if (setting == "show-watermark") return "Ctrl+F9";
        if (setting == "show-complete-label") return "Ctrl+F10";
        if (setting == "console-link") return "Ctrl+F5";      // v2.88.3 (Ctrl+F4 is 'unlimited-delay')
        if (setting == "console-window") return "Shift+F5";   // v2.88.3
        if (setting == "unlimited-delay") return "Ctrl+F4";
        return "";
    }

    inline const std::vector<Spec>& specs() {
        static const std::vector<Spec> all = [] {
            std::vector<Spec> v = {
        {"config-version", Kind::Int, CONFIG_VERSION, 1, 999, "", R"~(Config file version (internal, read-only): Used to upgrade this file when settings change between versions. Can't be changed with 'set' from the console; don't edit it.)~"},
        {"stage", Kind::Int, STAGE_DEFAULT, 0, STAGE_MAX, "", R"~(Buffer stage: 0 = 0.25f (1 tick), 1 = 0.5f, 2 = 1f, 3 = 2f, 4 = 4f / 2f wave, 5 = 6f / 3f wave, 6 = 8f / 4f wave, 7-10 = custom slots A-D, 11 = off. 1 tick = 0.25f at 240 steps/s. Same for every level; the stage key (F6) changes it and it is remembered between sessions.)~"},
        {"custom-ticks", Kind::Int, 40, 1, 128, "", R"~(Custom slot A: max delay (ticks): Max delay for custom slot A in every mode except wave and ship. 4 ticks = 1 frame at 60fps, so 128 = 32 frames. Applies immediately, also while playing.)~"},
        {"custom-wave-ticks", Kind::Int, 20, 0, 64, "", R"~(Custom slot A: max wave delay (ticks): Max delay for custom slot A while in wave. 0 = wave inputs are never delayed in this slot.)~"},
        {"custom-name", Kind::Str, 0, 0, 0, "Custom A", R"~(Custom slot A: name: Shown in the log and the in-game text (up to 24 letters, digits and basic symbols).)~"},
        {"custom-b-ticks", Kind::Int, 12, 1, 128, "", R"~(Custom slot B: max delay (ticks): Max delay for custom slot B in every mode except wave and ship. 4 ticks = 1 frame.)~"},
        {"custom-b-wave-ticks", Kind::Int, 8, 0, 64, "", R"~(Custom slot B: max wave delay (ticks): Max delay for custom slot B while in wave.)~"},
        {"custom-b-name", Kind::Str, 0, 0, 0, "Custom B", R"~(Custom slot B: name: Shown in the log and the in-game text.)~"},
        {"custom-c-ticks", Kind::Int, 20, 1, 128, "", R"~(Custom slot C: max delay (ticks): Max delay for custom slot C in every mode except wave and ship. 4 ticks = 1 frame.)~"},
        {"custom-c-wave-ticks", Kind::Int, 10, 0, 64, "", R"~(Custom slot C: max wave delay (ticks): Max delay for custom slot C while in wave.)~"},
        {"custom-c-name", Kind::Str, 0, 0, 0, "Custom C", R"~(Custom slot C: name: Shown in the log and the in-game text.)~"},
        {"custom-d-ticks", Kind::Int, 64, 1, 128, "", R"~(Custom slot D: max delay (ticks): Max delay for custom slot D in every mode except wave and ship. 4 ticks = 1 frame.)~"},
        {"custom-d-wave-ticks", Kind::Int, 32, 0, 64, "", R"~(Custom slot D: max wave delay (ticks): Max delay for custom slot D while in wave.)~"},
        {"custom-d-name", Kind::Str, 0, 0, 0, "Custom D", R"~(Custom slot D: name: Shown in the log and the in-game text.)~"},
        {"sim-budget-ms", Kind::Float, 8.0, 1.0, 30.0, "", R"~(Prediction time budget (ms): Max time spent predicting per click. Higher = finds longer saves on big windows, but may hitch on dense levels.)~"},
        {"smart-prediction", Kind::Bool, 1, 0, 0, "", R"~(Orb / pad / portal prediction: Let the ghost use orbs, pads, gravity portals and speed portals. Turn off if you see glitches with objects.)~"},
        {"ground-horizon", Kind::Int, 130, 48, 240, "", R"~(Cube / ball lookahead (ticks): How far ahead the prediction looks for cube, ball and spider. A cube jump lasts about 100 ticks, so the old 48 never saw the landing. Higher = catches later hits but costs more time per click.)~"},
        {"wave-horizon", Kind::Int, 65, 20, 160, "", R"~(Wave lookahead (ticks): How far ahead the wave prediction looks (mini wave uses 15 ticks less). 65 = 65 normal / 50 mini. Longer sees crashes further away but costs more time per click.)~"},
        {"orb-assume-hit", Kind::Bool, 1, 0, 0, "", R"~(Orbs: assume you click them (calculation mode): The prediction clicks every orb it touches, as if you hit it on time. Turn off if it predicts deaths for orbs you normally skip.)~"},
        {"orb-try-both", Kind::Bool, 1, 0, 0, "", R"~(Orbs: also try the other choice: If a prediction touches an orb and dies, run it again with the opposite orb choice (click / skip) and keep the better one, like a player who only clicks the orb when needed.)~"},
        {"avoid-moving-objects", Kind::Bool, 0, 0, 0, "", R"~(Don't buffer near moving objects: Experimental. The prediction sees the level frozen at your click. If the ghost's path touches spikes or blocks that moved recently (move/rotate triggers), the input goes through untouched instead of being delayed. Can cancel good saves on levels with lots of movement. Don't use it together with 'Predict moving objects': with 'Fix: movers conflict' on, predict wins and this one is ignored.)~"},
        {"wave-auto-release", Kind::Bool, 0, 0, 0, "", R"~(Wave auto-release: While you hold in wave, if holding would crash within the window below and letting go clearly survives, the mod lets go for you. Your own release is then ignored.)~"},
        {"auto-release-window", Kind::Int, 6, 2, 24, "", R"~(Auto-release window (ticks): Auto-release only fires when holding would crash within this many ticks. Small = lets go at the last moment. 4 ticks = 1 frame at 60fps.)~"},
        {"drift-check", Kind::Bool, 0, 0, 0, "", R"~(Drift check (debug): After each input, compare where the prediction said you'd go with where you actually went, and log the biggest gap in the live console. Use it to see how precise the predictions are on a level.)~"},
        {"wave-spam-gap", Kind::Int, 16, 4, 40, "", R"~(Wave spam: max gap (ticks): Presses/releases closer together than this count as spam. After 'Spam: inputs in a row' of them, wave inputs go straight to the game until you pause. 16 ticks = 4 frames at 60fps (about 7 clicks/sec). Raise it if spam still gets buffered, lower it if tight wave parts stop being buffered.)~"},
        {"wave-spam-streak", Kind::Int, 3, 2, 6, "", R"~(Spam: inputs in a row: How many fast presses/releases in a row it takes before spam goes straight to the game (wave, ship, and smart UFO / ball / swing). Lower = spam is let through sooner; higher = more fast inputs are still buffered.)~"},
        {"orb-align", Kind::Bool, 1, 0, 0, "", R"~(Orb alignment: If you press a little before reaching an orb (and the press wouldn't hit it), the press is held back until you're inside the orb so it hits. Works for every mode except ship and wave. Needs 'Orb / pad / portal prediction' on.)~"},
        {"orb-align-ticks", Kind::Int, 16, 1, 64, "", R"~(Orb alignment: max delay (ticks): How early a press may be and still be moved onto the orb. 16 ticks = 4 frames at 60fps.)~"},
        {"wave-v46", Kind::Bool, 0, 0, 0, "", R"~(Wave: old v4.6 logic: Use the old v4.6 wave rules (big-gain bar, 1-tick queued opposite). Off = the cube-style wave: a crash you could still turn away from counts as safe, and the smallest delay that clears the obstacle wins.)~"},
        {"wave-safety-margin", Kind::Bool, 1, 0, 0, "", R"~(Wave: safety margin: When a wave save is found, use a delay with one tick of room on both sides instead of the one that only just clears the spike, so tiny differences in the real run can't clip it. Can add 1 tick (0.25f) to some saves.)~"},
        {"show-path", Kind::Bool, 0, 0, 0, "", R"~(Path view (in-game drawing, off by default). Draws the predicted path after each input. Red = what the input would do now, green = what the game gets, white dot = delayed fire, red dot = crash. Costs two extra predictions per click while on. Between clicks a live view (blue = if you do nothing, yellow = if you click now) also re-predicts about 20 times a second, two predictions each (off in performance mode).)~"},
        {"orb-align-ground", Kind::Bool, 0, 0, 0, "", R"~(Orb alignment: also from the ground: Off: presses made while standing on the ground are normal jumps and never get moved onto an orb. On: a ground jump made just before an orb can be turned into an orb hit.)~"},
        {"full-ghost-state", Kind::Bool, 1, 0, 0, "", R"~(Full ghost state (v2.8): Copy the full player state (slopes, timers, forces, dash...) into the prediction ghost. Turn off to go back to the v2.7 ghost if predictions feel worse.)~"},
        {"robot-smart", Kind::Bool, 1, 0, 0, "", R"~(Robot: smart jumps: A robot press only counts as a crash if no hold length saves it (your usual hold, then short to long holds). Saves are found for presses that need a different jump height, and presses you can still control aren't delayed.)~"},
        {"robot-auto-release", Kind::Bool, 0, 0, 0, "", R"~(Robot auto-release: While you hold a robot jump, if holding longer would crash within the auto-release window and letting go survives, the mod lets go for you. Your own release is then ignored.)~"},
        {"level-memory", Kind::Bool, 1, 0, 0, "", R"~(Level memory (learns from your runs): Remembers, per level and per spot, which delays worked, where saves ended in deaths and where you keep dying. Delays that worked before are tried first; spots where saves keep failing are left alone; orange dots in the path view mark spots where you die a lot. F11 forgets the current level.)~"},
        {"auto-click", Kind::Bool, 0, 0, 0, "", R"~(Auto-click: When you're not holding and doing nothing would crash within the auto-release window, but a click right now (with your usual hold) is fully safe, the mod clicks for you. Your own click, if it still comes, is ignored. Cube, ball, UFO, robot, spider and swing (not wave / ship).)~"},
        {"predict-through-portals", Kind::Bool, 1, 0, 0, "", R"~(Predict through portals: Predictions keep going through mode portals (cube, ship, ball, UFO, wave, robot, spider, swing): the hidden ghost switches mode there instead of the prediction stopping. Dual, solo and teleport portals still stop it. Needs 'Full ghost state' on.)~"},
        {"spider-smart", Kind::Bool, 1, 0, 0, "", R"~(Spider: smart flips: A spider flip counts as done once you stand on the new surface for 12 ticks, and a crash you could have avoided by flipping back counts as safe. Stops the spider from delaying flips because of obstacles that need your next click.)~"},
        {"dash-auto-release", Kind::Bool, 0, 0, 0, "", R"~(Dash orb auto-release: While you hold a dash, if holding longer would crash within the auto-release window and letting go survives, the mod lets go for you (your own release is then ignored).)~"},
        {"perf-log", Kind::Bool, 0, 0, 0, "", R"~(Performance log (debug): After each click, log in the live console how long its predictions took, how many runs and ghost ticks it used, and the cost per ghost tick. Changes nothing about the decisions.)~"},
        {"ship-buffer", Kind::Bool, 1, 0, 0, "", R"~(Ship: buffer inputs: Buffer ship presses and releases. A crash only counts if you couldn't have recovered (the opposite input 8 / 16 / 24 ticks before it is tried), the smallest delay that clears wins, and only very fast taps go straight through (ship spam gap). Off = ship is never touched.)~"},
        {"ship-max-delay", Kind::Int, 4, 0, 16, "", R"~(Ship: max delay (ticks): Ship's own max delay, used instead of the F6 stage (unless 'Ship: use the stage's delay' lets it follow the stage). Ship has momentum, so small delays are enough and big ones feel laggy. 4 ticks = 1 frame at 60fps.)~"},
        {"orb-timing", Kind::Bool, 1, 0, 0, "", R"~(Orb timing: For orbs that only work at a specific moment inside them: a press already inside an orb that would crash is moved 1-6 ticks later in the orb if that survives, the orb timing that worked at a spot before is tried first, and predictions also try clicking later orbs 3 and 6 ticks into them.)~"},
        {"cube-late-crash", Kind::Int, 90, 0, 240, "", R"~(Cube: ignore crashes after (ticks): If pressing now only crashes after a normal cube / ball jump would have landed, the crash is the next obstacle's, not this click's: no delay can help, so the mod skips the search (saves time and stutter). 90 ticks = about 0.37 s. 0 = always search.)~"},
        {"predict-moving-objects", Kind::Bool, 0, 0, 0, "", R"~(Predict moving objects (experimental): Spikes, saws and blocks near your path that are moving right now (move / rotate triggers) are moved along their current motion during predictions: straight lines, curved / circular paths, spinning and speeding up / slowing down (followed until it would stop or pass 3x speed), so saves account for where they will be. Triggers that haven't started yet aren't predicted. Turn 'Don't buffer near moving objects' off when using this (with 'Fix: movers conflict' on, this one wins).)~"},
        {"robust-saves", Kind::Bool, 1, 0, 0, "", R"~(Robust saves: Before a save is used, it is also tested with you slightly ahead of and behind your real position (about half a tick). Saves that only work at the exact spot are swapped for a nearby delay that survives all three, if there is one. Costs a few extra predictions per save, never per normal click.)~"},
        {"ignore-touching-hazards", Kind::Bool, 1, 0, 0, "", R"~(Ignore hazards you're touching: Spikes / saws that overlap you at the moment of the click while you're alive can't be what kills you (e.g. a spike the level makes follow the player, or noclip), so predictions ignore them. Fixes predictions that died on their very first tick (GRIEF's wave).)~"},
        {"death-log", Kind::Bool, 0, 0, 0, "", R"~(Death log (debug): On every 'no save', log what killed the prediction ghost (object ID, type, where it is relative to you, hitboxes). Ghost deaths in the first 3 ticks are always logged. Use it with F8 when a level predicts badly.)~"},
        {"explain-inputs", Kind::Bool, 0, 0, 0, "", R"~(Explain every input (debug): Also log the inputs that were passed straight to the game and why: safe as it is, spam guard, late-crash rule, max delay 0. Busy log; turn on to see what the mod does in a part, then use F8.)~"},
        {"cbf-sync", Kind::Bool, 1, 0, 0, "", R"~(Click Between Frames sync: With the Click Between Frames mod, a click lands partway through a physics tick. The prediction then starts from that exact moment, and delayed inputs land on the tick the prediction tested. Has no effect without CBF.)~"},
        {"low-latency", Kind::Bool, 0, 0, 0, "", R"~(Low latency mode: For players who feel the buffer as lag or delayed clicks: clicks made while a delay is waiting are not shifted after it (no chain of delayed clicks), delays are at most 1 frame, searches are capped at 3 ms (no frame hitches), and robust saves / wave margin (which can add a tick) are off.)~"},
        {"ufo-smart", Kind::Bool, 1, 0, 0, "", R"~(UFO: smart flaps: A UFO crash only counts if one more flap 6 / 12 / 18 ticks before it couldn't have saved you, landing on something counts as safe, the smallest delay that works wins, and fast flapping goes straight through (spam guard). Off = UFO uses the plain rules like before.)~"},
        {"more-time-death-spots", Kind::Bool, 1, 0, 0, "", R"~(More time at spots you keep dying: At a spot where level memory has seen you die 2+ times, an input that is only safe if your next input comes in time (wave / ship / UFO / spider) gets the small delay that pushes the crash furthest away, so you have more time to react. Normal play elsewhere is not affected. Needs Level memory.)~"},
        {"ship-spam-gap", Kind::Int, 6, 2, 40, "", R"~(Ship: spam gap (ticks): Ship is steered by feathering, so only taps closer together than this count as spam and go straight through. The normal spam gap (16) made almost every ship input spam, so ship was barely buffered. 4 ticks = 1 frame at 60fps.)~"},
        {"ball-smart", Kind::Bool, 1, 0, 0, "", R"~(Ball: smart flips: Ball flips that land after only a few ticks (tight corridors) count as landings, a flip is only called safe after rolling 8 ticks on the new surface (spikes right after the landing spot), and fast flipping goes straight through (spam guard).)~"},
        {"swing-smart", Kind::Bool, 1, 0, 0, "", R"~(Swing: smart flips: A swing flip counts as safe if one more flip in time would have saved it (like the UFO), and fast flipping goes straight through (spam guard).)~"},
        {"training-mode", Kind::Bool, 0, 0, 0, "", R"~(Training mode (hints only): The buffer still predicts every input but never delays it. When an input would have been saved, a hint shows how early you were (e.g. 'click 0.5f early'), so you learn the timing yourself. Auto-release and auto-click still act in training mode unless 'Fix: no auto inputs in training mode' is on.)~"},
        {"per-level-settings", Kind::Bool, 0, 0, 0, "", R"~(Per-level settings: The switches are remembered for each level and come back when you open it again. The stage (F6) and low latency are the same for every level.)~"},
        {"death-heatmap", Kind::Bool, 0, 0, 0, "", R"~(Death heatmap on the progress bar (in-game output, off by default). Red marks where level memory saw you die.)~"},
        {"save-ding", Kind::Bool, 0, 0, 0, "", R"~(Save ding (in-game sound, off by default). Plays a short sound when a click is saved.)~"},
        {"performance-mode", Kind::Bool, 0, 0, 0, "", R"~(Performance mode: For PCs that drop frames when you click: predictions get at most 4 ms per click, robust saves are off (no extra robustness checks), orbs are only tried with a normal click (no late-click retries), and the drift check and the between-clicks live path view are off. If Path view is on, the path of each click's decision is still drawn after it (two extra predictions per click); performance mode doesn't change that. Saves can be a little less precise.)~"},
        {"auto-budget", Kind::Bool, 1, 0, 0, "", R"~(Auto time budget: While the game is already dropping frames (a frame took over twice as long as it should, and over 20 ms), clicks get at most 4 ms of prediction time so it doesn't get worse. Otherwise the time budget setting is used as it is.)~"},
        {"tps", Kind::Int, 240, 240, 1000, "", R"~(Physics TPS: Physics steps per second in levels. 240 is the normal game. Higher values change the physics. The buffer's windows scale with it automatically.)~"},
        {"show-hint-text", Kind::Bool, 0, 0, 0, "", R"~(Training hint text (in-game text, off by default). Shows "click 2f early" at the top of the screen in training mode.)~"},
        {"show-auto-tag", Kind::Bool, 0, 0, 0, "", R"~(AUTO INPUTS ON tag (in-game text, off by default). Small label while any auto-release or auto-click option is on.)~"},
        {"show-tps-text", Kind::Bool, 0, 0, 0, "", R"~(TPS text (in-game text, off by default). Small label with the physics TPS.)~"},
        {"show-watermark", Kind::Bool, 0, 0, 0, "", R"~(Watermark (in-game text, off by default). Small "Testing | stage" label in the corner of the level while the buffer is on.)~"},
        {"show-complete-label", Kind::Bool, 0, 0, 0, "", R"~(Level complete label (in-game text, off by default). Shows "Testing was on" and the attempt's save summary on the level complete screen if the buffer was on during the attempt.)~"},
        {"fix-restart-state", Kind::Bool, 1, 0, 0, "", R"~(Fix: clean restart state: On a manual restart (R, checkpoint, practice) the mod drops the level-memory outcome that was still waiting to be scored (no credit, no death), forgets the watched moving objects and resets the spam timing. Off = the v2.87.0 behaviour. On by default.)~"},
        {"fix-dual-harmless", Kind::Bool, 0, 0, 0, "", R"~(Fix: dual-mode touching hazards: In dual mode each player's prediction ignores only the hazards that overlap that player at the click (before, a hazard on P2 was ignored for P1 too, and the other way round). Off by default.)~"},
        {"fix-lowlat-queue", Kind::Bool, 0, 0, 0, "", R"~(Fix: low latency queue: In low latency mode a new press that comes in behind a queued release now fires one tick after it, instead of in the same update (which gave a zero-length release). Off by default.)~"},
        {"fix-group-on", Kind::Bool, 0, 0, 0, "", R"~(Fix: toggle orb switching a group on: When a toggle orb in a prediction switches a group back on, the ghost collides with it again (before, only switching off was remembered). Off by default.)~"},
        {"fix-orb-checks", Kind::Bool, 0, 0, 0, "", R"~(Fix: safety checks for orb alignment: Orb alignment saves also get the moving-object check ('Don't buffer near moving objects') and the robust check on the fallback pick (the timing that only crashes latest). Off by default.)~"},
        {"fix-more-time-checks", Kind::Bool, 0, 0, 0, "", R"~(Fix: safety checks for MORE TIME: The 'More time at spots you keep dying' delay also gets the moving-object check, and the robust check when it removes the crash completely. Off by default.)~"},
        {"fix-training-auto", Kind::Bool, 0, 0, 0, "", R"~(Fix: no auto inputs in training mode: In training mode, auto-release and auto-click stay off, so training never touches your inputs. Off by default.)~"},
        {"fix-mover-conflict", Kind::Bool, 0, 0, 0, "", R"~(Fix: movers conflict: If 'Predict moving objects' and 'Don't buffer near moving objects' are both on, predict wins and the other is ignored (they work against each other). Off by default.)~"},
        {"fix-auto-throttle", Kind::Bool, 0, 0, 0, "", R"~(Fix: auto-input throttle: After an auto-release or auto-click check took longer than the prediction time budget, the next checks wait about 1 frame (4 ticks). Cheap checks are never delayed. Off by default.)~"},
        {"unlimited-delay", Kind::Bool, 0, 0, 0, "", R"~(Unlimited delay (no cap): Removes the stage's cap on how EARLY an input may be: an early click or release is held back tick by tick until the first timing whose path survives (up to 100 seconds). The stage then only matters as on / off. Searching is still limited by the prediction time budget, so a very early input may not be reached; raise 'Prediction time budget' for that. While this is on, 'Auto time budget' no longer cuts the budget to one frame (the budget setting is used as it is, so very early inputs can cost frame time). Ship keeps its own limit unless 'Ship: use the stage's delay' is on. Off by default.)~"},
        {"ship-uses-stage", Kind::Bool, 1, 0, 0, "", R"~(Ship: use the stage's delay: The ship may use the stage's max delay (or unlimited delay) like every other mode, but never less than 'Ship: max delay'. Off = the ship only uses its own small limit. Below the 1f stage nothing changes. On by default.)~"},
        {"orb-press-passthrough", Kind::Bool, 1, 0, 0, "", R"~(Orb press passes through: A press that already touches an orb is your own orb hit and goes to the game unchanged, even if the prediction dislikes the path. Never when an earlier input is still waiting (the order is kept). Also, a press whose timing already activates an orb is never re-timed by orb alignment. Off = the old behaviour (orb timing may re-time a press inside an orb). On by default.)~"},
        {"orb-same-target", Kind::Bool, 1, 0, 0, "", R"~(Orb alignment: same orb only: A timing only counts if it activates the FIRST orb ahead, not another one further along, and a learned timing is only used for that orb. Applies to the fully safe pick and to the fallback pick (crashing clearly latest). Off = any orb counts. On by default.)~"},
        {"inverted-wave", Kind::Bool, 1, 0, 0, "", R"~(Upside-down wave rules: In an upside-down wave the corridors need quicker turns: the spam gap is at most 1.5 frames, a turn must come at least 3 frames after the input to count, the turn must survive 6 frames, and a save must be fully safe instead of only clearly better. Off = the normal wave rules. On by default.)~"},
        {"console-link", Kind::Bool, 1, 0, 0, "", R"~(Console link (named pipe): ON: the Testing app (or tools/pipe-test.ps1) can connect to the game over the named pipe. OFF: the pipe is closed and nothing can connect; an app that is connected loses its connection. Keys still work. Ctrl+F5 switches it while the game is focused. On by default.)~"},
        {"console-window", Kind::Bool, 1, 0, 0, "", R"~(Console window: Shows the live console window that opens with the game. OFF hides it (the log keeps running). Shift+F5 switches it while the game is focused. On by default.)~"},
            };
            std::vector<Spec> out(v.begin(), v.end());
            for (auto const& a : actions()) {
                out.push_back({ std::string("bind.") + a.name, Kind::Str, 0, 0, 0, a.defKey, std::string("Key for: ") + a.info });
            }
            for (auto const& s : v) {
                if (s.kind == Kind::Bool) {
                    out.push_back({ "bind.toggle:" + s.key, Kind::Str, 0, 0, 0, defaultToggleKey(s.key), "Key that toggles: " + s.key });
                }
            }
            return out;
        }();
        return all;
    }

    inline Spec const* findSpec(std::string const& key) {
        static const std::unordered_map<std::string, Spec const*> index = [] {
            std::unordered_map<std::string, Spec const*> m;
            for (auto const& s : specs()) m[s.key] = &s;
            return m;
        }();
        auto it = index.find(key);
        return it == index.end() ? nullptr : it->second;
    }

    inline bool isBind(std::string const& key) { return key.rfind("bind.", 0) == 0; }

    class Config {
    public:
        // Loads <dir>/config.json (falls back to config.bak.json, then defaults).
        void init(std::filesystem::path dir) {
            std::lock_guard lock(m_mutex);
            m_dir = std::move(dir);
            std::error_code ec;
            std::filesystem::create_directories(m_dir, ec);
            defaults();
            bool ok = loadFile(m_dir / "config.json", true);
            if (!ok) {
                // v2.87.1: a config.json that exists but can't be read is kept before it is replaced
                std::error_code ex;
                std::string kept;
                if (std::filesystem::exists(m_dir / "config.json", ex)) kept = preserveCorrupt();
                defaults();
                ok = loadFile(m_dir / "config.bak.json", false);
                if (!ok) defaults();
                m_loadNote = std::string(ok ? "config.json was unreadable, restored the last valid copy" : "no valid config found, defaults used") + kept;
                if (ok && !m_bindNote.empty()) m_loadNote += "; " + m_bindNote;  // v2.88.4
            }
            m_vals["config-version"] = Val::number(CONFIG_VERSION);
            saveLocked();
            m_ready = true;
        }
        std::string loadNote() const { std::lock_guard lock(m_mutex); return m_loadNote; }

        // Game thread, every few seconds: retries a save that failed, and returns a one-time message
        // ("" = nothing to say): once when saving starts failing, once when it works again.
        std::string pollSave() {
            std::lock_guard lock(m_mutex);
            if (m_dirty) saveLocked();
            std::string n;
            n.swap(m_note);
            return n;
        }

        bool b(std::string const& k) const { std::lock_guard lock(m_mutex); auto it = m_vals.find(k); return it != m_vals.end() && it->second.t == Val::Bool && it->second.b; }
        double d(std::string const& k) const { std::lock_guard lock(m_mutex); auto it = m_vals.find(k); return it != m_vals.end() ? it->second.n : 0.0; }
        int64_t i(std::string const& k) const { return static_cast<int64_t>(std::llround(d(k))); }
        std::string s(std::string const& k) const { std::lock_guard lock(m_mutex); auto it = m_vals.find(k); return it != m_vals.end() ? it->second.s : std::string(); }

        // Checks a value against the table. Returns "" when fine (and normalizes `v`), else the reason.
        std::string validate(std::string const& key, Val& v) const {
            std::lock_guard lock(m_mutex);
            return validateLocked(key, v);
        }

        // Validates and stores. persist=false keeps the value in memory only (per-level profile swaps).
        std::string set(std::string const& key, Val v, bool persist = true) {
            std::lock_guard lock(m_mutex);
            std::string err = validateLocked(key, v);
            if (!err.empty()) return err;
            m_vals[key] = v;
            if (persist) saveLocked();
            return "";
        }

        // what: "settings" (everything but keys), "binds" (keys only), "all"
        void reset(std::string const& what) {
            std::lock_guard lock(m_mutex);
            for (auto const& sp : specs()) {
                bool bind = isBind(sp.key);
                if (what == "all" || (what == "binds" && bind) || (what == "settings" && !bind)) m_vals[sp.key] = defaultOf(sp);
            }
            saveLocked();
        }

        std::string dump() const {
            std::lock_guard lock(m_mutex);
            return dumpLocked(true);
        }
        // v2.88.3: also carries the stage table, presets, custom slot keys and per-level keys, plus the mod version
        // when given. The companion app bundles this (schema-dump) and the mod writes it to schema.json at load.
        std::string schema(std::string const& modVersion = std::string()) const {
            std::string o = "{\"type\":\"schema\",\"settings\":[";
            bool first = true;
            for (auto const& sp : specs()) {
                if (!first) o += ',';
                first = false;
                o += "{\"key\":" + jstr(sp.key) + ",\"kind\":" + jstr(std::string(1, static_cast<char>(sp.kind)));
                if (sp.kind == Kind::Str) o += ",\"default\":" + jstr(sp.sdef);
                else if (sp.kind == Kind::Bool) o += std::string(",\"default\":") + (sp.def != 0 ? "true" : "false");
                else o += ",\"default\":" + jnum(sp.def) + ",\"min\":" + jnum(sp.lo) + ",\"max\":" + jnum(sp.hi);
                o += ",\"info\":" + jstr(sp.info) + "}";
            }
            o += "],\"actions\":[";
            first = true;
            for (auto const& x : actions()) {
                if (!first) o += ',';
                first = false;
                o += "{\"name\":" + jstr(x.name) + ",\"info\":" + jstr(x.info) + "}";
            }
            o += "],\"configVersion\":" + std::to_string(CONFIG_VERSION) + ",\"stageMax\":" + std::to_string(STAGE_MAX) +
                 ",\"customMaxTicks\":" + std::to_string(CUSTOM_MAX_TICKS) + ",\"customMaxWaveTicks\":" + std::to_string(CUSTOM_MAX_WAVE_TICKS);
            if (!modVersion.empty()) o += ",\"modVersion\":" + jstr(modVersion);
            o += ",\"stages\":[";
            for (int i = 0; i <= STAGE_MAX; ++i) {
                auto const& st = STAGE_TABLE[i];
                if (i) o += ',';
                o += "{\"stage\":" + std::to_string(i) + ",\"enabled\":" + (st.enabled ? "true" : "false") + ",\"ticks\":" + std::to_string(st.ticksAll) +
                     ",\"waveTicks\":" + std::to_string(st.ticksWave) + ",\"custom\":" + (st.custom ? "true" : "false") + ",\"slot\":" + std::to_string(st.slot) + "}";
            }
            o += "],\"customSlots\":[";
            for (int i = 0; i < CUSTOM_SLOT_COUNT; ++i) {
                if (i) o += ',';
                o += "{\"slot\":" + std::to_string(i) + ",\"letter\":" + jstr(std::string(1, static_cast<char>('A' + i))) +
                     ",\"stage\":" + std::to_string(STAGE_MAX - CUSTOM_SLOT_COUNT + i) + ",\"ticksKey\":" + jstr(CUSTOM_TICKS_KEYS[i]) +
                     ",\"waveKey\":" + jstr(CUSTOM_WAVE_KEYS[i]) + ",\"nameKey\":" + jstr(CUSTOM_NAME_KEYS[i]) + "}";
            }
            o += "],\"presets\":[";
            for (int i = 0; i < PRESET_COUNT; ++i) {
                auto const& pr = PRESETS[i];
                if (i) o += ',';
                o += "{\"name\":" + jstr(pr.name) + ",\"stage\":" + std::to_string(pr.stage) + ",\"info\":" + jstr(pr.info) + ",\"set\":{";
                for (int k = 0; k < 4; ++k) {
                    if (k) o += ',';
                    o += jstr(pr.sets[k].key) + ":" + (pr.sets[k].value ? "true" : "false");
                }
                o += "}}";
            }
            o += "],\"perLevel\":[";
            for (size_t i = 0; i < sizeof(PROFILE_KEYS) / sizeof(PROFILE_KEYS[0]); ++i) {
                if (i) o += ',';
                o += jstr(PROFILE_KEYS[i]);
            }
            return o + "]}";
        }

    private:
        static Val defaultOf(Spec const& sp) {
            switch (sp.kind) {
                case Kind::Bool: return Val::boolean(sp.def != 0);
                case Kind::Str: return Val::text(sp.sdef);
                default: return Val::number(sp.def);
            }
        }
        void defaults() { m_vals.clear(); for (auto const& sp : specs()) m_vals[sp.key] = defaultOf(sp); }

        std::string validateLocked(std::string const& key, Val& v, bool checkCollisions = true) const {
            auto const* sp = findSpec(key);
            if (!sp) return "unknown setting '" + key + "'";
            switch (sp->kind) {
                case Kind::Bool:
                    if (v.t != Val::Bool) return "'" + key + "' needs true or false";
                    return "";
                case Kind::Int:
                case Kind::Float: {
                    if (v.t != Val::Num) return "'" + key + "' needs a number";
                    if (sp->kind == Kind::Int) {
                        if (std::fabs(v.n - std::round(v.n)) > 1e-9) return "'" + key + "' needs a whole number";
                        v.n = std::round(v.n);
                    }
                    if (v.n < sp->lo || v.n > sp->hi) return "'" + key + "' must be between " + jnum(sp->lo) + " and " + jnum(sp->hi);
                    return "";
                }
                case Kind::Str: {
                    if (v.t != Val::Str) return "'" + key + "' needs text";
                    if (!isBind(key)) {  // free text: custom slot names (plain ASCII, up to 24 characters)
                        std::string clean;
                        for (unsigned char ch : v.s) {
                            if (ch >= 0x20 && ch <= 0x7E && clean.size() < 24) clean += static_cast<char>(ch);
                        }
                        while (!clean.empty() && clean.back() == ' ') clean.pop_back();
                        while (!clean.empty() && clean.front() == ' ') clean.erase(0, 1);
                        if (clean.empty()) return "'" + key + "' needs a name (letters, digits, basic symbols)";
                        v.s = clean;
                        return "";
                    }
                    if (v.s.empty()) return "";  // unbound
                    int vk = 0, mods = 0;
                    if (!parseKey(v.s, vk, mods)) return "'" + v.s + "' is not a valid key (examples: F8, Shift+F6, Ctrl+F1)";
                    v.s = keyName(vk, mods);
                    if (!checkCollisions) return "";  // v2.88.4: loadFile checks collisions after every binding is read
                    for (auto const& [k, other] : m_vals) {
                        if (k != key && isBind(k) && other.t == Val::Str && other.s == v.s) return "'" + v.s + "' is already used by " + k.substr(5);
                    }
                    return "";
                }
            }
            return "";
        }

        bool loadFile(std::filesystem::path const& p, bool makeBackup) {
            std::ifstream in(p, std::ios::binary);
            if (!in) return false;
            std::stringstream ss;
            ss << in.rdbuf();
            Obj o;
            if (!parseFlat(ss.str(), o)) return false;
            int fileVersion = 1;  // files from before the version key existed
            {
                auto vit = o.find("config-version");
                if (vit != o.end() && vit->second.t == Val::Num && vit->second.n >= 1 && vit->second.n <= 999) fileVersion = static_cast<int>(vit->second.n);
            }
            if (makeBackup && fileVersion != CONFIG_VERSION) {
                // keep a copy of a file from another version before it is rewritten
                std::error_code ec2;
                std::filesystem::copy_file(p, m_dir / ("config.v" + std::to_string(fileVersion) + ".json"), std::filesystem::copy_options::overwrite_existing, ec2);
                m_loadNote = fileVersion > CONFIG_VERSION
                    ? "config.json is from a newer version (settings this version doesn't know were ignored; a copy was kept)"
                    : "config.json was upgraded from an older version (a copy was kept)";
            }
            m_bindNote.clear();
            std::set<std::string> fileBinds;  // v2.88.4: bindings that config.json set (and that are valid keys)
            for (auto const& sp : specs()) {
                auto it = o.find(sp.key);
                if (it == o.end()) continue;
                Val v = it->second;
                // v3 added the 0.25f stage in front: a stage saved by an older version keeps its MEANING
                // (old 0..5 -> 1..6 presets, old 6..9 -> 7..10 custom A-D, old 10 (off) -> 11). Anything
                // outside the old 0..10 range is not a stage of that version and keeps the default.
                if (sp.key == "stage" && fileVersion < 3) {
                    if (v.t != Val::Num || v.n < 0 || v.n > 10 || std::fabs(v.n - std::round(v.n)) > 1e-9) continue;
                    v.n = std::round(v.n) + 1;
                }
                // v2.88.4: bindings are only syntax-checked here (bad text keeps the default); collisions are
                // resolved below once every binding is read, so a swap like stage=F8 / export=F6 is kept
                if (validateLocked(sp.key, v, false).empty()) {
                    m_vals[sp.key] = v;
                    if (isBind(sp.key)) fileBinds.insert(sp.key);
                }
            }
            resolveBindCollisions(fileBinds);
            if (!m_bindNote.empty()) m_loadNote += (m_loadNote.empty() ? "" : "; ") + m_bindNote;
            if (makeBackup) {
                std::error_code ec;
                std::filesystem::copy_file(p, m_dir / "config.bak.json", std::filesystem::copy_options::overwrite_existing, ec);
            }
            return true;
        }

        // v2.88.4: after a load, makes every bound key unique, in table order:
        //  1. bindings config.json did not set keep their default;
        //  2. a file binding that uses one of those defaults is rejected (its action goes back to its default);
        //  3. two file bindings with the same key: the first in table order keeps it;
        //  4. every rejected / losing binding gets its default if no binding holds it, else it is unbound ("").
        // Writes what changed to m_bindNote. "" (unbound) never collides.
        void resolveBindCollisions(std::set<std::string> const& fileBinds) {
            std::vector<Spec const*> binds;
            for (auto const& sp : specs()) {
                if (isBind(sp.key)) binds.push_back(&sp);
            }
            std::map<std::string, std::string> owner;  // key text -> binding that holds it
            for (auto const* sp : binds) {
                if (fileBinds.count(sp->key)) continue;
                auto const& t = m_vals[sp->key].s;
                if (!t.empty() && !owner.count(t)) owner[t] = sp->key;
            }
            std::vector<std::pair<Spec const*, std::string>> lost;  // binding, the binding that holds its key
            for (auto const* sp : binds) {
                if (!fileBinds.count(sp->key)) continue;
                auto const& t = m_vals[sp->key].s;
                if (t.empty()) continue;
                auto it = owner.find(t);
                if (it == owner.end()) owner[t] = sp->key;
                else lost.push_back({ sp, it->second });
            }
            for (auto const& [sp, holder] : lost) {
                std::string wanted = m_vals[sp->key].s;
                std::string def = sp->sdef;
                {
                    int vk = 0, mods = 0;
                    if (!def.empty() && parseKey(def, vk, mods)) def = keyName(vk, mods);
                }
                std::string now;
                if (!def.empty() && !owner.count(def)) {
                    now = def;
                    owner[def] = sp->key;
                }
                m_vals[sp->key] = Val::text(now);
                if (!m_bindNote.empty()) m_bindNote += "; ";
                m_bindNote += sp->key.substr(5) + " can't use " + wanted + " (already used by " + holder.substr(5) + "), " +
                              (now.empty() ? std::string("now unbound") : "kept " + now);
            }
            if (!m_bindNote.empty()) m_bindNote = "key conflicts in config.json: " + m_bindNote;
        }

        std::string dumpLocked(bool typed) const {
            std::string o = typed ? "{\"type\":\"config\"" : "{";
            bool first = !typed;
            for (auto const& sp : specs()) {
                auto it = m_vals.find(sp.key);
                if (it == m_vals.end()) continue;
                if (!first) o += ",\n ";
                first = false;
                o += jstr(sp.key) + ":" + jval(it->second);
            }
            return o + "}";
        }

        // v2.87.1: every step is checked. A failed save leaves the old file in place, keeps the dirty flag
        // (pollSave() retries) and leaves one note; the next success leaves one more.
        bool saveLocked() {
            if (m_dir.empty()) return true;
            std::string err;
            if (writeFileAtomic(m_dir / "config.json", dumpLocked(false) + "\n", &err)) {
                m_dirty = false;
                if (m_failing) {
                    m_failing = false;
                    m_note = "config.json saved again";
                }
                return true;
            }
            m_dirty = true;
            if (!m_failing) {
                m_failing = true;
                m_note = "could not save config.json (" + err + "); your changes are kept in memory and saving is retried";
            }
            return false;
        }

        // Copies an unreadable config.json to config.corrupt.json (or the first free config.corrupt.N.json,
        // so an earlier damaged file is not lost). Returns a text for the load note.
        std::string preserveCorrupt() {
            namespace fs = std::filesystem;
            std::error_code ec;
            fs::path src = m_dir / "config.json";
            for (int n = 0; n < 10; ++n) {
                fs::path dst = m_dir / (n == 0 ? std::string("config.corrupt.json") : "config.corrupt." + std::to_string(n) + ".json");
                if (n < 9 && fs::exists(dst, ec)) continue;
                std::error_code cec;
                fs::copy_file(src, dst, n < 9 ? fs::copy_options::none : fs::copy_options::overwrite_existing, cec);
                if (cec) return " (could not keep a copy of the damaged file)";
                return " (the damaged file was kept as " + dst.filename().string() + ")";
            }
            return "";
        }

        mutable std::mutex m_mutex;
        std::map<std::string, Val> m_vals;
        std::filesystem::path m_dir;
        std::string m_loadNote;
        std::string m_bindNote;  // v2.88.4: key conflicts found by the last loadFile
        std::string m_note;
        bool m_dirty = false;
        bool m_failing = false;
        bool m_ready = false;
    };

    inline Config& config() {
        static Config* c = new Config();  // never freed, threads may still use it at exit
        return *c;
    }
    inline bool b(char const* k) { return config().b(k); }
    inline double d(char const* k) { return config().d(k); }
    inline int64_t i(char const* k) { return config().i(k); }
    inline std::string s(char const* k) { return config().s(k); }
    // in-memory only (per-level profile swaps must not rewrite the file on every level)
    inline void setTemp(char const* k, bool v) { (void)config().set(k, Val::boolean(v), false); }

}  // namespace cfg
