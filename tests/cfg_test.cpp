// cfg_test: pure C++17 tests for src/cfg.hpp (config load, key bindings, stage migration, schema).
// Build and run (any compiler):
//   g++ -std=c++17 -Wall tests/cfg_test.cpp -o cfg_test && ./cfg_test
//   cl /std:c++17 /EHsc /utf-8 tests\cfg_test.cpp && cfg_test.exe
// Does not touch Geometry Dash or Geode: the game-side code (main.cpp) is not covered.
#include "../src/cfg.hpp"
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <fstream>
#include <set>
#include <string>

static int failures = 0;
#define CHECK(c) do { if (!(c)) { std::printf("FAIL line %d: %s\n", __LINE__, #c); ++failures; } } while (0)

namespace fs = std::filesystem;

static fs::path tempDir() {
    static int n = 0;
    fs::path d = fs::temp_directory_path() / ("cfg_test_" + std::to_string(std::rand()) + "_" + std::to_string(++n));
    std::error_code ec;
    fs::remove_all(d, ec);
    fs::create_directories(d);
    return d;
}

// Writes `json` as config.json in a fresh folder and loads it.
struct Loaded {
    fs::path dir;
    cfg::Config c;
    explicit Loaded(std::string const& json) : dir(tempDir()) {
        std::ofstream(dir / "config.json", std::ios::binary) << json;
        c.init(dir);
    }
    ~Loaded() { std::error_code ec; fs::remove_all(dir, ec); }
};

static bool has(std::string const& s, std::string const& part) { return s.find(part) != std::string::npos; }

// Minimal strict JSON validator (objects, arrays, strings, numbers, true/false/null).
struct Json {
    std::string const& s;
    size_t i = 0;
    void ws() { while (i < s.size() && (s[i] == ' ' || s[i] == '\n' || s[i] == '\r' || s[i] == '\t')) ++i; }
    bool lit(char const* w) { size_t n = std::strlen(w); if (s.compare(i, n, w) != 0) return false; i += n; return true; }
    bool str() {
        if (i >= s.size() || s[i] != '"') return false;
        for (++i; i < s.size(); ++i) {
            if (s[i] == '\\') { ++i; continue; }
            if (s[i] == '"') { ++i; return true; }
            if (static_cast<unsigned char>(s[i]) < 0x20) return false;
        }
        return false;
    }
    bool num() {
        size_t st = i;
        if (i < s.size() && s[i] == '-') ++i;
        while (i < s.size() && (std::isdigit(static_cast<unsigned char>(s[i])) || s[i] == '.' || s[i] == 'e' || s[i] == 'E' || s[i] == '+' || s[i] == '-')) ++i;
        return i > st;
    }
    bool value() {
        ws();
        if (i >= s.size()) return false;
        char ch = s[i];
        bool ok;
        if (ch == '{') {
            ++i; ws();
            if (i < s.size() && s[i] == '}') { ++i; return true; }
            for (;;) {
                ws();
                if (!str()) return false;
                ws();
                if (i >= s.size() || s[i++] != ':') return false;
                if (!value()) return false;
                ws();
                if (i < s.size() && s[i] == ',') { ++i; continue; }
                if (i < s.size() && s[i] == '}') { ++i; return true; }
                return false;
            }
        } else if (ch == '[') {
            ++i; ws();
            if (i < s.size() && s[i] == ']') { ++i; return true; }
            for (;;) {
                if (!value()) return false;
                ws();
                if (i < s.size() && s[i] == ',') { ++i; continue; }
                if (i < s.size() && s[i] == ']') { ++i; return true; }
                return false;
            }
        } else if (ch == '"') ok = str();
        else if (ch == 't') ok = lit("true");
        else if (ch == 'f') ok = lit("false");
        else if (ch == 'n') ok = lit("null");
        else ok = num();
        return ok;
    }
    bool valid() { if (!value()) return false; ws(); return i == s.size(); }
};

int main() {
    std::srand(12345);

    // --- default bindings are unique (the collision rules rely on it)
    {
        std::set<std::string> seen;
        for (auto const& sp : cfg::specs()) {
            if (!cfg::isBind(sp.key) || sp.sdef.empty()) continue;
            int vk = 0, mods = 0;
            CHECK(cfg::parseKey(sp.sdef, vk, mods));
            std::string k = cfg::keyName(vk, mods);
            if (seen.count(k)) std::printf("duplicate default key %s (%s)\n", k.c_str(), sp.key.c_str());
            CHECK(!seen.count(k));
            seen.insert(k);
        }
    }

    // --- removed in v2.88.4: the practice-mode settings no longer exist
    CHECK(cfg::findSpec("practice-only") == nullptr);
    CHECK(cfg::findSpec("auto-release-normal-runs") == nullptr);
    CHECK(cfg::findSpec("fix-practice-queue") == nullptr);
    CHECK(cfg::findSpec("bind.toggle:practice-only") == nullptr);

    // --- swap of two defaults (finding 1): stage F6 <-> export F8
    {
        Loaded l(R"({"config-version":3,"bind.stage":"F8","bind.export":"F6"})");
        CHECK(l.c.s("bind.stage") == "F8");
        CHECK(l.c.s("bind.export") == "F6");
        CHECK(l.c.loadNote().empty());
        // the saved file keeps the swap on the next load
        cfg::Config again;
        again.init(l.dir);
        CHECK(again.s("bind.stage") == "F8");
        CHECK(again.s("bind.export") == "F6");
    }

    // --- three-way rotation: stage F6 -> F8, export F8 -> F12, stats F12 -> F6
    {
        Loaded l(R"({"config-version":3,"bind.stats":"F6","bind.stage":"F8","bind.export":"F12"})");
        CHECK(l.c.s("bind.stage") == "F8");
        CHECK(l.c.s("bind.export") == "F12");
        CHECK(l.c.s("bind.stats") == "F6");
        CHECK(l.c.loadNote().empty());
    }

    // --- a file binding that collides with a default the file did not set: rejected, default kept, nothing unbound
    {
        Loaded l(R"({"config-version":3,"bind.stage":"F12"})");
        CHECK(l.c.s("bind.stage") == "F6");
        CHECK(l.c.s("bind.stats") == "F12");
        CHECK(has(l.c.loadNote(), "stage can't use F12"));
        CHECK(has(l.c.loadNote(), "already used by stats"));
        CHECK(has(l.c.loadNote(), "kept F6"));
    }

    // --- two file bindings with the same key: the first in table order wins, the other goes back to its default
    {
        Loaded l(R"({"config-version":3,"bind.export":"F2","bind.stage":"F2"})");
        CHECK(l.c.s("bind.stage") == "F2");   // stage comes before export in the table
        CHECK(l.c.s("bind.export") == "F8");
        CHECK(has(l.c.loadNote(), "export can't use F2"));
        CHECK(has(l.c.loadNote(), "kept F8"));
    }

    // --- ...and is unbound when its default is taken by another file binding
    {
        Loaded l(R"({"config-version":3,"bind.stage":"F2","bind.export":"F2","bind.path":"F8"})");
        CHECK(l.c.s("bind.stage") == "F2");
        CHECK(l.c.s("bind.path") == "F8");
        CHECK(l.c.s("bind.export") == "");
        CHECK(has(l.c.loadNote(), "export can't use F2"));
        CHECK(has(l.c.loadNote(), "now unbound"));
        // no key is used twice after a load
        std::set<std::string> seen;
        for (auto const& sp : cfg::specs()) {
            if (!cfg::isBind(sp.key)) continue;
            std::string k = l.c.s(sp.key.c_str());
            if (k.empty()) continue;
            CHECK(!seen.count(k));
            seen.insert(k);
        }
    }

    // --- a rejected binding whose own default is taken by a file binding becomes unbound
    {
        Loaded l(R"({"config-version":3,"bind.stage":"F12","bind.export":"F6"})");
        CHECK(l.c.s("bind.export") == "F6");
        CHECK(l.c.s("bind.stats") == "F12");
        CHECK(l.c.s("bind.stage") == "");
        CHECK(has(l.c.loadNote(), "now unbound"));
    }

    // --- malformed binding text keeps the default (and takes part in no conflict)
    {
        Loaded l(R"({"config-version":3,"bind.stage":"Hyper+Banana","bind.export":"F6"})");
        CHECK(l.c.s("bind.stage") == "F6");    // bad text: stage keeps its default, which counts as not set by the file
        CHECK(l.c.s("bind.export") == "F8");   // so export's F6 is rejected like any clash with an untouched default
        Loaded m(R"({"config-version":3,"bind.stage":"Hyper+Banana"})");
        CHECK(m.c.s("bind.stage") == "F6");
        CHECK(m.c.loadNote().empty());
    }

    // --- key names are normalized before comparing ("shift+f6" == "Shift+F6")
    {
        Loaded l(R"({"config-version":3,"bind.stage":"shift+f6"})");
        CHECK(l.c.s("bind.stage") == "Shift+F6" || l.c.s("bind.stage") == "F6");
        CHECK(l.c.s("bind.stage-back") == "Shift+F6");
    }

    // --- unbound "" is accepted and never collides
    {
        Loaded l(R"({"config-version":3,"bind.stage":"","bind.export":""})");
        CHECK(l.c.s("bind.stage") == "");
        CHECK(l.c.s("bind.export") == "");
        CHECK(l.c.loadNote().empty());
    }

    // --- duplicate JSON keys: the last one wins
    {
        Loaded l(R"({"config-version":3,"stage":3,"stage":5,"bind.stage":"F2","bind.stage":"F3"})");
        CHECK(l.c.i("stage") == 5);
        CHECK(l.c.s("bind.stage") == "F3");
    }

    // --- live set() still rejects a collision against the current values
    {
        Loaded l(R"({"config-version":3})");
        CHECK(!l.c.set("bind.stage", cfg::Val::text("F8")).empty());
        CHECK(l.c.s("bind.stage") == "F6");
        CHECK(l.c.set("bind.stage", cfg::Val::text("F2")).empty());
        CHECK(l.c.s("bind.stage") == "F2");
        CHECK(l.c.set("bind.stage", cfg::Val::text("")).empty());
        cfg::Val v = cfg::Val::text("F12");
        CHECK(!l.c.validate("bind.export", v).empty());
    }

    // --- stage migration (unchanged): v1/v2 stages move up by one, v3 is kept, bad values keep the default
    {
        Loaded a(R"({"stage":3})");
        CHECK(a.c.i("stage") == 4);
        CHECK(has(a.c.loadNote(), "upgraded from an older version"));
        Loaded b(R"({"config-version":2,"stage":10})");
        CHECK(b.c.i("stage") == 11);
        Loaded c(R"({"config-version":2,"stage":0})");
        CHECK(c.c.i("stage") == 1);
        Loaded d(R"({"config-version":2,"stage":11})");
        CHECK(d.c.i("stage") == cfg::STAGE_DEFAULT);
        Loaded e(R"({"config-version":1,"stage":2.5})");
        CHECK(e.c.i("stage") == cfg::STAGE_DEFAULT);
        Loaded f(R"({"config-version":3,"stage":11})");
        CHECK(f.c.i("stage") == 11);
        CHECK(f.c.loadNote().empty());
        Loaded g(R"({"config-version":3,"stage":0})");
        CHECK(g.c.i("stage") == 0);
        CHECK(e.c.i("config-version") == cfg::CONFIG_VERSION);
    }

    // --- an upgrade note and a key-conflict note are both kept
    {
        Loaded l(R"({"config-version":2,"bind.stage":"F12"})");
        CHECK(has(l.c.loadNote(), "upgraded"));
        CHECK(has(l.c.loadNote(), "stage can't use F12"));
    }

    // --- old practice-mode keys in a config are ignored without breaking the load
    {
        Loaded l(R"({"config-version":3,"practice-only":true,"auto-release-normal-runs":true,"fix-practice-queue":true,"bind.toggle:practice-only":"Shift+F10","stage":4})");
        CHECK(l.c.i("stage") == 4);
        CHECK(!has(l.c.dump(), "practice-only"));
    }

    // --- unreadable config.json falls back to config.bak.json, conflicts there are still reported
    {
        fs::path d = tempDir();
        std::ofstream(d / "config.json", std::ios::binary) << "{ not json";
        std::ofstream(d / "config.bak.json", std::ios::binary) << R"({"config-version":3,"bind.stage":"F12","stage":5})";
        cfg::Config c;
        c.init(d);
        CHECK(c.i("stage") == 5);
        CHECK(has(c.loadNote(), "restored the last valid copy"));
        CHECK(has(c.loadNote(), "stage can't use F12"));
        std::error_code ec;
        fs::remove_all(d, ec);
    }

    // --- schema: valid JSON with every section the app reads
    {
        std::string s = cfg::config().schema("v2.88.4");
        Json j{ s };
        CHECK(j.valid());
        for (char const* k : { "\"settings\"", "\"actions\"", "\"stages\"", "\"presets\"", "\"customSlots\"", "\"perLevel\"",
                               "\"configVersion\"", "\"stageMax\"", "\"customMaxTicks\"", "\"customMaxWaveTicks\"", "\"modVersion\":\"v2.88.4\"" }) {
            if (!has(s, k)) std::printf("schema misses %s\n", k);
            CHECK(has(s, k));
        }
        CHECK(!has(s, "practice-only"));
        CHECK(!has(s, "auto-release-normal-runs"));
        CHECK(!has(s, "fix-practice-queue"));
        CHECK(!has(s, "legit"));
    }

    // --- static tables agree with the settings table
    {
        CHECK(cfg::STAGE_TABLE[cfg::STAGE_MAX].enabled == false);
        CHECK(cfg::STAGE_DEFAULT >= 0 && cfg::STAGE_DEFAULT <= cfg::STAGE_MAX);
        for (int i = 0; i < cfg::PRESET_COUNT; ++i) {
            auto const& p = cfg::PRESETS[i];
            CHECK(p.stage >= 0 && p.stage < cfg::STAGE_MAX);
            CHECK(cfg::STAGE_TABLE[p.stage].enabled && !cfg::STAGE_TABLE[p.stage].custom);
            for (auto const& st : p.sets) {
                auto const* sp = cfg::findSpec(st.key);
                CHECK(sp != nullptr && sp->kind == cfg::Kind::Bool);
            }
        }
        for (auto const* k : cfg::PROFILE_KEYS) {
            auto const* sp = cfg::findSpec(k);
            CHECK(sp != nullptr && sp->kind == cfg::Kind::Bool);
        }
        for (int i = 0; i < cfg::CUSTOM_SLOT_COUNT; ++i) {
            CHECK(cfg::STAGE_TABLE[cfg::STAGE_MAX - cfg::CUSTOM_SLOT_COUNT + i].custom);
            CHECK(cfg::findSpec(cfg::CUSTOM_TICKS_KEYS[i]) != nullptr);
            CHECK(cfg::findSpec(cfg::CUSTOM_WAVE_KEYS[i]) != nullptr);
            CHECK(cfg::findSpec(cfg::CUSTOM_NAME_KEYS[i]) != nullptr);
        }
    }

    // v2.88.5: the black console window is off by default; the console link (the app's live view) stays on
    {
        auto const* w = cfg::findSpec("console-window");
        auto const* l = cfg::findSpec("console-link");
        CHECK(w != nullptr && w->kind == cfg::Kind::Bool && w->def == 0);
        CHECK(l != nullptr && l->kind == cfg::Kind::Bool && l->def == 1);
    }

    if (failures) {
        std::printf("%d check(s) failed\n", failures);
        return 1;
    }
    std::printf("cfg_test: all checks passed\n");
    return 0;
}
