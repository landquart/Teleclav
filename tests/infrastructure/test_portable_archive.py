"""Exercise the portable archive gate against complete and broken packages."""
import json
import subprocess
import sys
import tempfile
import unittest
import zipfile
from pathlib import Path

CHECK = Path(__file__).absolute().parents[2] / 'scripts/check_portable_archive.py'
FILES = {
    'Heimdall.exe': b'exe', 'Heimdall.dll': b'dll', 'hostfxr.dll': b'runtime',
    'hostpolicy.dll': b'runtime', 'coreclr.dll': b'runtime',
    'Heimdall.deps.json': b'{}', 'PresentationFramework.dll': b'wpf',
    'PresentationCore.dll': b'wpf', 'WindowsBase.dll': b'wpf',
    'WebView2Loader.dll': b'loader', 'LICENSE': b'license',
    'THIRD-PARTY-NOTICES.md': b'notices', 'config/settings.default.json': b'{}',
    'locales/en.json': b'{}', 'locales/fr.json': b'{}', 'locales/es.json': b'{}',
    'Assets/Terminal/VENDORED.json': b'{}',
    'THIRD-PARTY-LICENSES/libgit2-LICENSE.txt': b'license',
    'THIRD-PARTY-LICENSES/noVNC-LICENSE.txt': b'license',
    'THIRD-PARTY-LICENSES/noVNC-MPL-2.0.txt': b'license',
    'THIRD-PARTY-LICENSES/xterm-LICENSE.txt': b'license',
    'THIRD-PARTY-LICENSES/SQLite-LICENSE.txt': b'license',
    'THIRD-PARTY-LICENSES/WebView2-LICENSE.txt': b'license',
    'THIRD-PARTY-LICENSES/WebView2-NOTICE.txt': b'notice',
    'THIRD-PARTY-LICENSES/remark-math-LICENSE.txt': b'license',
    'THIRD-PARTY-LICENSES/milkdown-dependencies-LICENSES.txt': b'license',
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

    def test_unsafe_path_fails(self):
        files = dict(FILES); files['../outside'] = b'bad'
        result = self.check(files)
        self.assertNotEqual(0, result.returncode)
        self.assertIn('unsafe path', result.stderr)

    def test_absolute_path_fails(self):
        files = dict(FILES); files['/outside'] = b'bad'
        result = self.check(files)
        self.assertNotEqual(0, result.returncode)
        self.assertIn('unsafe path', result.stderr)

    def test_backslash_path_fails(self):
        # ZipInfo normalizes os.sep on Windows, so patch both ZIP headers to
        # construct the same hostile archive independently of the host OS.
        with tempfile.TemporaryDirectory() as tmp:
            archive = Path(tmp) / 'portable.zip'
            with zipfile.ZipFile(archive, 'w') as z:
                for name, data in FILES.items(): z.writestr(name, data)
                z.writestr('bad/path', b'bad')
            data = archive.read_bytes()
            self.assertEqual(2, data.count(b'bad/path'))
            archive.write_bytes(data.replace(b'bad/path', b'bad\\path'))
            result = subprocess.run([sys.executable, str(CHECK), str(archive)], capture_output=True, text=True)
            self.assertNotEqual(0, result.returncode)
            self.assertIn('unsafe path', result.stderr)
            # Simulate Windows ZipInfo normalizing separators during reading.
            code = "import os,runpy,sys,zipfile; os.sep=chr(92); sys.argv=[sys.argv[1],sys.argv[2]]; runpy.run_path(sys.argv[0],run_name='__main__')"
            result = subprocess.run([sys.executable, '-c', code, str(CHECK), str(archive)], capture_output=True, text=True)
            self.assertNotEqual(0, result.returncode)
            self.assertIn('unsafe path', result.stderr)

    def test_empty_required_file_fails(self):
        files = dict(FILES); files['LICENSE'] = b''
        result = self.check(files)
        self.assertNotEqual(0, result.returncode)
        self.assertIn('LICENSE', result.stderr)

    def test_nonempty_notice_is_accepted(self):
        files = dict(FILES); files['LICENSE'] = b'x'
        result = self.check(files)
        self.assertEqual(0, result.returncode, result.stderr)

    def test_invalid_runtime_config_fails(self):
        files = dict(FILES); files['Heimdall.runtimeconfig.json'] = b'not json'
        result = self.check(files)
        self.assertNotEqual(0, result.returncode)

    def test_invalid_zip_fails(self):
        with tempfile.TemporaryDirectory() as tmp:
            archive = Path(tmp) / 'bad.zip'; archive.write_bytes(b'not zip')
            result = subprocess.run([sys.executable, str(CHECK), str(archive)], capture_output=True, text=True)
            self.assertNotEqual(0, result.returncode)

    def test_missing_bundled_license_fails(self):
        files = dict(FILES); del files['THIRD-PARTY-LICENSES/libgit2-LICENSE.txt']
        result = self.check(files)
        self.assertNotEqual(0, result.returncode)
        self.assertIn('libgit2-LICENSE.txt', result.stderr)

    def test_missing_notice_fails(self):
        files = dict(FILES); del files['LICENSE']
        result = self.check(files)
        self.assertNotEqual(0, result.returncode)
        self.assertIn('LICENSE', result.stderr)

if __name__ == '__main__': unittest.main()
