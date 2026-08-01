# DanceBox

**DanceBox** 是一个面向玩家的舞蹈、音乐与角色模型模组。你可以在游戏中快速切换模型、播放带音乐的舞蹈，并把兼容的模型包、动画包和本地音乐加入自己的舞蹈库。

当前版本：**2.0.8**

## 主要功能

- 内置多组舞蹈资源与 **16 个模型资源包**。
- 按 **PageUp / PageDown** 快速切换当前舞蹈模型。
- 按 **Y** 随机播放舞蹈，并优先选择真正带音乐、动作明显、时长更合适的舞蹈。
- 模型默认生成在玩家视线前方约 **2.5 米**，会检测墙体和空间，尽量避免生成在玩家身体中或与场景穿模。
- 模型切换和出现/消失带有轻量淡入淡出效果。
- 模型动画跟随游戏实际渲染帧率；游戏默认 60 FPS 时，模型也按 60 FPS 更新。
- 模型资源按需加载，浏览模型列表时不会一次性载入全部模型。
- 支持在游戏内搜索舞蹈、音乐和模型，并可手动重新扫描资源。

## 默认按键

| 按键 | 功能 |
| --- | --- |
| **PageUp** | 切换到上一个模型 |
| **PageDown** | 切换到下一个模型 |
| **Y** | 随机播放一个优先带音乐的舞蹈 |
| **,（逗号）** | 打开或关闭 DanceBox 设置界面 |

按键和多数行为都可以在 BepInEx 配置文件或游戏内设置界面中调整。

## 如何使用

1. 安装本模组并启动游戏。
2. 进入游戏后按 **Y**，随机开始一个带音乐的舞蹈。
3. 按 **PageUp** 或 **PageDown** 选择其他模型。
4. 按 **,（逗号）** 打开设置界面：
   - **Models**：选择模型、调整距离、缩放、朝向和贴地设置。
   - **Music & Dances**：搜索并播放指定舞蹈，调整音乐音量。
   - **Playback**：调整移动、跳跃和空中状态下是否停止舞蹈。
   - **Import**：导入或重新扫描外部模型、动画和音乐资源。
   - **System**：查看扫描状态和性能相关选项。

## 支持的模型

DanceBox 支持打包在 Unity AssetBundle 中的角色模型。一个可显示并正常跳舞的模型通常需要：

- AssetBundle 内包含一个或多个 **GameObject 预制体**；
- 预制体或其子对象包含 **Animator**；
- Animator 使用有效的 **Humanoid Avatar**；
- 模型包含 MeshRenderer 或 SkinnedMeshRenderer 等可见 Renderer；
- 骨骼和 Avatar 映射完整，能够接收 Humanoid 动画重定向。

通常可以识别：

- 为 Unity 制作的 Humanoid 角色 AssetBundle；
- 符合上述条件的 Lethal Company ModelReplacement / Customize 类模型资源包；
- DLL 内嵌的 UnityFS 模型资源。DanceBox 只提取资源数据，**不会加载或执行外部 DLL 代码**。

以下格式**不能作为散装文件直接加载**：

- `.fbx`、`.vrm`、`.pmx`、`.pmd`、`.glb`、`.gltf`；
- `.prefab`、`.controller`、`.anim` 等 Unity 编辑器文件。

这些文件需要先在 Unity 中转换为 Humanoid 角色，并打包成兼容的 AssetBundle。不同 Unity 版本制作的资源包可能无法被游戏加载。

### 添加模型

把模型 AssetBundle 放入：

```text
BepInEx/plugins/DanceBox/model-bundles/
```

也可以放在其他 BepInEx 插件目录中。DanceBox 默认会扫描 `BepInEx/plugins` 下的兼容资源。添加文件后，在设置界面的 **Import** 页面执行重新扫描。

## 支持的舞蹈与动画

DanceBox 可读取 Unity AssetBundle 中的 **AnimationClip**：

- 推荐使用 **Humanoid / Mecanim** 动画；
- 支持循环舞蹈和一次性动作；
- 可自动导入资源包中的兼容动画；
- Legacy AnimationClip 不适用于当前 Animator 重定向流程；
- 非 Humanoid 动画默认不会作为普通角色舞蹈导入，除非在配置中明确允许。

把舞蹈或动画 AssetBundle 放入：

```text
BepInEx/plugins/DanceBox/bundles/
```

散装 `.anim` 文件不能直接放进游戏使用，必须先打包成 AssetBundle。

## 支持的音乐

音乐可通过两种方式加入：

1. AssetBundle 内自带 **AudioClip**；
2. 使用独立的本地音乐文件。

支持的独立音乐格式：

- `.ogg`
- `.wav`
- `.mp3`

把音乐放入：

```text
BepInEx/plugins/DanceBox/music/
```

为了自动匹配舞蹈，音乐文件名应尽量与动画名称相同或相近，例如：

```text
Default Dance.ogg
Floss.ogg
Dab.wav
HeadSpin 1.mp3
```

DanceBox 会按名称匹配动画和音乐。音乐支持空间距离效果，也可以跟随游戏音乐音量设置。请只使用你有权使用和发布的音乐。

## 安装方法

### 使用模组管理器

1. 安装 **BepInExPack for PEAK**。
2. 安装 DanceBox。
3. 启动游戏一次，等待配置文件生成。

### 手动安装

将发布包中的内容放入游戏目录，最终结构应类似：

```text
PEAK/
└─ BepInEx/
   └─ plugins/
      └─ DanceBox/
         ├─ com.dline.dancebox.dll
         ├─ bundles/
         ├─ model-bundles/
         └─ music/
```

从旧名称版本升级时，请删除旧的 `PEAKLethalDancesComplete` 或 `PEAKLethalDances` 文件夹，避免两个版本同时加载。

## 联机建议

- 建议一起游玩的玩家安装相同版本和相同资源包。
- 缺少某个模型、动画或音乐资源的玩家，可能看不到完全一致的效果。
- 自定义资源首次使用时可能出现一次短暂加载停顿，之后会复用已加载资源。

## 性能说明

- 模型采用按需加载，不会在启动时全部实例化。
- 离开屏幕的蒙皮模型不会持续进行无意义更新。
- 淡入淡出仅在切换期间使用临时透明材质，完成后恢复普通材质。
- 动画与游戏渲染帧率同步。高帧率会让动作更顺滑，也会增加相应的动画计算量。
- 模型复杂度、材质数量、骨骼数量和贴图尺寸会直接影响性能；高面数模型和大量透明材质更容易造成卡顿。

## 常见问题

### 按 Y 没有播放音乐

确认舞蹈资源中包含 AudioClip，或在 `music` 文件夹中放入与动画名称相近的 OGG/WAV/MP3 文件，并在设置界面重新扫描。

### 模型没有出现在列表中

确认模型包中存在带有效 Humanoid Avatar 的 Animator，并且模型预制体包含可见 Renderer。散装 FBX/VRM 文件不会直接出现。

### 第一次切换某个模型有一点卡

这是按需读取模型包造成的首次加载。后续再次使用同一模型通常会更快。

### 模型位置不理想

打开设置界面，在 **Models** 页面调整生成距离、缩放、旋转和贴地选项。默认距离为 2.5 米；空间不足时模组会自动选择更近或侧面的安全位置。

## 作者与致谢

- 作者：**dline**
- 抖音：**dline**
- 集成并修改 PEAKEmoteLib；相关许可见 `licenses/PEAKEmoteLib-MIT.txt`。
- LethalEmotesAPI 相关来源与许可见 `licenses/LethalEmotesAPI-LGPL-3.0.txt`。

---

# English

**DanceBox** is a player-focused dance, music and character-model mod. Switch visible dance models, play music-backed dances, and add compatible Humanoid model bundles, animation bundles and local music files to your library.

## Highlights

- Includes built-in dance resources and 16 model bundles.
- **PageUp / PageDown** switches the selected model.
- **Y** starts a random dance with priority given to entries that have real music and moving animation.
- Models are placed about 2.5 metres in view with obstacle checks and ground alignment.
- Lightweight fade-in and fade-out transitions are used when models appear or change.
- Model animation follows the game's rendered frame cadence: 60 FPS game output means 60 animation evaluations per second.
- Model bundles are loaded on demand to reduce startup time and memory use.
- Press **Comma (,)** to open the in-game settings and resource browser.

## Supported models

A visible model must be stored in a Unity AssetBundle and normally needs a GameObject prefab with an Animator, a valid Humanoid Avatar and at least one visible Renderer. Compatible ModelReplacement-style bundles and UnityFS resources embedded in DLLs may be discovered; foreign DLL code is never loaded or executed.

Loose FBX, VRM, PMX, PMD, GLB, GLTF, prefab, controller and anim files are not loaded directly. Convert them to a Humanoid Unity prefab and build a compatible AssetBundle first.

Place model bundles in:

```text
BepInEx/plugins/DanceBox/model-bundles/
```

## Supported dances

DanceBox reads AnimationClips from Unity AssetBundles. Humanoid Mecanim clips are recommended. Looping and one-shot clips are supported; Legacy clips are not compatible with the current Animator retargeting path.

Place dance bundles in:

```text
BepInEx/plugins/DanceBox/bundles/
```

## Supported music

DanceBox supports AudioClips inside AssetBundles and loose `.ogg`, `.wav` and `.mp3` files. Use a music filename matching the animation name for automatic pairing.

Place music in:

```text
BepInEx/plugins/DanceBox/music/
```

## Installation

Install BepInExPack for PEAK, then install DanceBox with a mod manager or copy the package into the game directory. For consistent multiplayer results, all players should use the same DanceBox version and resource set.

## Author

Created by **dline**. Please use only models, animations and music that you have permission to use and redistribute.
