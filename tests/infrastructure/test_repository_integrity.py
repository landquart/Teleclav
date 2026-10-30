"""Behavioral checks for the repository integrity gate."""
import hashlib
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

CHECK = Path(__file__).resolve().parents[2] / 'scripts' / 'check_repository_integrity.py'


class IntegrityTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)
        for path, content in {
            'LICENSE': 'Apache License Version 2.0',
            'THIRD-PARTY-NOTICES.md': 'Third-party notices',
            'Heimdall.slnx': '<Solution><Project Path="src/App/App.csproj" /></Solution>',
            'src/App/App.csproj': '<Project><ItemGroup><ProjectReference Include="../Core/Core.csproj" /></ItemGroup></Project>',
            'src/Core/Core.csproj': '<Project/>',
            'src/App/packages.lock.json': '{"version": 1, "dependencies": {}}',
            'src/Core/packages.lock.json': '{"version": 1, "dependencies": {}}',
        }.items():
            dest = self.root / path
            dest.parent.mkdir(parents=True, exist_ok=True)
            dest.write_text(content, encoding='utf-8')
        base = self.root / 'src/Heimdall.App/Assets/Terminal'
        base.mkdir(parents=True)
        (base / 'xterm.js').write_bytes(b'vendored')
        (base / 'VENDORED.json').write_text(json.dumps({'components': [{'files': [{'path': 'xterm.js', 'sha256': hashlib.sha256(b'vendored').hexdigest()}]}]}), encoding='utf-8')

    def tearDown(self):
        self.tmp.cleanup()

    def check(self):
        return subprocess.run([sys.executable, str(CHECK), str(self.root)], capture_output=True, text=True)

    def test_valid_repository_passes(self):
        result = self.check()
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)

    def test_missing_project_reference_fails(self):
        (self.root / 'src/Core/Core.csproj').unlink()
        result = self.check()
        self.assertNotEqual(0, result.returncode)
        self.assertIn('src/Core/Core.csproj', result.stderr.replace('\\', '/'))

    def test_missing_lock_file_fails(self):
        (self.root / 'src/App/packages.lock.json').unlink()
        result = self.check()
        self.assertNotEqual(0, result.returncode)
        self.assertIn('src/App/packages.lock.json', result.stderr.replace('\\', '/'))

    def test_tampered_vendored_file_fails(self):
        (self.root / 'src/Heimdall.App/Assets/Terminal/xterm.js').write_bytes(b'tampered')
        result = self.check()
        self.assertNotEqual(0, result.returncode)
        self.assertIn('terminal hash mismatch', result.stderr)

    def test_missing_license_fails(self):
        (self.root / 'LICENSE').unlink()
        result = self.check()
        self.assertNotEqual(0, result.returncode)
        self.assertIn('LICENSE', result.stderr)


if __name__ == '__main__':
    unittest.main()
