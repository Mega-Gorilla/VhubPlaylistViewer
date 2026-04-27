# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this repo is

A **VRChat world unitypackage** (UdonSharp + VPM) that adds an in-world search/browse UI for [VHub PlayList](https://playlist.vrc-hub.com). Users find a playlist inside VRChat, copy the resulting resolve URL via VRChat's keyboard, and paste it into a separate video player such as [KawaPlayer](https://github.com/Mega-Gorilla/KawaPlayer). This package is **standalone** and does not depend on KawaPlayer at runtime.

The repository **root itself is the VPM package** (same convention as KawaPlayer `com.vhub.kawaplayer`). There is no `Packages/com.x.y/` subdirectory.

## Where to look first

Two long-form design documents are the source of truth for any non-trivial change:

- `docs/server-api-spec.md` — Spec for new VRChat-facing JSON endpoints to be added to `kisaragi-official/vhub-playlist` (private). Drives every Runtime API client. Defines the `playlist`/`default-thumb` pool IDs the Editor PoolGenerator targets.
- `docs/unity-architecture.md` — Hierarchy structure, `#`/`*`-prefix naming convention, state machine, data flow, the four VRCUrl pools, and Udon coding conventions used here.

Read these before proposing architectural changes; do not duplicate their content into other files.

## Critical Udon constraints that shape the design

These are the non-obvious limits that drive most of the code:

- **`VRCUrl` cannot be constructed from a string at runtime.** All URLs the world fetches must be pre-baked into `VRCUrl[]` in the Editor (see `Editor/Scripts/PoolGenerator.cs`), with one exception: `VRCUrlInputField.GetUrl()` returns a `VRCUrl` from runtime-typed text after VRChat-side validation. This is why the search flow funnels through a `VRCUrlInputField` fed by `Keypad3D`, not a plain string.
- **Only HTTP GET via `VRCStringDownloader.LoadUrl(VRCUrl, IUdonEventReceiver)`.** No POST, no headers, no body. Dynamic state belongs in the URL or pool index.
- **Allowed Domains gate.** `playlist.vrc-hub.com` must be added by the world creator at vrchat.com. `Editor/Scripts/AllowedDomainsHelper.cs` surfaces this in the Inspector — keep that prominent.
- **No multi-player sync.** Every UdonBehaviour uses `[UdonBehaviourSyncMode(BehaviourSyncMode.None)]`. Each player searches/browses locally. Do not introduce `[UdonSynced]` without an explicit decision.

## Conventions specific to this repo

- **`#`-prefix on child Transforms** = the parent UdonBehaviour binds them in `Start()` via `GetComponentsInChildren<Transform>()` + name match. **`*`-prefix** = decorative, scripts must not touch it. This pattern (borrowed from yoshio_will's VisitorsInformationBoard) avoids dozens of `[SerializeField]` slots and makes the prefab self-documenting.
- **Listing UI uses template-clone**, not `List<GameObject>`: a hidden `#ResultTemplate` / `#TrackTemplate` is `Instantiate`d N times, repositioned with `anchoredPosition`, and the parent's `sizeDelta.y` is set to `lineHeight * count` for ScrollRect.
- **Data parsing always uses `VRC.SDK3.Data.DataDictionary` / `DataList`** (via `VRCJson.TryDeserializeFromJson`). Do not use `Newtonsoft.Json` here — that's an Editor-side dependency only.
- **Package + asmdef naming**: package id `com.vhub.kawaplayer-playlistviewer`, asmdef `MegaGorilla.KawaPlayer.PlaylistViewer.{Runtime,Editor}`, C# namespace `MegaGorilla.KawaPlayer.PlaylistViewer[.Editor]`. Do not change without coordinating with the VPM listing.

## Common workflows

### Building / compiling
There is no CLI build. Code is compiled by Unity (UdonSharp) when the user opens the project. **You cannot verify compilation from this environment.** When changing `.cs` files, write code that you are confident is syntactically valid and Udon-compatible; the user will surface compile errors after Unity reload.

### Adding/modifying Runtime UdonSharp scripts
Files live under `Runtime/Scripts/`. Each is a single `UdonSharpBehaviour`. Keep the constraint list above in mind. When you need a new URL, add it to one of the four pools in `Editor/Scripts/PoolGenerator.cs` rather than constructing it at runtime.

### Adding/modifying Editor scripts
Files live under `Editor/Scripts/`. Standard `UnityEditor` API plus `System.Net.HttpWebRequest` (used by `PoolGenerator.ValidatePoolId` to call the existing `/r/{poolId}/_validate` endpoint). The Editor asmdef is gated on `includePlatforms: ["Editor"]`.

### Adding GitHub issues / labels
Use `gh` CLI against `Mega-Gorilla/KawaPlayer_PlaylistViewer` (private). Existing labels: `area:{server-spec,unity-runtime,unity-editor,prefab,docs,meta}`, `priority:{p1,p2,p3}`, `blocked`. Issues #12–#14 are blocked by Unity-Editor-only work (prefab, Animator, screenshots).

### Coordinating server-side changes
The actual server (`kisaragi-official/vhub-playlist`) lives in a separate private repo. The user has access to it. The flow is: edit `docs/server-api-spec.md` here → user opens a PR there based on it (issue #16). Do not attempt to push to `vhub-playlist` directly without explicit instruction.

## Things that intentionally do not exist yet

- **No prefabs / Animator Controllers / materials.** Issues #12 and #13 cover them; they require Unity Editor and are out of scope for CLI-only sessions. The Runtime scripts assume a prefab will be assembled separately matching the Hierarchy diagram in `docs/unity-architecture.md`.
- **No `LICENSE.md`** (issue #15, intentionally TBD).
- **`_references/`** holds third-party reference materials (e.g., yoshio_will's VisitorsInformationBoard) and is `.gitignore`d. Do not commit anything from there or distribute it.

## Reference repos worth checking via `gh api`

- `Mega-Gorilla/KawaPlayer` (public, branch `develop`) — sister project. Its `Modules/PlaylistLoader/` and `package.json` are the canonical reference for VPM/asmdef conventions and the Reflection-based pool-baking pattern in `PoolGenerator`.
- `kisaragi-official/vhub-playlist` (private, accessible to the user) — the server. `docs/design/url-pool-server.md` documents the existing `/r/{poolId}/{playlistId}` and `/vrcurl/{poolId}/{index}` endpoints and the `pool_slots` LRU mechanism that `docs/server-api-spec.md` extends.
