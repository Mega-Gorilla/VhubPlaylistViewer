# Changelog

All notable changes to this project will be documented in this file.

## [Unreleased]

### Added
- Initial repository scaffold (.gitignore, README)
- Server API design draft (`docs/server-api-spec.md`)
- Unity-side architecture document (`docs/unity-architecture.md`)
- VPM package skeleton (package.json, Runtime/Editor asmdefs)
- Runtime UdonSharp scripts (skeleton implementations)
- Editor scripts (PoolGenerator, AllowedDomainsHelper)
- Phosphor-derived UI sprite set (`Runtime/Sprites/`, MIT) — 6 icons + 9-slice rounded panel + thumb placeholder (#23 Phase A foundational)
- `UISpinner` UdonBehaviour for the loading overlay (#23 Phase A)
- Theme color SerializeFields and tab active-state tracking on `PlaylistViewerController` (#23 Phase A)
- ResultRow card visuals: hover/press color via `Button.colors`, text primary/muted tint pulled from controller theme (#23 Phase A)
