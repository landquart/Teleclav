"""Verify essential contents of the Windows x64 portable ZIP without extracting it."""
import json
import sys
import zipfile
from pathlib import PurePosixPath

REQUIRED = (
    'Heimdall.exe', 'Heimdall.dll', 'Heimdall.runtimeconfig.json',
    'hostfxr.dll', 'hostpolicy.dll', 'coreclr.dll',
    'Heimdall.deps.json', 'PresentationFramework.dll', 'PresentationCore.dll',
    'WindowsBase.dll', 'WebView2Loader.dll',
    'LICENSE', 'THIRD-PARTY-NOTICES.md', 'config/settings.default.json',
    'locales/en.json', 'locales/fr.json', 'locales/es.json',
    'Assets/Terminal/VENDORED.json',
)


def check_paths(entries):
    names = {entry.filename: entry for entry in entries}
    if len(names) != len(entries):
        raise ValueError('duplicate archive entries')
    for entry in entries:
        raw_name = entry.orig_filename
        parts = PurePosixPath(raw_name).parts
        if raw_name.startswith('/') or '\\' in raw_name or '..' in parts:
            raise ValueError(f'unsafe path: {raw_name}')
    return names


def check_entries(archive):
    names = check_paths(archive.infolist())
    for name in REQUIRED:
        if name not in names or names[name].file_size == 0:
            raise ValueError(f'missing or empty: {name}')
    if archive.testzip() is not None:
        raise ValueError('archive contains corrupted data')


def check_runtime(archive):
    config = json.loads(archive.read('Heimdall.runtimeconfig.json'))['runtimeOptions']
    included = {item['name'] for item in config.get('includedFrameworks', [])}
    if not {'Microsoft.NETCore.App', 'Microsoft.WindowsDesktop.App'} <= included:
        raise ValueError('runtime configuration is not self-contained')


def check(path):
    with zipfile.ZipFile(path) as archive:
        check_entries(archive)
        check_runtime(archive)
    print('Portable ZIP integrity, required files and runtime configuration verified.')


if __name__ == '__main__':
    try:
        check(sys.argv[1])
    except (OSError, ValueError, KeyError, IndexError, zipfile.BadZipFile) as exc:
        print(f'ERROR: {exc}', file=sys.stderr)
        sys.exit(1)
