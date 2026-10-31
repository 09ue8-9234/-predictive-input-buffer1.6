#include "../src/prediction_policy.hpp"
#include <cstdio>
#include <cstdlib>

static int failures = 0;
#define CHECK(c) do { if (!(c)) { std::printf("FAIL line %d: %s\n", __LINE__, #c); ++failures; } } while (0)

struct FakePlayer { bool m_isDart = false; bool m_isUpsideDown = false; };
struct FakeOrb { int id; };

int main() {
    using namespace prediction_policy;

    // orb press pass-through
    CHECK(passOrbPress(true, true, true, true));
    CHECK(!passOrbPress(false, true, true, true));   // setting off: old behaviour
    CHECK(!passOrbPress(true, false, true, true));   // a release is never passed
    CHECK(!passOrbPress(true, true, false, true));   // not touching an orb
    CHECK(!passOrbPress(true, true, true, false));   // an earlier input is still queued: keep the order

    // same orb
    FakeOrb a{1}, b{2};
    CHECK(sameOrb(&a, &a));
    CHECK(!sameOrb(&a, &b));
    CHECK(!sameOrb<FakeOrb>(nullptr, nullptr));
    CHECK(!sameOrb<FakeOrb>(&a, nullptr));
    CHECK(usesTargetOrb(true, true, &a, &a));
    CHECK(!usesTargetOrb(true, true, &a, &b));       // used a different orb: doesn't count
    CHECK(!usesTargetOrb(true, false, &a, &a));
    CHECK(usesTargetOrb(false, true, &a, &b));       // setting off: any orb counts, as before
    CHECK(!usesTargetOrb(false, false, &a, &a));

    // fallback
    CHECK(orbFallbackBetter(true, true, 50, 40));
    CHECK(!orbFallbackBetter(true, false, 50, 40));  // other orb never wins
    CHECK(!orbFallbackBetter(true, true, 40, 40));
    CHECK(orbFallbackBetter(false, false, 50, 40));  // setting off: only the crash time counts

    // max delay
    CHECK(maxDelayTicks(false, false, false, false, 8, 8, 4) == 8);          // cube, stage
    CHECK(maxDelayTicks(true, false, false, false, 8, 4, 4) == 4);           // wave uses the wave window
    CHECK(maxDelayTicks(false, true, false, false, 32, 16, 4) == 4);         // ship keeps its own cap (old behaviour)
    CHECK(maxDelayTicks(false, true, false, false, 32, 16, 99) == 16);       // clamped to 16
    CHECK(maxDelayTicks(false, true, false, true, 32, 16, 4) == 32);         // ship follows the stage
    CHECK(maxDelayTicks(false, true, false, true, 2, 2, 4) == 4);            // ...but never below its own delay
    CHECK(maxDelayTicks(false, false, true, false, 8, 8, 4) == UNLIMITED_TICKS);
    CHECK(maxDelayTicks(true, false, true, false, 8, 0, 4) == UNLIMITED_TICKS);   // wave off in the stage: unlimited wins
    CHECK(maxDelayTicks(false, true, true, false, 8, 8, 4) == 4);            // unlimited alone doesn't change ship
    CHECK(maxDelayTicks(false, true, true, true, 8, 8, 4) == UNLIMITED_TICKS);    // ship follows unlimited
    CHECK(maxDelayTicks(false, false, false, false, 1, 1, 4) == 1);          // 0.25f stage

    // inverted wave detection
    FakePlayer p1, p2;
    CHECK(!invertedWave(&p1, &p2, false));
    p1.m_isDart = true;
    CHECK(!invertedWave(&p1, &p2, false));
    p1.m_isUpsideDown = true;
    CHECK(invertedWave(&p1, &p2, false));
    p1 = {}; p2.m_isDart = true; p2.m_isUpsideDown = true;
    CHECK(!invertedWave(&p1, &p2, false));           // P2 only counts in dual mode
    CHECK(invertedWave(&p1, &p2, true));
    CHECK(!invertedWave<FakePlayer>(nullptr, nullptr, true));

    // wave numbers (callers pass st() scaled values; here the 240/s values)
    CHECK(waveSpamGap(true, 24, true, 6) == 6);
    CHECK(waveSpamGap(true, 4, true, 6) == 4);       // never raises the gap
    CHECK(waveSpamGap(true, 24, false, 6) == 24);
    CHECK(waveSpamGap(false, 24, true, 6) == 24);    // setting off
    CHECK(waveMinimumTurn(true, true, 6, 12) == 12);
    CHECK(waveMinimumTurn(true, false, 6, 12) == 6);
    CHECK(waveMinimumTurn(false, true, 6, 12) == 6);
    CHECK(waveEscape(true, true, 16, 24) == 24);
    CHECK(waveEscape(false, true, 16, 24) == 16);
    CHECK(waveTarget(true, true, 65, 10, 12) == 65);
    CHECK(waveTarget(true, false, 65, 10, 12) == 22);
    CHECK(waveTarget(false, true, 65, 10, 12) == 22);
    CHECK(waveTarget(true, false, 20, 10, 12) == 20);

    static_assert(passOrbPress(true, true, true, true), "constexpr");
    static_assert(maxDelayTicks(false, false, true, false, 1, 1, 4) == 24000, "constexpr");
    std::printf(failures ? "FAILED (%d)\n" : "all prediction_policy tests passed\n", failures);
    return failures ? 1 : 0;
}
