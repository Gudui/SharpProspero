"""Narrow structured-vertex check over validated LLVM ISA, not a byte decoder."""
import argparse
import hashlib
import json
import re
from pathlib import Path


def instructions(text):
    return [line for raw in text.splitlines()
            if (line := raw.split(';', 1)[0].strip())]


def check_instructions(text):
    lines = instructions(text)
    fetches = [i for i, line in enumerate(lines) if line.startswith('buffer_')]
    if len(fetches) != 1:
        raise ValueError('expected exactly one buffer instruction')
    fetch = fetches[0]
    if not re.fullmatch(
            r'buffer_load_dwordx4\s+v\[8:11\],\s*v5,\s*s\[8:11\],\s*0\s+idxen',
            lines[fetch]):
        raise ValueError('wrong fetch width, destination, vertex index or descriptor slot')
    if fetch + 1 >= len(lines) or lines[fetch + 1] != 's_waitcnt vmcnt(0)':
        raise ValueError('fetch is not immediately followed by a complete VMEM wait')

    expected_exports = [
        'exp prim v1, off, off, off done',
        'exp pos0 v8, v9, v10, v13 done',
        'exp param1 v1, v6, v2, v4',
        'exp param0 v12, v7, v14, v3',
    ]
    positions = []
    for export in expected_exports:
        matches = [i for i, line in enumerate(lines) if line == export]
        if len(matches) != 1:
            raise ValueError('missing or duplicate expected export: ' + export)
        positions.append(matches[0])
    if positions != sorted(positions) or not (positions[0] < fetch < positions[1]):
        raise ValueError('primitive/fetch/parameter export order changed')
    if lines.count('v_mov_b32_e32 v2, v11') != 1:
        raise ValueError('fetched red is not routed to param1.z')
    if lines.count('v_mov_b32_e32 v13, 1.0') != 1:
        raise ValueError('position w is not constant one')
    if lines.count('s_endpgm') != 1 or lines.index('s_endpgm') < positions[-1]:
        raise ValueError('program termination changed')
    return len(lines)


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
        parser.exit(1, f'STRUCTURED_VERTEX_REJECTED: {exc}\n')
    print('STRUCTURED_VERTEX_STATIC_PASS target_causality=unproven')
