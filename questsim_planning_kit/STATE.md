# QuestSim implementation state

2026-10-25: Q00 presentation comparison complete; stopped at director selection gate.
Director rules retained in DIRECTOR.md. No production visual pipeline selected.

Completed: local Godot .NET launch; three disposable presentation modes (overhead
sprites, 3D sprites, 3D primitive models); first/third-person cameras in both 3D
modes; doorway, four moving actors, player, raised steps and selectable modular
piece. Plain C# fixture remains independent of rendered nodes. No server yet.

Verified on Manjaro/X11: .NET SDK 10.0.110/runtime 10.0.10 with Godot
4.6.2.stable.mono.official.71f334935. Zero build warnings/errors; seven Release
fixture checks; headless bootstrap; real desktop screenshots and focused Space
probe. All three GUI modes passed doorway traversal, wall blocking, raised steps,
construction selection/reject/place; both 3D modes passed first-person controls
and placement. Third-person orbit/tilt exercised. Captures inspected.

Five rendered-window samples (10s warmup/30s measurement), debug, uncapped, 960x540:
p95 intervals 0.338–0.733ms; process RSS 278.8–287.0 MiB on Ryzen 5950X/RTX 3080 Ti.
These tiny single-run baselines do not prove release or population performance.
Evidence, tradeoffs and exact limitations: PRESENTATION_EXPERIMENT.md.

Resolved: full access restored DNS and X11; local download SHA-256 verified against
official metadata. No desktop bridge needed; sibling helper inspected read-only
and left running/unmodified. Initial direct helper test failed because Godot appends
(DEBUG) to the window title; exact-title/PID matching corrected and probe passed.
Initial unused ASP.NET pack restore failure remains resolved by the documented flag.
Final log audit found an unparented 3D camera allocated in 2D mode at shutdown;
3D nodes now allocate only in 3D mode, and all frame samples were rerun after correction.

Checkpoints: original planning/bootstrap archives remain in .artifacts/checkpoints/.
After full access the masked .git was absent; a real local Git repo was initialized.
`git log --oneline` verifies checkpoints; no remote/push. Code and concise evidence
are versioned; tools/caches/raw logs remain ignored under .tools/.local/.artifacts.

Follow-up: director observed inactive Q/E/arrows/C in the running 2D mode. That
behavior is expected for a fixed overhead camera; the shared HUD was misleading.
HUD now lists only available controls per mode (3D tilt is Up/Down, not all arrows).
Build passes with zero warnings/errors; 2D and 3D HUD screenshots inspected.
Existing director-owned 2D process left running; restart to load updated text.

Known limits: third-person wall occlusion, billboard facing, placeholder animation,
no camera collision/mouse-look/NPC collision. No release exports, other hardware,
stacked floors, networking, persistence/reconnect or paused world clock tested.
Production animation/equipment cost and human gameplay preference remain unmeasured.

Pending director decision: representation/camera(s), with world structure separate.
Recommendation: provisional low-poly 3D with first/third-person for the next slice;
overhead is preferable if overview/initial asset scope takes priority. Reference
hardware and weekly capacity remain open; current machine is evidence, not target spec.

One next action: director selects presentation, then refine Q01 into CPU-only server,
two independent clients, shared movement/presence, safe persistent reconnect with
absent disconnected actors, and an empty-server-paused simulation clock. No further
visual pipeline or presentation-dependent controls until that selection.
