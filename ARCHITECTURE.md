# Valhicle - Architecture & Technical Documentation

This document provides a comprehensive technical reference for the **Valhicle** codebase. It is designed so that any AI assistant or human developer opening this project can immediately understand its architecture, runtime lifecycle, physics engine, networking, and critical bug-prevention patterns.

For the player vision and gameplay requirements, see [GAMEPLAY_SPEC.md](GAMEPLAY_SPEC.md).

---

## 1. Project Overview & Philosophy

* **Mod Name**: Valhicle
* **Target Game**: Valheim (Linux & Windows)
* **Framework**: BepInEx 5.4.x, HarmonyX, .NET Standard 2.1
* **Inspiration**: *Scrap Mechanic* and *Trailmakers* modular vehicle construction adapted to the Viking survival setting.
* **Core Constraint**: **Zero Unity Editor Requirement**. All modular vehicle pieces are procedurally assembled, cloned, and re-engineered at runtime from native Valheim game assets.

---

## 2. Codebase Structure

```text
Valhicle/
├── libs/                           # Reference assemblies stripped from Valheim & BepInEx
├── src/
│   ├── Plugin.cs                   # BepInEx BaseUnityPlugin entrypoint, config, logger, & Harmony patcher
│   ├── Core/
│   │   ├── VehicleCore.cs          # Physics manager, drivetrain, torque distribution & COM stability
│   │   ├── VehiclePiece.cs         # Component on all parts attached to a vehicle; handles docking/release
│   │   └── VehicleLift.cs          # Elevated 1.6m building lift (10 Wood, 1 Surtling Core)
│   ├── Components/
│   │   ├── VehicleWheel.cs         # Wheel sizes (Small, Medium, Large) + directional spin toggle [E]
│   │   ├── VehicleSuspension.cs    # Dynamic 3D coiled metal springs with real-time compression/stretch
│   │   ├── VehicleEngine.cs        # Small & Heavy steam boilers; burns Coal, emits smoke & feeds torque
│   │   ├── VehicleSeat.cs          # Driver chair + steering column + IDoodadController input capture
│   │   └── VehicleBearing.cs       # Button-shaped swivel bearing (Steering, FreeSpinning, Motorized)
│   ├── Patches/
│   │   ├── BuildingPatches.cs      # Auto-parents snapped pieces to nearby vehicles or lifts
│   │   ├── MechanicToolPatches.cs  # Left-click dock/release and diagnostics for Mechanic's Wrench
│   │   ├── PlayerPatches.cs        # Recipe registration on spawn & piece table sanitization
│   │   ├── TerminalPatches.cs      # Custom console commands (clearinventory, clearunequipped)
│   │   ├── WearNTearPatches.cs     # Structural integrity bypass so vehicle parts don't collapse
│   │   └── ZNetScenePatches.cs     # Prefab registration, ObjectDB hooks, & BuildUi crash prevention
│   └── Prefabs/
│       └── PrefabRegistry.cs       # Runtime assembly of all prefabs and piece tables
├── Valhicle.csproj                # C# build configuration with auto-deploy to BepInEx/plugins
├── README.md                      # Player-facing guide and installation instructions
└── ARCHITECTURE.md                # This file (developer & AI reference)
```

---

## 3. Core Systems & Lifecycle

### 3.1. Prefab Registration Lifecycle (`PrefabRegistry.cs`)
1. **Trigger Points**:
   - `ZNetScene.Awake` (Harmony Postfix)
   - `ObjectDB.Awake` and `ObjectDB.CopyOtherDB` (Harmony Postfix)
   - `Player.OnSpawned` (Harmony Postfix fallback)
2. **Template Isolation & Ghost Protection**:
   - Cloned prefabs must have `ZNetView.m_ghostInit = true` during cloning so they do NOT register inside `ZNetScene.m_instances` or spawn phantom ZDOs in the world.
   - Any template ZDO created is immediately purged with `CleanTemplateZNetView()`.
   - **Crucial Rule**: `_prefabRoot` must **NEVER** be destroyed in `Reset()`. Destroying `_prefabRoot` destroys the registered prefab GameObjects in memory while `ObjectDB` still references them, causing game-wide `NullReferenceException` crashes when opening build menus.
3. **Pure Mesh Extraction (`CreateVisualMeshChild`)**:
   - When extracting sub-models (e.g., cart wheels for the steering wheel or wheels, Dvergr mechanical springs for suspensions), we create a clean child GameObject carrying **only** `MeshFilter` and `MeshRenderer`.
   - All `Rigidbody`, `Joint`, `Cloth`, and script components are stripped. This prevents physics decoupling or parts falling to the ground when spawned.

### 3.2. Vehicle Building Lift (`VehicleLift.cs`)
* **Elevated Building Platform**:
  - Emulates *Scrap Mechanic*. The lift base sits on the ground with a central hydraulic pillar rising **1.6 meters high**.
  - On start, it automatically instantiates and docks a **Starter Vehicle Chassis Platform** (`valhicle_chassis`) at `PlatformMountPoint` (Y = 1.6m).
  - While docked: `Rb.isKinematic = true; Rb.useGravity = false;` (the vehicle is completely frozen in mid-air).
  - Players have full clearance to stand under and around the platform to attach suspensions, wheels, and drive components.
* **Physics Drop & Re-docking**:
  - Pressing **[E]** on the lift (or hitting it with the Mechanic's Wrench) calls `ReleaseVehicle()`.
  - The vehicle switches to `isKinematic = false; useGravity = true; Rb.WakeUp();` and drops to the ground via physics.
  - Pressing **[E]** when empty searches for the nearest loose vehicle within 10m and hoists it back onto the lift at 1.6m, freezing physics again for editing.

### 3.3. Drivetrain & Vehicle Physics (`VehicleCore.cs`)
* **Center of Mass Stability**: `Rb.centerOfMass` is lowered (`CenterOfMassOffset = -0.3f`) to prevent cart rollovers during tight cornering.
* **Engine Torque & Fuel**:
  - `VehicleEngine` burns Coal under throttle (`ThrottleInput != 0`).
  - Emits native Valheim steam/smoke particles (`vfx_Smoke`).
  - Feeds torque to `VehicleCore`, which evenly distributes propulsion across all motorized `VehicleWheel` components.
* **Wheel Raycast Physics (`VehicleWheel.cs`)**:
  - Downward raycasts calculate suspension spring-damper reaction forces.
  - Lateral friction counteracts sideways drifting.
  - Independent visual spinning: `WheelMeshTransform` is a child of the wheel piece, so visual rotation does **not** rotate the root piece, its colliders, or the raycast transform.
  - Direction inversion: Pressing `[E]` on any wheel toggles its spin polarity between Normal and Inverted (saved via ZDO).

### 3.4. Driver Seat & Controls (`VehicleSeat.cs`)
* **IDoodadController Integration**:
  - In base Valheim, pressing W/A/S/D while attached to a chair cancels attachment (`AttachStop()`) unless the player has an active `IDoodadController`.
  - `VehicleSeat` implements `IDoodadController`.
  - When the player presses **[E]** to sit, `player.StartDoodadControl(this)` and `player.AttachStart(..., "attach_chair", ...)` are called.
  - Valheim passes player inputs directly into `ApplyControlls(moveDir, lookDir, run, autoRun, block)`.
  - `moveDir.z` controls throttle, `moveDir.x` controls steering, and `block / Space` controls brakes.
  - The player remains seated and animated without accidental dismounting.

### 3.5. Suspensions & Bearings
* **Dynamic Springs (`VehicleSuspension.cs`)**:
  - Clones the authentic 3D coiled helical metal spring mesh from the Dvergr `MechanicalSpring` (`item_mechanicalspring`).
  - `UpdateVisuals(compressionRatio)` scales `SpringMeshTransform.localScale.y` in real-time, compressing and stretching the spring over bumps.
* **Button Swivel Bearings (`VehicleBearing.cs`)**:
  - Designed as a compact, sleek circular mechanical button puck (outer housing radius 0.22m, raised center button radius 0.18m with directional indicator notch).
  - Modes:
    - **Steering**: Rotates with driver A/D steering input (serves as steering knuckles).
    - **FreeSpinning**: Passive caster swivel.
    - **Motorized**: Continuous rotation for drills, turntables, or cranes.

---

## 4. Critical Bug-Prevention Patterns

### 4.1. The PieceTable & BuildUi Crash Fix
* **Problem**: Vanilla Valheim's `BuildUi.IsFavoritePiece` and `FavoritePieceList.IsFavorite` do not check for null when accessing `piece.gameObject.name`. If a destroyed or non-piece object enters `m_pieces`, the game crashes with an NRE and locks the build menu.
* **Solution**:
  1. `PieceTablePatches.UpdateAvailablePrefix`: Purges any null or non-piece GameObjects from `m_pieces` before the base method executes.
  2. `PlayerPatches.UpdateKnownRecipesListPrefix`: Sanitizes all piece tables in player inventory.
  3. `BuildUiPatches.IsFavoritePiecePrefix` & `FavoritePieceListPatches.IsFavoritePrefix`: Safely returns `false` if `piece == null || piece.gameObject == null`.
  4. `BuildUiPieceButtonPatches.SetupPrefix`: Skips button setup if `pieceInfo == null`.
  5. The Mechanic's Wrench is registered with `isPiece: false` so it is never added into piece tables as a building part.

### 4.2. Linux Steam Runtime & Pressure-Vessel Launch Isolation
* **Problem**: Steam on Linux runs Valheim inside the `SteamLinuxRuntime_soldier` container with `pressure-vessel`. Directly prefixing `./start_game_bepinex.sh %command%` applies host `LD_PRELOAD` to Steam's container setup script (`_v2-entry-point`), causing an immediate crash.
* **Solution**: Launch arguments in Steam should use:
  ```bash
  /home/kozmo/.local/share/Steam/steamapps/common/Valheim/start_game_bepinex.sh -console ; echo %command% > /dev/null
  ```
  Or launch directly via terminal:
  ```bash
  ./start_game_bepinex.sh -console
  ```

---

## 5. Console Commands for Testing
Accessible via the Valheim in-game console (**F5**) when `devcommands` is active:
* `clearinventory`: Removes all items from inventory.
* `clearunequipped`: Clears all items except equipped armor and weapons.
* `tod 0.5`: Sets game time to bright noon.
* `spawn valhicle_lift`: Spawns the 1.6m elevated building lift.
* `spawn valhicle_chassis`: Spawns the 2m x 2m modular chassis platform.
* `spawn valhicle_seat`: Spawns the driver station.
* `spawn valhicle_wheel_small` / `valhicle_wheel_medium` / `valhicle_wheel_large`: Spawns modular wheels.
* `spawn valhicle_suspension_std` / `valhicle_suspension_hd`: Spawns dynamic spring suspensions.
* `spawn valhicle_bearing`: Spawns the button swivel bearing.
* `spawn valhicle_engine_small` / `valhicle_engine_heavy`: Spawns coal-burning steam engines.
* `spawn valhicle_wrench`: Spawns the Mechanic's Wrench.
