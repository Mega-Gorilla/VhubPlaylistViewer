# Repository Guidelines

## Project Structure & Module Organization

This repository is a Unity 2022.3 VPM package for a VRChat world playlist viewer. Runtime UdonSharp code lives in `Runtime/Scripts/` and is compiled by `Runtime/MegaGorilla.KawaPlayer.PlaylistViewer.Runtime.asmdef`. Unity Editor tooling lives in `Editor/Scripts/` and is compiled by `Editor/MegaGorilla.KawaPlayer.PlaylistViewer.Editor.asmdef`. Design and integration references are in `docs/`; keep server changes aligned with `docs/server-api-spec.md`. `_references/` is for local reference material and should not become a source dependency.

## Build, Test, and Development Commands

There are no npm scripts or CLI build tasks in `package.json`; use Unity for compilation and validation.

- Open the package in a Unity 2022.3 project with VRChat Worlds SDK `>=3.8.1`.
- Let Unity recompile assemblies after edits and fix Console errors before committing.
- Use `Tools > KawaPlayer PlaylistViewer > Generate Pools` to validate scene-dependent editor tooling.
- Run `git status` before submitting changes to confirm only intended files are modified.

## Coding Style & Naming Conventions

Use C# with 4-space indentation and braces on their own lines, matching the existing files. Keep runtime code under namespace `MegaGorilla.KawaPlayer.PlaylistViewer`; editor-only code uses `.Editor`. Use PascalCase for classes, methods, and public constants such as `STATE_LOADING`. Use `_camelCase` for private fields, including `[SerializeField]` references. Prefer explicit null checks and UdonSharp-compatible APIs over newer C# features that may not compile in VRChat.

## Testing Guidelines

No automated test framework is currently configured. Validate changes by importing into Unity, checking assembly compile errors, and exercising affected behaviours in Play Mode or a VRChat test scene. For hierarchy-sensitive code, verify required `#`-prefixed objects are bound correctly, for example `#SearchView`, `#ResultTemplate`, and `#UrlField`. Document manual validation steps in the pull request.

## Commit & Pull Request Guidelines

Git history follows conventional commit style, for example `feat(runtime): add 6 UdonSharp runtime scripts (#4-#9)` and `docs: add server API spec and Unity architecture (#1, #2)`. Use `type(scope): summary`, with scopes such as `runtime`, `editor`, `docs`, `build`, or `chore`.

Pull requests should include a concise description, linked issue numbers when applicable, Unity version used for validation, and screenshots or short clips for visible UI changes. Note any server API assumptions, generated pool changes, or manual test coverage.

## Security & Configuration Tips

Do not hard-code private server credentials or tokens. Keep default URLs and pool IDs configurable through serialized fields or editor settings. Treat `package.json` metadata and VPM dependencies as release-facing configuration and update `CHANGELOG.md` when behavior changes.
