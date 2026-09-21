"""Compare the same-frame scene replay diagnostic; differences are not stereo proof."""
import argparse
import collections
import csv
import difflib
import json
import pathlib


def image_difference(directory, numbers=(900001, 900002)):
    images = []
    size = None
    for number in numbers:
        path = directory / f'frame-{number}.ppm'
        if not path.exists():
            return None
        with path.open('rb') as stream:
            if stream.readline().strip() != b'P6':
                raise ValueError('Expected diagnostic P6 capture')
            dimensions = tuple(map(int, stream.readline().split()))
            if size is not None and dimensions != size:
                raise ValueError('Capture dimensions differ')
            size = dimensions
            if stream.readline().strip() != b'255':
                raise ValueError('Expected 8-bit capture')
            pixels = stream.read()
            if len(pixels) != size[0] * size[1] * 3:
                raise ValueError('Incomplete capture; wait for the experiment to finish')
            images.append(pixels)
    differences = bytes(abs(a-b) for a, b in zip(*images))
    return {'width': size[0], 'height': size[1],
            'max_channel_difference': max(differences),
            'mean_channel_difference': sum(differences)/len(differences),
            'changed_pixels': sum(any(differences[i:i+3]) for i in range(0, len(differences), 3))}


def compare(directory):
    passes = []
    for number in (1, 2):
        with (directory / f'draws-pass-{number}.csv').open(newline='') as stream:
            passes.append([tuple(row) for row in csv.reader(stream)])
    a, b = passes
    changes = []
    for operation, i, j, k, m in difflib.SequenceMatcher(None, a, b, autojunk=False).get_opcodes():
        if operation != 'equal':
            changes.append({'operation': operation, 'first_range': [i, j],
                            'repeat_range': [k, m], 'first': a[i:j], 'repeat': b[k:m]})
    missing = collections.Counter(a) - collections.Counter(b)
    added = collections.Counter(b) - collections.Counter(a)
    return {'first_draws': len(a), 'repeat_draws': len(b),
            'sequence_identical': a == b,
            'image_difference': image_difference(directory),
            'missing': [{'draw': draw, 'count': count} for draw, count in missing.items()],
            'added': [{'draw': draw, 'count': count} for draw, count in added.items()],
            'changes': changes}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('trace', type=pathlib.Path)
    args = parser.parse_args()
    print(json.dumps(compare(args.trace), indent=2))
