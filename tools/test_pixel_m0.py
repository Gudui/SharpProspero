import unittest
from check_pixel_m0 import check_instructions

INIT = 's_mov_b32 m0, s0\n'
PAIR = 'v_interp_p1_f32_e32 v2, v0, attr1.w\nv_interp_p2_f32_e32 v2, v1, attr1.w\n'
END = 'exp mrt0 v2, v3, v4, v5 done vm\ns_endpgm\ns_code_end\n'


class PixelM0Tests(unittest.TestCase):
    def test_initialized_pair(self):
        check_instructions(INIT + PAIR + END)

    def test_spatial_z_pair(self):
        check_instructions(INIT + PAIR.replace('attr1.w', 'attr1.z') + END, 'z')

    def test_spatial_pair_negative_variants(self):
        pair = PAIR.replace('attr1.w', 'attr1.z')
        for name, text in {
            'cq-constant-w': INIT + PAIR + END,
            'mismatched': INIT + pair.replace('attr1.z', 'attr1.w', 1) + END,
            'wrong-slot': INIT + pair.replace('attr1', 'attr0') + END,
            'reversed': INIT + '\n'.join(reversed(pair.strip().splitlines())) + '\n' + END,
            'wrong-destination': INIT + pair.replace('v2,', 'v3,') + END,
            'missing-m0': pair + END,
        }.items():
            with self.subTest(name=name), self.assertRaises(ValueError):
                check_instructions(text, 'z')

    def test_negative_variants(self):
        for name, text in {
            'missing': PAIR + END,
            'late': PAIR + INIT + END,
            'wrong-source': INIT.replace('s0', 's1') + PAIR + END,
            'clobbered': INIT + 's_mov_b32 m0, 0\n' + PAIR + END,
            'source-clobbered': 's_mov_b32 s0, 0\n' + INIT + PAIR + END,
            'branch': 's_branch 4\n' + INIT + PAIR + END,
            'unterminated': INIT + PAIR,
            'hidden-after-end': PAIR + END + INIT,
            'no-interpolation': INIT + END,
        }.items():
            with self.subTest(name=name), self.assertRaises(ValueError):
                check_instructions(text)


if __name__ == '__main__':
    unittest.main()
