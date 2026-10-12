# Stack Tower VR

A hand-tracked block stacking game for Meta Quest. Pick blocks up with your bare hands, stack them as high as you can in 90 seconds, and try not to knock the tower over.

**[Download the latest APK](https://github.com/Alihamxamalik/Stack-Tower-VR/releases/latest)** · Version 0.1.0 · Built with Unity 6 and the Meta XR SDK

> This is an early build. One game mode (Timed Tower) is playable. Sounds are placeholders and the art is simple shapes. More modes are planned, see [Roadmap](#roadmap).

## What the game is

You stand (or sit) in front of a small pedestal. A block appears on a shelf beside it. You grab it with your hand, carry it over, and set it down on the pedestal. Then the next block appears, and you put it on top of the first. You keep going until the clock runs out or the tower falls.

There are no controllers needed and nothing to learn first: you pinch or close your hand around a block to hold it, and open your hand to let go. The game is real physics, so a block placed off-centre makes the tower lean, and a careless release can bring the whole thing down.

It runs in mixed reality by default, so the pedestal and tower appear in your own room through the headset's passthrough cameras. You can switch to a fully virtual room in the menu.

## How a run goes

A run of Timed Tower lasts 90 seconds and gets harder as it goes:

| Time | What happens |
| --- | --- |
| 0 to 20 s | **Learn.** Only standard cubes. Rotation snapping helps you line blocks up, and a short hint shows what to do. |
| 20 to 70 s | **Ramp.** New block types unlock one after another, blocks arrive faster, and the combo multiplier switches on. |
| 70 to 90 s | **Final rush.** Blocks arrive almost instantly, golden blocks show up four times as often, and the last 10 seconds tick down. |

The run ends when time is up, or early if the tower falls (the top drops to less than half of the best height you reached). The end card shows your score, your height, and your personal bests, with buttons to play again or go back to the menu.

## Blocks

| Block | Appears at | What is different about it |
| --- | --- | --- |
| Standard | Start | An 18 cm cube. The easy one. |
| Plank | 20 s | Twice as long and half as tall. Good for widening the base, bad for height. |
| Golden | 20 s | A cube worth 50 bonus points. Much more common in the final rush. |
| Wedge | 32 s | A light ramp shape with a sloped top. Hard to build on. |
| Cylinder | 38 s | Short and round. It rolls if you set it on its side. |
| Heavy | 46 s | Three times the weight of a standard cube. Punishes a weak base. |
| Slippery | 52 s | An ice cube. Things slide off it. |

## Scoring

- **10 points** for every block placed.
- **1 point per centimetre** each time the tower reaches a new maximum height.
- **+5 precision bonus** for landing a block within 2 cm of the centre of the block below.
- **Combo multiplier** for placing blocks within 3 seconds of each other: x1.5, then x2, then x3. A dropped block or a wobbling tower resets it.
- **+50** for each golden block.

Your best score and best height are saved on the headset. A glowing ring around the tower marks your best height, so you can see when you are about to beat it.

## Made for bare hands

Hand tracking is less precise than a controller, so the game quietly helps:

- **Ghost preview.** A see-through copy of the block shows where it will land. It turns green when it is lined up.
- **Soft alignment.** A block released within 3 cm and 10 degrees of the one below is pulled gently into line.
- **Rotation snap.** While you hold a block, it turns in 15 degree steps. On by default for the learn phase only; you can set it to always or off.
- **Soft release.** A block keeps only 30% of your hand's speed when you let go, so a twitch does not fling it.
- **Lost tracking hold.** If the headset loses sight of your hand, the block freezes in place for half a second instead of dropping.
- **Wobble warning.** The tower tints orange and a sound plays when it starts to sway.

Controllers also work. The headset switches by itself when you pick them up, and you get haptic pulses on grab, placement and wobble.

## Settings

The main menu has large buttons you press with a fingertip:

- **Hand:** right or left
- **Posture:** standing or seated
- **View:** mixed reality or virtual room
- **Rotation snap:** learn phase, always, or off
- **Volume**

The game pauses by itself when you take the headset off or open the system menu.

## Install on your headset

The game is not on the Meta Store, so you install the APK file directly (sideloading).

**You need:** a Meta Quest 3 or 3S (it was built for the 3S; other Quest headsets with hand tracking may work but were not the target), a USB-C cable, and a computer.

1. Turn on developer mode for your headset in the Meta Horizon phone app (Menu → Devices → your headset → Headset settings → Developer mode). Meta asks you to register as a developer first, which is free.
2. Download the `.apk` from the [Releases page](https://github.com/Alihamxamalik/Stack-Tower-VR/releases/latest).
3. Connect the headset to your computer and accept the "Allow USB debugging" prompt inside the headset.
4. Install it with one of these:
   - [SideQuest](https://sidequestvr.com/setup-howto): drag the APK onto the SideQuest window.
   - [Meta Quest Developer Hub](https://developers.meta.com/horizon/documentation/unity/ts-mqdh/): drag the APK onto your device.
   - Command line: `adb install StackTowerVR-0.1.0.apk`
5. In the headset, open the app Library, change the filter to **Unknown sources**, and start **Stack Tower VR**.

Make sure hand tracking is turned on in the headset (Settings → Movement tracking → Hand and body tracking), and play in a well-lit room.

## For developers

### Requirements

- Unity **6000.0.81f1** with the Android build module (OpenJDK, SDK and NDK)
- [Git LFS](https://git-lfs.com) (audio and textures are stored with it)
- Meta XR SDK 207.0.0 (Core, Interaction, Interaction OVR). Unity downloads it from Meta's package registry the first time the project opens, which takes a while.

### Open and build

```bash
git lfs install
git clone https://github.com/Alihamxamalik/Stack-Tower-VR.git
```

Open the folder in Unity Hub. Then:

- **Stack Tower → Setup → Run Full Setup** applies the project settings and regenerates the three scenes and the block prefabs from code. Regenerating overwrites the scenes and prefabs, so only run it on a fresh clone or when you mean to.
- **File → Build Profiles → Android → Build** makes the APK (IL2CPP, ARM64, Vulkan).

You can play without a headset: press Play in the editor and use the mouse to grab blocks and click buttons (mouse wheel moves the held block nearer or further, Q/E turn it, R/F tip it). On Windows you can also use Quest Link, and on any platform the Meta XR Simulator.

### Project layout

Everything specific to the game is under `Assets/_Project`.

| Folder | Contents |
| --- | --- |
| `Scripts/Core` | Run state machine (`GameManager`), events, save file, pause handling, scene flow |
| `Scripts/Gameplay` | Blocks, spawner and pool, tower monitoring, placement assists, scoring |
| `Scripts/XR` | The link to the Meta Interaction SDK, passthrough, hands or controllers detection, haptics, display setup |
| `Scripts/UI` | Menu, in-run HUD, end card, score popups, best-height ring |
| `Scripts/Audio` | Sound playback |
| `Scripts/Editor` | The setup pipeline that builds scenes, prefabs and settings from code |
| `Scripts/Debug` | Frame rate overlay and the mouse and keyboard controls for the editor |
| `Data` | ScriptableObjects: `GameTuning` (every tunable number), level configs, block definitions, physics materials |
| `Scenes` | `Bootstrap` (rig and services, stays loaded), `Menu`, `Game` |
| `Tests/PlayMode` | Play-mode tests for the placement maths, the boot flow and the game loop |

### How it is put together

- Systems talk through a static event hub (`GameEvents`) and do not reference each other directly. Scoring, for example, listens for placements and knows nothing about hands.
- Gameplay code has no dependency on the Meta SDK. Only `Scripts/XR` does, and `InteractionGrabBridge` turns SDK grab events into `BeginHold` / `EndHold` calls on a block, so the block decides its own physics.
- All blocks are pooled and warmed up before the run starts. Nothing is instantiated or destroyed during play.
- Settled blocks more than five layers below the top are made kinematic to save physics work, and wake up again if something hits the tower hard.
- Feel is tuned from one asset, `Data/GameTuning.asset`, which can be edited while the game is running in the editor.
- The app asks the headset for 90 Hz and uses fixed foveated rendering.

### Tests

Window → General → Test Runner → PlayMode → Run All.

## Roadmap

- More modes: Target Height, Zen, Daily Challenge
- A turntable handle to rotate the tower, and choosing where the pedestal sits in your room
- Distance grab as a comfort option
- Music, proper sound effects, bevelled block art and baked lighting
- Block skins and leaderboards

## Credits

Design and code by Ali Hamza. Built with [Unity](https://unity.com) and the [Meta XR SDK](https://developers.meta.com/horizon/develop/unity).
