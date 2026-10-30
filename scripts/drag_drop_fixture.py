#!/usr/bin/env python3
"""Create a deterministic drag/drop fixture and compare directory, size and SHA-256 manifests."""
import argparse
import hashlib
import json
import os
from pathlib import Path


def create(root, large_mib):
    root.mkdir(parents=False, exist_ok=False)
    (root / 'вложенная' / 'пустая').mkdir(parents=True)
    (root / 'empty').mkdir()
    (root / 'обычный файл.txt').write_bytes('Teleclav test\nПривет\n'.encode('utf-8'))
    (root / 'zero.bin').write_bytes(b'')
    (root / 'binary.bin').write_bytes(bytes(range(256)) * 4)
    block = bytes(range(256)) * 4096
    with (root / 'вложенная' / 'данные.bin').open('xb') as target:
        for _ in range(large_mib):
            target.write(block)


def manifest(root):
    if not root.is_dir() or root.is_symlink():
        raise ValueError('Root must be a real directory')
    dirs, files = [], []
    for parent, subdirs, names in os.walk(root, followlinks=False):
        base = Path(parent)
        for name in subdirs:
            path = base / name
            if path.is_symlink():
                raise ValueError('Symbolic links are not supported')
            dirs.append(path.relative_to(root).as_posix())
        for name in names:
            path = base / name
            if path.is_symlink() or not path.is_file():
                raise ValueError('Only regular files are supported')
            digest = hashlib.sha256()
            size = 0
            with path.open('rb') as file:
                for chunk in iter(lambda: file.read(1024 * 1024), b''):
                    digest.update(chunk)
                    size += len(chunk)
            files.append({'path': path.relative_to(root).as_posix(), 'size': size, 'sha256': digest.hexdigest()})
    return {'directories': sorted(dirs), 'files': sorted(files, key=lambda item: item['path'])}


def main():
    parser = argparse.ArgumentParser()
    commands = parser.add_subparsers(dest='command', required=True)
    maker = commands.add_parser('create')
    maker.add_argument('root', type=Path)
    maker.add_argument('--large-mib', type=int, default=128, choices=range(0, 8193), metavar='0..8192')
    for command in ('manifest', 'verify'):
        cmd = commands.add_parser(command)
        cmd.add_argument('root', type=Path)
        cmd.add_argument('manifest', type=Path)
    args = parser.parse_args()
    if args.command == 'create':
        create(args.root, args.large_mib)
    elif args.command == 'manifest':
        data = manifest(args.root)
        with args.manifest.open('x', encoding='utf-8', newline='\n') as output:
            json.dump(data, output, ensure_ascii=False, indent=2)
            output.write('\n')
    else:
        expected = json.loads(args.manifest.read_text(encoding='utf-8'))
        actual = manifest(args.root)
        if actual != expected:
            print('FAIL: paths, directories, sizes or SHA-256 differ')
            return 1
        print(f"PASS: {len(actual['files'])} files and {len(actual['directories'])} directories match")
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
