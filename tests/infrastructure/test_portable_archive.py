"""Exercise the portable archive gate against complete and broken packages."""
import json
import subprocess
import sys
import tempfile
import unittest
import zipfile
from pathlib import Path

CHECK = Path(__file__).resolve().parents[2] / 'scripts/check_portable_archive.py'
FILES = {
    'Heimdall.exe': b'exe', 'Heimdall.dll': b'dll', 'hostfxr.dll': b'runtime',
    'hostpolicy.dll': b'runtime', 'coreclr.dll': b'runtime',
    'Microsoft.WindowsDesktop.App.deps.json': b'{}',
    'WebView2Loader.dll': b'loader', 'LICENSE': b'license',
    'THIRD-PARTY-NOTICES.md': b'notices', 'config/settings.default.json': b'{}',
    'locales/en.json': b'{}', 'locales/fr.json': b'{}', 'locales/es.json': b'{}',
    'Assets/Terminal/VENDORED.json': b'{}',
    'Heimdall.runtimeconfig.json': json.dumps({'runtimeOptions': {'includedFrameworks': [{'name': 'Microsoft.NETCore.App'}, {'name': 'Microsoft.WindowsDesktop.App'}]}}).encode(),
}

class PortableArchiveTests(unittest.TestCase):
    def check(self, files):
        with tempfile.TemporaryDirectory() as tmp:
            archive = Path(tmp) / 'portable.zip'
            with zipfile.ZipFile(archive, 'w') as z:
                for name, data in files.items(): z.writestr(name, data)
            return subprocess.run([sys.executable, str(CHECK), str(archive)], capture_output=True, text=True)

    def test_valid_archive_passes(self):
        result = self.check(FILES)
        self.assertEqual(0, result.returncode, result.stderr)

    def test_missing_runtime_fails(self):
        files = dict(FILES); del files['coreclr.dll']
        result = self.check(files)
        self.assertNotEqual(0, result.returncode)
        self.assertIn('coreclr.dll', result.stderr)

    def test_framework_dependent_archive_fails(self):
        files = dict(FILES); files['Heimdall.runtimeconfig.json'] = b'{"runtimeOptions":{"framework":{"name":"Microsoft.WindowsDesktop.App"}}}'
        result = self.check(files)
        self.assertNotEqual(0, result.returncode)
        self.assertIn('self-contained', result.stderr)

    def test_missing_notice_fails(self):
        files = dict(FILES); del files['LICENSE']
        result = self.check(files)
        self.assertNotEqual(0, result.returncode)
        self.assertIn('LICENSE', result.stderr)

if __name__ == '__main__': unittest.main()
