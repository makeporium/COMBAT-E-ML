# The ML Boss, Explained Simply

## The short version

The project began with a rule-based boss: a fixed set of `if/else` decisions. It could choose a sensible move for distance or a visible player action, but it always made the same decision in the same situation.

The current default boss is a **utility-based adaptive agent**. It starts with values learned from the playtest telemetry, watches what the current player actually does, scores its available attack types, then chooses a good attack with a little controlled variation. It is designed to stop repeating moves that are being dodged or deflected.

It is therefore real adaptive game AI, but it is important to describe it accurately: the Unity game is **not running the Python classifier itself**. The Python/data pipeline produces aggregate priors and metrics. Unity loads those priors and runs a small, explainable utility model plus online updates during the fight.

## Why this exists

The main gameplay question is not “can the boss attack?” The rule-based version could already do that. The question is: **can the boss notice a repeated player habit and change its tactics fairly?**

Examples:

| Player habit | What the ML boss should infer | Intended response |
|---|---|---|
| Frequently deflects melee | Melee has high counter/parry risk | Use a different opener, range tool, or guard-crusher |
| Repeatedly dodges | Direct melee is less likely to connect | Vary timing or choose area/range pressure |
| Holds Guard | The player is vulnerable to a guard-crusher | Prefer `HeavyCrush` when it can reach |
| Stays far away | Closing the gap has future value | Prefer `GapCloser` instead of camping |

The boss does not read buttons or react instantly. It learns only from combat events that are visible to a spectator, and decisions use the delayed player observation supplied by the perception system.

## What happened before ML

1. The original `RuleBasedBossBrain.cs` acted as the baseline expert system.
2. Playtest sessions were collected as telemetry: position, health, stamina, posture, actions, defensive events, boss actions, and outcomes.
3. The data work in `everything after getting the dataset/` cleaned the raw logs, removed inactive/constant columns and leakage fields, and produced a 3,444-row active-combat dataset.
4. Python analysis estimated outcome priors for four tactical action archetypes and measured the offline models.
5. Those priors were exported to `Assets/StreamingAssets/utility_agent_config.json`, which ships with the game and is read by `MLBossBrain.cs`.

The historical docs report a cloning-model accuracy of 77.38%, plus outcome-model metrics of ROC-AUC 0.8189 for hit value and 0.9015 for parry risk. Those are offline dataset results, not a claim that the Unity runtime is executing those exact trained classifier objects.

## What Unity actually does during a fight

The boss groups attacks into four archetypes:

| Archetype | Typical moves | Why it matters |
|---|---|---|
| `MeleeStrike` | sweep, jab, normal combo | Best nearby, but easiest to deflect |
| `RangedThreat` | wave, shockwave | Pressure at range; not parryable |
| `GapCloser` | charge, thrust, lunge | Stops long-range stalling and sets up a follow-up |
| `HeavyCrush` | slam, overhead | High reward against a guard or vulnerable player |

For each available attack, `MLBossBrain` estimates a utility:

`utility = hit chance × damage − parry risk × posture penalty − stamina cost + tactical bonuses − cooldown/repeat penalties`

The choice also considers range, a short look-ahead bonus for gap-closing, cooldowns for waves/slams, and a repetition penalty. It samples from a softmax distribution rather than always selecting the maximum, so the boss remains readable and does not become a deterministic spam machine.

During the match it keeps five-second rolling rates for player attacks, dodges and successful deflects. Successful boss hits increase the relevant hit belief. A parried boss attack decreases its hit belief and raises that archetype’s parry-risk belief. This is the online-learning part.

## Is it working?

**Yes, with an important qualification.** The ML mode is the active default (`BossAIMode.MachineLearning`), creates `MLBossBrain`, loads the StreamingAssets configuration, receives combat events, scores attacks, and updates beliefs from outcomes. `F2`/`M` switches between the rule-based baseline and ML mode for comparison.

Before this pass, three faults made its reading less trustworthy:

1. A deliberate player parry emitted both `Deflected` and `PlayerParried`; the ML cadence tracker counted both, inflating the deflect rate.
2. A perfect dodge was counted once as a dash and again as a perfect-dodge action, inflating dodge cadence.
3. An attack that touched a target but was evaded, blocked, deflected, or parried could later also publish `AttackWhiffed`; the ML updater then penalized the same attack outcome twice.

These are corrected in the current source. A parry is now one defensive observation, a perfect dodge is one dodge with a separate quality outcome, and only an attack that contacted no valid target becomes a whiff.

## How to verify in play mode

The right-side **Combat Ledger** now makes the runtime state inspectable:

- `YOU • LAST ACTION` tells you which attack or defensive action the player just performed.
- `ML BOSS • LAST ACTION` displays the boss move and its inferred archetype.
- `ML LIVE` displays the current player-habit label, dodge/deflect cadence, chosen archetype, and utility score.

Test it by making several clean deflects against the same boss archetype. The deflect rate should rise by one per success, the ML display should classify a parry specialist once the threshold is reached, and repeated melee should become less attractive. Switch to Rule-Based with `F2` or `M` and the ledger will explicitly state that no live utility adaptation is active.

## Telemetry after the fixes

New sessions write `gameplay_telemetry_v2.csv`, rather than mixing the corrected schema into the legacy CSV. The v2 columns distinguish:

- normal dodges from perfect dodges;
- clean deflects from deliberate parry strikes;
- blocks, attacks, hits taken, boss attacks, boss parries, and boss hits taken.

The old `gameplay_telemetry.csv` and workbook remain untouched as historical material. Do not merge v1 and v2 rows without explicitly aligning their schemas.

## Limits and honest next steps

- The runtime policy uses telemetry-derived priors and online belief updates; it is not an embedded sklearn/ONNX classifier.
- Attack archetypes are currently inferred from attack names. An explicit archetype field on `AttackDefinition` would be safer as the move list grows.
- Runtime telemetry should be collected again after these counter fixes before retraining or reporting new evaluation metrics.
- A held Guard is intentionally not counted as a successful deflect; success is determined only when it actually meets an incoming attack.
