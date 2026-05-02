# Third-Party Notices

VHub PlaylistViewer (`com.vhub.kawaplayer-playlistviewer`) is licensed under the
**Apache License, Version 2.0** (see [LICENSE.md](./LICENSE.md)).

This package incorporates components from the third-party software listed below.
Per Apache 2.0 §4(d), the attribution notices in [NOTICE](./NOTICE) must be
included verbatim in any redistribution. This file provides the full license
texts for each third-party component for reference.

---

## 1. Phosphor Icons (MIT) — bundled in `Runtime/Sprites/`

**What it is**: Open-source icon family. We rasterized the bold-weight SVG sources
via `resvg-py` (4× supersampling → 128×128 PNG) to produce 7 sprite assets in
this package.

**Source**: https://phosphoricons.com/ ・ https://github.com/phosphor-icons/core

**Files derived from Phosphor Icons**:
- `Runtime/Sprites/UI_IconBack.png` (from `arrow-left-bold`)
- `Runtime/Sprites/UI_IconSearch.png` (from `magnifying-glass-bold`)
- `Runtime/Sprites/UI_IconRefresh.png` (from `arrow-clockwise-bold`)
- `Runtime/Sprites/UI_IconMusic.png` (from `music-notes-bold`)
- `Runtime/Sprites/UI_IconError.png` (from `warning-bold`)
- `Runtime/Sprites/UI_LoadingSpinner.png` (from `circle-notch-bold`)
- `Runtime/Sprites/UI_ThumbPlaceholder.png` (composite of `UI_IconMusic` on a dark navy background)

### License (MIT)

```
MIT License

Copyright (c) 2023 Phosphor Icons

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE.
```

---

## 2. UdonSharp (MIT) — runtime dependency, not bundled

**What it is**: C# → Udon transpiler used by every UdonSharpBehaviour script in
this package. Provided to end users via the VRChat Worlds SDK
(`com.vrchat.worlds`); this package does not bundle UdonSharp source code, only
uses its public API (`UdonSharp` / `UdonSharpEditor` namespaces).

**Source**: https://github.com/vrchat-community/UdonSharp

This attribution is provided as a courtesy; no Apache 2.0 attribution obligation
exists for unbundled dependencies.

### License (MIT)

```
MIT License

Copyright (c) 2020 Merlin

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE.
```

---

## 3. Newtonsoft.Json (MIT) — Editor-only dependency, not bundled

**What it is**: JSON parsing library used by `Editor/Scripts/PoolGenerator.cs`
to parse the server's `/api/vrc/yt-thumb-direct-baking` response during
Editor-time pool baking. Provided to end users via Unity's
`com.unity.nuget.newtonsoft-json` package (Unity-bundled); this package does not
bundle Newtonsoft.Json source code, only uses its public API
(`Newtonsoft.Json.Linq` namespace) at Editor-time.

**Source**: https://github.com/JamesNK/Newtonsoft.Json

This attribution is provided as a courtesy; no Apache 2.0 attribution obligation
exists for unbundled dependencies.

### License (MIT)

```
The MIT License (MIT)

Copyright (c) 2007 James Newton-King

Permission is hereby granted, free of charge, to any person obtaining a copy of
this software and associated documentation files (the "Software"), to deal in
the Software without restriction, including without limitation the rights to
use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of
the Software, and to permit persons to whom the Software is furnished to do so,
subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE.
```

---

## 4. VRChat Worlds SDK — runtime dependency, governed by VRChat Creator EULA

**What it is**: VRChat World creation framework providing `VRC.SDKBase` /
`VRC.SDK3.*` / `VRC.Udon.*` namespaces. Required dependency declared in
`package.json` (`com.vrchat.worlds >= 3.8.1`). This package uses only the
public API; no SDK source code is bundled.

**Source**: https://creators.vrchat.com/worlds/

**License**: VRChat Creator EULA (https://hello.vrchat.com/legal/sdk).
The EULA governs end users' use of the VRChat SDK; this package's Apache 2.0
license does not modify those terms.

---

## 5. TextMeshPro / Unity Editor — implicit dependencies

These are part of the Unity Editor environment and are not bundled here. Their
licenses are governed by the Unity Editor terms.
