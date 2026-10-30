"""Check solution references, lock files, required notices and vendored terminal hashes.

This is a structural check, not a legal review of third-party licenses.
"""
import hashlib
import json
import sys
import xml.etree.ElementTree as ET
from pathlib import Path


def check(root):
    errors = []

    def require(path):
        if not path.is_file() or path.stat().st_size == 0:
            errors.append(f'missing or empty file: {path}')
            return False
        return True

    for name in ('LICENSE', 'THIRD-PARTY-NOTICES.md'):
        require(root / name)

    solution = root / 'Heimdall.slnx'
    if require(solution):
        try:
            projects = [root / node.attrib['Path'].replace('\\', '/')
                        for node in ET.parse(solution).iter('Project')]
            if not projects:
                errors.append('solution contains no projects')
            for project in projects:
                if not require(project):
                    continue
                lock = project.parent / 'packages.lock.json'
                if require(lock):
                    json.loads(lock.read_text(encoding='utf-8-sig'))
                for ref in ET.parse(project).iter('ProjectReference'):
                    require((project.parent / ref.attrib['Include'].replace('\\', '/')).resolve())
        except (ET.ParseError, KeyError, json.JSONDecodeError) as exc:
            errors.append(f'invalid project or lock file: {exc}')

    terminal = root / 'src/Heimdall.App/Assets/Terminal'
    manifest = terminal / 'VENDORED.json'
    if require(manifest):
        try:
            data = json.loads(manifest.read_text(encoding='utf-8-sig'))
            files = [item for component in data['components'] for item in component['files']]
            if not files:
                errors.append('terminal manifest has no files')
            for item in files:
                path = (terminal / item['path']).resolve()
                if not path.is_relative_to(terminal.resolve()):
                    errors.append(f'terminal manifest path escapes directory: {item["path"]}')
                elif require(path) and hashlib.sha256(path.read_bytes()).hexdigest() != item['sha256'].lower():
                    errors.append(f'terminal hash mismatch: {item["path"]}')
        except (ValueError, KeyError, TypeError) as exc:
            errors.append(f'invalid terminal manifest: {exc}')

    for error in errors:
        print(f'ERROR: {error}', file=sys.stderr)
    if errors:
        return 1
    print('Repository structure, notices and terminal asset hashes verified.')
    return 0


if __name__ == '__main__':
    root = Path(sys.argv[1]).resolve() if len(sys.argv) > 1 else Path(__file__).resolve().parents[1]
    sys.exit(check(root))
