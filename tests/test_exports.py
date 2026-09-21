import pathlib
import sys
import unittest
sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1] / 'tools'))
from generate_exports import exports, generate


class ExportsTest(unittest.TestCase):
    def test_actual_system_exports_are_preserved(self):
        items = exports(pathlib.Path('C:/Windows/SysWOW64/d3d11.dll').read_bytes())
        definitions, code = generate(items)
        self.assertGreaterEqual(len(items), 40)
        for name, ordinal in items:
            self.assertIn(f'  {name}=', definitions)
            self.assertIn(f' @{ordinal}\n', definitions)
        self.assertIn('D3D11CreateDevice=ProxyCreateDevice ', definitions)
        self.assertIn('D3D11On12CreateDevice=', definitions)

    def test_reject_non_pe(self):
        with self.assertRaises(ValueError):
            exports(bytes(1024))


if __name__ == '__main__':
    unittest.main()
