# T1D CGM Trainer (VR)

Mixed-reality trainer for type 1 diabetes: applying a CGM sensor to your own arm, and carb counting at true scale.

## What has been made

Build the scenes with menu **T1D CGM > Build Demo Scenes** (regenerates both scenes and the build list).

- **Scene 1, CGM application** (`Assets/T1DCGM/Scenes/CGMApplication.unity`, mixed reality / passthrough)
  - Intro: what a CGM is (a glucose reading every 5 minutes).
  - Arm calibration: touch the controller to your left shoulder, elbow and the back of your upper arm.
  - Clean the site with an alcohol wipe, pick up a one-piece applicator, press it flat onto the back of your real
    arm (TRIGGER), hold still, pull away. Scored on site, flatness, steadiness and site rotation (old sites are saved).
  - Guidance tiers: Guided (virtual sleeve, green zone, mirror view of the back of the arm, haptics),
    Haptics only, No help.
  - Teleport to the LUNCH pad to go to scene 2.
- **Scene 2, Lunch** (`Assets/T1DCGM/Scenes/Lunch.unity`, virtual cafeteria)
  - Serve 50 g of carbs of rice, pasta and bread, by eye at real size. Scoop with a serving spoon; pick up bread.
  - Answer shown at true scale next to your plate, with a 1 cup ball (about a fist).
  - Afterwards the CGM (wall display and a phone) plays back 4 hours of readings for the meal you served.
- **Passthrough prototype** (`Assets/T1DCGM/Scenes/PassthroughPrototype.unity`): passthrough status and a touch test.
- Food models for the side table: low-poly food pack in `Assets/Food Pack-Demo`.

Controls: GRIP pick up, TRIGGER press applicator, A continue / record touch, B recalibrate, X (left) bring the
board in front of you, thumbstick forward to teleport, poke 3D buttons with the controller.

## Requirements

- Unity **6000.3.23f1** (packages restore from `Packages/manifest.json`)
- Meta **Quest 3** (Quest 1/2 have no colour passthrough)
- Windows PC with the **Meta Quest Link** app, set as the active OpenXR runtime, with
  **Developer Runtime Features** and **Passthrough over Meta Quest Link** turned on
