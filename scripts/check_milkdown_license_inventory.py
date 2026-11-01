"""Ensure each locked production npm package appears in the license inventory.

This is a completeness guard, not a legal compatibility determination.
"""
import json
import sys
from pathlib import Path


def read_covered(text):
    covered = set()
    for line in text.splitlines():
        if line.startswith('Packages: '):
            covered.update(line.removeprefix('Packages: ').split(', '))
    return covered


def validate_packages(packages, covered):
    errors = []
    production_count = 0
    for path, data in packages.items():
        if not path or data.get('dev'):
            continue
        production_count += 1
        version = data.get('version')
        if not version or not data.get('license'):
            errors.append(f'{path}: missing version or license metadata')
        elif f"{path.removeprefix('node_modules/')}@{version}" not in covered:
            errors.append(f"missing notice: {path.removeprefix('node_modules/')}@{version}")
    if not production_count:
        errors.append('lock has no production package entries')
    if errors:
        raise ValueError('\n'.join(errors))


def check(lock_path, notice_path):
    lock = json.loads(Path(lock_path).read_text(encoding='utf-8-sig'))
    text = Path(notice_path).read_text(encoding='utf-8-sig')
    validate_packages(lock['packages'], read_covered(text))
    print('Locked production npm packages are listed in the license inventory.')


if __name__ == '__main__':
    try:
        check(sys.argv[1], sys.argv[2])
    except (OSError, ValueError, KeyError, IndexError) as exc:
        print(f'ERROR: {exc}', file=sys.stderr)
        sys.exit(1)
