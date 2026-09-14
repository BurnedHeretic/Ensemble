<div align="center">

# E N S E M B L E

### // HALO WARS MAP EDITOR //

**A standalone map editing and modding tool for Halo Wars: Definitive Edition on Windows.**

`INITIAL PUBLIC RELEASE`

![Platform](https://img.shields.io/badge/PLATFORM-WINDOWS-17384C?style=for-the-badge&labelColor=071B2B)
![Framework](https://img.shields.io/badge/FRAMEWORK-.NET%208-B65B22?style=for-the-badge&labelColor=071B2B)
![Game](https://img.shields.io/badge/GAME-HALO%20WARS%20DE-6EA8C3?style=for-the-badge&labelColor=071B2B)
![Status](https://img.shields.io/badge/STATUS-INITIAL%20RELEASE-E47A2A?style=for-the-badge&labelColor=071B2B)

**OPEN // INSPECT // SCULPT // POPULATE // EXPORT**

</div>

---

## // OVERVIEW

**Ensemble** is a fan-made editor for **Halo Wars: Definitive Edition** designed to make the game's maps and ERA archives practical to inspect, modify, and extend without manually working through every underlying file.

The editor combines a Halo Wars-inspired interface with a native 3D viewport, terrain editing, object manipulation, cross-map object importing, archive browsing, map metadata tools, and export workflows.

The goal is simple: make Halo Wars map modding feel like working in an actual level editor rather than editing raw archive data by hand.

> **Important:** this is the initial public release. Back up original game files and work on copies of maps while testing.

---

## // CORE SYSTEMS

### 3D MAP VIEWPORT

- Native 3D map viewport with Halo Wars-style presentation.
- Reads and displays supported **UGX** geometry directly from Halo Wars ERA archives.
- Displays terrain textures and map geometry together.
- Orbit, pan, zoom, object selection, and map fitting controls.
- Selected objects can be repositioned with an **X / Y / Z transform gizmo**.
- Live coordinate feedback while objects are moved.

### TERRAIN EDITING

- Loads Halo Wars **XTD terrain height data**.
- Persistent terrain sculpting with raise/lower brushes.
- Adjustable brush radius and strength.
- Undo, redo, and reset sculpt operations.
- Synchronises edited terrain with **XSD gameplay/simulation terrain** when saving.
- Terrain texture import and preview support.
- Terrain, heightmap, grid, and hidden display modes.
- Vegetation removal tools.

### OBJECT EDITING

- Select, move, duplicate, and delete supported map objects.
- Supports both gameplay **SCN objects** and **SC2 ArtObjects**.
- Automatic grounding when placing compatible objects on a different map.
- Object movement participates in Ensemble's normal undo/redo and save history.
- Friendly display names are used where possible while original internal names remain available.

### GLOBAL OBJECT BROWSER

- Automatically scans Halo Wars `.era` archives from the detected game directory.
- Removes duplicate catalogue entries so the same reusable object is not listed repeatedly.
- Search and filter tools for quickly finding useful objects.
- Readable object names alongside internal Halo Wars identifiers.
- Native **3D previews** where a UGX model can be resolved.
- **2D schematic fallback previews** for unsupported or unresolved objects.
- Cross-map object importing without manually opening the donor map first.
- Optional external ERA support for custom maps outside the game directory.

### ERA / MAP TOOLS

- Open and inspect Halo Wars ERA archives.
- Browse archive contents from inside Ensemble.
- Extract individual files or complete archives.
- Edit supported map metadata.
- Custom map display names and descriptions.
- Map thumbnail import.
- Export/register custom maps with Halo Wars.
- Optional Halo Wars executable patch workflow for modular map support.

---

## // QUICK START

1. Download the latest Ensemble release and extract it to its own folder.
2. Run `Ensemble.exe`.
3. Open a Halo Wars map `.era`.
4. If Ensemble does not find the game automatically, use **Tools → Locate Halo Wars Game Assets...** and select `root.era`.
5. Double-click the map's scenario file in the ERA tree to open the 3D map view.
6. Use the **Terrain** and **Objects** menus to edit the map.
7. Save to a working copy and test the result in Halo Wars.

> Do not use your only copy of a stock ERA as a test file. Keep clean backups.

---

## // OBJECT BROWSER WORKFLOW

Open **Objects → Add Object...** or press **Insert**.

Ensemble scans the configured Halo Wars game assets and builds a reusable object library. Select an object to preview it, then place/import it into the current map.

The browser can pull objects from other stock maps while keeping the currently opened map as the save target. This allows environmental props, gameplay hooks, structures, and other supported objects to be reused without manually extracting and rebuilding the donor map first.

Because some Halo Wars objects depend on map-specific logic or external assets, imported objects should always be tested in-game.

---

## // VIEWPORT CONTROLS

| Input | Action |
|---|---|
| **LMB** | Select / interact with object |
| **RMB Drag** | Orbit camera |
| **MMB Drag** | Pan camera |
| **Mouse Wheel** | Zoom |
| **Ctrl + 0** | Fit map |
| **Insert** | Open Object Browser |
| **Delete** | Delete selected supported object |
| **Ctrl + D** | Duplicate selected supported object |
| **Ctrl + Z** | Undo |
| **Ctrl + Y** | Redo |
| **Ctrl + S** | Save |
| **Ctrl + Shift + S** | Save As |

When the transform gizmo is visible:

- **Red** = X axis
- **Green** = Y axis
- **Blue** = Z axis
- Centre/free-move handle = ground-plane movement

---

## // GAME ASSET DETECTION

Ensemble can automatically detect common Halo Wars PC installations and remembers the configured game directory.

If detection fails:

**Tools → Locate Halo Wars Game Assets... → select `root.era`**

Once configured, Ensemble can scan the available ERA files for object-library and asset-resolution purposes.

---

## // BUILDING FROM SOURCE

Ensemble is a **C# / WPF / .NET 8** application.

```bash
git clone https://github.com/BurnedHeretic/Ensemble.git
cd Ensemble
dotnet restore
dotnet build -c Release
```

Requirements:

- Windows 10/11
- .NET 8 SDK for development
- Halo Wars: Definitive Edition assets for map/asset testing

The project targets `net8.0-windows` and uses WPF.

---

## // CURRENT LIMITATIONS

This is an initial public release and Halo Wars contains many specialised object and archive behaviours.

- The 3D viewport is an editor representation, not the original Halo Wars renderer.
- Some uncommon or specialised UGX assets may fail to resolve and use a fallback representation.
- Cross-map object import cannot guarantee that every map-specific script, trigger, dependency, or game rule follows the imported object automatically.
- Some internal object names still require further friendly-name mapping.
- Custom mesh import is experimental; fully automated game-ready dependency injection for arbitrary new meshes is still an area for future work.
- Modified maps should be tested in-game before being distributed.

If something fails, include the map/ERA name, the action that caused the issue, and any exception text when reporting it.

---

## // PROJECT STATUS

The initial public release focuses on the core editing loop:

**OPEN ERA → LOAD MAP → EDIT TERRAIN / OBJECTS → SAVE → TEST IN GAME**

Future development can expand format coverage, object metadata, rendering accuracy, custom asset injection, and additional map authoring tools.

---

## // CREDITS

**Ensemble** is created and maintained by **BurnedHeretic**.

Special thanks to the Halo Wars modding community and to the technical work that has helped document Halo Wars file formats and asset behaviour over the years.

Halo Wars, its assets, names, trademarks, and related intellectual property belong to their respective owners. **Ensemble is a fan-made modding tool and is not affiliated with or endorsed by Microsoft or Xbox Game Studios.**

---

## // SOURCE & FEEDBACK

GitHub repository:

**https://github.com/BurnedHeretic/Ensemble**

Use the repository for source code, updates, issue reports, and development progress.

<div align="center">

---

### E N S E M B L E

`// MODIFY THE BATTLEFIELD //`

</div>
