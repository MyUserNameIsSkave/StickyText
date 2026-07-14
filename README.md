# StickyText

Scene view tool for placing world-space TextMeshPro sticky notes on level geometry. Drag a rectangle in the viewport, type, and you're done: annotations, bug pins, and blockout notes that live in the scene, organized with coloured tags and stripped from your builds automatically.

https://github.com/MyUserNameIsSkave/StickyText/releases/download/v1.0.0/Stickytext_Demo.mp4

## Features

- **Drag-to-place**: drag a rectangle directly on level geometry in the Scene view; the label sizes itself to the rectangle and auto-fits its text.
- **Three placement modes**: Face Camera, Align to Surface, and Fixed Distance (see [Placement modes](#placement-modes)), switchable mid-drag. If the drag doesn't hit any collision, Face Camera and Align to Surface both fall back to the Fixed Distance plane.
- **Coloured tag system**: tags drive the text/background colour and the Editor Only default of every label using them. Recolour a tag and every label using it follows, with per-label overrides sticking until the tag is reassigned.
- **Management window** (Tools ▸ StickyText): searchable/sortable label list with a jump-to-label button per row, plus a tag manager and settings.
- **Scene view overlay** mirroring the most-used settings while placing, so you rarely need to leave the viewport.
- **Configurable build stripping**: None / Editor Only / All, set independently for Development and Release builds.
- **Mesh-picking or collider-raycast** surface detection for placement, with independent sample counts for each.

## Installation

**Via git URL** (Package Manager ▸ `+` ▸ *Install package from git URL…*):

```
https://github.com/MyUserNameIsSkave/StickyText.git
```

Or download the `.unitypackage` from the [latest release](https://github.com/MyUserNameIsSkave/StickyText/releases/latest) and import it (**Assets > Import Package > Custom Package...**, or just double-click it).

## Requirements

- Unity 6000.0 or newer
- TextMeshPro (`com.unity.ugui` 2.0), installed automatically as a dependency

## Usage

1. Activate the **StickyText** tool in the Scene view toolbar (or bind a shortcut under Edit ▸ Shortcuts ▸ StickyText).
2. Drag a rectangle on your level geometry and type. The label sizes itself to the rectangle.
3. Open **Tools ▸ StickyText** for label management (search, sort, jump to a label with its ◉ button, delete), tag management, and settings.

### Placement modes

| Mode | Behaviour |
| --- | --- |
| **Face Camera** | The label faces the camera, placed at the depth of the nearest geometry under the rectangle. |
| **Align to Surface** | The label lies flat on the surface hit at the drag start, with optional world-axis snapping on floors/ceilings. |
| **Fixed Distance** | The label floats at a fixed, adjustable distance from the camera (hold Ctrl, or pick the tab in the overlay). |

If a drag doesn't hit any collision, Face Camera and Align to Surface both fall back to floating the label at the Fixed Distance plane instead.

Press **N** to cycle base modes; hold **Ctrl** for Fixed Distance.

### Tags

Create coloured tags from the Management page (Tools ▸ StickyText). Each tag drives the text/background colours and the Editor Only default of every label using it: recolour the tag and every label follows. Per-label overrides stick until you reassign the tag.

### Editor Only & build stripping

Labels marked **Editor Only** are dev-only markers: hidden in Play Mode, and stripped from builds. What gets stripped from Development and Release builds is configurable separately (None / Editor Only / All) in the Settings page.

## AI disclosure

Parts of this package (notably the Editor tooling, rendering/material setup, and refactoring passes) were written with the assistance of an LLM (Claude, by Anthropic). Design decisions and testing were human-driven; the LLM was used as a coding assistant, not an autonomous author.

(And yes, this README was drafted by the same LLM.)

## License

MIT — see [LICENSE.md](LICENSE.md).
