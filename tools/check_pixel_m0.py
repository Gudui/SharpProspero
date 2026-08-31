"""Narrow straight-line M0 prerequisite check over validated LLVM ISA, not a byte decoder."""
import argparse
import hashlib
import json
import re
from pathlib import Path


def check_instructions(text):
    initialized = False
    interpolations = 0
    ended = False
    count = 0
    allowed = {'s_inst_prefetch', 's_mov_b32', 'v_mov_b32_e32', 's_nop',
               'v_interp_p1_f32_e32', 'v_interp_p2_f32_e32', 'exp', 's_endpgm', 's_code_end'}
    for raw in text.splitlines():
        line = raw.split(';', 1)[0].strip()
        if not line:
            continue
        count += 1
        opcode = line.split()[0]
        if opcode not in allowed:
            raise ValueError('unsupported instruction/control flow: ' + line)
        if ended:
            if opcode != 's_code_end':
                raise ValueError('unexpected code after termination')
            continue
        if opcode == 's_code_end':
            raise ValueError('padding before termination')
        if opcode == 's_mov_b32':
            if not re.fullmatch(r's_mov_b32\s+m0,\s*s0', line):
                raise ValueError('unsupported scalar write or wrong M0 source: ' + line)
            initialized = True
        if opcode.startswith('v_interp_'):
            if not initialized:
                raise ValueError('interpolation before M0 initialization')
            interpolations += 1
        if opcode == 's_endpgm':
            ended = True
    if not ended or interpolations != 2:
        raise ValueError('expected terminated straight-line one-pair pixel probe')
    return count


def check_run(run, shader):
    manifest = json.loads((run / 'manifest.json').read_text(encoding='utf-8-sig'))
    validation = json.loads((run / 'validation.json').read_text(encoding='utf-8-sig'))
    if not validation.get('passed') or validation.get('invalid_line_count') != 0:
        raise ValueError('LLVM validation failed')
    if manifest.get('gpu') != 'gfx1030' or manifest.get('analysis_scope') != 'full_section':
        raise ValueError('requires full gfx1030 decode')
    data = shader.read_bytes()
    if hashlib.sha256(data).hexdigest() != manifest['shader_sha256']:
        raise ValueError('stale shader manifest')
    start, size = manifest['code_offset'], manifest['code_size']
    if size != manifest['section_size'] or hashlib.sha256(data[start:start + size]).hexdigest() != manifest['code_sha256']:
        raise ValueError('code range/hash mismatch')
    count = check_instructions((run / 'isa.txt').read_text(encoding='utf-8-sig'))
    if count != validation['instruction_count']:
        raise ValueError('instruction count mismatch')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(__doc__)
    parser.add_argument('--run', type=Path, required=True)
    parser.add_argument('--shader', type=Path, required=True)
    args = parser.parse_args()
    try:
        check_run(args.run, args.shader)
    except (OSError, ValueError, KeyError) as exc:
        parser.exit(1, f'PIXEL_M0_REJECTED: {exc}\n')
    print('PIXEL_M0_STATIC_PASS target_causality=unproven')
