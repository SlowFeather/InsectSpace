# Vendor Baseline

The Framework-related UPM archives were copied unchanged from the reference GameFramework local
release packages. Framework.Core remains a precompiled SDK. Neither source nor
native editor toolchains were copied into gameplay.

- Framework SDK family: 1.0.0-alpha.7.
- Framework ZString package: 2.5.0-framework.1.
- HybridCLR: 8.5.0.
- YooAsset: 3.0.5.
- WeChat Tuanjie SDK: changelog **0.1.34**, official repository
  `wechat-miniprogram/minigame-tuanjie-transform-sdk`, pinned commit
  `a09d4b29daa1dd8358b09b5b5639554ab08cfdc2`.
  All 651 upstream file blobs were verified before packaging. The complete source
  tree is repackaged with a `package/` root; no SDK DLLs or source files are patched.
  Upstream declares UPM version **0.1.1** despite the 0.1.34 changelog; this metadata
  is retained, so identify this baseline by commit/archive SHA, not UPM version alone.
- The editor's `wxsdkver.txt` suggested 0.1.32. That complete candidate remains
  archived with commit `f67e7851c345f514e9079819cae16273ff05b33c` for provenance, but is
  not selected: native linking exposed its missing `WX_SyncFunction_tnnt` JS symbol.
  The official later snapshot supplies the matching bridge and runtime.
  See `.artifacts/validation/unity-WeChatNative-sdk032-failed.log`.
- `tools/Import-WeChatSdk.ps1` verifies the selected full Git tree before packaging;
  use `-ArchivePath` for a complete official tarball when raw-file downloads are unavailable.
  Acquisition and native conversion still do not prove device compatibility.
- `dotnet/` contains the corresponding Framework.Core, Deterministic, KCP adapter,
  and underlying Kcp binaries for server/test consumers.
- `dependencies.lock.json` records SHA-256 hashes for the archives and DLLs.
- Three deterministic packages use `1.0.0-alpha.7.insectspace.1` derivatives to
  add missing UPM metadata. `packaging-overrides.json` records their provenance
  and unchanged binary hashes. Original archives are retained, not overwritten.
- Third-party notices/licenses inside the archives remain intact.
- Luban runtime source retains its MIT `LICENSE.txt` in the client runtime folder;
  provenance is the reference project's snapshot of luban_examples commit
  `c52e273f1f50a263e9257dddb35c8e222d9777b8`.

Use `tools/Import-ReferenceDependencies.ps1` only for deliberate SDK baseline
updates. Do not unpack and patch release archives in place. Source-code review,
upstream licenses, package provenance, and platform compatibility remain release
responsibilities; checksums alone are not a signature or a license grant.
