# StickyText

Scene view tool for placing world-space TextMeshPro sticky notes on level geometry. Drag a rectangle in the viewport, type, done — annotations, bug pins, and blockout notes that live in the scene, organized with coloured tags, and stripped from your builds automatically.

## Installation

**Via git URL** (Package Manager ▸ `+` ▸ *Install package from git URL…*):

```
https://github.com/MyUserNameIsSkave/StickyText.git?path=Packages/com.stickytext.core
```

Requires Unity 6000.0+ and TextMeshPro (`com.unity.ugui` 2.0, installed automatically).

## Getting started

1. Activate the **StickyText** tool in the Scene view toolbar (or bind a shortcut under Edit ▸ Shortcuts ▸ StickyText).
2. Drag a rectangle on your level geometry and type. The label sizes itself to the rectangle.
3. Open **Tools ▸ StickyText** for label management (search, sort, refocus, delete), tag management, and settings.

### Placement modes

| Mode | Behaviour |
| --- | --- |
| **Face Camera** | The label faces the camera, placed at the depth of the nearest geometry under the rectangle. |
| **Align to Surface** | The label lies flat on the surface hit at the drag start — with optional world-axis snapping on floors/ceilings. |
| **Fixed Distance** | The label floats at a fixed, adjustable distance from the camera (hold Ctrl, or pick the tab in the overlay). |

Press **M** to cycle base modes; hold **Ctrl** for Fixed Distance.

### Tags

Create coloured tags from the Management page (Tools ▸ StickyText). Each tag drives the text/background colours and the Editor Only default of every label using it — recolour the tag, every label follows. Per-label overrides stick until you reassign the tag.

### Editor Only & build stripping

Labels marked **Editor Only** are dev-only markers: hidden in Play Mode, and stripped from builds. What gets stripped from Development and Release builds is configurable separately (None / Editor Only / All) in the Settings page.

## License

MIT — see [LICENSE.md](LICENSE.md).
