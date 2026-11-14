# T1D CGM Trainer (VR)

Mixed-reality trainer for type 1 diabetes: applying a CGM sensor to your own arm, and carb counting at true scale.

## What has been made

- **Passthrough prototype** (`Assets/T1DCGM/Scenes/PassthroughPrototype.unity`): checks that colour passthrough
  works over Meta Quest Link. A status panel shows whether passthrough is running. A touch test drops a marker where
  the controller touches your arm and shows the distance between the last two markers.
  - Right controller: **A** drop marker, **B** passthrough on/off
  - Left controller: **X** move panel in front of you, **Y** clear markers
- **Scene builder:** menu **T1D CGM > Build Passthrough Prototype** regenerates the scene and turns on the needed
  OpenXR features (Meta Quest: Session, Meta Quest: Camera (Passthrough)).
- **Shared scripts** (`Assets/T1DCGM/Scripts/Shared`): progress board, guide arrow, scene teleport pad,
  procedural meshes, press button, labels, generated tones.

## Requirements

- Unity **6000.3.23f1** (packages restore from `Packages/manifest.json`)
- Meta **Quest 3** (Quest 1/2 have no colour passthrough)
- Windows PC with the **Meta Quest Link** app, set as the active OpenXR runtime, with
  **Developer Runtime Features** and **Passthrough over Meta Quest Link** turned on
