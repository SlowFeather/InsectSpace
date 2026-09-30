# InsectSpace Engineering Rules

- Read `README.md`, `docs/Architecture.md`, and `docs/Team-Ownership.md` before editing.
- The reference GF repository is read-only. Never modify it as part of this project.
- `vendor/`, `shared/`, client `Assets/InsectSpace/Runtime/`, and `Assets/InsectSpace/Editor/`
  are platform-team owned. Changes require a platform review and regression checks.
- Do not edit vendor binaries or unpack them into gameplay source. Update a complete,
  versioned SDK release and its checksums together.
- Gameplay belongs in `Assets/InsectSpace/HotUpdate/Modules/<Owner>/`.
  AOT assemblies must not reference hot-update assemblies or generated table types.
- Contracts and deterministic simulation have no Unity dependency.
- Shared-world AOI replication is NOT battle lockstep. Home realm, world instance,
  and battle room identities must remain separate. Routing is server-authoritative.
- Do not place secrets, WeChat app secrets, session tickets, or production addresses
  into source, logs, client configuration, or example data.
- Never use Unity physics, floating-point math, wall-clock time, or platform random
  generators in authoritative battle simulation.
- Keep mock/local paths explicitly labelled. Never silently use local simulation
  as a fallback when authentication, patching, or an online connection fails.
- Run `tools/Test-Foundation.ps1`, `tools/Test-Architecture.ps1`, and relevant Unity
  tests. Keep `docs/Validation.md` honest about what has actually been executed.
- A release requires the platform gates in `docs/WeChat-Release-Gates.md`. Editor
  success is not proof of WeChat transport, HybridCLR native support, or device QA.
