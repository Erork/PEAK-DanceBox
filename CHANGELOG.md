# DanceBox Changelog

## 2.0.8

### New name and release package

- Renamed the mod to **DanceBox** for a shorter, clearer player-facing identity.
- Unified the in-game title, plugin display name, DLL name, install folder, source project, manifest and Thunderstore metadata.
- Rewrote the README for players with complete installation, controls, model, animation and music instructions.
- Documented supported Humanoid AssetBundle models, AnimationClip bundles, embedded UnityFS resources, and OGG/WAV/MP3 music.
- Added clear notes for unsupported loose FBX/VRM/PMX/GLB/Unity editor files and how to prepare them.
- Updated the settings performance text to reflect game-frame animation instead of the removed 30 FPS cap.

### Upgrade note

- DanceBox uses the new plugin file `com.dline.dancebox.dll` and install folder `BepInEx/plugins/DanceBox`.
- Remove older `PEAKLethalDancesComplete` or `PEAKLethalDances` folders before installing to prevent duplicate loading.

## 2.0.7

- Restored model animation updates to the game's actual rendered frame cadence. The default 60 FPS game setting now produces smooth 60 FPS model animation.
- Increased the default model viewing distance to 2.5 metres.
- Added collision-aware forward/side placement and final ground alignment.
- Added lightweight model fade-in, fade-out and cross-fade transitions.
- Restored original opaque materials after each transition to avoid permanent transparent-material overhead.

## 2.0.6

- Added fixed controls: PageUp/PageDown for model switching and Y for music-first random dance selection.
- Random selection now prefers dances with actual music and moving animation, while avoiding likely pose, idle, preview and test entries when possible.
- Made model packs load only when a dance actually uses them.
- Prepared and positioned models before making them visible, reducing body-origin spawning and obvious clipping.
- Added off-screen skinning and visibility-check performance optimizations.
- Removed obsolete player-facing performance and logging settings.

## 2.0.5

- Fixed dance music becoming silent after startup by keeping AudioClip-backed bundles available for streaming and background loading.
- Fixed invalid or inactive game audio sources incorrectly reducing dance music volume to zero.
- Kept normal mod logging disabled by default.

## 2.0.4

- Added configurable local resource discovery for model, dance and music files.
- Added cached scanning so unchanged files do not need to be fully inspected every launch.
- Added manual **Apply and rescan** controls in the in-game settings.
- Added safe embedded UnityFS extraction without executing foreign DLL code.

## 2.0.3

- Added recursive discovery under BepInEx plugin folders.
- Added support for loose OGG, WAV and MP3 music files.
- Added external model and dance resource importing.

## 2.0.0

- Introduced the combined dance, music and visible Humanoid model system.
- Added synchronized custom dance playback, model selection, spatial music and in-game configuration.
