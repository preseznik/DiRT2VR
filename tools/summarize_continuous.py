"""Summarize a completed desktop continuous-render diagnostic, not VR acceptance."""
import argparse
import csv
import json
import math
import pathlib
import statistics

from compare_passes import compare, image_difference


def rows(path):
    with path.open(newline='') as stream:
        return list(csv.DictReader(stream))


def summarize(directory):
    pairs = rows(directory / 'stereo-frames.csv')
    if not pairs:
        raise ValueError('No completed pairs; wait for the benchmark to exit')
    frames = [int(row['frame']) for row in pairs]
    cpu = sorted(float(row['cpu_ms']) for row in pairs if int(row['frame']) not in (300, 1200, 3000, 4500, 6000))
    result = {
        'receipt': directory.name,
        'options': json.loads((directory / 'diagnostic-options.json').read_text(encoding='utf-8-sig')),
        'pairs': len(pairs), 'first_frame': frames[0], 'last_frame': frames[-1],
        'nonconsecutive_frames': sum(b != a + 1 for a, b in zip(frames, frames[1:])),
        'draw_count_mismatches': sum(row['left_draws'] != row['right_draws'] for row in pairs),
        'camera_restoration_failures': sum(row['cameras_restored'] != '1' for row in pairs),
        'incomplete_pairs': sum(row['pair_ready'] != '1' for row in pairs),
        'disabled': 'CONTINUOUS disabled' in (directory / 'trace.log').read_text(),
        'cpu_submission_ms_median': statistics.median(cpu) if cpu else None,
        'cpu_submission_ms_p95': cpu[math.ceil(len(cpu) * .95) - 1] if cpu else None,
        'sampled_images': {str(frame): image_difference(directory, (900001 + 2 * (frame - 3000), 900002 + 2 * (frame - 3000))) for frame in (3000, 4500, 6000)},
        'limit': 'Desktop diagnostic; CPU submission is not GPU time, and completion does not prove simulation integrity or VR performance.'
    }
    if (directory / 'draws-pass-1.csv').exists():
        result['frame_3000_draw_sequence_identical'] = compare(directory)['sequence_identical']
    memory_path = directory / 'address-space.csv'
    if memory_path.exists():
        memory = [row for row in rows(memory_path) if frames[0] <= int(row['frame']) <= frames[-1]]
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
    (args.trace / 'continuous-summary.json').write_text(output + '\n')
    print(output)
    raise SystemExit(1 if any(result[key] for key in ('draw_count_mismatches', 'camera_restoration_failures', 'incomplete_pairs', 'disabled')) else 0)
