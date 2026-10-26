# Orbital Forecast

A Kerbal Space Program 1 (1.12.x) plugin for planning interplanetary and
interstellar transfers. It projects where every celestial body will be at a
chosen future time and draws "ghost" markers in the map view, so you can line
up a transfer by eye and tie the forecast directly to your maneuver nodes.

## Features

- **Forecast any time** — slider (days ahead), `+1d / +10d / +1y` buttons, or
  jump straight to the time of a maneuver node.
- **Ghost markers in map view** — each selected body is drawn at its projected
  position, with a line from where it is now to where it will be.
- **Phase-angle readout** — signed phase angle of each body relative to your
  current body, the number you match for a Hohmann transfer.
- **Interstellar-aware** — positions are resolved by walking each body's
  reference chain, so moons and other star systems (Kopernicus packs such as
  Promised Worlds) are handled correctly.
- **Configurable** via `GameData/OrbitalForecast/settings.cfg` (ModuleManager
  patchable): marker size/colors, whether moons show by default, line toggle,
  default step.

Depends on nothing but KSP itself (uses the stock app-launcher button).

## Building

No Visual Studio or .NET SDK needed — it compiles against KSP's own assemblies
with the C# compiler included in Windows:

```powershell
.\build.ps1                 # builds GameData\OrbitalForecast\Plugins\OrbitalForecast.dll
.\build.ps1 -Install        # also copies GameData\OrbitalForecast into your KSP install
```

Pass `-KspPath "D:\...\Kerbal Space Program"` if your install is elsewhere.

## Making it CKAN-installable

CKAN indexes mods from a small metadata file; the mod itself is hosted
elsewhere (SpaceDock or GitHub releases). `OrbitalForecast.netkan` is ready to
go — replace `YOUR_GITHUB` / `YOUR_NAME`, publish a GitHub release whose zip
has `GameData/OrbitalForecast/...` at its root, then open a pull request adding
the `.netkan` to <https://github.com/KSP-CKAN/NetKAN>. The bundled
`OrbitalForecast.version` file (KSP-AVC format) lets CKAN auto-detect new
releases via the `$vref` line.

## Transfer nodes

Each body row has a **node** button that drops a transfer maneuver node aimed at
that body, leaving *now* — not only at the phase window. A Lambert solver finds
the transfer that connects your current position to where the body will be at
arrival, scanning flight times for the cheapest one, then builds the ejection as
a full 3-D escape hyperbola (so inclined targets like Moho are handled). The
node is finally refined against KSP's own patched-conic solver to shrink the
real closest approach, which it reports on screen.

Near the phase window (`T−` ≈ 0, the row turns green) a transfer is cheap; far
from it you pay extra Δv to go directly. For a hard departure the reliable
pattern is **eject, then correct**: burn the rough node, coast into solar orbit,
then hit **node** again for a precise mid-course correction from where you are.

## Status

v0.9 — anytime Lambert transfers with 3-D ejection and solver-based node
refinement; time slider with day/hour/minute/second entry, star-system folders,
per-system select, phase-angle + time-to-window readout, click-your-trajectory
time picking, and ghost markers that use KSP's own propagation.

## License

MIT. See [LICENSE](LICENSE).
