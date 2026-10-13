# GunBound Replica (Unity)

A replica of the classic artillery games **GunBound / DragonBound**, built in Unity 6 (6000.0.32f1, 2D URP).
Knight vs Dragon on the Adium map, hot-seat on one keyboard.

## Features
- **Destructible map**: every explosion carves an elliptical crater with a scorched rim, and mobiles fall into holes. Falling off the map knocks you out.
- **Mobiles with full sprite-sheet animation**: idle, walk, attack, hit and defeat. They tilt to the slope, and your angle is relative to that tilt.
- **DragonBound-accurate weapons** (data from the [DragonBound reverse-engineering project](https://github.com/DragonBoundR/DragonBound)):
  - **Knight**: Shot 1 is an orb, then 1 sword drops from the sky. Shot 2 drops 3 swords. SS drops 6.
  - **Dragon**: Shot 1 is 4 staggered fireballs. Shot 2 is 6 fireballs in two waves. SS is an orb that calls down 6 more from above.
- **DragonBound turn rules**:
  - turn delay = shot delay + mobile delay + 10 × seconds used
  - lowest delay goes next
  - passing adds mobile delay + 10 × seconds used
- **Wind** (0–26): the angle changes every 2 turns and the strength every 3. It pushes shells with wind% of gravity.
- **Camera** follows the active mobile and shells. Hold **Tab** for the whole map.
- **GunBound HUD** built from the project's art:
  - control panel with LAST shot, name/HP/weapon and a big angle readout
  - minimap with a red viewport frame
  - power bar with a marker at your last shot's power, and a move bar
  - turn-timer digits, wind gauge, turn order with delays
  - HP tags, damage numbers and markers for off-screen shells
- **Main menu** (Start / Exit), looping soundtrack, win screen and restart.

## Controls
| Key | Action |
|---|---|
| ← / → (A / D) | Move (uses the MOVE bar) |
| ↑ / ↓ (W / S) | Angle (tap = 1°, hold = fast) |
| Space (hold, release) | Charge power and fire |
| 1 / 2 / 3 | Shot 1 / Shot 2 / SS (SS once per match) |
| P | Pass turn |
| Tab (hold) | View whole map |
| Enter | Start from the menu |
| R after game over / Ctrl+R | Restart |

## Setup
1. Open the project in Unity 6000.0.32f1 and open `Assets/Scenes/SampleScene.unity`.
2. The scene is already set up. To rebuild it, use **GunBound ▸ Setup Scene**. It's safe to run again; it rewires the mobiles, weapons, HUD art and menu.
3. Press Play.

## Project layout
```
Assets/Scripts/Core       DestructibleTerrain, WeaponDef (ShellDef / VolleyShot / StrikeDef), SpriteFactory
Assets/Scripts/Gameplay   GameManager (menu, turns, wind, camera, volleys, strikes), Mobile, Projectile, ExplosionFX
Assets/Scripts/UI         GameHUD
Assets/Scripts/Debug      AutoPilot – test bot that plays a match (editor only)
Assets/Editor             GunBoundSetup (menu tool), GunBoundAutomation (file-driven test hooks)
Legacy/                   Original prototype scripts (not compiled, kept for reference)
```

All stats (damage, craters, delays, volleys, strikes, sprites) are editable per mobile in the Inspector.
