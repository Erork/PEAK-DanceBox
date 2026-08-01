using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;

namespace PEAKEmoteLib;

/// <summary>
/// Plays Humanoid dance clips through a bundled source rig. Model-pack rigs
/// can be rendered as a complete temporary replacement; otherwise the source
/// rig remains hidden and its solved pose is transferred to PEAK's skeleton.
/// </summary>
[DefaultExecutionOrder(32000)]
internal sealed class SourceModelEmoteDriver : MonoBehaviour
{
    private const float VisibilityCheckInterval = 0.25f;
    private const float MinimumSpawnDistance = 0.8f;
    private const float SpawnCollisionRadius = 0.32f;
    private const float VisibleModelFadeInDuration = 0.18f;
    private const float VisibleModelFadeOutDuration = 0.22f;

    private static readonly BoneRule[] BoneRules =
    {
        new(HumanBodyBones.Hips, null, BoneSide.Center, 0.46f, "hips", "hip", "pelvis", "waist", "root", "spine1"),
        new(HumanBodyBones.Spine, HumanBodyBones.Hips, BoneSide.Center, 0.58f, "spine", "spine2", "spine3", "mid", "waist", "torso", "body"),
        new(HumanBodyBones.Chest, HumanBodyBones.Spine, BoneSide.Center, 0.69f, "chest", "torso", "mid", "spine5", "spine6", "upperbody", "thorax"),
        new(HumanBodyBones.UpperChest, HumanBodyBones.Chest, BoneSide.Center, 0.77f, "upperchest", "torso", "mid", "chest2", "spine8", "spine9", "spine10"),
        new(HumanBodyBones.Neck, HumanBodyBones.UpperChest, BoneSide.Center, 0.86f, "neck", "cervical"),
        new(HumanBodyBones.Head, HumanBodyBones.Neck, BoneSide.Center, 0.95f, "head", "skull"),

        new(HumanBodyBones.LeftShoulder, HumanBodyBones.UpperChest, BoneSide.Left, 0.78f, "shoulder", "sshoulder", "clavicle", "collar"),
        new(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftShoulder, BoneSide.Left, 0.75f, "upperarm", "uparm", "armupper", "bicep", "arm"),
        new(HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftUpperArm, BoneSide.Left, 0.72f, "lowerarm", "forearm", "loarm", "elbow"),
        new(HumanBodyBones.LeftHand, HumanBodyBones.LeftLowerArm, BoneSide.Left, 0.69f, "hand", "wrist", "palm"),
        new(HumanBodyBones.RightShoulder, HumanBodyBones.UpperChest, BoneSide.Right, 0.78f, "shoulder", "sshoulder", "clavicle", "collar"),
        new(HumanBodyBones.RightUpperArm, HumanBodyBones.RightShoulder, BoneSide.Right, 0.75f, "upperarm", "uparm", "armupper", "bicep", "arm"),
        new(HumanBodyBones.RightLowerArm, HumanBodyBones.RightUpperArm, BoneSide.Right, 0.72f, "lowerarm", "forearm", "loarm", "elbow"),
        new(HumanBodyBones.RightHand, HumanBodyBones.RightLowerArm, BoneSide.Right, 0.69f, "hand", "wrist", "palm"),

        new(HumanBodyBones.LeftThumbProximal, HumanBodyBones.LeftHand, BoneSide.Left, 0.69f, "thumbproximal", "thumb1", "finger11"),
        new(HumanBodyBones.LeftThumbIntermediate, HumanBodyBones.LeftThumbProximal, BoneSide.Left, 0.69f, "thumbintermediate", "thumb2", "finger12"),
        new(HumanBodyBones.LeftThumbDistal, HumanBodyBones.LeftThumbIntermediate, BoneSide.Left, 0.69f, "thumbdistal", "thumb3", "finger13"),
        new(HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftHand, BoneSide.Left, 0.69f, "indexproximal", "index1", "finger21"),
        new(HumanBodyBones.LeftIndexIntermediate, HumanBodyBones.LeftIndexProximal, BoneSide.Left, 0.69f, "indexintermediate", "index2", "finger22"),
        new(HumanBodyBones.LeftIndexDistal, HumanBodyBones.LeftIndexIntermediate, BoneSide.Left, 0.69f, "indexdistal", "index3", "finger23"),
        new(HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftHand, BoneSide.Left, 0.69f, "middleproximal", "middle1", "finger31"),
        new(HumanBodyBones.LeftMiddleIntermediate, HumanBodyBones.LeftMiddleProximal, BoneSide.Left, 0.69f, "middleintermediate", "middle2", "finger32"),
        new(HumanBodyBones.LeftMiddleDistal, HumanBodyBones.LeftMiddleIntermediate, BoneSide.Left, 0.69f, "middledistal", "middle3", "finger33"),
        new(HumanBodyBones.LeftRingProximal, HumanBodyBones.LeftHand, BoneSide.Left, 0.69f, "ringproximal", "ring1", "finger41"),
        new(HumanBodyBones.LeftRingIntermediate, HumanBodyBones.LeftRingProximal, BoneSide.Left, 0.69f, "ringintermediate", "ring2", "finger42"),
        new(HumanBodyBones.LeftRingDistal, HumanBodyBones.LeftRingIntermediate, BoneSide.Left, 0.69f, "ringdistal", "ring3", "finger43"),
        new(HumanBodyBones.LeftLittleProximal, HumanBodyBones.LeftHand, BoneSide.Left, 0.69f, "littleproximal", "pinky1", "finger51"),
        new(HumanBodyBones.LeftLittleIntermediate, HumanBodyBones.LeftLittleProximal, BoneSide.Left, 0.69f, "littleintermediate", "pinky2", "finger52"),
        new(HumanBodyBones.LeftLittleDistal, HumanBodyBones.LeftLittleIntermediate, BoneSide.Left, 0.69f, "littledistal", "pinky3", "finger53"),
        new(HumanBodyBones.RightThumbProximal, HumanBodyBones.RightHand, BoneSide.Right, 0.69f, "thumbproximal", "thumb1", "finger11"),
        new(HumanBodyBones.RightThumbIntermediate, HumanBodyBones.RightThumbProximal, BoneSide.Right, 0.69f, "thumbintermediate", "thumb2", "finger12"),
        new(HumanBodyBones.RightThumbDistal, HumanBodyBones.RightThumbIntermediate, BoneSide.Right, 0.69f, "thumbdistal", "thumb3", "finger13"),
        new(HumanBodyBones.RightIndexProximal, HumanBodyBones.RightHand, BoneSide.Right, 0.69f, "indexproximal", "index1", "finger21"),
        new(HumanBodyBones.RightIndexIntermediate, HumanBodyBones.RightIndexProximal, BoneSide.Right, 0.69f, "indexintermediate", "index2", "finger22"),
        new(HumanBodyBones.RightIndexDistal, HumanBodyBones.RightIndexIntermediate, BoneSide.Right, 0.69f, "indexdistal", "index3", "finger23"),
        new(HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightHand, BoneSide.Right, 0.69f, "middleproximal", "middle1", "finger31"),
        new(HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.RightMiddleProximal, BoneSide.Right, 0.69f, "middleintermediate", "middle2", "finger32"),
        new(HumanBodyBones.RightMiddleDistal, HumanBodyBones.RightMiddleIntermediate, BoneSide.Right, 0.69f, "middledistal", "middle3", "finger33"),
        new(HumanBodyBones.RightRingProximal, HumanBodyBones.RightHand, BoneSide.Right, 0.69f, "ringproximal", "ring1", "finger41"),
        new(HumanBodyBones.RightRingIntermediate, HumanBodyBones.RightRingProximal, BoneSide.Right, 0.69f, "ringintermediate", "ring2", "finger42"),
        new(HumanBodyBones.RightRingDistal, HumanBodyBones.RightRingIntermediate, BoneSide.Right, 0.69f, "ringdistal", "ring3", "finger43"),
        new(HumanBodyBones.RightLittleProximal, HumanBodyBones.RightHand, BoneSide.Right, 0.69f, "littleproximal", "pinky1", "finger51"),
        new(HumanBodyBones.RightLittleIntermediate, HumanBodyBones.RightLittleProximal, BoneSide.Right, 0.69f, "littleintermediate", "pinky2", "finger52"),
        new(HumanBodyBones.RightLittleDistal, HumanBodyBones.RightLittleIntermediate, BoneSide.Right, 0.69f, "littledistal", "pinky3", "finger53"),

        new(HumanBodyBones.LeftUpperLeg, HumanBodyBones.Hips, BoneSide.Left, 0.43f, "upperleg", "upleg", "thigh", "femur", "hip", "leg"),
        new(HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftUpperLeg, BoneSide.Left, 0.23f, "lowerleg", "loleg", "calf", "shin", "knee"),
        new(HumanBodyBones.LeftFoot, HumanBodyBones.LeftLowerLeg, BoneSide.Left, 0.07f, "foot", "ankle"),
        new(HumanBodyBones.LeftToes, HumanBodyBones.LeftFoot, BoneSide.Left, 0.02f, "toes", "toe", "ball"),
        new(HumanBodyBones.RightUpperLeg, HumanBodyBones.Hips, BoneSide.Right, 0.43f, "upperleg", "upleg", "thigh", "femur", "hip", "leg"),
        new(HumanBodyBones.RightLowerLeg, HumanBodyBones.RightUpperLeg, BoneSide.Right, 0.23f, "lowerleg", "loleg", "calf", "shin", "knee"),
        new(HumanBodyBones.RightFoot, HumanBodyBones.RightLowerLeg, BoneSide.Right, 0.07f, "foot", "ankle"),
        new(HumanBodyBones.RightToes, HumanBodyBones.RightFoot, BoneSide.Right, 0.02f, "toes", "toe", "ball")
    };

    private static readonly HashSet<HumanBodyBones> CoreBones = new()
    {
        HumanBodyBones.Hips,
        HumanBodyBones.Spine,
        HumanBodyBones.Chest,
        HumanBodyBones.Head,
        HumanBodyBones.LeftUpperArm,
        HumanBodyBones.LeftLowerArm,
        HumanBodyBones.RightUpperArm,
        HumanBodyBones.RightLowerArm,
        HumanBodyBones.LeftUpperLeg,
        HumanBodyBones.LeftLowerLeg,
        HumanBodyBones.RightUpperLeg,
        HumanBodyBones.RightLowerLeg
    };

    // PEAK's native Scout rig uses a compact, stable joint vocabulary.  Do not
    // fuzzy-match these roles: earlier builds accepted helpers such as Scout,
    // Armature, AimJoint and Hat, then wrote their world positions and launched
    // the whole character hierarchy to extreme coordinates.
    private static readonly Dictionary<HumanBodyBones, string[]> NativePeakBoneNames = new()
    {
        [HumanBodyBones.Hips] = new[] { "hip", "waist" },
        [HumanBodyBones.Spine] = new[] { "mid", "spine3", "spine2" },
        [HumanBodyBones.Chest] = new[] { "torso", "chest", "spine6", "spine5" },
        [HumanBodyBones.UpperChest] = new[] { "upperchest", "spine8", "spine9", "spine10" },
        [HumanBodyBones.Neck] = new[] { "neck" },
        [HumanBodyBones.Head] = new[] { "head" },

        [HumanBodyBones.LeftShoulder] = new[] { "sshoulderl", "shoulderl" },
        [HumanBodyBones.LeftUpperArm] = new[] { "arml" },
        [HumanBodyBones.LeftLowerArm] = new[] { "elbowl" },
        [HumanBodyBones.LeftHand] = new[] { "handl", "fingerl" },
        [HumanBodyBones.RightShoulder] = new[] { "sshoulderr", "shoulderr" },
        [HumanBodyBones.RightUpperArm] = new[] { "armr" },
        [HumanBodyBones.RightLowerArm] = new[] { "elbowr" },
        [HumanBodyBones.RightHand] = new[] { "handr", "fingerr" },

        [HumanBodyBones.LeftThumbProximal] = new[] { "finger11l" },
        [HumanBodyBones.LeftThumbIntermediate] = new[] { "finger12l" },
        [HumanBodyBones.LeftThumbDistal] = new[] { "finger13l" },
        [HumanBodyBones.LeftIndexProximal] = new[] { "finger21l" },
        [HumanBodyBones.LeftIndexIntermediate] = new[] { "finger22l" },
        [HumanBodyBones.LeftIndexDistal] = new[] { "finger23l" },
        [HumanBodyBones.LeftMiddleProximal] = new[] { "finger31l" },
        [HumanBodyBones.LeftMiddleIntermediate] = new[] { "finger32l" },
        [HumanBodyBones.LeftMiddleDistal] = new[] { "finger33l" },
        [HumanBodyBones.LeftRingProximal] = new[] { "finger41l" },
        [HumanBodyBones.LeftRingIntermediate] = new[] { "finger42l" },
        [HumanBodyBones.LeftRingDistal] = new[] { "finger43l" },
        [HumanBodyBones.LeftLittleProximal] = new[] { "finger51l" },
        [HumanBodyBones.LeftLittleIntermediate] = new[] { "finger52l" },
        [HumanBodyBones.LeftLittleDistal] = new[] { "finger53l" },
        [HumanBodyBones.RightThumbProximal] = new[] { "finger11r" },
        [HumanBodyBones.RightThumbIntermediate] = new[] { "finger12r" },
        [HumanBodyBones.RightThumbDistal] = new[] { "finger13r" },
        [HumanBodyBones.RightIndexProximal] = new[] { "finger21r" },
        [HumanBodyBones.RightIndexIntermediate] = new[] { "finger22r" },
        [HumanBodyBones.RightIndexDistal] = new[] { "finger23r" },
        [HumanBodyBones.RightMiddleProximal] = new[] { "finger31r" },
        [HumanBodyBones.RightMiddleIntermediate] = new[] { "finger32r" },
        [HumanBodyBones.RightMiddleDistal] = new[] { "finger33r" },
        [HumanBodyBones.RightRingProximal] = new[] { "finger41r" },
        [HumanBodyBones.RightRingIntermediate] = new[] { "finger42r" },
        [HumanBodyBones.RightRingDistal] = new[] { "finger43r" },
        [HumanBodyBones.RightLittleProximal] = new[] { "finger51r" },
        [HumanBodyBones.RightLittleIntermediate] = new[] { "finger52r" },
        [HumanBodyBones.RightLittleDistal] = new[] { "finger53r" },

        [HumanBodyBones.LeftUpperLeg] = new[] { "legl", "hipl" },
        [HumanBodyBones.LeftLowerLeg] = new[] { "kneel" },
        [HumanBodyBones.LeftFoot] = new[] { "footl" },
        [HumanBodyBones.LeftToes] = new[] { "stoe1l", "toel", "toesl" },
        [HumanBodyBones.RightUpperLeg] = new[] { "legr", "hipr" },
        [HumanBodyBones.RightLowerLeg] = new[] { "kneer" },
        [HumanBodyBones.RightFoot] = new[] { "footr" },
        [HumanBodyBones.RightToes] = new[] { "stoe1r", "toer", "toesr" }
    };

    private static readonly HashSet<string> ForbiddenTargetNames = new()
    {
        "bone", "armature", "scout", "character", "aimjoint", "hat",
        "propellerhat", "root", "rig", "model"
    };

    private readonly List<BoneBinding> boneBindings = new();
    private readonly List<TargetTransformState> targetStates = new();

    private CharacterAnimations characterAnimations = null!;
    private GameObject? proxyRoot;
    private Animator? proxyAnimator;
    private Renderer[] proxyRenderers = Array.Empty<Renderer>();
    private readonly List<RendererState> peakRendererStates = new();
    private readonly List<Material> runtimeMaterials = new();
    private PlayableGraph graph;
    private AnimationClipPlayable playable;
    private bool graphCreated;
    private bool loop;
    private float clipLength;
    private float playbackStartedAt;
    private float diagnosticAt;
    private bool diagnosticWritten;
    private float hipsTranslationScale = 1f;
    private bool rootSafetyCorrectionLogged;
    private bool visibleModel;
    private bool peakRenderersHidden;
    private bool visibilityFailSafeLogged;
    private int visibleModelLayer;
    private string visibleModelValidationSummary = string.Empty;
    private Vector3 importedRootScale = Vector3.one;
    private Vector3 visibleModelRootScale = Vector3.one;
    private Vector3 visibleModelRootLocalPosition = Vector3.zero;
    private Quaternion visibleModelRootLocalRotation = Quaternion.identity;
    private bool visibleModelPlacementLocked;
    private float nextVisibilityCheckAt;
    private VisibleModelFadeController? visibleModelFadeController;

    public bool IsPlaying => proxyRoot != null && graphCreated && (visibleModel || boneBindings.Count > 0);
    public bool IsVisibleModel => IsPlaying && visibleModel;

    public void Initialize(CharacterAnimations animations)
    {
        characterAnimations = animations;
        enabled = false;
    }

    public bool Play(Emote emote, SourceRigAsset sourceRig, bool showSourceModel)
    {
        StopPlayback();

        try
        {
            Animator targetAnimator = characterAnimations.character.refs.animator;
            Transform characterRoot = characterAnimations.character.transform;
            visibleModel = showSourceModel;

            proxyRoot = Instantiate(sourceRig.Prefab);
            proxyRoot.name = $"PEAKEmoteLib_HiddenSolver_{sourceRig.Prefab.name}";
            proxyRoot.SetActive(false);
            proxyRoot.transform.SetParent(characterRoot, false);
            proxyRoot.transform.localPosition = visibleModel
                ? Vector3.forward * Mathf.Max(MinimumSpawnDistance, RuntimeOptions.VisibleModelForwardOffset)
                : Vector3.zero;
            proxyRoot.transform.localRotation = Quaternion.identity;
            importedRootScale = proxyRoot.transform.localScale;
            // ModelReplacement prefabs may serialize an authoring/import scale on
            // the prefab root (xiehen70 is 82.882). That scale is not a character
            // size and must never participate in visible-model height matching.
            // Hidden pose solvers keep their authored scale because their local
            // skeleton coordinates are already normalized during retargeting.
            if (visibleModel)
            {
                proxyRoot.transform.localScale = Vector3.one;
            }
            visibleModelRootScale = proxyRoot.transform.localScale;
            visibleModelRootLocalPosition = proxyRoot.transform.localPosition;
            visibleModelRootLocalRotation = proxyRoot.transform.localRotation;
            visibleModelPlacementLocked = false;

            Transform? animatorTransform = string.IsNullOrEmpty(sourceRig.AnimatorPath)
                ? proxyRoot.transform
                : proxyRoot.transform.Find(sourceRig.AnimatorPath);
            proxyAnimator = animatorTransform == null ? null : animatorTransform.GetComponent<Animator>();
            if (proxyAnimator == null || proxyAnimator.avatar == null ||
                !proxyAnimator.avatar.isValid || !proxyAnimator.avatar.isHuman)
            {
                DanceLog.Error(
                    $"Source rig '{sourceRig.Prefab.name}' was instantiated, but its Humanoid Animator/Avatar is unavailable.");
                StopPlayback();
                return false;
            }

            SanitizeSourceRig(proxyRoot, proxyAnimator, visibleModel);
            ActivatePath(proxyAnimator.transform, proxyRoot.transform);
            proxyRoot.SetActive(true);
            proxyRenderers = proxyRoot.GetComponentsInChildren<Renderer>(true);

            proxyAnimator.enabled = true;
            proxyAnimator.applyRootMotion = false;
            proxyAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            proxyAnimator.updateMode = AnimatorUpdateMode.Normal;
            proxyAnimator.runtimeAnimatorController = null;
            proxyAnimator.Rebind();
            proxyAnimator.Update(0f);
            foreach (BoneRule rule in BoneRules)
            {
                Transform? sourceBone = proxyAnimator.GetBoneTransform(rule.Bone);
                if (sourceBone != null)
                {
                    ActivatePath(sourceBone, proxyRoot.transform);
                }
            }

            Dictionary<HumanBodyBones, SourceBoneState> sourceBones = CaptureSourceBones(proxyAnimator);
            if (visibleModel)
            {
                PrepareVisibleModel(characterRoot, sourceBones);
                proxyAnimator.Update(0f);
                if (!ValidateVisibleModel(out visibleModelValidationSummary))
                {
                    DanceLog.Error(
                        $"Visible replacement model '{sourceRig.SelectionName}' has no renderable mesh/material after compatibility repair: " +
                        visibleModelValidationSummary);
                    StopPlayback();
                    return false;
                }
            }
            else if (!BuildBoneBindings(characterRoot, targetAnimator, sourceBones))
            {
                StopPlayback();
                return false;
            }

            graph = PlayableGraph.Create($"PEAKEmoteLib_HiddenSolver_{emote.AnimationClip.name}");
            graphCreated = true;
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            playable = AnimationClipPlayable.Create(graph, emote.AnimationClip);
            playable.SetApplyFootIK(false);
            playable.SetApplyPlayableIK(false);
            playable.SetSpeed(0d);
            AnimationPlayableOutput output = AnimationPlayableOutput.Create(graph, "HumanoidSourceModel", proxyAnimator);
            output.SetSourcePlayable(playable);
            graph.Play();
            playable.SetTime(0d);
            playable.SetDone(false);
            graph.Evaluate(0f);
            nextVisibilityCheckAt = Time.unscaledTime;
            if (visibleModel)
            {
                // Final placement must use the animated frame-zero feet, not the
                // prefab bind pose. This keeps different leg lengths and shoe
                // offsets from placing the replacement below the floor.
                FinalizeVisibleModelPlacement(characterRoot);
                visibleModelFadeController = proxyRoot.AddComponent<VisibleModelFadeController>();
                visibleModelFadeController.BeginFadeIn(proxyRenderers, VisibleModelFadeInDuration);
            }
            else
            {
                ApplyRetargetedPose();
            }

            loop = emote.Type == Emote.EmoteType.Loop;
            clipLength = Mathf.Max(0.05f, emote.AnimationClip.length);
            playbackStartedAt = Time.time;
            diagnosticAt = Time.unscaledTime + 0.35f;
            diagnosticWritten = false;
            rootSafetyCorrectionLogged = false;
            enabled = true;

            if (visibleModel)
            {
                DanceLog.Info(
                    $"Playing '{emote.DisplayName}' on visible replacement model " +
                    $"'{sourceRig.Prefab.name}/{proxyAnimator.avatar.name}' from '{sourceRig.SourceName}'; " +
                    $"renderers={proxyRenderers.Length}, layer={visibleModelLayer}, scale={proxyRoot.transform.localScale.x:0.000}, " +
                    $"importedRootScale={FormatVector(importedRootScale)}, validation=[{visibleModelValidationSummary}].");
            }
            else
            {
                string mappingSummary = string.Join(", ", boneBindings
                    .Take(32)
                    .Select(binding => $"{binding.Bone}:{binding.Target.name}"));
                DanceLog.Info(
                    $"Playing '{emote.DisplayName}' on PEAK's original model using hidden Humanoid solver " +
                    $"'{sourceRig.Prefab.name}/{proxyAnimator.avatar.name}'; mappedBones={boneBindings.Count}, " +
                    $"hiddenSourceRenderers={proxyRenderers.Length}, mappings=[{mappingSummary}].");
            }
            return true;
        }
        catch (Exception exception)
        {
            DanceLog.Error($"Source-model playback failed for '{emote.DisplayName}': {exception}");
            StopPlayback();
            return false;
        }
    }

    public void StopPlayback()
    {
        StopPlayback(false);
    }

    private void StopPlayback(bool immediate)
    {
        enabled = false;

        if (graphCreated || graph.IsValid())
        {
            try
            {
                if (graph.IsValid())
                {
                    graph.Destroy();
                }
            }
            catch (Exception exception)
            {
                DanceLog.Debug($"Could not destroy hidden-solver PlayableGraph cleanly: {exception.Message}");
            }
        }
        graphCreated = false;

        RestoreTargetPose();
        RestorePeakRenderers();
        boneBindings.Clear();
        targetStates.Clear();

        if (proxyRoot != null)
        {
            GameObject rootToRelease = proxyRoot;
            VisibleModelFadeController? fadeController = visibleModelFadeController;
            if (!immediate && visibleModel && fadeController != null && rootToRelease.activeInHierarchy)
            {
                fadeController.FadeOutAndDestroy(VisibleModelFadeOutDuration);
            }
            else
            {
                Destroy(rootToRelease);
            }
            proxyRoot = null;
        }
        foreach (Material material in runtimeMaterials)
        {
            if (material != null)
            {
                Destroy(material);
            }
        }
        runtimeMaterials.Clear();
        proxyAnimator = null;
        proxyRenderers = Array.Empty<Renderer>();
        loop = false;
        clipLength = 0f;
        playbackStartedAt = 0f;
        diagnosticAt = 0f;
        diagnosticWritten = false;
        hipsTranslationScale = 1f;
        visibleModel = false;
        peakRenderersHidden = false;
        visibilityFailSafeLogged = false;
        visibleModelLayer = 0;
        visibleModelValidationSummary = string.Empty;
        importedRootScale = Vector3.one;
        visibleModelRootScale = Vector3.one;
        visibleModelRootLocalPosition = Vector3.zero;
        visibleModelRootLocalRotation = Quaternion.identity;
        visibleModelPlacementLocked = false;
        nextVisibilityCheckAt = 0f;
        visibleModelFadeController = null;
    }

    private void OnDestroy()
    {
        StopPlayback(true);
    }

    private void LateUpdate()
    {
        if (!graph.IsValid() || proxyRoot == null || proxyAnimator == null || (!visibleModel && boneBindings.Count == 0))
        {
            StopPlayback();
            return;
        }

        float elapsed = Mathf.Max(0f, Time.time - playbackStartedAt);
        double sampleTime = loop && clipLength > 0.05f
            ? elapsed % clipLength
            : Math.Min(elapsed, clipLength);
        // Manual PlayableGraphs do not inherit the Animator's normal update.
        // Evaluate once per rendered game frame so the visible model follows the
        // game's configured/actual frame rate instead of imposing a separate
        // 30 FPS animation cap. A 60 FPS game therefore produces 60 model pose
        // updates, while higher frame-rate settings remain equally smooth.
        playable.SetTime(sampleTime);
        playable.SetDone(false);
        graph.Evaluate(0f);
        if (!visibleModel)
        {
            ApplyRetargetedPose();
        }
        else
        {
            // Humanoid clips and imported prefab metadata must not be allowed to
            // restore the serialized authoring scale after graph evaluation.
            if (visibleModelPlacementLocked)
            {
                // Humanoid clips from ModelReplacement packs can contain root
                // translation/rotation curves. Keep the replacement attached to
                // the stable player root while allowing hips and limbs to animate.
                proxyRoot.transform.localPosition = visibleModelRootLocalPosition;
                proxyRoot.transform.localRotation = visibleModelRootLocalRotation;
                proxyRoot.transform.localScale = visibleModelRootScale;
            }
            else if ((proxyRoot.transform.localScale - visibleModelRootScale).sqrMagnitude > 0.000001f)
            {
                proxyRoot.transform.localScale = visibleModelRootScale;
            }
            UpdateVisibleModelSafety();
        }

        if (!diagnosticWritten && Time.unscaledTime >= diagnosticAt)
        {
            diagnosticWritten = true;
            int visibleSourceRenderers = proxyRenderers.Count(renderer =>
                renderer != null && renderer.enabled && !renderer.forceRenderingOff && renderer.gameObject.activeInHierarchy);
            if (visibleModel)
            {
                int cameraVisibleRenderers = CountRenderersVisibleToGameplayCamera();
                string rendererDetails = BuildVisibleRendererDiagnostics();
                DanceLog.Info(
                    $"Visible-model playback check: graphPlaying={graph.IsPlaying()}, playableTime={playable.GetTime():0.000}s, " +
                    $"visibleModelRenderers={visibleSourceRenderers}, cameraVisibleRenderers={cameraVisibleRenderers}, " +
                    $"PEAKRenderersHidden={peakRenderersHidden}, PEAKRenderersStillEnabled={CountEnabledPeakRenderers()}, " +
                    $"details=[{rendererDetails}].");
            }
            else
            {
                int liveTargets = boneBindings.Count(binding => binding.Target != null && binding.Target.gameObject.activeInHierarchy);
                DanceLog.Info(
                    $"PEAK-model retarget check: graphPlaying={graph.IsPlaying()}, playableTime={playable.GetTime():0.000}s, " +
                    $"liveTargetBones={liveTargets}/{boneBindings.Count}, visibleSourceRenderers={visibleSourceRenderers}, " +
                    $"PEAKRenderersStillEnabled={CountEnabledPeakRenderers()}.");
            }
        }
    }

    private void PrepareVisibleModel(
        Transform characterRoot,
        Dictionary<HumanBodyBones, SourceBoneState> sourceBones)
    {
        if (proxyRoot == null || proxyAnimator == null)
        {
            return;
        }

        proxyRoot.transform.localRotation = Quaternion.Euler(0f, RuntimeOptions.VisibleModelYaw, 0f);
        // Always discard the prefab authoring/import scale before measuring.
        // xiehen70 serializes 82.882 on the root; multiplying that value was the
        // direct cause of the 70-140 metre renderer bounds in 1.3.3.
        proxyRoot.transform.localScale = Vector3.one;
        proxyAnimator.Update(0f);

        float manualScale = Mathf.Max(0.01f, RuntimeOptions.VisibleModelScale);
        float targetRatio = Mathf.Clamp(RuntimeOptions.VisibleModelTargetHeightRatio, 0.5f, 1.5f);
        float autoScale = 1f;
        float sourceVisualHeight = 0f;
        float targetVisualHeight = 0f;
        string sourceMethod = "none";
        string targetMethod = "none";

        bool sourceHeightValid = TryGetVisibleModelGeometryHeight(characterRoot.up, out sourceVisualHeight);
        if (sourceHeightValid)
        {
            sourceMethod = "mesh-local-bounds";
        }
        else if (TryGetFullBodyBoneHeight(sourceBones, characterRoot.up, out sourceVisualHeight))
        {
            sourceHeightValid = true;
            sourceMethod = "full-body-bones";
        }

        // PEAK has many independent body-part/accessory renderers. Skeleton
        // height is a cleaner reference than a renderer union that may include
        // backpacks, ropes or held items.
        bool targetHeightValid = TryGetPeakFullBodyBoneHeight(characterRoot, characterRoot.up, out targetVisualHeight);
        if (targetHeightValid)
        {
            targetMethod = "peak-full-body-bones";
        }
        else if (TryGetPeakReferenceHeight(characterRoot, characterRoot.up, out targetVisualHeight))
        {
            targetHeightValid = true;
            targetMethod = "peak-hips-head-estimate";
        }
        else if (TryGetPeakVisualHeight(characterRoot, characterRoot.up, out targetVisualHeight))
        {
            targetHeightValid = true;
            targetMethod = "peak-mesh-local-bounds";
        }

        if (RuntimeOptions.AutoScaleVisibleModel)
        {
            if (sourceHeightValid && targetHeightValid)
            {
                autoScale = Mathf.Clamp(targetVisualHeight * targetRatio / sourceVisualHeight, 0.001f, 10f);
            }
            else if (sourceHeightValid)
            {
                // A canonical unit-root ModelReplacement mesh is normally authored
                // in metres. If PEAK's inactive third-person body cannot provide a
                // usable reference, retain that natural size and apply only the
                // requested target ratio instead of falling back to prefab scale.
                if (sourceVisualHeight > 10f)
                {
                    // Centimetre-authored meshes can still report 100+ units at
                    // a unit root. Use a conservative human-height fallback.
                    targetVisualHeight = 1.65f;
                    targetMethod = "human-height-failsafe";
                    autoScale = Mathf.Clamp(targetVisualHeight * targetRatio / sourceVisualHeight, 0.001f, 10f);
                }
                else
                {
                    targetVisualHeight = sourceVisualHeight;
                    targetMethod = "natural-model-height";
                    autoScale = targetRatio;
                }
            }
            else
            {
                // Last-resort fail-safe: unit root plus the user ratio is bounded
                // and cannot recreate the 82.882x giant-model failure.
                autoScale = targetRatio;
                sourceMethod = "unit-root-failsafe";
                targetMethod = "unit-root-failsafe";
            }
        }

        float appliedScale = manualScale * autoScale;
        proxyRoot.transform.localScale = Vector3.one * appliedScale;
        visibleModelRootScale = proxyRoot.transform.localScale;
        proxyAnimator.Update(0f);

        // Verify the result from local mesh bounds after scaling. If a malformed
        // nested transform still makes the model oversized, correct it before
        // hips alignment. This guard does not use Renderer.bounds, which may be
        // stale or animation-inflated while the object is inactive.
        if (RuntimeOptions.AutoScaleVisibleModel &&
            targetHeightValid &&
            TryGetVisibleModelGeometryHeight(characterRoot.up, out float scaledSourceHeight))
        {
            float maximumHeight = targetVisualHeight * Mathf.Min(1.10f, targetRatio * 1.10f);
            if (scaledSourceHeight > maximumHeight && maximumHeight > 0.001f)
            {
                float correction = Mathf.Clamp(maximumHeight / scaledSourceHeight, 0.001f, 1f);
                appliedScale *= correction;
                proxyRoot.transform.localScale = Vector3.one * appliedScale;
                visibleModelRootScale = proxyRoot.transform.localScale;
                proxyAnimator.Update(0f);
                targetMethod += "+oversize-guard";
            }
        }

        float finalVisualHeight = 0f;
        if (TryGetVisibleModelGeometryHeight(characterRoot.up, out finalVisualHeight))
        {
            float absoluteMaximum = targetHeightValid
                ? Mathf.Max(0.5f, targetVisualHeight * 1.10f)
                : 2.25f;
            if (finalVisualHeight > absoluteMaximum)
            {
                float correction = Mathf.Clamp(absoluteMaximum / finalVisualHeight, 0.001f, 1f);
                appliedScale *= correction;
                proxyRoot.transform.localScale = Vector3.one * appliedScale;
                visibleModelRootScale = proxyRoot.transform.localScale;
                proxyAnimator.Update(0f);
                TryGetVisibleModelGeometryHeight(characterRoot.up, out finalVisualHeight);
                targetMethod += "+absolute-height-guard";
            }
        }

        DanceLog.Info(
            $"Visible-model scale: importedRootScale={FormatVector(importedRootScale)}, normalizedRootScale=(1,1,1), " +
            $"sourceMethod={sourceMethod}, sourceHeight={sourceVisualHeight:0.000}, " +
            $"targetMethod={targetMethod}, targetHeight={targetVisualHeight:0.000}, " +
            $"autoFactor={autoScale:0.000}, manualFactor={manualScale:0.000}, " +
            $"finalScale={appliedScale:0.000}, finalHeight={finalVisualHeight:0.000}.");

        // Position is finalized after the custom graph evaluates frame zero so
        // foot grounding uses the actual dance pose rather than the prefab bind pose.
        visibleModelLayer = ResolveVisibleCharacterLayer(characterRoot);
        SetLayerRecursively(proxyRoot.transform, visibleModelLayer);
        RepairVisibleMaterials();
        RepairVisibleRendererState(sourceBones);
        if (RuntimeOptions.HidePeakRenderers)
        {
            CapturePeakRendererStates(characterRoot);
        }
        else
        {
            peakRendererStates.Clear();
        }

        peakRenderersHidden = false;
        visibilityFailSafeLogged = false;
    }

    private void FinalizeVisibleModelPlacement(Transform characterRoot)
    {
        if (proxyRoot == null || proxyAnimator == null)
        {
            return;
        }

        Vector3 up = characterRoot.up.sqrMagnitude > 0.0001f
            ? characterRoot.up.normalized
            : Vector3.up;
        Vector3 correction = Vector3.zero;
        string horizontalMethod = "player-root";
        string verticalMethod = "player-root";
        float verticalCorrection = 0f;

        Transform? sourceHips = proxyAnimator.GetBoneTransform(HumanBodyBones.Hips);
        if (sourceHips != null &&
            TryFindPeakReferencePair(characterRoot, out Transform targetHips, out _))
        {
            Vector3 hipsDelta = targetHips.position - sourceHips.position;
            correction += Vector3.ProjectOnPlane(hipsDelta, up);
            horizontalMethod = $"hips:{targetHips.name}";

            if (!RuntimeOptions.GroundVisibleModelFeet)
            {
                verticalCorrection = Vector3.Dot(hipsDelta, up);
                correction += up * verticalCorrection;
                verticalMethod = "hips-fallback";
            }
        }

        if (RuntimeOptions.GroundVisibleModelFeet &&
            TryGetVisibleModelSupportPoint(up, out Vector3 sourceSupport, out string sourceSupportName) &&
            TryGetPeakSupportPoint(characterRoot, up, out Vector3 targetSupport, out string targetSupportName))
        {
            verticalCorrection = Vector3.Dot(targetSupport - sourceSupport, up) +
                                 RuntimeOptions.VisibleModelGroundOffset;
            correction += up * verticalCorrection;
            verticalMethod = $"feet:{sourceSupportName}->{targetSupportName}";
        }
        else if (RuntimeOptions.GroundVisibleModelFeet && sourceHips != null &&
                 TryFindPeakReferencePair(characterRoot, out Transform fallbackHips, out _))
        {
            verticalCorrection = Vector3.Dot(fallbackHips.position - sourceHips.position, up) +
                                 RuntimeOptions.VisibleModelGroundOffset;
            correction += up * verticalCorrection;
            verticalMethod = "hips-no-foot-data";
        }

        proxyRoot.transform.position += correction;
        proxyRoot.transform.position += up * RuntimeOptions.VisibleModelHeightOffset;

        Vector3 spawnOffset = ResolveVisibleSpawnOffset(characterRoot, up, out string spawnMethod);
        proxyRoot.transform.position += spawnOffset;
        horizontalMethod += "+" + spawnMethod;

        if (RuntimeOptions.GroundVisibleModelFeet &&
            TryGroundVisibleModelAtSpawn(characterRoot, up, out float groundCorrection, out string groundName))
        {
            proxyRoot.transform.position += up * groundCorrection;
            verticalCorrection += groundCorrection;
            verticalMethod += $"+spawn-ground:{groundName}";
        }

        visibleModelRootLocalPosition = proxyRoot.transform.localPosition;
        visibleModelRootLocalRotation = proxyRoot.transform.localRotation;
        visibleModelRootScale = proxyRoot.transform.localScale;
        visibleModelPlacementLocked = true;

        DanceLog.Info(
            $"Visible-model placement locked: horizontal={horizontalMethod}, vertical={verticalMethod}, " +
            $"verticalCorrection={verticalCorrection:0.000}, groundOffset={RuntimeOptions.VisibleModelGroundOffset:0.000}, " +
            $"localPosition={FormatVector(visibleModelRootLocalPosition)}, localScale={FormatVector(visibleModelRootScale)}.");
    }

    private Vector3 ResolveVisibleSpawnOffset(Transform characterRoot, Vector3 up, out string method)
    {
        float desiredDistance = Mathf.Clamp(RuntimeOptions.VisibleModelForwardOffset, MinimumSpawnDistance, 5f);
        Vector3 forward = GetPreferredSpawnForward(characterRoot, up);
        Vector3 right = Vector3.Cross(up, forward).normalized;
        if (right.sqrMagnitude <= 0.0001f)
        {
            right = Vector3.ProjectOnPlane(characterRoot.right, up).normalized;
        }

        Vector3[] directions = { forward, right, -right, -forward };
        string[] names = { "view-forward", "right-side", "left-side", "behind" };
        float[] preferences = { 0.04f, 0.02f, 0.01f, 0f };
        Vector3 origin = characterRoot.position + up * 0.9f;

        int bestIndex = 0;
        float bestDistance = GetClearSpawnDistance(origin, up, directions[0], desiredDistance, characterRoot);
        float bestScore = bestDistance + preferences[0];
        for (int index = 1; index < directions.Length; index++)
        {
            float clearDistance = GetClearSpawnDistance(origin, up, directions[index], desiredDistance, characterRoot);
            float score = clearDistance + preferences[index];
            if (score > bestScore)
            {
                bestIndex = index;
                bestDistance = clearDistance;
                bestScore = score;
            }
        }

        method = names[bestIndex] + $":{bestDistance:0.00}m";
        return directions[bestIndex] * bestDistance;
    }

    private Vector3 GetPreferredSpawnForward(Transform characterRoot, Vector3 up)
    {
        Vector3 forward = Vector3.ProjectOnPlane(characterRoot.forward, up).normalized;
        if (characterAnimations.character == Character.localCharacter)
        {
            Camera? camera = Camera.main;
            if (camera == null || !camera.enabled || !camera.gameObject.activeInHierarchy)
            {
                camera = Camera.allCameras.FirstOrDefault(candidate =>
                    candidate != null && candidate.enabled && candidate.gameObject.activeInHierarchy &&
                    candidate.cameraType == CameraType.Game);
            }

            if (camera != null)
            {
                Vector3 cameraForward = Vector3.ProjectOnPlane(camera.transform.forward, up).normalized;
                if (cameraForward.sqrMagnitude > 0.0001f)
                {
                    forward = cameraForward;
                }
            }
        }

        return forward.sqrMagnitude > 0.0001f ? forward : Vector3.forward;
    }

    private float GetClearSpawnDistance(
        Vector3 origin,
        Vector3 up,
        Vector3 direction,
        float desiredDistance,
        Transform characterRoot)
    {
        const float startPadding = 0.42f;
        const float obstaclePadding = 0.12f;
        float castLength = Mathf.Max(0.01f, desiredDistance - startPadding);
        Vector3 castStart = direction * startPadding;
        Vector3 lower = origin - up * 0.55f + castStart;
        Vector3 upper = origin + up * 0.65f + castStart;
        RaycastHit[] hits = Physics.CapsuleCastAll(
            lower,
            upper,
            SpawnCollisionRadius,
            direction,
            castLength,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);

        float clearDistance = desiredDistance;
        foreach (RaycastHit hit in hits.OrderBy(item => item.distance))
        {
            Collider? collider = hit.collider;
            if (collider == null || IsOwnedCollider(collider, characterRoot))
            {
                continue;
            }

            clearDistance = Mathf.Min(clearDistance, startPadding + hit.distance - obstaclePadding);
            break;
        }

        return Mathf.Clamp(clearDistance, MinimumSpawnDistance, desiredDistance);
    }

    private bool TryGroundVisibleModelAtSpawn(
        Transform characterRoot,
        Vector3 up,
        out float correction,
        out string surfaceName)
    {
        correction = 0f;
        surfaceName = "none";
        if (!TryGetVisibleModelSupportPoint(up, out Vector3 supportPoint, out _))
        {
            return false;
        }

        RaycastHit[] hits = Physics.RaycastAll(
            supportPoint + up * 2f,
            -up,
            5f,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);
        foreach (RaycastHit hit in hits.OrderBy(item => item.distance))
        {
            Collider? collider = hit.collider;
            if (collider == null || IsOwnedCollider(collider, characterRoot))
            {
                continue;
            }

            correction = Vector3.Dot(hit.point - supportPoint, up) + RuntimeOptions.VisibleModelGroundOffset;
            surfaceName = collider.name;
            return Mathf.Abs(correction) <= 3f;
        }

        return false;
    }

    private bool IsOwnedCollider(Collider collider, Transform characterRoot)
    {
        Transform transform = collider.transform;
        return transform == characterRoot || transform.IsChildOf(characterRoot) ||
               (proxyRoot != null && (transform == proxyRoot.transform || transform.IsChildOf(proxyRoot.transform)));
    }

    private bool TryGetVisibleModelSupportPoint(Vector3 up, out Vector3 point, out string supportName)
    {
        point = Vector3.zero;
        supportName = "none";
        if (proxyAnimator == null)
        {
            return false;
        }

        var candidates = new List<Transform>();
        AddBoneIfPresent(candidates, proxyAnimator, HumanBodyBones.LeftToes);
        AddBoneIfPresent(candidates, proxyAnimator, HumanBodyBones.RightToes);
        AddBoneIfPresent(candidates, proxyAnimator, HumanBodyBones.LeftFoot);
        AddBoneIfPresent(candidates, proxyAnimator, HumanBodyBones.RightFoot);
        return TryGetLowestSupportPoint(candidates, up, out point, out supportName);
    }

    private static bool TryGetPeakSupportPoint(
        Transform characterRoot,
        Vector3 up,
        out Vector3 point,
        out string supportName)
    {
        foreach (Transform skeletonRoot in DiscoverPeakSkeletonRoots(characterRoot)
            .OrderBy(root => IsThirdPersonHierarchy(root) ? 0 : HasFullBodyReference(root) ? 1 : 2))
        {
            if (IsFirstPersonHierarchy(skeletonRoot))
            {
                continue;
            }

            Transform[] transforms = skeletonRoot.GetComponentsInChildren<Transform>(true);
            var candidates = new List<Transform>();
            AddNamedTransform(candidates, transforms, "stoe1l", "lefttoes", "toel", "footl", "leftfoot");
            AddNamedTransform(candidates, transforms, "stoe1r", "righttoes", "toer", "footr", "rightfoot");
            if (TryGetLowestSupportPoint(candidates, up, out point, out supportName))
            {
                supportName = skeletonRoot.name + "/" + supportName;
                return true;
            }
        }

        Transform[] fallback = characterRoot.GetComponentsInChildren<Transform>(true)
            .Where(transform => !IsHiddenSolver(transform) && !IsFirstPersonHierarchy(transform))
            .ToArray();
        var fallbackCandidates = new List<Transform>();
        AddNamedTransform(fallbackCandidates, fallback, "stoe1l", "lefttoes", "toel", "footl", "leftfoot");
        AddNamedTransform(fallbackCandidates, fallback, "stoe1r", "righttoes", "toer", "footr", "rightfoot");
        if (TryGetLowestSupportPoint(fallbackCandidates, up, out point, out supportName))
        {
            supportName = "fallback/" + supportName;
            return true;
        }

        point = characterRoot.position;
        supportName = "character-root";
        return true;
    }

    private static void AddBoneIfPresent(List<Transform> list, Animator animator, HumanBodyBones bone)
    {
        Transform? transform = animator.GetBoneTransform(bone);
        if (transform != null && !list.Contains(transform))
        {
            list.Add(transform);
        }
    }

    private static void AddNamedTransform(
        List<Transform> list,
        IEnumerable<Transform> transforms,
        params string[] names)
    {
        Transform? transform = FindByNormalizedNames(transforms, names);
        if (transform != null && !list.Contains(transform))
        {
            list.Add(transform);
        }
    }

    private static bool TryGetLowestSupportPoint(
        IEnumerable<Transform> candidates,
        Vector3 up,
        out Vector3 point,
        out string supportName)
    {
        Vector3 axis = up.sqrMagnitude > 0.0001f ? up.normalized : Vector3.up;
        Transform? lowest = null;
        float lowestProjection = float.PositiveInfinity;
        foreach (Transform candidate in candidates)
        {
            if (candidate == null || !IsFinite(candidate.position))
            {
                continue;
            }
            float projection = Vector3.Dot(candidate.position, axis);
            if (projection < lowestProjection)
            {
                lowestProjection = projection;
                lowest = candidate;
            }
        }

        if (lowest == null)
        {
            point = Vector3.zero;
            supportName = "none";
            return false;
        }

        point = lowest.position;
        supportName = lowest.name;
        return true;
    }

    private bool TryGetVisibleModelGeometryHeight(Vector3 up, out float height)
    {
        return TryGetRendererGeometryHeight(proxyRenderers, up, out height);
    }

    private bool TryGetPeakVisualHeight(Transform characterRoot, Vector3 up, out float height)
    {
        Renderer[] candidates = characterRoot.GetComponentsInChildren<Renderer>(true)
            .Where(renderer => renderer != null &&
                               !IsHiddenSolver(renderer.transform) &&
                               (proxyRoot == null || !renderer.transform.IsChildOf(proxyRoot.transform)) &&
                               !IsFirstPersonHierarchy(renderer.transform) &&
                               (renderer is SkinnedMeshRenderer || renderer is MeshRenderer))
            .ToArray();

        Renderer[] thirdPerson = candidates
            .Where(renderer => IsThirdPersonHierarchy(renderer.transform))
            .ToArray();
        if (thirdPerson.Length > 0 && TryGetRendererGeometryHeight(thirdPerson, up, out height) && height < 10f)
        {
            return true;
        }

        return TryGetRendererGeometryHeight(candidates, up, out height) && height < 10f;
    }

    private static bool TryGetRendererGeometryHeight(
        IEnumerable<Renderer> renderers,
        Vector3 up,
        out float height)
    {
        Vector3 axis = up.sqrMagnitude > 0.0001f ? up.normalized : Vector3.up;
        float minimum = float.PositiveInfinity;
        float maximum = float.NegativeInfinity;
        int accepted = 0;

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null || !TryGetRendererLocalBounds(renderer, out Bounds localBounds))
            {
                continue;
            }

            Vector3 center = localBounds.center;
            Vector3 extents = localBounds.extents;
            for (int x = -1; x <= 1; x += 2)
            {
                for (int y = -1; y <= 1; y += 2)
                {
                    for (int z = -1; z <= 1; z += 2)
                    {
                        Vector3 localCorner = center + Vector3.Scale(extents, new Vector3(x, y, z));
                        Vector3 worldCorner = renderer.transform.TransformPoint(localCorner);
                        if (!IsFinite(worldCorner))
                        {
                            continue;
                        }
                        float projected = Vector3.Dot(worldCorner, axis);
                        minimum = Mathf.Min(minimum, projected);
                        maximum = Mathf.Max(maximum, projected);
                    }
                }
            }
            accepted++;
        }

        height = accepted > 0 ? maximum - minimum : 0f;
        return accepted > 0 && !float.IsNaN(height) && !float.IsInfinity(height) && height > 0.01f && height < 1000f;
    }

    private static bool TryGetRendererLocalBounds(Renderer renderer, out Bounds bounds)
    {
        if (renderer is SkinnedMeshRenderer skinned)
        {
            bounds = skinned.localBounds;
            if ((!IsFinite(bounds.center) || !IsFinite(bounds.extents) || bounds.extents.sqrMagnitude <= 0.000001f) &&
                skinned.sharedMesh != null)
            {
                bounds = skinned.sharedMesh.bounds;
            }
        }
        else if (renderer is MeshRenderer)
        {
            MeshFilter? filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
            {
                bounds = default;
                return false;
            }
            bounds = filter.sharedMesh.bounds;
        }
        else
        {
            bounds = default;
            return false;
        }

        return IsFinite(bounds.center) && IsFinite(bounds.extents) &&
               bounds.extents.sqrMagnitude > 0.000001f && bounds.extents.magnitude < 1000f;
    }

    private static bool TryGetProjectedRendererHeight(
        IEnumerable<Renderer> renderers,
        Vector3 up,
        out float height)
    {
        Vector3 axis = up.sqrMagnitude > 0.0001f ? up.normalized : Vector3.up;
        float minimum = float.PositiveInfinity;
        float maximum = float.NegativeInfinity;
        int accepted = 0;

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null)
            {
                continue;
            }

            Bounds bounds = renderer.bounds;
            if (!IsFinite(bounds.center) || !IsFinite(bounds.extents) ||
                bounds.extents.sqrMagnitude <= 0.000001f || bounds.extents.magnitude > 25f)
            {
                continue;
            }

            Vector3 center = bounds.center;
            Vector3 extents = bounds.extents;
            for (int x = -1; x <= 1; x += 2)
            {
                for (int y = -1; y <= 1; y += 2)
                {
                    for (int z = -1; z <= 1; z += 2)
                    {
                        Vector3 corner = center + Vector3.Scale(extents, new Vector3(x, y, z));
                        float projected = Vector3.Dot(corner, axis);
                        minimum = Mathf.Min(minimum, projected);
                        maximum = Mathf.Max(maximum, projected);
                    }
                }
            }
            accepted++;
        }

        height = accepted > 0 ? maximum - minimum : 0f;
        return accepted > 0 && !float.IsNaN(height) && !float.IsInfinity(height) && height > 0.05f && height < 25f;
    }

    private static bool TryGetFullBodyBoneHeight(
        Dictionary<HumanBodyBones, SourceBoneState> sourceBones,
        Vector3 up,
        out float height)
    {
        height = 0f;
        if (!sourceBones.TryGetValue(HumanBodyBones.Head, out SourceBoneState head))
        {
            return false;
        }

        var feet = new List<Vector3>(2);
        if (sourceBones.TryGetValue(HumanBodyBones.LeftFoot, out SourceBoneState leftFoot))
        {
            feet.Add(leftFoot.Transform.position);
        }
        if (sourceBones.TryGetValue(HumanBodyBones.RightFoot, out SourceBoneState rightFoot))
        {
            feet.Add(rightFoot.Transform.position);
        }
        if (feet.Count == 0)
        {
            return false;
        }

        Vector3 footCenter = feet.Aggregate(Vector3.zero, (sum, point) => sum + point) / feet.Count;
        Vector3 axis = up.sqrMagnitude > 0.0001f ? up.normalized : Vector3.up;
        height = Mathf.Abs(Vector3.Dot(head.Transform.position - footCenter, axis));
        return height > 0.01f && height < 1000f;
    }

    private static bool TryGetPeakFullBodyBoneHeight(Transform characterRoot, Vector3 up, out float height)
    {
        height = 0f;
        foreach (Transform skeletonRoot in DiscoverPeakSkeletonRoots(characterRoot)
            .OrderBy(root => IsThirdPersonHierarchy(root) ? 0 : HasFullBodyReference(root) ? 1 : 2))
        {
            if (IsFirstPersonHierarchy(skeletonRoot))
            {
                continue;
            }

            Transform[] transforms = skeletonRoot.GetComponentsInChildren<Transform>(true);
            Transform? head = FindByNormalizedNames(transforms, "head");
            Transform? leftFoot = FindByNormalizedNames(transforms, "footl", "leftfoot", "stoe1l", "toel", "lefttoes");
            Transform? rightFoot = FindByNormalizedNames(transforms, "footr", "rightfoot", "stoe1r", "toer", "righttoes");
            if (head == null || (leftFoot == null && rightFoot == null))
            {
                continue;
            }

            Vector3 footCenter = leftFoot != null && rightFoot != null
                ? (leftFoot.position + rightFoot.position) * 0.5f
                : (leftFoot != null ? leftFoot.position : rightFoot!.position);
            Vector3 axis = up.sqrMagnitude > 0.0001f ? up.normalized : Vector3.up;
            height = Mathf.Abs(Vector3.Dot(head.position - footCenter, axis));
            if (height > 0.05f && height < 25f)
            {
                return true;
            }
        }

        // Hierarchy names have changed across PEAK builds. Fall back to all
        // non-first-person transforms instead of giving up and preserving a bad
        // imported model scale.
        Transform[] all = characterRoot.GetComponentsInChildren<Transform>(true)
            .Where(transform => !IsHiddenSolver(transform) && !IsFirstPersonHierarchy(transform))
            .ToArray();
        Transform? fallbackHead = FindByNormalizedNames(all, "head");
        Transform? fallbackLeftFoot = FindByNormalizedNames(all, "footl", "leftfoot", "stoe1l", "toel", "lefttoes");
        Transform? fallbackRightFoot = FindByNormalizedNames(all, "footr", "rightfoot", "stoe1r", "toer", "righttoes");
        if (fallbackHead != null && (fallbackLeftFoot != null || fallbackRightFoot != null))
        {
            Vector3 footCenter = fallbackLeftFoot != null && fallbackRightFoot != null
                ? (fallbackLeftFoot.position + fallbackRightFoot.position) * 0.5f
                : (fallbackLeftFoot != null ? fallbackLeftFoot.position : fallbackRightFoot!.position);
            Vector3 axis = up.sqrMagnitude > 0.0001f ? up.normalized : Vector3.up;
            height = Mathf.Abs(Vector3.Dot(fallbackHead.position - footCenter, axis));
            return height > 0.05f && height < 25f;
        }

        return false;
    }

    private static bool TryGetPeakReferenceHeight(Transform characterRoot, Vector3 up, out float height)
    {
        height = 0f;
        if (!TryFindPeakReferencePair(characterRoot, out Transform hips, out Transform head))
        {
            return false;
        }

        Vector3 axis = up.sqrMagnitude > 0.0001f ? up.normalized : Vector3.up;
        float hipsToHead = Mathf.Abs(Vector3.Dot(head.position - hips.position, axis));
        if (hipsToHead <= 0.05f || hipsToHead >= 10f)
        {
            return false;
        }

        // Native PEAK bind proportions place hips near normalized height 0.46
        // and head near 0.95. Estimate feet-to-head from that verified ratio.
        height = hipsToHead * (0.88f / 0.49f);
        return height > 0.2f && height < 10f;
    }

    private static bool TryFindPeakReferencePair(
        Transform characterRoot,
        out Transform hips,
        out Transform head)
    {
        // Full-body model placement must never be based on the first-person rig.
        // Prefer an explicit third-person hierarchy, then any non-first-person rig.
        foreach (Transform skeletonRoot in DiscoverPeakSkeletonRoots(characterRoot)
            .OrderBy(root => IsThirdPersonHierarchy(root) ? 0 : HasFullBodyReference(root) ? 1 : IsFirstPersonHierarchy(root) ? 3 : 2)
            .ThenByDescending(root => root.gameObject.activeInHierarchy))
        {
            if (IsFirstPersonHierarchy(skeletonRoot))
            {
                continue;
            }

            Transform[] transforms = skeletonRoot.GetComponentsInChildren<Transform>(true);
            Transform? candidateHips = FindByNormalizedNames(transforms, "waist", "hip", "spine1");
            Transform? candidateHead = FindByNormalizedNames(transforms, "head");
            if (candidateHips != null && candidateHead != null)
            {
                hips = candidateHips;
                head = candidateHead;
                return true;
            }
        }

        Transform[] preferred = characterRoot.GetComponentsInChildren<Transform>(true)
            .Where(transform => !IsHiddenSolver(transform) && !IsFirstPersonHierarchy(transform))
            .ToArray();
        hips = FindByNormalizedNames(preferred, "waist", "hip", "spine1")!;
        head = FindByNormalizedNames(preferred, "head")!;
        if (hips != null && head != null)
        {
            return true;
        }

        // No first-person fallback: placing a full replacement on the camera/arm
        // rig is worse than keeping its natural unit-root placement.
        hips = null!;
        head = null!;
        return false;
    }

    private static bool HasFullBodyReference(Transform root)
    {
        var names = new HashSet<string>(root.GetComponentsInChildren<Transform>(true)
            .Select(transform => NormalizeName(transform.name)));
        bool hasHead = names.Contains("head");
        bool hasHips = names.Contains("waist") || names.Contains("hip") || names.Contains("spine1");
        bool hasLegs = (names.Contains("kneel") || names.Contains("leftlowerleg")) &&
                       (names.Contains("kneer") || names.Contains("rightlowerleg"));
        bool hasFeet = (names.Contains("footl") || names.Contains("leftfoot")) &&
                       (names.Contains("footr") || names.Contains("rightfoot"));
        return hasHead && hasHips && hasLegs && hasFeet;
    }

    private static bool IsFirstPersonHierarchy(Transform transform)
    {
        Transform? current = transform;
        while (current != null)
        {
            if (NormalizeName(current.name).Contains("firstperson"))
            {
                return true;
            }
            current = current.parent;
        }
        return false;
    }

    private static bool IsThirdPersonHierarchy(Transform transform)
    {
        Transform? current = transform;
        while (current != null)
        {
            if (NormalizeName(current.name).Contains("thirdperson"))
            {
                return true;
            }
            current = current.parent;
        }
        return false;
    }

    private static Transform? FindByNormalizedNames(IEnumerable<Transform> transforms, params string[] names)
    {
        foreach (string name in names)
        {
            Transform? match = transforms
                .Where(transform => transform != null && NormalizeName(transform.name) == name)
                .OrderByDescending(transform => transform.gameObject.activeInHierarchy)
                .ThenBy(transform => GetHierarchyDepth(transform))
                .FirstOrDefault();
            if (match != null)
            {
                return match;
            }
        }
        return null;
    }

    private static void SetLayerRecursively(Transform root, int layer)
    {
        foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
        {
            transform.gameObject.layer = layer;
        }
    }

    private static int ResolveVisibleCharacterLayer(Transform characterRoot)
    {
        int defaultLayer = LayerMask.NameToLayer("Default");
        if (defaultLayer >= 0)
        {
            return defaultLayer;
        }
        return characterRoot.gameObject.layer;
    }

    private void RepairVisibleMaterials()
    {
        Shader? fallback = FindVisibleFallbackShader();
        if (fallback == null)
        {
            DanceLog.Error("No URP-compatible fallback shader was found for the replacement model.");
            return;
        }

        foreach (Renderer renderer in proxyRenderers)
        {
            if (renderer == null)
            {
                continue;
            }

            Material[] materials = renderer.sharedMaterials;
            if (materials == null || materials.Length == 0)
            {
                materials = new Material[1];
            }

            bool changed = false;
            for (int index = 0; index < materials.Length; index++)
            {
                Material? original = materials[index];
                if (IsMaterialCompatibleWithCurrentPipeline(original))
                {
                    continue;
                }

                Material replacement = CreateCompatibleMaterial(original, fallback);
                materials[index] = replacement;
                runtimeMaterials.Add(replacement);
                changed = true;
            }

            if (changed)
            {
                renderer.sharedMaterials = materials;
            }
        }
    }

    private static Shader? FindVisibleFallbackShader()
    {
        string pipelineName = GetCurrentRenderPipelineName();
        if (pipelineName.IndexOf("Universal", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return Shader.Find("Universal Render Pipeline/Lit") ??
                   Shader.Find("Universal Render Pipeline/Unlit") ??
                   Shader.Find("W/Character") ??
                   Shader.Find("Unlit/Texture") ??
                   Shader.Find("Standard");
        }

        return Shader.Find("W/Character") ??
               Shader.Find("Standard") ??
               Shader.Find("Unlit/Texture") ??
               Shader.Find("Universal Render Pipeline/Lit") ??
               Shader.Find("Universal Render Pipeline/Unlit");
    }

    private static string GetCurrentRenderPipelineName()
    {
        RenderPipelineAsset? pipeline = GraphicsSettings.currentRenderPipeline;
        return pipeline == null ? "BuiltIn" : pipeline.GetType().FullName ?? pipeline.GetType().Name;
    }

    private static bool IsMaterialCompatibleWithCurrentPipeline(Material? material)
    {
        if (material == null || material.shader == null || !material.shader.isSupported)
        {
            return false;
        }

        string shaderName = material.shader.name ?? string.Empty;
        if (shaderName.IndexOf("InternalErrorShader", StringComparison.OrdinalIgnoreCase) >= 0 ||
            shaderName.IndexOf("FallbackError", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return false;
        }

        string pipelineName = GetCurrentRenderPipelineName();
        string pipelineTag = material.GetTag("RenderPipeline", false, string.Empty) ?? string.Empty;
        bool universal = pipelineName.IndexOf("Universal", StringComparison.OrdinalIgnoreCase) >= 0;
        if (universal)
        {
            // xiehen70 contains lilToon/HDRP shaders. Unity can report those
            // shader assets as supported even though the active URP renderer has
            // no matching SubShader pass, which produces an enabled but invisible renderer.
            if (pipelineTag.IndexOf("HD", StringComparison.OrdinalIgnoreCase) >= 0 ||
                shaderName.IndexOf("HDRP", StringComparison.OrdinalIgnoreCase) >= 0 ||
                shaderName.IndexOf("High Definition", StringComparison.OrdinalIgnoreCase) >= 0 ||
                shaderName.IndexOf("lilToon", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }
        }

        return true;
    }

    private static Material CreateCompatibleMaterial(Material? original, Shader fallback)
    {
        Material replacement = new(fallback)
        {
            name = (original == null ? "PEAKEmoteLib_Material" : original.name) + "_URPCompatible"
        };

        Texture? texture = FindPrimaryTexture(original);
        if (texture != null)
        {
            if (replacement.HasProperty("_BaseMap")) replacement.SetTexture("_BaseMap", texture);
            if (replacement.HasProperty("_MainTex")) replacement.SetTexture("_MainTex", texture);
            if (replacement.HasProperty("_BaseColorMap")) replacement.SetTexture("_BaseColorMap", texture);
        }

        Color color = FindPrimaryColor(original);
        if (color.a < 0.05f)
        {
            color.a = 1f;
        }
        if (replacement.HasProperty("_BaseColor")) replacement.SetColor("_BaseColor", color);
        if (replacement.HasProperty("_Color")) replacement.SetColor("_Color", color);

        // Force a conventional opaque, depth-writing material. ModelReplacement
        // packs often preserve lilToon/HDRP transparent state values which are
        // meaningless on PEAK's URP shaders and can make the whole body invisible.
        if (replacement.HasProperty("_Surface")) replacement.SetFloat("_Surface", 0f);
        if (replacement.HasProperty("_Blend")) replacement.SetFloat("_Blend", 0f);
        if (replacement.HasProperty("_AlphaClip")) replacement.SetFloat("_AlphaClip", 0f);
        if (replacement.HasProperty("_SrcBlend")) replacement.SetFloat("_SrcBlend", (float)BlendMode.One);
        if (replacement.HasProperty("_DstBlend")) replacement.SetFloat("_DstBlend", (float)BlendMode.Zero);
        if (replacement.HasProperty("_ZWrite")) replacement.SetFloat("_ZWrite", 1f);
        replacement.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
        replacement.DisableKeyword("_ALPHATEST_ON");
        replacement.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        replacement.renderQueue = (int)RenderQueue.Geometry;
        return replacement;
    }

    private static Texture? FindPrimaryTexture(Material? material)
    {
        if (material == null)
        {
            return null;
        }

        string[] properties =
        {
            "_BaseMap", "_MainTex", "_BaseColorMap", "_UnlitColorMap", "_MainTexture", "_Albedo"
        };
        foreach (string property in properties)
        {
            if (material.HasProperty(property))
            {
                Texture? texture = material.GetTexture(property);
                if (texture != null)
                {
                    return texture;
                }
            }
        }
        return null;
    }

    private static Color FindPrimaryColor(Material? material)
    {
        if (material == null)
        {
            return Color.white;
        }
        if (material.HasProperty("_BaseColor")) return material.GetColor("_BaseColor");
        if (material.HasProperty("_Color")) return material.GetColor("_Color");
        if (material.HasProperty("_MainColor")) return material.GetColor("_MainColor");
        return Color.white;
    }

    private void RepairVisibleRendererState(Dictionary<HumanBodyBones, SourceBoneState> sourceBones)
    {
        foreach (Renderer renderer in proxyRenderers)
        {
            if (renderer == null)
            {
                continue;
            }

            ActivatePath(renderer.transform, proxyRoot!.transform);
            renderer.enabled = true;
            renderer.forceRenderingOff = false;
            renderer.allowOcclusionWhenDynamic = true;
            renderer.renderingLayerMask = uint.MaxValue;

            if (renderer is SkinnedMeshRenderer skinnedMesh)
            {
                skinnedMesh.updateWhenOffscreen = false;
                if (skinnedMesh.rootBone == null && proxyAnimator != null)
                {
                    skinnedMesh.rootBone = proxyAnimator.GetBoneTransform(HumanBodyBones.Hips) ?? proxyAnimator.transform;
                }
                RepairSkinnedMeshBounds(skinnedMesh, sourceBones);
            }
        }
    }

    private static void RepairSkinnedMeshBounds(
        SkinnedMeshRenderer renderer,
        Dictionary<HumanBodyBones, SourceBoneState> sourceBones)
    {
        List<Transform> bones = renderer.bones
            .Where(bone => bone != null)
            .ToList();
        if (bones.Count == 0)
        {
            bones.AddRange(sourceBones.Values.Select(state => state.Transform).Where(transform => transform != null));
        }
        if (bones.Count == 0)
        {
            return;
        }

        Vector3 first = renderer.transform.InverseTransformPoint(bones[0].position);
        Bounds bounds = new(first, Vector3.zero);
        for (int index = 1; index < bones.Count; index++)
        {
            bounds.Encapsulate(renderer.transform.InverseTransformPoint(bones[index].position));
        }

        float largest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
        float padding = Mathf.Max(0.5f, largest * 0.35f);
        bounds.Expand(padding * 2f);
        renderer.localBounds = bounds;
    }

    private bool ValidateVisibleModel(out string summary)
    {
        int meshRenderers = 0;
        int vertices = 0;
        int materialSlots = 0;
        int compatibleMaterials = 0;

        foreach (Renderer renderer in proxyRenderers)
        {
            if (renderer == null || !renderer.enabled || renderer.forceRenderingOff || !renderer.gameObject.activeInHierarchy)
            {
                continue;
            }

            if (renderer is SkinnedMeshRenderer skinned)
            {
                Mesh? mesh = skinned.sharedMesh;
                if (mesh == null || mesh.vertexCount <= 0)
                {
                    continue;
                }
                meshRenderers++;
                vertices += mesh.vertexCount;
            }
            else if (renderer is MeshRenderer)
            {
                MeshFilter? filter = renderer.GetComponent<MeshFilter>();
                Mesh? mesh = filter == null ? null : filter.sharedMesh;
                if (mesh == null || mesh.vertexCount <= 0)
                {
                    continue;
                }
                meshRenderers++;
                vertices += mesh.vertexCount;
            }
            else
            {
                continue;
            }

            foreach (Material material in renderer.sharedMaterials)
            {
                materialSlots++;
                if (IsMaterialCompatibleWithCurrentPipeline(material))
                {
                    compatibleMaterials++;
                }
            }
        }

        summary = $"pipeline={GetCurrentRenderPipelineName()}, meshRenderers={meshRenderers}, vertices={vertices}, " +
                  $"materials={compatibleMaterials}/{materialSlots}";
        return meshRenderers > 0 && vertices > 0 && materialSlots > 0 && compatibleMaterials == materialSlots;
    }

    private void CapturePeakRendererStates(Transform characterRoot)
    {
        peakRendererStates.Clear();
        foreach (Renderer renderer in characterRoot.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null || IsHiddenSolver(renderer.transform))
            {
                continue;
            }
            peakRendererStates.Add(new RendererState(renderer, renderer.enabled, renderer.forceRenderingOff));
        }
    }

    private void UpdateVisibleModelSafety()
    {
        if (!RuntimeOptions.HidePeakRenderers || peakRenderersHidden || Time.unscaledTime < nextVisibilityCheckAt)
        {
            return;
        }

        nextVisibilityCheckAt = Time.unscaledTime + VisibilityCheckInterval;
        bool replacementVisible = CountRenderersVisibleToGameplayCamera() > 0;
        if (replacementVisible)
        {
            foreach (RendererState state in peakRendererStates)
            {
                if (state.Renderer != null)
                {
                    state.Renderer.forceRenderingOff = true;
                }
            }
            peakRenderersHidden = true;
            return;
        }

        if (!visibilityFailSafeLogged && Time.unscaledTime >= diagnosticAt)
        {
            visibilityFailSafeLogged = true;
            DanceLog.Warning(
                "Replacement model has not been confirmed visible to a gameplay camera. PEAK's original body is intentionally kept visible " +
                "to prevent the character-disappears failure mode.");
        }
    }

    private int CountRenderersVisibleToGameplayCamera()
    {
        Camera[] cameras = Camera.allCameras
            .Where(camera => camera != null && camera.enabled && camera.gameObject.activeInHierarchy &&
                             camera.cameraType == CameraType.Game)
            .ToArray();
        if (cameras.Length == 0)
        {
            return 0;
        }

        Plane[][] frustums = cameras
            .Select(camera => GeometryUtility.CalculateFrustumPlanes(camera))
            .ToArray();
        int visibleCount = 0;
        foreach (Renderer renderer in proxyRenderers)
        {
            if (renderer == null || !renderer.enabled || renderer.forceRenderingOff ||
                !renderer.gameObject.activeInHierarchy || !renderer.isVisible)
            {
                continue;
            }

            Bounds bounds = renderer.bounds;
            if (!IsFinite(bounds.center) || !IsFinite(bounds.extents) || bounds.extents.sqrMagnitude <= 0.000001f)
            {
                continue;
            }

            for (int index = 0; index < cameras.Length; index++)
            {
                Camera camera = cameras[index];
                if ((camera.cullingMask & (1 << renderer.gameObject.layer)) == 0)
                {
                    continue;
                }
                if (GeometryUtility.TestPlanesAABB(frustums[index], bounds))
                {
                    visibleCount++;
                    break;
                }
            }
        }
        return visibleCount;
    }

    private static string FormatVector(Vector3 value)
    {
        return $"({value.x:0.###},{value.y:0.###},{value.z:0.###})";
    }

    private static bool IsFinite(Vector3 value)
    {
        return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
               !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
               !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }

    private string BuildVisibleRendererDiagnostics()
    {
        return string.Join(" | ", proxyRenderers.Where(renderer => renderer != null).Select(renderer =>
        {
            string mesh = "none";
            if (renderer is SkinnedMeshRenderer skinned)
            {
                mesh = skinned.sharedMesh == null ? "null" : $"{skinned.sharedMesh.name}:{skinned.sharedMesh.vertexCount}v";
            }
            else if (renderer is MeshRenderer)
            {
                MeshFilter? filter = renderer.GetComponent<MeshFilter>();
                mesh = filter == null || filter.sharedMesh == null ? "null" : $"{filter.sharedMesh.name}:{filter.sharedMesh.vertexCount}v";
            }

            string shaders = string.Join(",", renderer.sharedMaterials.Select(material =>
                material == null || material.shader == null ? "null" : material.shader.name));
            Bounds bounds = renderer.bounds;
            return $"{renderer.name}:layer={renderer.gameObject.layer},mesh={mesh},shaders={shaders}," +
                   $"boundsCenter={bounds.center},boundsSize={bounds.size},isVisible={renderer.isVisible}";
        }));
    }

    private void RestorePeakRenderers()
    {
        foreach (RendererState state in peakRendererStates)
        {
            if (state.Renderer == null)
            {
                continue;
            }
            // Do not restore renderer.enabled: perspective switching may have
            // legitimately changed it during the dance. We only own forceRenderingOff.
            state.Renderer.forceRenderingOff = state.ForceRenderingOff;
        }
        peakRendererStates.Clear();
        peakRenderersHidden = false;
    }

    private bool BuildBoneBindings(
        Transform characterRoot,
        Animator targetAnimator,
        Dictionary<HumanBodyBones, SourceBoneState> sourceBones)
    {
        boneBindings.Clear();
        targetStates.Clear();

        GameObject? activeProxyRoot = proxyRoot;
        if (activeProxyRoot == null)
        {
            DanceLog.Error("Cannot build PEAK bone bindings because the Humanoid proxy root is missing.");
            return false;
        }
        Transform proxyTransform = activeProxyRoot.transform;

        List<TargetSkeleton> skeletons = CollectTargetSkeletons(characterRoot, targetAnimator);
        var globallyMappedTargets = new HashSet<Transform>();
        var mappedRoles = new HashSet<HumanBodyBones>();
        bool hasNativePeakRig = skeletons.Any(skeleton =>
            skeleton.Root != characterRoot && skeleton.IsNativePeakRig);

        foreach (TargetSkeleton skeleton in skeletons)
        {
            if (hasNativePeakRig && !skeleton.IsNativePeakRig)
            {
                continue;
            }
            if (skeleton.Root == characterRoot && boneBindings.Count > 0)
            {
                continue;
            }

            var mappedInSkeleton = new Dictionary<HumanBodyBones, Transform>();
            foreach (BoneRule rule in BoneRules)
            {
                if (!sourceBones.TryGetValue(rule.Bone, out SourceBoneState sourceState))
                {
                    continue;
                }

                Transform? parentTarget = null;
                if (rule.Parent != null)
                {
                    mappedInSkeleton.TryGetValue(rule.Parent.Value, out parentTarget);
                }

                Transform? target = skeleton.IsNativePeakRig
                    ? FindExactNativePeakTarget(skeleton, rule, parentTarget, globallyMappedTargets)
                    : FindBestTargetBone(skeleton, rule, parentTarget, globallyMappedTargets);
                if (target == null)
                {
                    continue;
                }

                mappedInSkeleton[rule.Bone] = target;
                globallyMappedTargets.Add(target);
                mappedRoles.Add(rule.Bone);

                bool transferPosition = RuntimeOptions.TransferPelvisPosition &&
                                        rule.Bone == HumanBodyBones.Hips &&
                                        skeleton.IsNativePeakRig &&
                                        !skeleton.IsFirstPersonRig;
                targetStates.Add(new TargetTransformState(target, target.localRotation, target.localPosition));
                boneBindings.Add(new BoneBinding(
                    rule.Bone,
                    sourceState.Transform,
                    null,
                    0f,
                    target,
                    skeleton.Root,
                    Quaternion.Inverse(proxyTransform.rotation) * sourceState.Transform.rotation,
                    Quaternion.Inverse(skeleton.Root.rotation) * target.rotation,
                    proxyTransform.InverseTransformPoint(sourceState.Transform.position),
                    skeleton.Root.InverseTransformPoint(target.position),
                    1f,
                    transferPosition));
            }

            AddPeakSpineDistribution(
                skeleton,
                mappedInSkeleton,
                sourceBones,
                globallyMappedTargets,
                mappedRoles);
            ConfigureSkeletonSpace(skeleton, mappedInSkeleton, sourceBones);
        }

        int mappedCore = mappedRoles.Count(CoreBones.Contains);
        bool hasTorso = mappedRoles.Contains(HumanBodyBones.Hips) &&
                        (mappedRoles.Contains(HumanBodyBones.Spine) || mappedRoles.Contains(HumanBodyBones.Chest)) &&
                        mappedRoles.Contains(HumanBodyBones.Head);
        bool hasLimbs = mappedRoles.Contains(HumanBodyBones.LeftUpperArm) ||
                        mappedRoles.Contains(HumanBodyBones.RightUpperArm) ||
                        mappedRoles.Contains(HumanBodyBones.LeftUpperLeg) ||
                        mappedRoles.Contains(HumanBodyBones.RightUpperLeg);

        bool strongMap = hasTorso && hasLimbs && mappedCore >= 7;
        bool usablePartialMap = boneBindings.Count >= 6 && mappedCore >= 3 && (hasTorso || hasLimbs);
        if (!strongMap && !usablePartialMap)
        {
            string candidates = string.Join(", ", skeletons
                .SelectMany(skeleton => skeleton.Bones)
                .Select(transform => transform.name)
                .Distinct()
                .Take(80));
            string missing = string.Join(", ", CoreBones
                .Where(role => !mappedRoles.Contains(role))
                .Select(role => role.ToString()));
            DanceLog.Error(
                $"Could not build even a partial PEAK pose map (core={mappedCore}, total={boneBindings.Count}, " +
                $"hasTorso={hasTorso}, hasLimbs={hasLimbs}, missingCore=[{missing}]). " +
                $"Candidate bones=[{candidates}].");
            boneBindings.Clear();
            targetStates.Clear();
            return false;
        }

        if (!strongMap)
        {
            string missing = string.Join(", ", CoreBones
                .Where(role => !mappedRoles.Contains(role))
                .Select(role => role.ToString()));
            DanceLog.Warning(
                $"Using degraded PEAK pose retarget instead of cancelling playback: core={mappedCore}, " +
                $"total={boneBindings.Count}, hasTorso={hasTorso}, hasLimbs={hasLimbs}, missingCore=[{missing}]. " +
                "Mapped joints will animate and music will continue; unmapped joints remain under PEAK's normal rig.");
        }

        // Apply parent joints before children. Native PEAK torso bindings are
        // added after limb discovery, so insertion order is not a safe pose order.
        boneBindings.Sort((left, right) =>
            GetHierarchyDepth(left.Target).CompareTo(GetHierarchyDepth(right.Target)));

        hipsTranslationScale = boneBindings
            .Where(binding => binding.Bone == HumanBodyBones.Hips)
            .Select(binding => binding.SkeletonScale)
            .FirstOrDefault();
        if (hipsTranslationScale <= 0.001f)
        {
            hipsTranslationScale = 1f;
        }
        DanceLog.Info(
            $"Built PEAK model pose map: skeletonGroups={skeletons.Count}, mappedRoles={mappedRoles.Count}, " +
            $"targetBindings={boneBindings.Count}, coreRoles={mappedCore}, strongMap={strongMap}, " +
            $"hipsScale={hipsTranslationScale:0.000}, positionTransfer={boneBindings.Any(binding => binding.TransferPosition)}.");
        return true;
    }

    private void ConfigureSkeletonSpace(
        TargetSkeleton skeleton,
        Dictionary<HumanBodyBones, Transform> mappedTargets,
        Dictionary<HumanBodyBones, SourceBoneState> sourceBones)
    {
        GameObject? activeProxyRoot = proxyRoot;
        if (activeProxyRoot == null ||
            !sourceBones.TryGetValue(HumanBodyBones.Hips, out SourceBoneState sourceHips) ||
            !sourceBones.TryGetValue(HumanBodyBones.Head, out SourceBoneState sourceHead) ||
            !mappedTargets.TryGetValue(HumanBodyBones.Hips, out Transform targetHips) ||
            !mappedTargets.TryGetValue(HumanBodyBones.Head, out Transform targetHead))
        {
            return;
        }

        Transform proxyTransform = activeProxyRoot.transform;
        Vector3 sourceHipsPoint = proxyTransform.InverseTransformPoint(sourceHips.Transform.position);
        Vector3 sourceHeadPoint = proxyTransform.InverseTransformPoint(sourceHead.Transform.position);
        Vector3 targetHipsPoint = skeleton.Root.InverseTransformPoint(targetHips.position);
        Vector3 targetHeadPoint = skeleton.Root.InverseTransformPoint(targetHead.position);

        float sourceHeight = Vector3.Distance(sourceHipsPoint, sourceHeadPoint);
        float targetHeight = Vector3.Distance(targetHipsPoint, targetHeadPoint);
        float scale = sourceHeight > 0.001f && targetHeight > 0.001f
            ? Mathf.Clamp(targetHeight / sourceHeight, 0.05f, 20f)
            : 1f;

        Quaternion basis = Quaternion.identity;
        if (TryGetLateralPair(sourceBones, mappedTargets, skeleton, out Vector3 sourceLeft, out Vector3 sourceRight,
                out Vector3 targetLeft, out Vector3 targetRight))
        {
            Vector3 sourceUp = (sourceHeadPoint - sourceHipsPoint).normalized;
            Vector3 targetUp = (targetHeadPoint - targetHipsPoint).normalized;
            Vector3 sourceRightAxis = (sourceRight - sourceLeft).normalized;
            Vector3 targetRightAxis = (targetRight - targetLeft).normalized;
            Vector3 sourceForward = Vector3.Cross(sourceRightAxis, sourceUp).normalized;
            Vector3 targetForward = Vector3.Cross(targetRightAxis, targetUp).normalized;
            if (sourceForward.sqrMagnitude > 0.5f && targetForward.sqrMagnitude > 0.5f)
            {
                Quaternion sourceBasis = Quaternion.LookRotation(sourceForward, sourceUp);
                Quaternion targetBasis = Quaternion.LookRotation(targetForward, targetUp);
                basis = targetBasis * Quaternion.Inverse(sourceBasis);
            }
        }

        foreach (BoneBinding binding in boneBindings.Where(binding => binding.TargetSpaceRoot == skeleton.Root))
        {
            binding.RotationBasis = basis;
            binding.SkeletonScale = scale;
        }
    }

    private bool TryGetLateralPair(
        Dictionary<HumanBodyBones, SourceBoneState> sourceBones,
        Dictionary<HumanBodyBones, Transform> mappedTargets,
        TargetSkeleton skeleton,
        out Vector3 sourceLeft,
        out Vector3 sourceRight,
        out Vector3 targetLeft,
        out Vector3 targetRight)
    {
        HumanBodyBones leftRole = HumanBodyBones.LeftShoulder;
        HumanBodyBones rightRole = HumanBodyBones.RightShoulder;
        if (!sourceBones.ContainsKey(leftRole) || !sourceBones.ContainsKey(rightRole) ||
            !mappedTargets.ContainsKey(leftRole) || !mappedTargets.ContainsKey(rightRole))
        {
            leftRole = HumanBodyBones.LeftUpperLeg;
            rightRole = HumanBodyBones.RightUpperLeg;
        }

        GameObject? activeProxyRoot = proxyRoot;
        if (activeProxyRoot != null &&
            sourceBones.TryGetValue(leftRole, out SourceBoneState sourceLeftState) &&
            sourceBones.TryGetValue(rightRole, out SourceBoneState sourceRightState) &&
            mappedTargets.TryGetValue(leftRole, out Transform targetLeftTransform) &&
            mappedTargets.TryGetValue(rightRole, out Transform targetRightTransform))
        {
            Transform proxyTransform = activeProxyRoot.transform;
            sourceLeft = proxyTransform.InverseTransformPoint(sourceLeftState.Transform.position);
            sourceRight = proxyTransform.InverseTransformPoint(sourceRightState.Transform.position);
            targetLeft = skeleton.Root.InverseTransformPoint(targetLeftTransform.position);
            targetRight = skeleton.Root.InverseTransformPoint(targetRightTransform.position);
            return true;
        }

        sourceLeft = Vector3.zero;
        sourceRight = Vector3.right;
        targetLeft = Vector3.zero;
        targetRight = Vector3.right;
        return false;
    }

    private static bool IsNativeSpineRole(HumanBodyBones bone)
    {
        return bone == HumanBodyBones.Hips ||
               bone == HumanBodyBones.Spine ||
               bone == HumanBodyBones.Chest ||
               bone == HumanBodyBones.UpperChest;
    }

    private void AddPeakSpineDistribution(
        TargetSkeleton skeleton,
        Dictionary<HumanBodyBones, Transform> mappedTargets,
        Dictionary<HumanBodyBones, SourceBoneState> sourceBones,
        HashSet<Transform> globallyMappedTargets,
        HashSet<HumanBodyBones> mappedRoles)
    {
        if (proxyRoot == null || !skeleton.IsNativePeakRig)
        {
            return;
        }

        for (int index = 1; index <= 10; index++)
        {
            Transform? target = skeleton.Bones.FirstOrDefault(transform =>
                NormalizeName(transform.name) == "spine" + index);
            if (target == null || globallyMappedTargets.Contains(target) ||
                !TryGetPeakSpineSample(index, sourceBones, out HumanBodyBones sourceRole,
                    out SourceBoneState sourceA, out SourceBoneState sourceB, out float blend))
            {
                continue;
            }

            Quaternion bindRotationA = Quaternion.Inverse(proxyRoot.transform.rotation) * sourceA.Transform.rotation;
            Quaternion bindRotationB = Quaternion.Inverse(proxyRoot.transform.rotation) * sourceB.Transform.rotation;

            globallyMappedTargets.Add(target);
            mappedRoles.Add(sourceRole);
            targetStates.Add(new TargetTransformState(target, target.localRotation, target.localPosition));
            boneBindings.Add(new BoneBinding(
                sourceRole,
                sourceA.Transform,
                sourceB.Transform,
                blend,
                target,
                skeleton.Root,
                Quaternion.Slerp(bindRotationA, bindRotationB, blend),
                Quaternion.Inverse(skeleton.Root.rotation) * target.rotation,
                Vector3.Lerp(
                    proxyRoot.transform.InverseTransformPoint(sourceA.Transform.position),
                    proxyRoot.transform.InverseTransformPoint(sourceB.Transform.position),
                    blend),
                skeleton.Root.InverseTransformPoint(target.position),
                1f,
                false));

            if (index == 1 && !mappedTargets.ContainsKey(HumanBodyBones.Hips))
            {
                mappedTargets[HumanBodyBones.Hips] = target;
            }
            else if (index == 3 && !mappedTargets.ContainsKey(HumanBodyBones.Spine))
            {
                mappedTargets[HumanBodyBones.Spine] = target;
            }
            else if (index == 6 && !mappedTargets.ContainsKey(HumanBodyBones.Chest))
            {
                mappedTargets[HumanBodyBones.Chest] = target;
            }
            else if (index == 8 && !mappedTargets.ContainsKey(HumanBodyBones.UpperChest))
            {
                mappedTargets[HumanBodyBones.UpperChest] = target;
            }
        }
    }

    private static bool TryGetPeakSpineSample(
        int index,
        Dictionary<HumanBodyBones, SourceBoneState> sourceBones,
        out HumanBodyBones sourceRole,
        out SourceBoneState sourceA,
        out SourceBoneState sourceB,
        out float blend)
    {
        HumanBodyBones roleA;
        HumanBodyBones roleB;
        switch (index)
        {
            case 1:
                sourceRole = roleA = roleB = HumanBodyBones.Hips;
                blend = 0f;
                break;
            case 2:
                sourceRole = HumanBodyBones.Spine;
                roleA = HumanBodyBones.Hips;
                roleB = HumanBodyBones.Spine;
                blend = 0.5f;
                break;
            case 3:
                sourceRole = roleA = roleB = HumanBodyBones.Spine;
                blend = 0f;
                break;
            case 4:
                sourceRole = HumanBodyBones.Chest;
                roleA = HumanBodyBones.Spine;
                roleB = HumanBodyBones.Chest;
                blend = 0.33f;
                break;
            case 5:
                sourceRole = HumanBodyBones.Chest;
                roleA = HumanBodyBones.Spine;
                roleB = HumanBodyBones.Chest;
                blend = 0.66f;
                break;
            case 6:
                sourceRole = roleA = roleB = HumanBodyBones.Chest;
                blend = 0f;
                break;
            case 7:
                sourceRole = HumanBodyBones.UpperChest;
                roleA = HumanBodyBones.Chest;
                roleB = HumanBodyBones.UpperChest;
                blend = 0.5f;
                break;
            case 8:
                sourceRole = roleA = roleB = HumanBodyBones.UpperChest;
                blend = 0f;
                break;
            case 9:
                sourceRole = HumanBodyBones.UpperChest;
                roleA = HumanBodyBones.UpperChest;
                roleB = HumanBodyBones.Neck;
                blend = 0.5f;
                break;
            case 10:
                sourceRole = HumanBodyBones.UpperChest;
                roleA = roleB = HumanBodyBones.Neck;
                blend = 0f;
                break;
            default:
                sourceRole = HumanBodyBones.Spine;
                sourceA = null!;
                sourceB = null!;
                blend = 0f;
                return false;
        }

        if (!sourceBones.TryGetValue(roleA, out sourceA) ||
            !sourceBones.TryGetValue(roleB, out sourceB))
        {
            sourceA = null!;
            sourceB = null!;
            return false;
        }
        return true;
    }

    private static Dictionary<HumanBodyBones, SourceBoneState> CaptureSourceBones(Animator animator)
    {
        var result = new Dictionary<HumanBodyBones, SourceBoneState>();
        foreach (BoneRule rule in BoneRules)
        {
            Transform? transform = animator.GetBoneTransform(rule.Bone);
            if (transform == null || result.ContainsKey(rule.Bone))
            {
                continue;
            }

            result.Add(rule.Bone, new SourceBoneState(transform));
        }
        return result;
    }

    private List<TargetSkeleton> CollectTargetSkeletons(Transform characterRoot, Animator targetAnimator)
    {
        var result = new List<TargetSkeleton>();
        var seenRoots = new HashSet<Transform>();

        foreach (SkinnedMeshRenderer renderer in characterRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (renderer == null || IsHiddenSolver(renderer.transform))
            {
                continue;
            }

            Transform root = renderer.rootBone != null ? renderer.rootBone : targetAnimator.transform;
            if (!seenRoots.Add(root))
            {
                TargetSkeleton existing = result.First(skeleton => skeleton.Root == root);
                existing.AddRendererBones(renderer);
                continue;
            }

            var skeleton = new TargetSkeleton(root, true);
            skeleton.AddRendererBones(renderer);
            result.Add(skeleton);
        }

        // PEAK's native model uses explicit first/third-person skeleton roots
        // and procedural body-part transforms rather than a Humanoid Avatar.
        // Discover each rig separately so both views receive the same pose.
        foreach (Transform root in DiscoverPeakSkeletonRoots(characterRoot))
        {
            if (!seenRoots.Add(root))
            {
                continue;
            }

            var skeleton = new TargetSkeleton(root, false);
            skeleton.AddHierarchy(root);
            result.Add(skeleton);
        }

        // A character-wide fallback keeps compatibility with model mods whose
        // skeleton root is renamed. It is evaluated after concrete rig groups.
        var fallback = new TargetSkeleton(characterRoot, false);
        fallback.AddHierarchy(characterRoot);
        result.Add(fallback);

        return result
            .OrderBy(skeleton => skeleton.Root == characterRoot ? 1 : 0)
            .ThenByDescending(skeleton => skeleton.IsNativePeakRig)
            .ThenByDescending(skeleton => skeleton.FromSkinnedRenderer)
            .ThenBy(skeleton => skeleton.Bones.Count)
            .ToList();
    }

    private static IEnumerable<Transform> DiscoverPeakSkeletonRoots(Transform characterRoot)
    {
        Transform[] all = characterRoot.GetComponentsInChildren<Transform>(true);
        var roots = new HashSet<Transform>();

        foreach (Transform transform in all)
        {
            if (IsHiddenSolver(transform))
            {
                continue;
            }

            string name = NormalizeName(transform.name);
            if (name.Contains("skeletonthirdperson") || name.Contains("skeletonfirstperson") ||
                name == "thirdpersonskeleton" || name == "firstpersonskeleton")
            {
                roots.Add(transform);
            }
        }

        // Some PEAK builds expose the rig root as a generic object (for example
        // rig_g). Detect it structurally from the native joint set confirmed in
        // Assembly-CSharp: Shoulder_L/R, Hip_L/R and Neck.
        foreach (Transform leftShoulder in all.Where(transform =>
                     NormalizeName(transform.name) == "shoulderl" || NormalizeName(transform.name) == "sshoulderl"))
        {
            Transform? current = leftShoulder.parent;
            while (current != null && current != characterRoot)
            {
                Transform[] descendants = current.GetComponentsInChildren<Transform>(true);
                var names = new HashSet<string>(descendants.Select(descendant => NormalizeName(descendant.name)));
                if ((names.Contains("shoulderr") || names.Contains("sshoulderr")) && names.Contains("hipl") && names.Contains("hipr") &&
                    names.Contains("neck") && names.Any(name => name.StartsWith("spine", StringComparison.Ordinal)))
                {
                    roots.Add(current);
                    break;
                }
                current = current.parent;
            }
        }

        return roots.OrderBy(root => GetHierarchyDepth(root));
    }

    private static int GetHierarchyDepth(Transform transform)
    {
        int depth = 0;
        Transform? current = transform;
        while (current != null)
        {
            depth++;
            current = current.parent;
        }
        return depth;
    }

    private static Transform? FindBestTargetBone(
        TargetSkeleton skeleton,
        BoneRule rule,
        Transform? mappedParent,
        HashSet<Transform> globallyMappedTargets)
    {
        Transform? best = null;
        int bestScore = int.MinValue;

        foreach (Transform candidate in skeleton.Bones)
        {
            if (candidate == null || globallyMappedTargets.Contains(candidate) || IsHiddenSolver(candidate) ||
                !IsSafeTargetTransform(skeleton, candidate) || !NameMatchesRule(candidate.name, rule))
            {
                continue;
            }

            int score = ScoreCandidate(skeleton, candidate, rule, mappedParent);
            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        // Generic/custom models may still use longer anatomical names, but a
        // candidate must now have a real name match and a strong score.
        return bestScore >= 170 ? best : null;
    }

    private static Transform? FindExactNativePeakTarget(
        TargetSkeleton skeleton,
        BoneRule rule,
        Transform? mappedParent,
        HashSet<Transform> globallyMappedTargets)
    {
        if (!NativePeakBoneNames.TryGetValue(rule.Bone, out string[]? acceptedNames))
        {
            return null;
        }

        foreach (string acceptedName in acceptedNames)
        {
            List<Transform> exactMatches = skeleton.Bones
                .Where(candidate => candidate != null &&
                    !globallyMappedTargets.Contains(candidate) &&
                    !IsHiddenSolver(candidate) &&
                    IsSafeTargetTransform(skeleton, candidate) &&
                    NormalizeName(candidate.name) == acceptedName)
                .ToList();

            if (exactMatches.Count == 0)
            {
                continue;
            }

            if (mappedParent != null)
            {
                Transform? descendant = exactMatches
                    .OrderBy(candidate =>
                    {
                        int depth = DescendantDepth(candidate, mappedParent, 12);
                        return depth > 0 ? depth : int.MaxValue;
                    })
                    .FirstOrDefault(candidate => DescendantDepth(candidate, mappedParent, 12) > 0);
                if (descendant != null)
                {
                    return descendant;
                }
            }

            return exactMatches
                .OrderByDescending(candidate => skeleton.RendererBones.Contains(candidate))
                .ThenBy(candidate => GetHierarchyDepth(candidate))
                .First();
        }

        return null;
    }

    private static bool NameMatchesRule(string candidateName, BoneRule rule)
    {
        string compact = NormalizeName(candidateName);
        foreach (string alias in rule.Aliases)
        {
            string normalizedAlias = NormalizeName(alias);
            if (compact == normalizedAlias)
            {
                return true;
            }

            // Very short aliases such as arm, leg and hip previously matched
            // Armature or unrelated helper objects. Only exact-match them.
            if (normalizedAlias.Length <= 3)
            {
                continue;
            }

            if (compact.StartsWith(normalizedAlias, StringComparison.Ordinal) ||
                compact.EndsWith(normalizedAlias, StringComparison.Ordinal) ||
                compact.Contains(normalizedAlias))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsSafeTargetTransform(TargetSkeleton skeleton, Transform candidate)
    {
        if (candidate == skeleton.Root || candidate.parent == null)
        {
            return false;
        }

        string compact = NormalizeName(candidate.name);
        if (ForbiddenTargetNames.Contains(compact) || compact.StartsWith("bone", StringComparison.Ordinal) ||
            compact.StartsWith("character", StringComparison.Ordinal) ||
            compact.StartsWith("propeller", StringComparison.Ordinal) ||
            compact.Contains("weight") || compact.Contains("attach") || compact.Contains("socket") ||
            compact.Contains("renderer") || compact.Contains("mesh") || compact.Contains("item") ||
            compact.Contains("camera"))
        {
            return false;
        }

        return true;
    }

    private static int ScoreCandidate(TargetSkeleton skeleton, Transform candidate, BoneRule rule, Transform? mappedParent)
    {
        string compact = NormalizeName(candidate.name);
        int score = 0;

        int anatomyScore = 0;
        foreach (string alias in rule.Aliases)
        {
            string normalizedAlias = NormalizeName(alias);
            if (compact == normalizedAlias)
            {
                anatomyScore = Math.Max(anatomyScore, 260);
            }
            else if (compact.EndsWith(normalizedAlias, StringComparison.Ordinal) ||
                     compact.StartsWith(normalizedAlias, StringComparison.Ordinal))
            {
                anatomyScore = Math.Max(anatomyScore, 210);
            }
            else if (compact.Contains(normalizedAlias))
            {
                anatomyScore = Math.Max(anatomyScore, normalizedAlias.Length <= 3 ? 115 : 170);
            }
        }
        score += anatomyScore;

        BoneSide candidateSide = DetectSide(candidate.name);
        if (rule.Side != BoneSide.Center)
        {
            if (candidateSide == rule.Side)
            {
                score += 90;
            }
            else if (candidateSide != BoneSide.Center)
            {
                score -= 600;
            }
            else
            {
                float localX = skeleton.Root.InverseTransformPoint(candidate.position).x;
                if ((rule.Side == BoneSide.Left && localX < -0.001f) ||
                    (rule.Side == BoneSide.Right && localX > 0.001f))
                {
                    score += 30;
                }
            }
        }
        else if (candidateSide != BoneSide.Center)
        {
            score -= 500;
        }

        if (skeleton.RendererBones.Contains(candidate))
        {
            score += 45;
        }

        if (mappedParent != null)
        {
            int depth = DescendantDepth(candidate, mappedParent, 8);
            if (depth > 0)
            {
                score += Math.Max(25, 125 - depth * 14);
            }
            else
            {
                score -= 80;
            }
        }

        if (skeleton.TryGetNormalizedHeight(candidate, out float height))
        {
            score += Mathf.RoundToInt(Mathf.Max(-20f, 45f - Mathf.Abs(height - rule.ExpectedHeight) * 130f));
        }

        // Discourage mesh, renderer and helper nodes when a real joint exists.
        if (compact.Contains("mesh") || compact.Contains("renderer") || compact.Contains("attach") ||
            compact.Contains("item") || compact.Contains("prop") || compact.Contains("socket"))
        {
            score -= 100;
        }

        return score;
    }

    private void ApplyRetargetedPose()
    {
        if (characterAnimations == null || characterAnimations.character == null)
        {
            return;
        }

        // The retargeter must never author character/root motion.  Capture the
        // physical root as a final guard in case a future model exposes an
        // unexpected transform that passes mapping validation.
        Transform physicalRoot = characterAnimations.character.transform;
        Vector3 protectedPosition = physicalRoot.position;
        Quaternion protectedRotation = physicalRoot.rotation;

        foreach (BoneBinding binding in boneBindings)
        {
            if (binding.Source == null || binding.Target == null)
            {
                continue;
            }

            if (proxyRoot == null || binding.TargetSpaceRoot == null)
            {
                continue;
            }

            Quaternion sourceWorldRotation = binding.SourceSecondary == null
                ? binding.Source.rotation
                : Quaternion.Slerp(binding.Source.rotation, binding.SourceSecondary.rotation, binding.SourceBlend);
            Quaternion sourceNowSolverLocal =
                Quaternion.Inverse(proxyRoot.transform.rotation) *
                sourceWorldRotation;
            Quaternion sourceDelta =
                sourceNowSolverLocal *
                Quaternion.Inverse(binding.SourceBindSolverLocalRotation);
            Quaternion mappedDelta =
                binding.RotationBasis *
                sourceDelta *
                Quaternion.Inverse(binding.RotationBasis);
            Quaternion weightedDelta = Quaternion.Slerp(
                Quaternion.identity,
                mappedDelta,
                binding.RotationWeight);
            Quaternion desiredTargetRootLocalRotation =
                weightedDelta * binding.TargetBindRootLocalRotation;
            binding.Target.rotation =
                binding.TargetSpaceRoot.rotation * desiredTargetRootLocalRotation;

            if (binding.TransferPosition && RuntimeOptions.MaxPelvisOffset > 0f)
            {
                Vector3 sourceWorldPosition = binding.SourceSecondary == null
                    ? binding.Source.position
                    : Vector3.Lerp(binding.Source.position, binding.SourceSecondary.position, binding.SourceBlend);
                Vector3 sourceNowSolverLocalPosition = proxyRoot.transform.InverseTransformPoint(sourceWorldPosition);
                Vector3 sourcePositionDelta =
                    sourceNowSolverLocalPosition - binding.SourceBindSolverLocalPosition;
                Vector3 mappedPositionDelta =
                    binding.RotationBasis * sourcePositionDelta * binding.SkeletonScale;
                mappedPositionDelta *= RuntimeOptions.PelvisPositionWeight;
                mappedPositionDelta = Vector3.ClampMagnitude(
                    mappedPositionDelta,
                    RuntimeOptions.MaxPelvisOffset);
                binding.Target.position = binding.TargetSpaceRoot.TransformPoint(
                    binding.TargetBindRootLocalPosition + mappedPositionDelta);
            }
        }

        float rootDistance = Vector3.Distance(physicalRoot.position, protectedPosition);
        float rootAngle = Quaternion.Angle(physicalRoot.rotation, protectedRotation);
        if (rootDistance > 0.001f || rootAngle > 0.05f)
        {
            physicalRoot.SetPositionAndRotation(protectedPosition, protectedRotation);
            if (!rootSafetyCorrectionLogged)
            {
                rootSafetyCorrectionLogged = true;
                DanceLog.Error(
                    $"Retarget root-motion safety guard corrected an unexpected character-root change " +
                    $"(distance={rootDistance:0.0000}, angle={rootAngle:0.00}). Only clamped pelvis translation is allowed.");
            }
        }
    }

    private void RestoreTargetPose()
    {
        foreach (TargetTransformState state in targetStates)
        {
            if (state.Transform == null)
            {
                continue;
            }

            state.Transform.localRotation = state.LocalRotation;
            state.Transform.localPosition = state.LocalPosition;
        }
    }

    private int CountEnabledPeakRenderers()
    {
        if (characterAnimations == null || characterAnimations.character == null)
        {
            return 0;
        }

        return characterAnimations.character.GetComponentsInChildren<Renderer>(true).Count(renderer =>
            renderer != null && !IsHiddenSolver(renderer.transform) && renderer.enabled &&
            !renderer.forceRenderingOff && renderer.gameObject.activeInHierarchy);
    }

    private static void ActivatePath(Transform transform, Transform root)
    {
        Transform? current = transform;
        while (current != null)
        {
            current.gameObject.SetActive(true);
            if (current == root)
            {
                break;
            }
            current = current.parent;
        }
    }

    private static void SanitizeSourceRig(GameObject root, Animator selectedAnimator, bool keepRenderers)
    {
        // Older Lethal Company model bundles can contain MonoBehaviour entries
        // whose scripts are not present in PEAK. Unity returns those entries as
        // null elements from GetComponentsInChildren<Behaviour>(); dereferencing
        // one was the exact 1.3.0 crash at this method's old line 1452.
        int missingComponents = 0;

        foreach (Behaviour behaviour in root.GetComponentsInChildren<Behaviour>(true))
        {
            if (behaviour == null)
            {
                missingComponents++;
                continue;
            }
            if (behaviour != selectedAnimator)
            {
                behaviour.enabled = false;
            }
        }
        foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
        {
            if (collider != null)
            {
                collider.enabled = false;
            }
        }
        foreach (Rigidbody rigidbody in root.GetComponentsInChildren<Rigidbody>(true))
        {
            if (rigidbody == null)
            {
                continue;
            }
            rigidbody.isKinematic = true;
            rigidbody.detectCollisions = false;
        }
        foreach (AudioSource audioSource in root.GetComponentsInChildren<AudioSource>(true))
        {
            if (audioSource != null)
            {
                audioSource.enabled = false;
            }
        }
        foreach (Camera camera in root.GetComponentsInChildren<Camera>(true))
        {
            if (camera != null)
            {
                camera.enabled = false;
            }
        }
        foreach (Light light in root.GetComponentsInChildren<Light>(true))
        {
            if (light != null)
            {
                light.enabled = false;
            }
        }
        foreach (Animator animator in root.GetComponentsInChildren<Animator>(true))
        {
            if (animator != null)
            {
                animator.enabled = animator == selectedAnimator;
            }
        }
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null)
            {
                missingComponents++;
                continue;
            }
            if (keepRenderers)
            {
                ActivatePath(renderer.transform, root.transform);
                renderer.enabled = true;
                renderer.forceRenderingOff = false;
            }
            else
            {
                renderer.enabled = false;
                renderer.forceRenderingOff = true;
            }
            if (renderer is SkinnedMeshRenderer skinnedMesh)
            {
                skinnedMesh.updateWhenOffscreen = false;
            }
        }

        if (missingComponents > 0)
        {
            DanceLog.Warning(
                $"Source rig '{root.name}' contains {missingComponents} missing/unsupported component slot(s); " +
                "they were ignored so animation and music playback can continue.");
        }
    }

    private static bool IsHiddenSolver(Transform transform)
    {
        Transform? current = transform;
        while (current != null)
        {
            if (current.name.StartsWith("PEAKEmoteLib_HiddenSolver_", StringComparison.Ordinal))
            {
                return true;
            }
            current = current.parent;
        }
        return false;
    }

    private static int DescendantDepth(Transform candidate, Transform ancestor, int maximumDepth)
    {
        Transform? current = candidate.parent;
        int depth = 1;
        while (current != null && depth <= maximumDepth)
        {
            if (current == ancestor)
            {
                return depth;
            }
            current = current.parent;
            depth++;
        }
        return -1;
    }

    private static BoneSide DetectSide(string name)
    {
        string lower = name.ToLowerInvariant();
        string compact = NormalizeName(name);
        if (compact.Contains("left"))
        {
            return BoneSide.Left;
        }
        if (compact.Contains("right"))
        {
            return BoneSide.Right;
        }

        string[] tokens = lower
            .Replace('.', ' ')
            .Replace('_', ' ')
            .Replace('-', ' ')
            .Replace(':', ' ')
            .Replace('/', ' ')
            .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Any(token => token == "l" || token == "lf" || token == "left"))
        {
            return BoneSide.Left;
        }
        if (tokens.Any(token => token == "r" || token == "rt" || token == "right"))
        {
            return BoneSide.Right;
        }

        string[] anatomy =
        {
            "arm", "forearm", "hand", "wrist", "shoulder", "clavicle", "leg", "thigh", "calf", "shin", "foot", "toe"
        };
        if (compact.Length > 2 && compact[0] == 'l' && anatomy.Any(part => compact.Substring(1).Contains(part)))
        {
            return BoneSide.Left;
        }
        if (compact.Length > 2 && compact[0] == 'r' && anatomy.Any(part => compact.Substring(1).Contains(part)))
        {
            return BoneSide.Right;
        }
        if (compact.Length > 2 && compact[compact.Length - 1] == 'l' && anatomy.Any(part => compact.Contains(part)))
        {
            return BoneSide.Left;
        }
        if (compact.Length > 2 && compact[compact.Length - 1] == 'r' && anatomy.Any(part => compact.Contains(part)))
        {
            return BoneSide.Right;
        }

        return BoneSide.Center;
    }

    private static string NormalizeName(string value)
    {
        return new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    }

    private enum BoneSide
    {
        Center,
        Left,
        Right
    }

    private sealed class BoneRule
    {
        public BoneRule(HumanBodyBones bone, HumanBodyBones? parent, BoneSide side, float expectedHeight, params string[] aliases)
        {
            Bone = bone;
            Parent = parent;
            Side = side;
            ExpectedHeight = expectedHeight;
            Aliases = aliases;
        }

        public HumanBodyBones Bone { get; }
        public HumanBodyBones? Parent { get; }
        public BoneSide Side { get; }
        public float ExpectedHeight { get; }
        public string[] Aliases { get; }
    }

    private sealed class SourceBoneState
    {
        public SourceBoneState(Transform transform)
        {
            Transform = transform;
        }

        public Transform Transform { get; }
    }

    private sealed class BoneBinding
    {
        public BoneBinding(
            HumanBodyBones bone,
            Transform source,
            Transform? sourceSecondary,
            float sourceBlend,
            Transform target,
            Transform targetSpaceRoot,
            Quaternion sourceBindSolverLocalRotation,
            Quaternion targetBindRootLocalRotation,
            Vector3 sourceBindSolverLocalPosition,
            Vector3 targetBindRootLocalPosition,
            float rotationWeight,
            bool transferPosition)
        {
            Bone = bone;
            Source = source;
            SourceSecondary = sourceSecondary;
            SourceBlend = sourceBlend;
            Target = target;
            TargetSpaceRoot = targetSpaceRoot;
            SourceBindSolverLocalRotation = sourceBindSolverLocalRotation;
            TargetBindRootLocalRotation = targetBindRootLocalRotation;
            SourceBindSolverLocalPosition = sourceBindSolverLocalPosition;
            TargetBindRootLocalPosition = targetBindRootLocalPosition;
            RotationWeight = rotationWeight;
            TransferPosition = transferPosition;
            RotationBasis = Quaternion.identity;
            SkeletonScale = 1f;
        }

        public HumanBodyBones Bone { get; }
        public Transform Source { get; }
        public Transform? SourceSecondary { get; }
        public float SourceBlend { get; }
        public Transform Target { get; }
        public Transform TargetSpaceRoot { get; }
        public Quaternion SourceBindSolverLocalRotation { get; }
        public Quaternion TargetBindRootLocalRotation { get; }
        public Vector3 SourceBindSolverLocalPosition { get; }
        public Vector3 TargetBindRootLocalPosition { get; }
        public float RotationWeight { get; }
        public bool TransferPosition { get; }
        public Quaternion RotationBasis { get; set; }
        public float SkeletonScale { get; set; }
    }

    private sealed class TargetSkeleton
    {
        private Bounds? bounds;

        public TargetSkeleton(Transform root, bool fromSkinnedRenderer)
        {
            Root = root;
            FromSkinnedRenderer = fromSkinnedRenderer;
        }

        public Transform Root { get; }
        public bool FromSkinnedRenderer { get; }
        public List<Transform> Bones { get; } = new();
        public HashSet<Transform> RendererBones { get; } = new();
        public bool IsFirstPersonRig
        {
            get
            {
                Transform? current = Root;
                while (current != null)
                {
                    if (NormalizeName(current.name).Contains("firstperson"))
                    {
                        return true;
                    }
                    current = current.parent;
                }
                return false;
            }
        }

        public bool IsNativePeakRig
        {
            get
            {
                var names = new HashSet<string>(Bones.Select(bone => NormalizeName(bone.name)));
                bool hasShoulders =
                    (names.Contains("sshoulderl") || names.Contains("shoulderl")) &&
                    (names.Contains("sshoulderr") || names.Contains("shoulderr"));
                bool hasLegs = names.Contains("kneel") && names.Contains("kneer") &&
                               names.Contains("footl") && names.Contains("footr");
                bool hasArms = names.Contains("arml") && names.Contains("armr") &&
                               names.Contains("elbowl") && names.Contains("elbowr");
                bool hasTorso = (names.Contains("hip") || names.Contains("waist")) && names.Contains("mid") && names.Contains("torso");
                return hasShoulders && hasLegs && hasArms && hasTorso;
            }
        }

        public void AddHierarchy(Transform root)
        {
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
            {
                if (!IsHiddenSolver(transform))
                {
                    Add(transform, false);
                }
            }
        }

        public void AddRendererBones(SkinnedMeshRenderer renderer)
        {
            foreach (Transform bone in renderer.bones)
            {
                if (bone == null)
                {
                    continue;
                }
                Add(bone, true);

                Transform? current = bone.parent;
                while (current != null)
                {
                    Add(current, true);
                    if (current == Root)
                    {
                        break;
                    }
                    current = current.parent;
                }
            }
            if (renderer.rootBone != null)
            {
                Add(renderer.rootBone, true);
            }
        }

        public void Add(Transform transform, bool rendererBone)
        {
            if (!Bones.Contains(transform))
            {
                Bones.Add(transform);
                bounds = null;
            }
            if (rendererBone)
            {
                RendererBones.Add(transform);
            }
        }

        public bool TryGetNormalizedHeight(Transform transform, out float height)
        {
            EnsureBounds();
            if (bounds == null || bounds.Value.size.y <= 0.001f)
            {
                height = 0.5f;
                return false;
            }

            float y = Root.InverseTransformPoint(transform.position).y;
            height = Mathf.InverseLerp(bounds.Value.min.y, bounds.Value.max.y, y);
            return true;
        }

        private void EnsureBounds()
        {
            if (bounds != null || Bones.Count == 0)
            {
                return;
            }

            Vector3 first = Root.InverseTransformPoint(Bones[0].position);
            Bounds computed = new(first, Vector3.zero);
            for (int index = 1; index < Bones.Count; index++)
            {
                computed.Encapsulate(Root.InverseTransformPoint(Bones[index].position));
            }
            bounds = computed;
        }
    }

    private sealed class RendererState
    {
        public RendererState(Renderer renderer, bool enabled, bool forceRenderingOff)
        {
            Renderer = renderer;
            Enabled = enabled;
            ForceRenderingOff = forceRenderingOff;
        }

        public Renderer Renderer { get; }
        public bool Enabled { get; }
        public bool ForceRenderingOff { get; }
    }

    private sealed class TargetTransformState
    {
        public TargetTransformState(Transform transform, Quaternion localRotation, Vector3 localPosition)
        {
            Transform = transform;
            LocalRotation = localRotation;
            LocalPosition = localPosition;
        }

        public Transform Transform { get; }
        public Quaternion LocalRotation { get; }
        public Vector3 LocalPosition { get; }
    }
}
