// schema-dump: prints the mod's complete schema (cfg::Config::schema) to a file. Built by the app workflow
// from src/cfg.hpp (which is pure C++), so the app's bundled schema can never drift from the mod.
//   schema-dump <output-file> [mod-version]
#include <cstdio>
#include <fstream>
#include "../src/cfg.hpp"

int main(int argc, char** argv) {
    if (argc < 2) {
        std::fprintf(stderr, "usage: schema-dump <output-file> [mod-version]\n");
        return 2;
    }
    std::string text = cfg::config().schema(argc > 2 ? argv[2] : "") + "\n";
    std::ofstream out(argv[1], std::ios::binary | std::ios::trunc);
    out.write(text.data(), static_cast<std::streamsize>(text.size()));
    out.flush();
    return out.good() ? 0 : 1;
}
