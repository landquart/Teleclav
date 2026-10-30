import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

SCRIPT = Path(__file__).resolve().parents[2] / 'scripts' / 'drag_drop_fixture.py'

class DragDropFixtureTests(unittest.TestCase):
    def test_create_manifest_and_detect_changed_bytes(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp) / 'fixture'
            subprocess.run([sys.executable, str(SCRIPT), 'create', str(root), '--large-mib', '1'], check=True)
            manifest = Path(temp) / 'manifest.json'
            subprocess.run([sys.executable, str(SCRIPT), 'manifest', str(root), str(manifest)], check=True)
            data = json.loads(manifest.read_text(encoding='utf-8'))
            self.assertIn('вложенная/пустая', data['directories'])
            self.assertTrue(any(row['path'] == 'вложенная/данные.bin' and row['size'] == 1048576 for row in data['files']))
            subprocess.run([sys.executable, str(SCRIPT), 'verify', str(root), str(manifest)], check=True)
            (root / 'вложенная' / 'данные.bin').write_bytes(b'changed')
            self.assertNotEqual(0, subprocess.run([sys.executable, str(SCRIPT), 'verify', str(root), str(manifest)], capture_output=True).returncode)

    def test_create_refuses_existing_directory(self):
        with tempfile.TemporaryDirectory() as temp:
            result = subprocess.run([sys.executable, str(SCRIPT), 'create', temp], capture_output=True)
            self.assertNotEqual(0, result.returncode)

if __name__ == '__main__': unittest.main()
