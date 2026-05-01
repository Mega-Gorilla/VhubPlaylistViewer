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
- News tab (`#TabNews`) wired to the deployed `/api/vrc/news?p=0` endpoint (vhub-playlist#97 / PR #99, V1 single page). New `_tabNewsBg` SerializeField + `_activeTabIndex == 3` for News active state. `ListingClient.LoadNews()` fetches the single news URL (paging-guard means p>=1 returns 400, so the client uses a single `_newsUrl` not a pool). `RenderResultList` switches into news mode (title→#Name, body→#Owner, publishedAt YYYY-MM-DD→#TrackCount, placeholder thumbnail). News rows are read-only — `OnSelectResultByIndex` early-returns when `_currentTab == "news"` (no DetailView navigation, V1 spec) (#23 Phase A-5 / News tab)

### Changed
- Removed `#TabSearch` button. The Search input field's `OnEndEdit` (Enter / VRChat keyboard close) now triggers `Controller.RequestSearch` directly, replacing the dual "tab + input field" pattern with a single Enter-to-search flow. Placeholder text updated to hint at the Enter behavior. Empty tab slot will be filled by a News tab once [vhub-playlist#97](https://github.com/kisaragi-official/vhub-playlist/issues/97) ships (#23 Phase A-3)

### Fixed
- `_AutoLoadPopular` race: if the user clicked a different tab during the 2-second auto-load delay, the delayed callback still called `RequestPopular(0)` and overwrote the user's tab selection (Popular tab snapped back to active blue, Recent went to inactive surface). `_AutoLoadPopular` now early-returns when `_activeTabIndex != -1`, so any user interaction before the delay elapses suppresses the auto-load (#23 Phase A user-reported VR test bug)
- `OnSelectResultByIndex` desync: `_pendingOwnerName` and `_pendingYtThumbIndex` were updated before `PlaylistResolver.Resolve(...)` accepted the request, so a second row click during an in-flight resolve could overwrite the pending metadata while the resolver kept loading the first playlist. `Resolve` now returns `bool` (true when accepted, false when busy / invalid), and the controller updates the pending fields only on `true`. Validation also runs before any pending mutation, so invalid rows can no longer briefly clear the previous pending values. Phase A-4 made this much more visible because the cover thumbnail is large (#34 review)
