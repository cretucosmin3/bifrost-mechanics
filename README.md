# Valhicle - Modular Motorized Vehicle Mod for Valheim

A physics-driven, modular vehicle construction mod for Valheim inspired by **Scrap Mechanic** and **Trailmakers**, powered by **BepInEx 5** and **HarmonyX**.

* **Gameplay Vision & Player Goals**: [GAMEPLAY_SPEC.md](GAMEPLAY_SPEC.md)
* **Technical Architecture & Internals**: [ARCHITECTURE.md](ARCHITECTURE.md)

---

## Key Features

* **The Vehicle Building Lift (`valhicle_lift`)**:
  * Building platform costing **10 Wood, 1 Surtling Core**.
  * Features an elevated **1.6m high** hydraulic cradle that automatically holds a **Starter Vehicle Chassis Platform** (`valhicle_chassis`) in the air with physics frozen (`isKinematic = true`).
  * Provides full clearance underneath to snap wheels, suspensions, and drive systems.
  * Press **[E]** on the lift to **Release / Drop** the vehicle to the ground via physics.
  * Press **[E]** when empty to **Dock** the nearest vehicle back into the air for editing.

* **Mechanic's Wrench Tool (`valhicle_wrench`)**:
  * Handcrafted tool (**4 Wood, 2 Stone**).
  * **Right-Click**: Opens the dedicated **Vehicle Workshop Menu**.
  * **Left-Click on a Cart**: Docks or releases the vehicle to/from the nearest Lift.

* **Modular Mechanical Components**:
  * **3 Wheel Sizes**: Small ($0.40\text{m}$), Medium ($0.65\text{m}$), Large ($1.00\text{m}$). Pure Viking wheel geometry on independent spinning hubs. Press `[E]` to toggle spin direction (**Forward / Inverted**).
  * **2 Dynamic Spring Suspensions**: Standard and Heavy-Duty. Uses real 3D coiled metal springs extracted from Dvergr mechanical contraptions that dynamically stretch and compress in real-time over terrain.
  * **2 Steam Engines**: Small ($4,000\text{ Nm}$) and Heavy ($9,000\text{ Nm}$). Burns **Coal** under throttle and emits steam/smoke particles.
  * **Driver Seat & Steering Wheel**: Authentic Viking chair with an angled steering column and animated steering wheel. Mount with `[E]` to drive with **W/A/S/D** and brake with **Space** (uses native `IDoodadController` so you stay securely seated).
  * **Mechanical Swivel Bearing**: Compact button puck. Press `[E]` to cycle modes: **Steering** (turns with driver A/D steering inputs), **FreeSpinning** (passive swivel), or **Motorized** (continuous rotation).

* **Zero Unity Editor Required**: All pieces are procedurally synthesized from in-game Viking assets at runtime.
* **WearNTear Bypass**: Vehicle parts receive structural support directly from the chassis and do not collapse.

---

## Project Layout

```text
Valhicle/
├── libs/                           # Local reference assemblies (BepInEx, Valheim, Unity)
├── src/
│   ├── Core/
│   │   ├── VehicleCore.cs          # Physics manager, drivetrain, torque distribution & COM stability
│   │   ├── VehiclePiece.cs         # Tracks pieces attached to vehicles & handles dismantling
│   │   └── VehicleLift.cs          # 1.6m elevated building platform & lift
│   ├── Components/
│   │   ├── VehicleWheel.cs         # 3 sizes + toggleable spin direction
│   │   ├── VehicleSuspension.cs    # 2 types + dynamic coiled metal spring compression
│   │   ├── VehicleEngine.cs        # 2 steam engines + Coal fuel & smoke effects
│   │   ├── VehicleSeat.cs          # Driver seat + animated steering wheel + IDoodadController
│   │   └── VehicleBearing.cs       # Button swivel bearings (Steering, FreeSpinning, Motorized)
│   ├── Patches/
│   │   ├── BuildingPatches.cs      # Auto-parents snapped pieces to the cart & handles lift docking
│   │   ├── MechanicToolPatches.cs  # Direct click-to-dock, release, and diagnostics for the Wrench
│   │   ├── PlayerPatches.cs        # Recipe registration & piece table sanitization
│   │   ├── TerminalPatches.cs      # Custom console commands (clearinventory, clearunequipped)
│   │   ├── WearNTearPatches.cs     # Structural integrity bypass so vehicle pieces never collapse
│   │   └── ZNetScenePatches.cs     # Prefab registration & BuildUi NRE crash prevention
│   ├── Prefabs/
│   │   └── PrefabRegistry.cs       # Runtime assembly of all prefabs and piece tables
│   └── Plugin.cs                   # BepInEx entrypoint & Harmony loader
├── Valhicle.csproj                # .NET Standard 2.1 C# project file
├── README.md                      # General user guide
└── ARCHITECTURE.md                # In-depth technical and developer documentation
```

---

## How to Build

Run the following command from this directory:

```bash
dotnet build
```

The compiled mod will be output to:
```text
bin/Debug/Valhicle.dll
```

The build target automatically deploys the `.dll` directly into your Valheim plugins folder:
```text
~/.local/share/Steam/steamapps/common/Valheim/BepInEx/plugins/Valhicle.dll
```

---

## Running on Linux (Steam & Native Script)

Because modern Steam on Linux runs Valheim inside the `SteamLinuxRuntime_soldier` container with `pressure-vessel`, do **not** use `./start_game_bepinex.sh %command%` directly (which causes container preload conflicts).

Instead, use either of the following methods:

### Option A: Launching via Steam
1. In Steam, right-click **Valheim** -> **Properties...**
2. Under **Launch Options**, set:
   ```text
   /home/kozmo/.local/share/Steam/steamapps/common/Valheim/start_game_bepinex.sh -console ; echo %command% > /dev/null
   ```

### Option B: Launching via Terminal
Open a terminal in the Valheim directory and run:
```bash
./start_game_bepinex.sh -console
```

---

## Testing & Console Commands (F5)

Ensure `devcommands` is active in the console:
* `clearinventory`: Wipes entire inventory.
* `clearunequipped`: Wipes inventory while preserving equipped gear.
* `tod 0.5`: Sets time to bright midday.
* `spawn valhicle_lift`: Spawns the 1.6m elevated building lift.
* `spawn valhicle_chassis`: Spawns a 2m x 2m vehicle chassis platform.
* `spawn valhicle_seat`: Spawns the driver station with steering column.
* `spawn valhicle_wheel_small` / `valhicle_wheel_medium` / `valhicle_wheel_large`: Spawns wheels.
* `spawn valhicle_suspension_std` / `valhicle_suspension_hd`: Spawns coiled spring suspensions.
* `spawn valhicle_bearing`: Spawns the button swivel bearing.
* `spawn valhicle_engine_small` / `valhicle_engine_heavy`: Spawns steam engines.
* `spawn valhicle_wrench`: Spawns the Mechanic's Wrench.
