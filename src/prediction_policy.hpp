#pragma once

// v2.88.0: the decision rules added in this version, kept free of Geode includes so they can be
// compiled and tested without the game (tests/prediction_policy_test.cpp, run by the GitHub build).
// The orb pass-through / same-orb rules and the inverted-wave numbers follow BASU's
// "Click Before Frames" v2.84.0 (basu.input-buffer), the older version of this mod's lineage.
// All tick values here are in 240 steps/s ticks; callers scale them with st() where they need to.
#include <algorithm>

namespace prediction_policy {
    // Longest delay the "unlimited-delay" setting allows (100 s at 240 steps/s). The search is still
    // bounded by the prediction time budget; this only removes the stage's cap.
    constexpr int UNLIMITED_TICKS = 24000;

    // v2.89: with "unlimited-delay" on, every mode (cube, ship, wave, ufo, ...) looks this far ahead:
    // 128 frames at 60 fps = 512 ticks (the normal look-aheads are 130 ticks for cube / ball / spider,
    // 65 for the wave and 48 for ship / ufo / swing). Callers pass st()-scaled values.
    constexpr int UNLIMITED_LOOKAHEAD_TICKS = 512;

    // Look-ahead (ticks) for a prediction: the normal one, or the extended one when unlimited-delay is
    // on. Never shorter than the normal one, so a custom value above 128 frames still wins.
    constexpr int lookAheadTicks(bool unlimited, int normal, int extended) {
        return unlimited ? std::max(normal, extended) : normal;
    }

    // A press that already touches an orb is the player's own orb hit: it goes to the game unchanged,
    // but never past a delay that is still waiting (that would reorder the inputs).
    constexpr bool passOrbPress(bool enabled, bool down, bool touchingOrb, bool queueEmpty) {
        return enabled && down && touchingOrb && queueEmpty;
    }

    // The orb a timing uses must be the first orb ahead, not another one further along.
    template <typename T>
    constexpr bool sameOrb(T const* target, T const* used) {
        return target && target == used;
    }

    // With "same target" on, a timing only counts as using the orb if it used the TARGET orb.
    template <typename T>
    constexpr bool usesTargetOrb(bool sameTargetOnly, bool usedAnyOrb, T const* target, T const* used) {
        return sameTargetOnly ? (usedAnyOrb && sameOrb(target, used)) : usedAnyOrb;
    }

    // Fallback orb timing (nothing fully safe): a later crash is better, but only for the target orb.
    constexpr bool orbFallbackBetter(bool sameTargetOnly, bool usesTarget, int crashAt, int bestCrashAt) {
        return (!sameTargetOnly || usesTarget) && crashAt > bestCrashAt;
    }

    // Longest delay in ticks (before st() scaling) for the mode being played. `unlimited` removes the
    // cap for every mode, ship included (v2.89; before, the ship kept its own 0..16 limit). Without it
    // the ship keeps its own limit (clamped 0..16) unless `shipUsesStage`, in which case it may also use
    // the stage's delay (never less than its own).
    constexpr int maxDelayTicks(bool anyWave, bool anyShip, bool unlimited, bool shipUsesStage, int stageAll, int stageWave, int shipMax) {
        int all = unlimited ? UNLIMITED_TICKS : stageAll;
        int wave = unlimited ? UNLIMITED_TICKS : stageWave;
        int ship = unlimited ? UNLIMITED_TICKS : std::clamp(shipMax, 0, 16);
        if (shipUsesStage) {
            ship = std::max(ship, all);
        }
        return anyWave ? wave : (anyShip ? ship : all);
    }

    // Upside-down wave (P1, or P2 in dual mode).
    template <typename Player>
    bool invertedWave(Player const* p1, Player const* p2, bool dual) {
        return (p1 && p1->m_isDart && p1->m_isUpsideDown) || (dual && p2 && p2->m_isDart && p2->m_isUpsideDown);
    }

    // Inverted wave corridors need quicker turns: a shorter spam gap, a later minimum turn, a longer escape
    // and a full-safety target. `enabled` = the inverted-wave setting; off gives the normal values back.
    constexpr int waveSpamGap(bool enabled, int configuredGap, bool inverted, int invertedGap) {
        return enabled && inverted ? std::min(configuredGap, invertedGap) : configuredGap;
    }
    constexpr int waveMinimumTurn(bool enabled, bool inverted, int normalMinimum, int invertedMinimum) {
        return enabled && inverted ? invertedMinimum : normalMinimum;
    }
    constexpr int waveEscape(bool enabled, bool inverted, int normalEscape, int invertedEscape) {
        return enabled && inverted ? invertedEscape : normalEscape;
    }
    constexpr int waveTarget(bool enabled, bool inverted, int horizon, int base, int improvement) {
        return enabled && inverted ? horizon : std::min(horizon, base + improvement);
    }
}
