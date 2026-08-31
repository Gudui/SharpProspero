# GPU-CP single known varying

Experimental branch codex/known-varying starts from CO 8916347, not the integration worktree.
CO was confirmed stable orange with clean Circle exit and 24 exact vectors; see the application
session docs/ps5-test-session-2026-08-31-gpu-co.md. This is still a diagnostic mesh resource.

CP changes only red from immediate 1.0 to attr1.w through one p1/p2 pair. Unchanged VS exports
param1.w=1.0 for all vertices. Green=0.5, blue=0, alpha=1 stay immediate. Expected image is the same
orange; support would qualify a constant-valued parameter read, not spatial interpolation/full ABI.
No m0, barycentric, header/linkage/export, producer, resource or lifecycle edits.

Generator requires exact CO SHA and replaces only four bytes at 0x44 and four at 0x54.
LLVM 21.1.8 gfx1030 assembly: 000708C8 = v_interp_p1_f32_e32 v2,v0,attr1.w;
010709C8 = v_interp_p2_f32_e32 v2,v1,attr1.w. Remaining v3/v4/v5 moves retained.
Application hypothesis/design checkpoint df68320, session docs/ps5-test-session-2026-08-31-gpu-cp.md.
Target result pending. Do not promote as a production shader or a fix for CK noise.

Actual LLVM run in app .cache/shader_runs/20260831-195309-370-mesh_ps-gfx1030 passes 63 instructions,
zero invalid lines; manifest/validation/ISA reviewed. CP container SHA-256
a8b8411104c3c54d3bc963a7ea3f2809a76451d9f7e868f50af046bab4c6fb08; code SHA-256
657a8cd7ff9d58cbd7685fe19eb9aa26b870893645f8b28e91e96a667dc22735. Full SDK suite 1759/1759 PASS.
Exact two-instruction reversal recovers CO; retained tests also recover CN/CK and check header equality.
