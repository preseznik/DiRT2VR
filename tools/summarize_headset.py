"""Summarize a completed in-game headset diagnostic, not visual VR acceptance."""
import argparse
import csv
import json
import pathlib
import re
import math
import statistics


def rows(path):
    with path.open(newline='') as stream:
        return list(csv.DictReader(stream))


def summarize(directory):
    frames = rows(directory / 'headset-frames.csv') if (directory / 'headset-frames.csv').exists() else []
    screen = rows(directory / 'screen-frames.csv') if (directory / 'screen-frames.csv').exists() else []
    all_frames = frames + screen
    if not all_frames:
        raise ValueError('No headset frames; wait for the launcher to exit')
    submitted = [row for row in frames if row['submitted'] == '1']
    log = (directory / 'trace.log').read_text()
    result = {
        'receipt': directory.name,
        'options': json.loads((directory / 'diagnostic-options.json').read_text(encoding='utf-8-sig')),
        'scene_ticks': len(frames), 'submitted_scene_pairs': len(submitted),
        'visible_submitted_pairs': sum(row['visible'] == '1' for row in submitted),
        'screen_ticks': len(screen),
        'visible_screen_frames': sum(row['submitted'] == '1' and row['visible'] == '1' for row in screen),
        'first_frame': min(int(row['frame']) for row in all_frames),
        'last_frame': max(int(row['frame']) for row in all_frames),
        'camera_restoration_failures': sum(row['cameras_restored'] != '1' for row in frames),
        'incomplete_submitted_pairs': sum(row['pair_ready'] != '1' for row in submitted),
        'missing_submitted_projections': sum(int(row['left_projection_uploads']) == 0 or
                                             int(row['right_projection_uploads']) == 0 for row in submitted),
        'draw_count_differences': sum(row['left_draws'] != row['right_draws'] for row in submitted),
        'render_thread_stopped': 'OpenXR stopping on render thread' in log,
        'captured_pairs': re.findall(r'OpenXR captured pair=(\d+) frame=(\d+) images=(\d+)/(\d+)', log),
        'mode_changes': re.findall(r'OpenXR requested mode=(\w+) frame=(\d+)', log),
        'limit': 'Runtime submissions and camera checks only. Tick time includes xrWaitFrame. GPU timestamps cover the inner stereo render and copies, excluding outer preparation, compositor and headset transport. No full-race or visual acceptance is inferred.'
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
    if (directory / 'gpu-frames.csv').exists():
        gpu = rows(directory / 'gpu-frames.csv')
        valid = sorted(float(row['eye_pair_gpu_ms']) for row in gpu if row['valid'] == '1')
        result['gpu_eye_pair'] = {
            'samples': len(valid), 'invalid_samples': len(gpu) - len(valid),
            'median_ms': statistics.median(valid) if valid else None,
            'p95_ms': valid[math.ceil(len(valid) * .95) - 1] if valid else None,
            'over_11_11_ms': sum(value > 1000/90 for value in valid)
        }
    if submitted and (directory / 'frames.csv').exists():
        stereo_ids = {int(row['frame']) for row in submitted}
        intervals = sorted(float(row['interval_ms']) for row in rows(directory / 'frames.csv')
                           if int(row['frame']) in stereo_ids and float(row['interval_ms']) > 0)
        if intervals:
            result['stereo_present_intervals'] = {
                'samples': len(intervals), 'median_ms': statistics.median(intervals),
                'p95_ms': intervals[math.ceil(len(intervals) * .95) - 1],
                'maximum_ms': max(intervals),
                'over_22_22_ms': sum(value > 2000/90 for value in intervals),
                'limit': 'CPU intervals at game Present for submitted stereo frames, including focus/transition/diagnostic stalls; not compositor delivery or motion-to-photon latency.'
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
    raise SystemExit(1 if result['visible_submitted_pairs'] + result['visible_screen_frames'] < 60 or any(result[key] for key in
        ('camera_restoration_failures', 'incomplete_submitted_pairs', 'missing_submitted_projections', 'render_thread_stopped')) else 0)
