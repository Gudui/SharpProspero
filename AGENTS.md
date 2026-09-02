# SharpProspero agent instructions

These instructions apply to this repository. Keep them concise and stable: current artifacts, active
experiments and temporary blockers belong in the GuitarHeroPs5 evidence graph and `HANDOFF.md`, not
here.

## Mission and boundary

SharpProspero is an engine-neutral PS5 platform and runtime substrate. It may provide application
hosting, graphics, audio, input, storage and native toolchain primitives. It must not become a game
engine or an adapter for one particular engine.

- ECS, scene graphs, physics, animation, navigation, material systems and editors live above this SDK.
- MonoGame-, Stride-, YARG- and other engine/game-specific APIs live in their own adapter repositories.
- Generic PS5 behavior belongs here; do not create an application-local `Safe*` or duplicate SDK.
- `Renderer3D` is the graphics-RHI acceptance client, not the final abstraction.
- Preserve existing public behavior unless an explicitly reviewed, documented versioning decision
  says otherwise.
- An unrelated utility change is allowed only when it still belongs in this SDK; "unrelated to the
  engine substrate" does not by itself make a generic library feature in scope.

## Read only what the task needs

Do not load the complete engine-substrate design for unrelated work. Use this routing table.

| Work | Required reading in `docs/engine-substrate-design.md` |
|---|---|
| Architecture or cross-layer review | Entire document |
| `ProsperoApp`, runtime or external hosting | Authority and maintenance; source inventory HOST-01–03; Runtime host; Compatibility; Slice 1; relevant ADRs |
| Frame, command, submit or presentation | Proven constraints; PRES-01/GPU-01/CMD-01; dependency rule; frame state machine; error model; Slice 2 |
| GPU resources or direct memory | RES-01–02; resources and pipelines; resource lifetime; error model; validation |
| Shader, pipeline or binding | SHD-01/STATE-01; resources and pipelines; shader contract; validation; relevant ADRs |
| `Renderer3D` migration | Entire document and architecture acceptance criteria |
| Unrelated SDK/tooling work | No engine-substrate design reading required |

Read `docs/engine-substrate.md` when changing scope, priorities, delivery order or the architectural
endpoint. Read `CONTRIBUTING.md` before preparing a contribution.

For work coordinated from the sibling GuitarHeroPs5 checkout, its `HANDOFF.md` is the current cursor
and `docs/evidence-graph/` is the target-evidence authority. Do not reconstruct hardware truth from
memory or from this file.

## Non-negotiable engine-substrate rules

1. Engine-facing APIs and migrated `Renderer3D` code must not require or expose
   `SharpProspero.Graphics.Agc`, `SharpProspero.Interop.Agc`, `SharpProspero.Interop.VideoOut`,
   `DirectMemoryRegion`, native pointers, register offsets, raw descriptors or command handles.
2. A draw records work only. It never closes, submits, retires or presents a frame.
3. Frame close, submit, checked suspension, retirement and presentation are distinct lifecycle
   responsibilities, even when the backend encodes a native flip before the public `Present` call.
4. Exactly one component owns presentation for a display frame. Preserve the target-proven ordering:
   record the flip on the graphics timeline, submit, check `AgcDevice.SuspendPoint()`, then await or
   advance exact display retirement. Only the graphics frame/device backend may encode that flip while
   closing or submitting the frame; a `Draw` recording primitive never encodes it.
5. Never infer GPU or display retirement from elapsed time or one vertical blank. Every wait is bounded
   and reports the frame/submission identity and last observed state on failure.
6. Command, state, upload, shader and resource memory remains alive and unmodified until every
   referencing submission retires. Disposal is idempotent and deferred or rejected while in flight.
7. The public RHI binds logical resources. Only its backend translates immutable reflection into AGC
   registers and descriptors.
8. A shader container, header/resource metadata, register declarations, microcode, stage linkage and
   host bindings are one indivisible contract. Never graft `.shader_text` into a different container or
   keep metadata from another shader lineage.
9. Resource contracts must match by kind, logical slot, dword offset, width and small/full size form.
   Offsets come from serialized metadata; guessed or fallback offsets are forbidden.
10. Unsupported state and invalid transitions fail before unsafe packet emission or native submission.
11. Artifact integrity, successful execution and correct rendering are separate claims. Host tests,
    graceful exit and visible pixels do not substitute for one another.
12. Do not select or implement an engine adapter until the RHI acceptance criteria are satisfied.

These constraints apply even when a build and the existing host suite pass. Several violations have
previously compiled, passed host tests and failed only on hardware.

## Architecture-change protocol

Before changing ownership, dependency direction, frame sequencing, resource lifetime, public RHI
surface or shader contract:

1. Classify the change using the routing table and read the required sections.
2. Name the affected inventory IDs and architecture decisions in the working notes or change
   description.
3. Resolve any `Open` ADR required by the current delivery slice before implementation.
4. State the smallest coherent slice and its compatibility boundary. Do not mix runtime extraction,
   graphics architecture and new hardware capability in one change.
5. Add host enforcement for the dependency, state or lifetime rule before or with implementation.
6. Update `docs/engine-substrate-design.md` in the same commit when observed ownership, accepted
   lifetime, state transitions, API contracts or decisions change.
7. Update its source-inventory commit after the reviewed implementation lands. Supersede decisions in
   the log; do not silently rewrite them.

No graphics RHI implementation begins until Slice 0 architecture enforcement passes. Keep
`ProsperoApp`, the CPU `DisplayDevice` path and low-level AGC diagnostics available as compatibility
controls throughout migration.

Correct rendering requires a prewritten visible oracle and a matching independent observation or
measurement for the identity-proven artifact, alongside successful execution evidence. Record all
three claims separately in the controlling target session and evidence graph.

## Renderer and target work

The following safeguards are triggered by the risk of the work, regardless of which repository or
application coordinates it. Read the relevant skill completely from
`../GuitarHeroPs5/.claude/skills/` before acting:

- `prospero-safety-contracts` before changing input, rendering, storage or startup order;
- `prospero-renderer-probe-method` for every renderer change or GPU probe;
- `prospero-baseline-audit` at handoff/resume, capability boundaries, dependency changes, before probe
  builds, after non-discriminating failures and before defect attribution;
- `prospero-renderer-review` before approving shader/container, binding or renderer-probe work;
- `prospero-static-analysis` before interpreting decrypted `.sprx` or `.sb` instructions;
- `ps5-title-build` before building, staging or registering a title; and
- `ps5-target-evidence` before capturing or interpreting a target run.

A renderer change modifies frame/submit/presentation ordering, GPU resource or register behavior,
shader/container/linkage/binding behavior, or an RHI operation backed by them. A GPU probe is a
purpose-built target artifact that tests one such hardware claim. Pure descriptions and host-only
state validation still follow the architecture protocol, but are not target probes unless they build
or execute that artifact.

Before any renderer probe implementation or build, record a falsifiable hypothesis, qualified control,
single changed variable, support/refutation/inconclusive observations, outcome-specific next actions,
complete shader provenance and resource contract. Run the GuitarHeroPs5 renderer-readiness and
shader-contract gates. If they fail, do not build.

No instruction here authorizes console, deployment, registration, network-listener or other remote
actions. Obtain the authority required by the controlling task.

## Evidence and persistence

| Information | Durable location |
|---|---|
| Architectural destination and delivery order | `docs/engine-substrate.md` |
| Current ownership, states, lifetimes and decisions | `docs/engine-substrate-design.md` |
| Current cross-repository task and deployed artifact | `../GuitarHeroPs5/HANDOFF.md` |
| Hardware claims and experimental history | `../GuitarHeroPs5/docs/evidence-graph/` |
| One target experiment's hypothesis and result | Corresponding GuitarHeroPs5 test-session document |

For coordinated port work, load `project-evidence-graph`, begin with `graph.ps1 context`, update the
graph and bounded `HANDOFF.md` after durable progress, then validate both. A result absent from the
graph is not registered project truth.

## Tests, commits and contributions

- Run tests proportional to the change. A behavior fix needs a regression test that fails on the old
  behavior; an architecture rule needs a negative or near-miss fixture it rejects.
- A host build or unit test never qualifies new target behavior. Run a target test only when the change
  crosses a hardware capability or changes target-visible ordering.
- Commit each coherent design, enforcement, implementation and target-evidence milestone. Do not carry
  evidence debt into another slice.
- Keep generic fixes separate from diagnostic artifacts and unrelated cleanup.
- `origin` is the owner's fork. `upstream` is fetch-only; never attempt to push to it. An upstream PR
  requires a generic, documented, tested change and the issue-first process in `CONTRIBUTING.md`.
- Preserve user changes and do not reformat unrelated files.

Before ending architecture or port work, verify that the living design, evidence graph and handoff
agree with the code and that the worktrees are either clean or have every remaining change reported.
