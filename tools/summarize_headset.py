"""Summarize a completed in-game headset diagnostic, not visual VR acceptance."""
import argparse
import csv
import json
import pathlib
import re


def rows(path):
    with path.open(newline='') as stream:
        return list(csv.DictReader(stream))


def summarize(directory):
    frames = rows(directory / 'headset-frames.csv')
    if not frames:
        raise ValueError('No headset frames; wait for the launcher to exit')
    submitted = [row for row in frames if row['submitted'] == '1']
    log = (directory / 'trace.log').read_text()
    result = {
        'receipt': directory.name,
        'options': json.loads((directory / 'diagnostic-options.json').read_text(encoding='utf-8-sig')),
        'scene_ticks': len(frames), 'submitted_scene_pairs': len(submitted),
        'visible_submitted_pairs': sum(row['visible'] == '1' for row in submitted),
        'first_frame': int(frames[0]['frame']), 'last_frame': int(frames[-1]['frame']),
        'camera_restoration_failures': sum(row['cameras_restored'] != '1' for row in frames),
        'incomplete_submitted_pairs': sum(row['pair_ready'] != '1' for row in submitted),
        'missing_submitted_projections': sum(int(row['left_projection_uploads']) == 0 or
                                             int(row['right_projection_uploads']) == 0 for row in submitted),
        'draw_count_differences': sum(row['left_draws'] != row['right_draws'] for row in submitted),
        'render_thread_stopped': 'OpenXR stopping on render thread' in log,
        'captured_pairs': re.findall(r'OpenXR captured pair=(\d+) frame=(\d+) images=(\d+)/(\d+)', log),
        'limit': 'Runtime submissions and camera checks only. Draw counts may differ with visibility. Tick time includes xrWaitFrame; no GPU timing or visual/head-tracking acceptance is inferred.'
    }
    memory = [row for row in rows(directory / 'address-space.csv')
              if result['first_frame'] <= int(row['frame']) <= result['last_frame']]
    if memory:
        result['address_space'] = {
            'all_samples_complete': all(row['complete'] == '1' for row in memory),
            'limit_bytes': int(memory[0]['limit_bytes']),
            'peak_committed_bytes': max(int(row['committed_bytes']) for row in memory),
            'minimum_free_bytes': min(int(row['free_bytes']) for row in memory),
            'minimum_largest_free_block_bytes': min(int(row['largest_free_bytes']) for row in memory)
        }
    return result


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('trace', type=pathlib.Path)
    args = parser.parse_args()
    result = summarize(args.trace)
    output = json.dumps(result, indent=2)
    (args.trace / 'headset-summary.json').write_text(output + '\n')
    print(output)
    raise SystemExit(1 if result['visible_submitted_pairs'] < 60 or any(result[key] for key in
        ('camera_restoration_failures', 'incomplete_submitted_pairs', 'missing_submitted_projections', 'render_thread_stopped')) else 0)
