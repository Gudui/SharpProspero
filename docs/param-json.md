---
title: The sce_sys/param.json fields
parent: Toolchain
nav_order: 6
---

# The `sce_sys/param.json` fields

Every package carries a `sce_sys/param.json` describing the title to the system: its identifiers, its
version, its category, and the name shown on the home screen. The templates ship one filled in from the
`--title` and `--titleId` you pass, so a first build needs no editing. This page describes the fields you
are most likely to change and what the rest are for.

## Identity

| Field | What it is |
|---|---|
| `titleId` | The nine-character title id, letters then digits, e.g. `PPSA99099`. Identifies the title. |
| `conceptId` | The numeric concept id (usually the digits of the title id). |
| `contentId` | The full content id: `UP9000-<titleId>_00-<16 chars>`. The package is keyed by this. |
| `contentVersion` | The content version string, e.g. `01.000.000`. Raise it for an update. |
| `masterVersion` | The master version, e.g. `01.00`. |

## The name on screen

`localizedParameters` holds the display name, per language. `defaultLanguage` names the fallback, and
each language code carries a `titleName`. Add a language by adding another entry:

```json
"localizedParameters": {
  "defaultLanguage": "en-US",
  "en-US": { "titleName": "My Game" },
  "ja-JP": { "titleName": "私のゲーム" }
}
```

## Category, badge and rights

| Field | What it is |
|---|---|
| `applicationCategoryType` | The kind of title (an application category number). `0` suits a normal application. |
| `applicationDrmType` | The rights model. `free` is the homebrew choice; the value is a string. |
| `contentBadgeType` | The badge drawn on the icon. `2` is the ordinary badge. |
| `attribute`, `attribute2`, `attribute3` | Feature-flag bitmasks (background audio, cross-save, and so on). Leave `0` unless a feature needs one. |
| `downloadDataSize` | The extra download size to reserve, in bytes. `0` for a self-contained package. |

## System and SDK versions

| Field | What it is |
|---|---|
| `requiredSystemSoftwareVersion` | The lowest firmware the title runs on, as a hex value (`0x0000000000000000` means unset). The toolchain's `sysver` command settles this against the modules a package ships. |
| `sdkVersion` | The SDK version the title was built against, as a hex value. |

## Everything else

`pubtools` records the tool that produced the metadata (`creationDate`, `toolVersion`); `versionFileUri`
points at an update-info file for a title that ships updates, and is empty otherwise. The packager reads
`titleId`, `contentId` and the version fields; leave the rest as the template writes them unless a title
needs the feature a field controls.
