# T1D CGM Trainer (VR)

Mixed-reality trainer for type 1 diabetes: applying a CGM sensor to your own arm, and carb counting at true scale.

## What has been made

Build the scenes with menu **T1D CGM > Build Demo Scenes** (regenerates both scenes and the build list). The
generated scenes, meshes, materials, textures and font are not in git (see `.gitignore`), so run it after every pull.

- **Scene 1, CGM application** (`Assets/T1DCGM/Scenes/CGMApplication.unity`, mixed reality / passthrough)
  - Intro: what a CGM is (a glucose reading every 5 minutes).
  - Arm calibration: touch the controller to your left shoulder, elbow and the back of your upper arm.
  - A figure of you (seen from behind) next to the board shows each calibration touch point with a pulsing yellow dot.
  - Clean the site with an alcohol wipe, pick up a one-piece applicator (it keeps the angle you grab it at; hold left
    GRIP near it and twist your left hand to turn it), press it
    flat onto the back of your real arm (TRIGGER clicks it in, no waiting), pull away. Scored on site, flatness and
    site rotation (old sites are saved).
  - Guidance tiers: Guided (virtual sleeve, green zone, mirror view of the back of the arm, haptics),
    Haptics only, No help.
  - Teleport to the LUNCH pad to go to scene 2.
- **Scene 2, Lunch** (`Assets/T1DCGM/Scenes/Lunch.unity`, virtual cafeteria buffet)
  - A tablet floating in front of you gives short illustrated pages (poke NEXT / BACK, or press A / B). When the
    rounds start it glides to each station and hovers above the food.
  - Part 1, guess: a bowl of rice and a bowl of penne, built grain by grain and piece by piece at real size; pick
    them up to look. Dial in a carb estimate on the tablet (-10 / -5 / +5 / +10, LOCK IN). The answer gives the
    carbs per cup and the number of cups, and shows the same amount in measuring cups labelled with their carbs.
  - Part 2, build: put bread (sandwich, thick-cut, bagel), then fruit (banana, apple, orange, watermelon), on a
    plate until it holds a random carb target (25 to 100 g, in 5 g steps), then DONE. Each food has a name card;
    taking a piece leaves a fresh one, so the supply never runs out. The blue RESTART button at
    every station starts that activity again.
  - Every answer shows bars for what you counted and what you ate, whether glucose goes high or low, and the CGM
    trace over 4 hours (REPLAY). The end screen charts every meal with a star score. No insulin doses are shown.
    PLAY AGAIN starts over.
- **Passthrough prototype** (`Assets/T1DCGM/Scenes/PassthroughPrototype.unity`): passthrough status and a touch test.
- Food models for the side table: low-poly food pack in `Assets/Food Pack-Demo`.
- Font: Atkinson Hyperlegible (Braille Institute, SIL Open Font License) in `Assets/T1DCGM/FontSources`; Inter is
  the fallback for symbols.

Controls: GRIP pick up, TRIGGER press applicator, A continue / record touch, B recalibrate (scene 1) or back (scene 2),
X (left) bring the board in front of you, thumbstick forward to teleport, poke 3D buttons with the controller.

## Requirements

- Unity **6000.3.23f1** (packages restore from `Packages/manifest.json`)
- Meta **Quest 3** (Quest 1/2 have no colour passthrough)
- Windows PC with the **Meta Quest Link** app, set as the active OpenXR runtime, with
  **Developer Runtime Features** and **Passthrough over Meta Quest Link** turned on
