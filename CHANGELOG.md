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
- SearchView/DetailView layout reorganization: brand `#Header` (top, "VHub PlaylistViewer"), grouped `#SearchBar` (search icon + input), `#TabRow` (HorizontalLayoutGroup, equal-width tabs), and `#DetailHeader` (BackButton + section title) — testing-chamber scene wiring done in MCP, documented in `docs/unity-architecture.md` §13.6 for the upcoming #12 prefab export (#23 Phase A-3)
- DetailView modernization: large playlist cover thumbnail (`#PlaylistThumbnail`, RawImage 200×200) on the left of the cover row, with metadata stacked on the right; track template restyled as a card (UI_RoundedPanel surface tint + 60px height + Position/Title only, per-track thumbnail intentionally omitted). Carry-over `_pendingYtThumbIndex` from the listing item is fed to `ThumbnailLoader.LoadYtThumbnail` so the cover art matches the playlist the user just selected (#23 Phase A-4)

### Changed
- Removed `#TabSearch` button. The Search input field's `OnEndEdit` (Enter / VRChat keyboard close) now triggers `Controller.RequestSearch` directly, replacing the dual "tab + input field" pattern with a single Enter-to-search flow. Placeholder text updated to hint at the Enter behavior. Empty tab slot will be filled by a News tab once [vhub-playlist#97](https://github.com/kisaragi-official/vhub-playlist/issues/97) ships (#23 Phase A-3)
