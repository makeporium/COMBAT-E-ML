import os
import shutil
import pandas as pd
import numpy as np

# Inputs
SOURCES = [
    {
        'path': r'c:\Users\astro\Desktop\GAME\gameplay_telemetry shivam new.csv',
        'player_tag': 'shivam_new',
        'display_name': 'Shivam (Session 2 - Deflect Focus)'
    },
    {
        'path': r'c:\Users\astro\Desktop\GAME\gameplay_telemetry sidd.csv',
        'player_tag': 'sidd',
        'display_name': 'Sidd (High Mobility & Parry)'
    },
    {
        'path': r'c:\Users\astro\Desktop\GAME\gameplay_telemetry_sivam.csv',
        'player_tag': 'sivam_old',
        'display_name': 'Sivam (Session 1 - Aggressive Brawler)'
    },
    {
        'path': r'c:\Users\astro\Desktop\GAME\gameplay_telemetry_swam.csv',
        'player_tag': 'swam',
        'display_name': 'Swam (Evasive Shinobi / Marathon Duels)'
    },
    {
        'path': r'C:\Users\astro\Desktop\College Ayush\GAME\adaptive-boss-arena-main\adaptive-boss-arena-main\gameplay_telemetry.csv',
        'player_tag': 'ayush_baseline',
        'display_name': 'Ayush (Developer Baseline)'
    }
]

ARCHETYPE_MAP = {
    'Wide Sweep': 'MeleeStrike',
    'Jab': 'MeleeStrike',
    'Cataclysmic Wave': 'RangedThreat',
    'Shockwave': 'RangedThreat',
    'Charge': 'GapCloser',
    'Ground Slam': 'HeavyCrush',
    'Perilous Overhead': 'HeavyCrush'
}

OUTPUT_DIRS = [
    r'C:\Users\astro\Desktop\GAME\adaptive-boss-arena-main\adaptive-boss-arena-main',
    r'C:\Users\astro\Desktop\College Ayush\GAME\adaptive-boss-arena-main\adaptive-boss-arena-main',
    r'C:\Users\astro\Desktop\GAME',
    r'C:\Users\astro\Desktop'
]

def compile_master_telemetry():
    print("=" * 80)
    print("COMPILING MASTER MULTI-PLAYER TELEMETRY WORKBOOK")
    print("=" * 80)

    all_dfs = []
    global_game_counter = 0
    player_summary_rows = []

    for src in SOURCES:
        p = src['path']
        tag = src['player_tag']
        dname = src['display_name']

        if not os.path.exists(p):
            print(f"Warning: File not found: {p}")
            continue

        df = pd.read_csv(p)
        raw_rows = len(df)
        local_games = sorted(df['game_id'].unique())
        num_games = len(local_games)

        # Mapping to global game IDs
        game_id_map = {lg: global_game_counter + idx + 1 for idx, lg in enumerate(local_games)}
        df['global_game_id'] = df['game_id'].map(game_id_map)
        df['player_source'] = tag
        df['tactical_archetype'] = df['boss_action'].map(ARCHETYPE_MAP).fillna('None/Idle')

        # Put global_game_id right next to original game_id
        cols = list(df.columns)
        cols.insert(1, cols.pop(cols.index('global_game_id')))
        cols.insert(2, cols.pop(cols.index('player_source')))
        cols.insert(19, cols.pop(cols.index('tactical_archetype')))
        df = df[cols]

        global_game_counter += num_games
        all_dfs.append(df)

        # Stats for summary sheet
        match_times = df.groupby('game_id')['timestamp'].max()
        outcomes = df[df['encounter_status'].isin(['PlayerWon', 'BossWon'])]['encounter_status'].value_counts()
        p_wins = outcomes.get('PlayerWon', 0)
        b_wins = outcomes.get('BossWon', 0)
        
        final_counters = df.groupby('game_id')[[
            'count_player_attacks', 'count_player_dodges', 'count_player_deflects', 
            'count_player_blocks', 'count_player_hits_taken', 'count_boss_attacks'
        ]].max()

        player_summary_rows.append({
            'Player Tag': tag,
            'Display Description': dname,
            'Total Recorded Frames': raw_rows,
            'Active Decision Frames': df['boss_action'].notna().sum(),
            'Total Matches': num_games,
            'Global Game Range': f"{min(game_id_map.values())} – {max(game_id_map.values())}",
            'Player Wins': p_wins,
            'Boss Wins': b_wins,
            'Player Win Rate (%)': round(p_wins / (p_wins + b_wins) * 100, 1) if (p_wins + b_wins) > 0 else 0,
            'Avg Match Length (s)': round(match_times.mean(), 1),
            'Max Match Length (s)': round(match_times.max(), 1),
            'Avg Distance to Boss (m)': round(df['distance_to_boss'].mean(), 2),
            'Guard Time (%)': round((df['player_is_guarding'] == 1).mean() * 100, 2),
            'Avg Attacks / Match': round(final_counters['count_player_attacks'].mean(), 1),
            'Avg Dodges / Match': round(final_counters['count_player_dodges'].mean(), 1),
            'Avg Deflects / Match': round(final_counters['count_player_deflects'].mean(), 1),
            'Avg Blocks / Match': round(final_counters['count_player_blocks'].mean(), 1),
            'Avg Hits Taken / Match': round(final_counters['count_player_hits_taken'].mean(), 1),
        })

        print(f"  ✓ Loaded '{tag}': {raw_rows} frames, {num_games} matches -> Global Game IDs #{min(game_id_map.values())} to #{max(game_id_map.values())}")

    master_df = pd.concat(all_dfs, ignore_index=True)
    summary_df = pd.DataFrame(player_summary_rows)

    # Boss action crosstab by player
    active_master = master_df[master_df['boss_action'].notna()]
    action_crosstab = pd.crosstab(active_master['boss_action'], active_master['player_source'], margins=True, margins_name='TOTAL')
    action_pct = pd.crosstab(active_master['boss_action'], active_master['player_source'], normalize='columns') * 100
    action_pct['TOTAL'] = active_master['boss_action'].value_counts(normalize=True) * 100
    action_pct = action_pct.round(2)

    # Archetype crosstab
    arch_crosstab = pd.crosstab(active_master['tactical_archetype'], active_master['player_source'], margins=True, margins_name='TOTAL')
    arch_pct = pd.crosstab(active_master['tactical_archetype'], active_master['player_source'], normalize='columns') * 100
    arch_pct['TOTAL'] = active_master['tactical_archetype'].value_counts(normalize=True) * 100
    arch_pct = arch_pct.round(2)

    primary_csv = os.path.join(OUTPUT_DIRS[0], 'master_telemetry.csv')
    primary_xlsx = os.path.join(OUTPUT_DIRS[0], 'master_telemetry.xlsx')

    print(f"\nWriting Master CSV: {primary_csv}")
    master_df.to_csv(primary_csv, index=False)

    print(f"Writing Master Excel: {primary_xlsx}")
    with pd.ExcelWriter(primary_xlsx, engine='openpyxl') as writer:
        master_df.to_excel(writer, sheet_name='Master_Telemetry', index=False)
        summary_df.to_excel(writer, sheet_name='Player_Summary', index=False)
        action_crosstab.to_excel(writer, sheet_name='Action_Counts')
        action_pct.to_excel(writer, sheet_name='Action_Percentages')
        arch_crosstab.to_excel(writer, sheet_name='Archetype_Counts')
        arch_pct.to_excel(writer, sheet_name='Archetype_Percentages')

    # Copy to all other target directories
    for od in OUTPUT_DIRS[1:]:
        os.makedirs(od, exist_ok=True)
        dest_csv = os.path.join(od, 'master_telemetry.csv')
        dest_xlsx = os.path.join(od, 'master_telemetry.xlsx')
        shutil.copyfile(primary_csv, dest_csv)
        shutil.copyfile(primary_xlsx, dest_xlsx)
        print(f"  &rarr; Copied to: {dest_xlsx}")

    print("\n" + "=" * 80)
    print("MASTER COMPILATION COMPLETE!")
    print("=" * 80)
    print(f"Total Combined Rows: {len(master_df)}")
    print(f"Total Unique Matches: {global_game_counter}")
    print(f"Total Active Decision Frames: {len(active_master)} ({len(active_master)/len(master_df)*100:.1f}%)")
    print(f"Total Feature Columns: {len(master_df.columns)}")
    print("\nRaw Boss Actions Distribution:")
    print(active_master['boss_action'].value_counts())
    print("\n4 Tactical Archetypes Distribution:")
    print(active_master['tactical_archetype'].value_counts())

if __name__ == '__main__':
    compile_master_telemetry()
