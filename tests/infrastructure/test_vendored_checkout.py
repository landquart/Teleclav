"""Pinned assets must retain their exact bytes under Windows-style Git checkout."""
import subprocess
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).absolute().parents[2]
SAMPLES = (
    'src/Heimdall.App/Assets/Terminal/xterm.min.js',
    'src/Heimdall.App/Assets/Terminal/xterm.min.css',
    'src/Heimdall.App/Assets/Terminal/addon-fit.min.js',
    'src/Heimdall.App/Assets/Terminal/addon-webgl.min.js',
    'src/Heimdall.App/Assets/drawio/js/app.min.js',
    'src/Heimdall.App/Assets/milkdown/index.html',
    'src/Heimdall.App/Assets/milkdown-editor/dist/index.html',
    'src/Heimdall.App/Assets/milkdown-editor/package-lock.json',
    'src/Heimdall.App/Assets/vnc/core/rfb.js',
    'src/Heimdall.App/Assets/vnc/vendor/pako/lib/zlib/deflate.js',
)

class VendoredCheckoutTests(unittest.TestCase):
    def test_pinned_files_survive_autocrlf_checkout(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            subprocess.run(['git', 'init', '-q', str(root)], check=True)
            (root / '.gitattributes').write_bytes((ROOT / '.gitattributes').read_bytes())
            for name in SAMPLES:
                path = root / name
                path.parent.mkdir(parents=True, exist_ok=True)
                # Use the committed blob as the authoritative bytes.
                blob = subprocess.check_output(['git', 'show', f'HEAD:{name}'], cwd=ROOT)
                path.write_bytes(blob)
            subprocess.run(['git', '-c', 'core.autocrlf=false', 'add', '.'], cwd=root, check=True, capture_output=True)
            for name in SAMPLES:
                (root / name).unlink()
            subprocess.run(['git', '-c', 'core.autocrlf=true', 'checkout-index', '-f', '-a'], cwd=root, check=True, capture_output=True)
            for name in SAMPLES:
                expected = subprocess.check_output(['git', 'show', f'HEAD:{name}'], cwd=ROOT)
                self.assertEqual(expected, (root / name).read_bytes(), name)

if __name__ == '__main__': unittest.main()
