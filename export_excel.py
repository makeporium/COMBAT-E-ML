import os
import csv
import sys
import openpyxl
from openpyxl.styles import Font, PatternFill, Alignment, Border, Side
from openpyxl.utils import get_column_letter

CSV_FILE = "gameplay_telemetry.csv"
XLSX_FILE = "gameplay_telemetry.xlsx"

VARIABLE_DICTIONARY = [
    # Encounter & Time
    ("game_id", "Encounter", "TelemetryManager", "Integer", ">= 1", "Sequential attempt/game counter. Increments each time the fight resets or starts fresh across play sessions."),
    ("timestamp", "Encounter", "ITimeService / Time.time", "Float", ">= 0.0 (Seconds)", "Elapsed combat time (in seconds) since the start of the current match attempt."),
    ("frame", "Encounter", "Time.frameCount", "Integer", ">= 0", "Unity engine render/simulation frame count when this telemetry snapshot was captured."),
    ("encounter_status", "Encounter", "TelemetryManager", "String", "Fighting, BossWon, PlayerWon, Reset", "Current status of the boss fight (Fighting = active combat, BossWon = player defeated, PlayerWon = boss defeated)."),
    
    # Spatial & Physical
    ("distance_to_boss", "Spatial", "Player & Boss Transform", "Float", ">= 0.0 (Meters)", "Euclidean distance between player and boss in the arena (in meters)."),
    ("relative_angle_deg", "Spatial", "Transform.forward & Vector3.SignedAngle", "Float", "-180.0 to +180.0 (Degrees)", "Relative angle from boss forward vector to player position. 0 = directly in front, +/-180 = directly behind."),
    
    # Player Vitals & State
    ("player_state", "Player Vitals", "PlayerController (StateMachine)", "String", "PlayerIdleState, PlayerMoveState, PlayerAttackState, etc.", "Active state machine state of the player character."),
    ("player_hp", "Player Vitals", "PlayerController.Health", "Float", "0.0 to 100.0 (HP)", "Current player hit points remaining."),
    ("player_hp_pct", "Player Vitals", "PlayerController.Health.Normalized", "Float", "0.0 to 1.0", "Normalized player hit points percentage (1.0 = 100% full health)."),
    ("player_stamina", "Player Vitals", "PlayerController.Stamina", "Float", "0.0 to 100.0 (Stamina)", "Current player stamina available for dashing, attacking, and guarding."),
    ("player_stamina_pct", "Player Vitals", "PlayerController.Stamina.Normalized", "Float", "0.0 to 1.0", "Normalized player stamina percentage (1.0 = full stamina)."),
    ("player_posture", "Player Vitals", "PlayerController.Posture", "Float", "0.0 to 100.0 (Posture)", "Player guard/posture meter. Reaching zero results in guard break / stagger."),
    ("player_posture_pct", "Player Vitals", "PlayerController.Posture.Normalized", "Float", "0.0 to 1.0", "Normalized player posture meter (1.0 = unbroken guard)."),
    ("player_is_guarding", "Player Vitals", "PlayerController.IsGuarding", "Binary Flag", "0 or 1", "Whether the player is currently holding the guard/deflect stance (1 = True, 0 = False)."),
    ("player_is_invulnerable", "Player Vitals", "PlayerController.IsInvulnerable", "Binary Flag", "0 or 1", "Whether the player has active invincible frames (i-frames) during a dash or recovery."),
    
    # Boss Vitals & State
    ("boss_state", "Boss Vitals", "BossController (StateMachine)", "String", "BossIdleState, BossApproachState, BossAttackState, etc.", "Active state machine state of the boss AI."),
    ("boss_action", "Boss Vitals", "BossController.CurrentAttack", "String", "None, Ground Slam, Cataclysmic Wave, etc.", "Exact name of the specific attack, telegraph, or action currently being executed by the boss."),
    ("boss_hp", "Boss Vitals", "BossController.Health", "Float", "0.0 to 250.0 (HP)", "Current boss hit points remaining."),
    ("boss_hp_pct", "Boss Vitals", "BossController.Health.Normalized", "Float", "0.0 to 1.0", "Normalized boss hit points percentage (1.0 = 100% full health)."),
    ("boss_poise", "Boss Vitals", "BossController.Poise", "Float", "0.0 to 100.0 (Poise)", "Boss poise / stagger threshold meter. Drained by player deflects and heavy attacks."),
    ("boss_poise_pct", "Boss Vitals", "BossController.Poise.Normalized", "Float", "0.0 to 1.0", "Normalized boss poise meter (1.0 = full poise)."),
    ("boss_phase", "Boss Vitals", "BossController.PhaseIndex", "Integer", "0 or 1", "Combat phase index of the boss encounter (0 = Phase 1, 1 = Phase 2 empowered)."),
    
    # 12 In-Game Adaptive Habit Features (BehaviorProfile)
    ("feature_attack_frequency", "Adaptation Feature", "BehaviorFeature.AttackFrequency", "Float", "0.0 to 1.0", "Measured pace of player attack inputs relative to the sustainable maximum attack rate."),
    ("feature_heavy_attack_ratio", "Adaptation Feature", "BehaviorFeature.HeavyAttackRatio", "Float", "0.0 to 1.0", "Proportion of the player's attacks that are heavy/committed strikes rather than lights."),
    ("feature_post_dodge_aggression", "Adaptation Feature", "BehaviorFeature.PostDodgeAggression", "Float", "0.0 to 1.0", "Probability that the player immediately throws an attack right out of a dodge/dash."),
    ("feature_dash_direction_bias", "Adaptation Feature", "BehaviorFeature.DashDirectionBias", "Float", "0.0 to 1.0", "How predictable/lopsided player dodge directions are (e.g. consistently dodging left/backward)."),
    ("feature_preferred_distance", "Adaptation Feature", "BehaviorFeature.PreferredDistance", "Float", "0.0 to 1.0", "Player's habitual engagement distance normalized across arena dimensions (high = stays far away)."),
    ("feature_aggression", "Adaptation Feature", "BehaviorFeature.Aggression", "Float", "0.0 to 1.0", "Player pressure balance: ratio of closing distance vs retreating (high = relentless pressuring)."),
    ("feature_whiff_ratio", "Adaptation Feature", "BehaviorFeature.WhiffRatio", "Float", "0.0 to 1.0", "Share of player attacks that miss completely without hitting the boss (indicates panic swinging)."),
    ("feature_heal_frequency", "Adaptation Feature", "BehaviorFeature.HealFrequency", "Float", "0.0 to 1.0", "How often the player commits to healing items/flasks during active combat."),
    ("feature_dodge_timing_consistency", "Adaptation Feature", "BehaviorFeature.DodgeTimingConsistency", "Float", "0.0 to 1.0", "How reliably the player dodges at the exact same beat/frame during boss wind-ups."),
    ("feature_evasion_success", "Adaptation Feature", "BehaviorFeature.EvasionSuccess", "Float", "0.0 to 1.0", "Proportion of boss attacks cleanly avoided by the player (overall player evasion proficiency)."),
    ("feature_guard_reliance", "Adaptation Feature", "BehaviorFeature.GuardReliance", "Float", "0.0 to 1.0", "How frequently the player chooses to block/turtle behind guard rather than dodge attacks."),
    ("feature_deflect_skill", "Adaptation Feature", "BehaviorFeature.DeflectSkill", "Float", "0.0 to 1.0", "Deflect precision: fraction of guards timed perfectly on the impact beat vs standard blocking."),
    
    # 10 In-Game Boss Dynamic Tuning Parameters (BossTuning)
    ("tuning_attack_delay", "Boss Tuning", "BossTuningParameter.AttackDelay", "Float", "0.0 to 3.0 (Seconds)", "Extra telegraph hold time inserted before releasing attacks, adapted to bait and punish panic rolls."),
    ("tuning_preferred_range", "Boss Tuning", "BossTuningParameter.PreferredRange", "Float", "0.0 to 5.0 (Meters)", "Distance the boss AI tries to maintain when positioning or approaching the player."),
    ("tuning_parry_chance", "Boss Tuning", "BossTuningParameter.ParryChance", "Float", "0.0 to 1.0 (Probability)", "Dynamic probability the boss enters parry stance when anticipating player heavy attacks."),
    ("tuning_gap_closer_weight", "Boss Tuning", "BossTuningParameter.GapCloserWeight", "Float", "0.0 to 1.0 (Weight)", "AI action selection weight given to lunging and gap-closing attacks against distant players."),
    ("tuning_dodge_prediction_strength", "Boss Tuning", "BossTuningParameter.DodgePredictionStrength", "Float", "0.0 to 1.0 (Bias)", "How strongly the boss aims its swings to intercept the player's favoured dodge direction."),
    ("tuning_aggression", "Boss Tuning", "BossTuningParameter.Aggression", "Float", "0.0 to 1.0 (Weight)", "Overall willingness of the boss to press attacks continuously instead of circling/recovering."),
    ("tuning_combo_extension_chance", "Boss Tuning", "BossTuningParameter.ComboExtensionChance", "Float", "0.0 to 1.0 (Probability)", "Likelihood the boss chains an additional follow-up strike into a combo sequence."),
    ("tuning_feint_chance", "Boss Tuning", "BossTuningParameter.FeintChance", "Float", "0.0 to 1.0 (Probability)", "Probability the boss cancels a wind-up telegraph into a feint to punish parry-masters."),
    ("tuning_area_denial_weight", "Boss Tuning", "BossTuningParameter.AreaDenialWeight", "Float", "0.0 to 1.0 (Weight)", "Weight given to sweeping ground-slam and shockwave attacks that deny safe arena space."),
    ("tuning_unblockable_weight", "Boss Tuning", "BossTuningParameter.UnblockableWeight", "Float", "0.0 to 1.0 (Weight)", "Weight given to guard-piercing perilous attacks to counter players who turtle behind block."),
    
    # Cumulative Event Tallies
    ("count_player_attacks", "Combat Counts", "State & Combat Events", "Integer", ">= 0", "Cumulative number of attack swings executed by the player in this game."),
    ("count_player_dodges", "Combat Counts", "State & Combat Events", "Integer", ">= 0", "Cumulative number of dodges/dashes executed by the player in this game."),
    ("count_player_deflects", "Combat Counts", "Combat Events", "Integer", ">= 0", "Cumulative count of successful timed deflects performed by the player in this game."),
    ("count_player_blocks", "Combat Counts", "Combat Events", "Integer", ">= 0", "Cumulative count of standard blocked attacks absorbed by the player in this game."),
    ("count_player_hits_taken", "Combat Counts", "Health & Combat Events", "Integer", ">= 0", "Cumulative number of damaging hits received by the player from boss attacks in this game."),
    ("count_boss_attacks", "Combat Counts", "State & Combat Events", "Integer", ">= 0", "Cumulative number of attacks launched by the boss in this game."),
    ("count_boss_parries", "Combat Counts", "State & Combat Events", "Integer", ">= 0", "Cumulative number of successful parries performed by the boss in this game."),
    ("count_boss_hits_taken", "Combat Counts", "Health & Combat Events", "Integer", ">= 0", "Cumulative number of damaging hits received by the boss from player attacks in this game.")
]

def export_to_excel():
    script_dir = os.path.dirname(os.path.abspath(__file__))
    csv_path = os.path.join(script_dir, CSV_FILE)
    xlsx_path = os.path.join(script_dir, XLSX_FILE)

    if not os.path.exists(csv_path):
        print(f"[export_excel] CSV file not found: {csv_path}")
        return

    wb = openpyxl.Workbook()

    # --- SHEET 1: Gameplay Data ---
    ws_data = wb.active
    ws_data.title = "Gameplay_Data"
    ws_data.views.sheetView[0].showGridLines = True

    header_font = Font(name="Segoe UI", size=11, bold=True, color="FFFFFF")
    header_fill = PatternFill(start_color="1F4E78", end_color="1F4E78", fill_type="solid")
    center_align = Alignment(horizontal="center", vertical="center")
    left_align = Alignment(horizontal="left", vertical="center")
    right_align = Alignment(horizontal="right", vertical="center")
    thin_border = Border(
        left=Side(style="thin", color="D9D9D9"),
        right=Side(style="thin", color="D9D9D9"),
        top=Side(style="thin", color="D9D9D9"),
        bottom=Side(style="thin", color="D9D9D9")
    )
    alt_fill = PatternFill(start_color="F2F6FA", end_color="F2F6FA", fill_type="solid")

    with open(csv_path, "r", encoding="utf-8") as f:
        reader = csv.reader(f)
        row_idx = 1
        for row in reader:
            if not row:
                continue
            ws_data.append(row)
            is_header = (row_idx == 1)
            fill = header_fill if is_header else (alt_fill if row_idx % 2 == 0 else None)
            font = header_font if is_header else Font(name="Segoe UI", size=10)

            for col_idx, val in enumerate(row, start=1):
                cell = ws_data.cell(row=row_idx, column=col_idx)
                cell.font = font
                if fill:
                    cell.fill = fill
                cell.border = thin_border
                # Attempt to convert numeric cells
                if not is_header:
                    try:
                        if "." in val:
                            cell.value = float(val)
                            cell.alignment = right_align
                        else:
                            cell.value = int(val)
                            cell.alignment = right_align
                    except ValueError:
                        cell.alignment = left_align
                else:
                    cell.alignment = center_align
            row_idx += 1

    # Auto-adjust column widths
    for col in ws_data.columns:
        max_len = max(len(str(cell.value or '')) for cell in col)
        col_letter = get_column_letter(col[0].column)
        ws_data.column_dimensions[col_letter].width = max(max_len + 3, 12)

    # --- SHEET 2: Variable Dictionary ---
    ws_dict = wb.create_sheet(title="Variable_Dictionary")
    ws_dict.views.sheetView[0].showGridLines = True

    dict_header_font = Font(name="Segoe UI", size=11, bold=True, color="FFFFFF")
    dict_header_fill = PatternFill(start_color="203764", end_color="203764", fill_type="solid")
    category_fill = PatternFill(start_color="EAECEE", end_color="EAECEE", fill_type="solid")

    dict_headers = ["Variable Name", "Category", "In-Game Source / Component", "Data Type", "Valid Range / Units", "Detailed Meaning & Combat Context"]
    ws_dict.append(dict_headers)

    for col_idx in range(1, len(dict_headers) + 1):
        cell = ws_dict.cell(row=1, column=col_idx)
        cell.font = dict_header_font
        cell.fill = dict_header_fill
        cell.alignment = Alignment(horizontal="center", vertical="center", wrap_text=True)
        cell.border = thin_border

    ws_dict.row_dimensions[1].height = 28

    for row_idx, var_info in enumerate(VARIABLE_DICTIONARY, start=2):
        ws_dict.append(list(var_info))
        fill = alt_fill if row_idx % 2 == 0 else None
        for col_idx in range(1, len(var_info) + 1):
            cell = ws_dict.cell(row=row_idx, column=col_idx)
            cell.font = Font(name="Segoe UI", size=10, bold=(col_idx == 1))
            if fill:
                cell.fill = fill
            cell.border = thin_border
            if col_idx == 1:
                cell.alignment = Alignment(horizontal="left", vertical="center")
            elif col_idx in (2, 4):
                cell.alignment = Alignment(horizontal="center", vertical="center")
            elif col_idx == 6:
                cell.alignment = Alignment(horizontal="left", vertical="center", wrap_text=True)
            else:
                cell.alignment = Alignment(horizontal="left", vertical="center")

    dict_widths = [32, 20, 32, 16, 24, 80]
    for idx, width in enumerate(dict_widths, start=1):
        col_letter = get_column_letter(idx)
        ws_dict.column_dimensions[col_letter].width = width

    wb.save(xlsx_path)
    print(f"[export_excel] Successfully generated '{xlsx_path}' with 2 sheets: Gameplay_Data and Variable_Dictionary.")

if __name__ == "__main__":
    export_to_excel()
