import unittest

from check_structured_vertex import check_instructions


VALID = """
s_inst_prefetch 0x3
exp prim v1, off, off, off done
buffer_load_dwordx4 v[8:11], v5, s[8:11], 0 idxen
s_waitcnt vmcnt(0)
v_mov_b32_e32 v13, 1.0
v_mov_b32_e32 v2, v11
exp pos0 v8, v9, v10, v13 done
exp param1 v1, v6, v2, v4
exp param0 v12, v7, v14, v3
s_endpgm
s_code_end
"""


class StructuredVertexTests(unittest.TestCase):
    def test_accepts_exact_contract(self):
        self.assertEqual(11, check_instructions(VALID))

    def test_rejects_fetch_near_misses(self):
        for old, new in [
            ('v[8:11]', 'v[7:10]'),
            ('v5, s[8:11]', 'v0, s[8:11]'),
            ('s[8:11]', 's[12:15]'),
            ('dwordx4', 'dwordx3'),
            ('s_waitcnt vmcnt(0)', 's_nop 0'),
        ]:
            with self.subTest(new=new), self.assertRaises(ValueError):
                check_instructions(VALID.replace(old, new, 1))

    def test_rejects_export_and_routing_near_misses(self):
        for old, new in [
            ('v_mov_b32_e32 v2, v11', 'v_mov_b32_e32 v2, v10'),
            ('v_mov_b32_e32 v13, 1.0', 'v_mov_b32_e32 v13, 0'),
            ('exp pos0 v8, v9, v10, v13 done', 'exp pos0 v8, v9, v10, v13'),
            ('exp param1 v1, v6, v2, v4', 'exp param1 v2, v6, v1, v4'),
        ]:
            with self.subTest(new=new), self.assertRaises(ValueError):
                check_instructions(VALID.replace(old, new, 1))


if __name__ == '__main__':
    unittest.main()
