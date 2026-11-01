"""License inventory must track the locked production dependency set."""
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

CHECK = Path(__file__).absolute().parents[2] / 'scripts/check_milkdown_license_inventory.py'

class MilkdownLicenseInventoryTests(unittest.TestCase):
    def run_check(self, entries, notice):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            lock = root / 'package-lock.json'
            licenses = root / 'licenses.txt'
            lock.write_text(json.dumps({'packages': entries}), encoding='utf-8')
            licenses.write_text(notice, encoding='utf-8')
            return subprocess.run([sys.executable, str(CHECK), str(lock), str(licenses)], capture_output=True, text=True)

    def test_current_production_packages_are_covered(self):
        result = self.run_check({'': {}, 'node_modules/a': {'version': '1.2.3', 'license': 'MIT'}, 'node_modules/dev': {'version': '4', 'dev': True}}, 'Packages: a@1.2.3\nactual license text\n')
        self.assertEqual(0, result.returncode, result.stderr)

    def test_missing_package_fails(self):
        result = self.run_check({'': {}, 'node_modules/a': {'version': '1.2.3', 'license': 'MIT'}}, 'Packages: other@1.2.3\nactual license text\n')
        self.assertNotEqual(0, result.returncode)
        self.assertIn('a@1.2.3', result.stderr)

    def test_empty_production_set_fails(self):
        result = self.run_check({'': {}}, 'Packages: a@1.2.3\nactual license text\n')
        self.assertNotEqual(0, result.returncode)

    def test_missing_license_metadata_fails(self):
        result = self.run_check({'': {}, 'node_modules/a': {'version': '1.2.3'}}, 'Packages: a@1.2.3\nactual license text\n')
        self.assertNotEqual(0, result.returncode)
        self.assertIn('license', result.stderr)

if __name__ == '__main__': unittest.main()
