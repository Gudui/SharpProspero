# GPU-CO isolated constant orange

Experimental branch codex/constant-orange starts at the CN-tested de6ad14, not the separate
integration worktree. Normal mesh resource names remain diagnostic on this experimental branch;
do not promote this resource substitution as a production mesh shader.

Only runtime diff: mesh_ps.sb source operands at 0x48 F2→F0 and 0x4C F2→80.
LLVM 21.1.8 gfx1030 confirms v3=0.5 and v4=0, retaining v2=v5=1 and the same export/header.
RGBA=(1,0.5,0,1), intended stable orange; not target-qualified yet.
Container SHA-256 ff49d8756e41d443954a99463b1a050d47b7d25587ee9cb0ccc357c2e5f75b8f;
code SHA-256 4b7979bd8cdd88f0e1665f39d987e69f437997040b554f860d7b55a6339d25af.
Original CN hash ab3722ac8e950ead1a53adfa9c2ff0d85be68cae0eb11ea2bc40ffb85461490d
is recovered by reversing exactly those two bytes; tests assert this plus unchanged header.

Application session: docs/ps5-test-session-2026-08-31-gpu-co.md in GuitarHeroPs5.
Actual LLVM run: .cache/shader_runs/20260831-183631-473-mesh_ps-gfx1030 in that repository;
validation passed 63 instructions, zero invalid lines; manifest and ISA reviewed.
First full suite correctly rejected two white-resource identity pins. Branch-specific expectations
were updated to exact orange hashes/instructions, retaining all metadata and CK-restoration checks.
No generic SDK logic, render state, shader export, vertex shader or lifecycle changed.
