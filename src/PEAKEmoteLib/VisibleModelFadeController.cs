using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PEAKEmoteLib;

/// <summary>
/// Performs short transition-only alpha fades for visible replacement models.
/// Materials are cloned only while a fade is active. After fade-in the original
/// opaque materials are restored immediately, avoiding permanent transparent
/// rendering, extra material instances and sorting overhead during the dance.
/// </summary>
internal sealed class VisibleModelFadeController : MonoBehaviour
{
    private static readonly string[] ColorProperties =
    {
        "_BaseColor", "_Color", "_MainColor", "_TintColor"
    };

    private readonly List<RendererFadeEntry> rendererEntries = new();
    private readonly List<Material> temporaryMaterials = new();
    private Renderer[] trackedRenderers = Array.Empty<Renderer>();
    private FadeMode mode;
    private float duration;
    private float elapsed;
    private float startAlpha;
    private float currentAlpha = 1f;

    public void BeginFadeIn(Renderer[] renderers, float fadeDuration)
    {
        trackedRenderers = renderers ?? Array.Empty<Renderer>();
        if (!CreateTemporaryFadeMaterials())
        {
            return;
        }

        mode = FadeMode.In;
        duration = Mathf.Max(0.01f, fadeDuration);
        elapsed = 0f;
        startAlpha = 0f;
        currentAlpha = 0f;
        ApplyAlpha(0f);
        enabled = true;
    }

    public void FadeOutAndDestroy(float fadeDuration)
    {
        if (!gameObject.activeInHierarchy)
        {
            Destroy(gameObject);
            return;
        }

        if (rendererEntries.Count == 0 && !CreateTemporaryFadeMaterials())
        {
            Destroy(gameObject);
            return;
        }

        mode = FadeMode.Out;
        duration = Mathf.Max(0.01f, fadeDuration);
        elapsed = 0f;
        startAlpha = Mathf.Clamp01(currentAlpha);
        enabled = true;
    }

    private void Update()
    {
        if (mode == FadeMode.None)
        {
            enabled = false;
            return;
        }

        elapsed += Time.unscaledDeltaTime;
        float progress = Mathf.Clamp01(elapsed / duration);
        float eased = Mathf.SmoothStep(0f, 1f, progress);

        if (mode == FadeMode.In)
        {
            currentAlpha = Mathf.Lerp(startAlpha, 1f, eased);
            ApplyAlpha(currentAlpha);
            if (progress >= 1f)
            {
                CompleteFadeIn();
            }
            return;
        }

        currentAlpha = Mathf.Lerp(startAlpha, 0f, eased);
        ApplyAlpha(currentAlpha);
        if (progress >= 1f)
        {
            Destroy(gameObject);
        }
    }

    private bool CreateTemporaryFadeMaterials()
    {
        ReleaseTemporaryMaterials(true);
        var sharedFadeClones = new Dictionary<Material, Material>();

        foreach (Renderer renderer in trackedRenderers)
        {
            if (renderer == null)
            {
                continue;
            }

            Material[] originals = renderer.sharedMaterials;
            if (originals == null || originals.Length == 0)
            {
                continue;
            }

            Material[] faded = new Material[originals.Length];
            bool hasMaterial = false;
            for (int index = 0; index < originals.Length; index++)
            {
                Material? original = originals[index];
                if (original == null)
                {
                    continue;
                }

                if (!sharedFadeClones.TryGetValue(original, out Material? clone))
                {
                    clone = new Material(original)
                    {
                        name = original.name + "_PEAKFade",
                        hideFlags = HideFlags.DontSave
                    };
                    ConfigureTransparentMaterial(clone);
                    sharedFadeClones[original] = clone;
                    temporaryMaterials.Add(clone);
                }
                faded[index] = clone;
                hasMaterial = true;
            }

            if (!hasMaterial)
            {
                continue;
            }

            renderer.sharedMaterials = faded;
            rendererEntries.Add(new RendererFadeEntry(renderer, originals, faded));
        }

        return rendererEntries.Count > 0;
    }

    private static void ConfigureTransparentMaterial(Material material)
    {
        material.SetOverrideTag("RenderType", "Transparent");
        if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
        if (material.HasProperty("_Mode")) material.SetFloat("_Mode", 3f);
        if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);
        if (material.HasProperty("_AlphaClip")) material.SetFloat("_AlphaClip", 0f);
        if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);

        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.EnableKeyword("_ALPHABLEND_ON");
        material.DisableKeyword("_ALPHATEST_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.renderQueue = (int)RenderQueue.Transparent;
    }

    private void ApplyAlpha(float alpha)
    {
        float clamped = Mathf.Clamp01(alpha);
        foreach (RendererFadeEntry entry in rendererEntries)
        {
            foreach (Material material in entry.FadedMaterials)
            {
                if (material == null)
                {
                    continue;
                }

                foreach (string property in ColorProperties)
                {
                    if (!material.HasProperty(property))
                    {
                        continue;
                    }

                    Color color = material.GetColor(property);
                    float baseAlpha = entry.GetBaseAlpha(material, property, color.a);
                    color.a = baseAlpha * clamped;
                    material.SetColor(property, color);
                }
            }
        }
    }

    private void CompleteFadeIn()
    {
        currentAlpha = 1f;
        ApplyAlpha(1f);
        ReleaseTemporaryMaterials(true);
        mode = FadeMode.None;
        enabled = false;
    }

    private void ReleaseTemporaryMaterials(bool restoreOriginals)
    {
        if (restoreOriginals)
        {
            foreach (RendererFadeEntry entry in rendererEntries)
            {
                if (entry.Renderer != null)
                {
                    entry.Renderer.sharedMaterials = entry.OriginalMaterials;
                }
            }
        }

        rendererEntries.Clear();
        foreach (Material material in temporaryMaterials)
        {
            if (material != null)
            {
                Destroy(material);
            }
        }
        temporaryMaterials.Clear();
    }

    private void OnDestroy()
    {
        // The owning model is already being destroyed, so restoring original
        // material arrays is unnecessary and can touch renderers mid-destruction.
        ReleaseTemporaryMaterials(false);
    }

    private enum FadeMode
    {
        None,
        In,
        Out
    }

    private sealed class RendererFadeEntry
    {
        private readonly Dictionary<string, float> baseAlphas = new(StringComparer.Ordinal);

        public RendererFadeEntry(Renderer renderer, Material[] originalMaterials, Material[] fadedMaterials)
        {
            Renderer = renderer;
            OriginalMaterials = originalMaterials;
            FadedMaterials = fadedMaterials;

            foreach (Material material in fadedMaterials)
            {
                if (material == null)
                {
                    continue;
                }

                foreach (string property in ColorProperties)
                {
                    if (material.HasProperty(property))
                    {
                        baseAlphas[BuildKey(material, property)] = material.GetColor(property).a;
                    }
                }
            }
        }

        public Renderer Renderer { get; }
        public Material[] OriginalMaterials { get; }
        public Material[] FadedMaterials { get; }

        public float GetBaseAlpha(Material material, string property, float fallback)
        {
            return baseAlphas.TryGetValue(BuildKey(material, property), out float value) ? value : fallback;
        }

        private static string BuildKey(Material material, string property)
        {
            return material.GetInstanceID() + ":" + property;
        }
    }
}
