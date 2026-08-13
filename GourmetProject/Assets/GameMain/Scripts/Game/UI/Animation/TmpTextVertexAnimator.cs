using TMPro;
using UnityEngine;

namespace GameStartStudio.UI
{
    public enum TmpTextAnimationPreset
    {
        LegacyCustom = 0,
        Food = 1,
        Reward = 2,
        Negative = 3,
        Event = 4,
        Shop = 5,
        Interest = 6,
        Slot = 7,
        Boss = 8,
        TipTitle = 9,
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(TMP_Text))]
    public sealed class TmpTextVertexAnimator : MonoBehaviour
    {
        [Header("Preset")]
        [SerializeField] private TmpTextAnimationPreset preset = TmpTextAnimationPreset.LegacyCustom;
        [SerializeField, Min(0f)] private float intensity = 1f;
        [SerializeField, Min(0.05f)] private float speed = 1f;

        [Header("Legacy Custom - Breath Scale")]
        [SerializeField] private float breathDuration = 1.9f;
        [SerializeField] private float breathScale = 0.045f;

        [Header("Legacy Custom - Character Wave")]
        [SerializeField] private float waveInterval = 2.35f;
        [SerializeField] private float waveTravelDuration = 0.75f;
        [SerializeField] private float perCharacterDelay = 0.055f;
        [SerializeField] private float waveHeight = 8f;

        [Header("Legacy Custom - Shimmer")]
        [SerializeField] private float shimmerInterval = 3.2f;
        [SerializeField] private float shimmerWidth = 0.16f;
        [SerializeField] private Color shimmerColor = new Color(1f, 0.96f, 0.58f, 1f);
        [SerializeField, Range(0f, 1f)] private float shimmerStrength = 0.42f;

        private TMP_Text text;
        private RectTransform rectTransformCache;
        private Vector3 homeScale = Vector3.one;
        private float elapsed;
        private TMP_MeshInfo[] cachedMeshInfo;
        private bool hasCachedMeshInfo;
        private Vector2 cachedRectSize;
        private string cachedText;

        public TmpTextAnimationPreset Preset => preset;
        public float Intensity => intensity;
        public float Speed => speed;

        private float AnimationTime => elapsed * Mathf.Max(0.05f, speed);

        private void Awake()
        {
            CacheReferences();
            CaptureHomeScale();
        }

        private void OnEnable()
        {
            elapsed = 0f;
            CacheReferences();
            CaptureHomeScale();
            CacheOriginalMeshInfo();
        }

        private void OnDisable()
        {
            ResetScale();
            RestoreOriginalMesh();
        }

        private void OnRectTransformDimensionsChange()
        {
            // Layout groups can resize a tip title after Bind/Rebuild. Do not keep
            // replaying vertices generated for the old (often zero-width) rect.
            hasCachedMeshInfo = false;
        }

        private void Update()
        {
            if (!Application.isPlaying || text == null || !isActiveAndEnabled)
            {
                ResetScale();
                return;
            }

            if (!text.gameObject.activeInHierarchy)
            {
                return;
            }

            if (RectSizeChanged() || TextChanged())
            {
                hasCachedMeshInfo = false;
            }

            if (!hasCachedMeshInfo)
            {
                CacheOriginalMeshInfo();
            }

            elapsed += Time.unscaledDeltaTime;
            ApplyScaleAnimation();
            ApplyTextAnimation();
        }

        public void SetPreset(
            TmpTextAnimationPreset animationPreset,
            float animationIntensity = 1f,
            float animationSpeed = 1f)
        {
            CacheReferences();
            RestoreOriginalMesh();
            ResetScale();

            preset = animationPreset;
            intensity = Mathf.Max(0f, animationIntensity);
            speed = Mathf.Max(0.05f, animationSpeed);
            elapsed = 0f;

            CaptureHomeScale();
            CacheOriginalMeshInfo();
        }

        /// <summary>文字内容改变后重新缓存未动画的 TMP 网格。</summary>
        public void Rebuild()
        {
            CacheReferences();
            CacheOriginalMeshInfo();
        }

        private void CacheReferences()
        {
            if (text == null)
            {
                text = GetComponent<TMP_Text>();
            }

            if (rectTransformCache == null)
            {
                rectTransformCache = transform as RectTransform;
            }
        }

        private void CaptureHomeScale()
        {
            if (rectTransformCache != null)
            {
                homeScale = rectTransformCache.localScale;
            }
        }

        private void CacheOriginalMeshInfo()
        {
            hasCachedMeshInfo = false;
            if (text == null)
            {
                return;
            }

            // A tooltip can be rebound while hidden or before TMP has reparsed the
            // new string. Force a real regeneration so a shorter title never keeps
            // the previous title's character buffers.
            text.ForceMeshUpdate(true, true);
            if (!TryCreateMeshSnapshot(text.textInfo, out TMP_MeshInfo[] meshSnapshot))
            {
                return;
            }

            cachedMeshInfo = meshSnapshot;
            cachedText = text.text;
            cachedRectSize = rectTransformCache != null
                ? rectTransformCache.rect.size
                : Vector2.zero;
            hasCachedMeshInfo = true;
        }

        private bool RectSizeChanged()
        {
            if (!hasCachedMeshInfo || rectTransformCache == null)
            {
                return false;
            }

            return (rectTransformCache.rect.size - cachedRectSize).sqrMagnitude > 0.01f;
        }

        private bool TextChanged()
        {
            return hasCachedMeshInfo && text != null && cachedText != text.text;
        }

        private static bool TryCreateMeshSnapshot(
            TMP_TextInfo textInfo,
            out TMP_MeshInfo[] meshSnapshot)
        {
            meshSnapshot = null;
            if (textInfo == null || textInfo.meshInfo == null || textInfo.meshInfo.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < textInfo.characterCount; i++)
            {
                TMP_CharacterInfo characterInfo = textInfo.characterInfo[i];
                if (!characterInfo.isVisible)
                {
                    continue;
                }

                int materialIndex = characterInfo.materialReferenceIndex;
                int vertexIndex = characterInfo.vertexIndex;
                if (materialIndex < 0 || materialIndex >= textInfo.meshInfo.Length || vertexIndex < 0)
                {
                    return false;
                }

                TMP_MeshInfo source = textInfo.meshInfo[materialIndex];
                if (source.vertices == null || source.colors32 == null ||
                    vertexIndex + 3 >= source.vertices.Length || vertexIndex + 3 >= source.colors32.Length)
                {
                    return false;
                }
            }

            meshSnapshot = new TMP_MeshInfo[textInfo.meshInfo.Length];
            for (int i = 0; i < textInfo.meshInfo.Length; i++)
            {
                TMP_MeshInfo source = textInfo.meshInfo[i];
                if (source.vertices != null)
                {
                    meshSnapshot[i].vertices = (Vector3[])source.vertices.Clone();
                }

                if (source.colors32 != null)
                {
                    meshSnapshot[i].colors32 = (Color32[])source.colors32.Clone();
                }
            }

            return true;
        }

        private void ApplyScaleAnimation()
        {
            if (rectTransformCache == null)
            {
                return;
            }

            rectTransformCache.localScale = homeScale * ScaleMultiplier();
        }

        private float ScaleMultiplier()
        {
            float time = AnimationTime;
            switch (preset)
            {
                case TmpTextAnimationPreset.LegacyCustom:
                {
                    float phase = Mathf.Repeat(time / Mathf.Max(0.1f, breathDuration), 1f);
                    float softened = Mathf.SmoothStep(0f, 1f, Mathf.Sin(phase * Mathf.PI));
                    return 1f + softened * breathScale * intensity;
                }
                case TmpTextAnimationPreset.Reward:
                    return 1f + WindowPulse(time, 3f, 0.42f, 0f) * 0.012f * intensity;
                case TmpTextAnimationPreset.Boss:
                {
                    float first = WindowPulse(time, 3.2f, 0.14f, 0f);
                    float second = WindowPulse(time, 3.2f, 0.14f, 0.19f);
                    return 1f + (first + second * 0.72f) * 0.04f * intensity;
                }
                case TmpTextAnimationPreset.TipTitle:
                    // Tip titles live inside layout groups. Scaling the RectTransform
                    // makes bold CJK glyphs blur and can fight layout rebuilding.
                    return 1f;
                default:
                    return 1f;
            }
        }

        private static float WindowPulse(float time, float interval, float duration, float offset)
        {
            float local = Mathf.Repeat(time, interval) - offset;
            if (local < 0f || local > duration)
            {
                return 0f;
            }

            return Mathf.Sin(Mathf.Clamp01(local / duration) * Mathf.PI);
        }

        private void ApplyTextAnimation()
        {
            if (text == null || !hasCachedMeshInfo || cachedMeshInfo == null)
            {
                return;
            }

            TMP_TextInfo textInfo = text.textInfo;
            if (textInfo == null || textInfo.characterCount == 0)
            {
                return;
            }

            if (!TryGetHorizontalBounds(textInfo, out float minX, out float width))
            {
                return;
            }

            bool animateVertices = UsesVertexMotion(preset);
            int visibleCharacterIndex = 0;
            for (int i = 0; i < textInfo.characterCount; i++)
            {
                TMP_CharacterInfo characterInfo = textInfo.characterInfo[i];
                if (!characterInfo.isVisible)
                {
                    continue;
                }

                int materialIndex = characterInfo.materialReferenceIndex;
                int vertexIndex = characterInfo.vertexIndex;
                if (!TryGetCharacterBuffers(
                        textInfo,
                        materialIndex,
                        vertexIndex,
                        out Vector3[] sourceVertices,
                        out Color32[] sourceColors,
                        out Vector3[] targetVertices,
                        out Color32[] targetColors))
                {
                    visibleCharacterIndex++;
                    continue;
                }

                Vector3 center = Vector3.zero;
                Vector3 offset = Vector3.zero;
                float cos = 1f;
                float sin = 0f;
                if (animateVertices)
                {
                    center = CharacterCenter(sourceVertices, vertexIndex);
                    CharacterMotion(visibleCharacterIndex, out offset, out float angleDegrees);
                    float radians = angleDegrees * Mathf.Deg2Rad;
                    cos = Mathf.Cos(radians);
                    sin = Mathf.Sin(radians);
                }

                for (int j = 0; j < 4; j++)
                {
                    int index = vertexIndex + j;
                    if (animateVertices)
                    {
                        Vector3 relative = sourceVertices[index] - center;
                        Vector3 rotated = new Vector3(
                            relative.x * cos - relative.y * sin,
                            relative.x * sin + relative.y * cos,
                            relative.z);
                        targetVertices[index] = center + rotated + offset;
                    }

                    targetColors[index] = ShimmerColor(sourceColors[index], sourceVertices[index].x, minX, width);
                }

                visibleCharacterIndex++;
            }

            TMP_VertexDataUpdateFlags updateFlags = TMP_VertexDataUpdateFlags.Colors32;
            if (animateVertices)
            {
                updateFlags |= TMP_VertexDataUpdateFlags.Vertices;
                for (int i = 0; i < textInfo.materialCount; i++)
                {
                    textInfo.meshInfo[i].ClearUnusedVertices();
                }
            }

            text.UpdateVertexData(updateFlags);
        }

        private static bool UsesVertexMotion(TmpTextAnimationPreset animationPreset)
        {
            switch (animationPreset)
            {
                case TmpTextAnimationPreset.LegacyCustom:
                case TmpTextAnimationPreset.Food:
                case TmpTextAnimationPreset.Negative:
                case TmpTextAnimationPreset.Event:
                case TmpTextAnimationPreset.Interest:
                case TmpTextAnimationPreset.Slot:
                case TmpTextAnimationPreset.Boss:
                    return true;
                default:
                    return false;
            }
        }

        private bool TryGetHorizontalBounds(TMP_TextInfo textInfo, out float minX, out float width)
        {
            minX = 0f;
            float maxX = 0f;
            bool found = false;
            for (int i = 0; i < textInfo.characterCount; i++)
            {
                TMP_CharacterInfo characterInfo = textInfo.characterInfo[i];
                if (!characterInfo.isVisible)
                {
                    continue;
                }

                int materialIndex = characterInfo.materialReferenceIndex;
                int vertexIndex = characterInfo.vertexIndex;
                if (materialIndex < 0 || materialIndex >= cachedMeshInfo.Length)
                {
                    continue;
                }

                Vector3[] vertices = cachedMeshInfo[materialIndex].vertices;
                if (vertices == null || vertexIndex < 0 || vertexIndex + 3 >= vertices.Length)
                {
                    continue;
                }

                for (int j = 0; j < 4; j++)
                {
                    float x = vertices[vertexIndex + j].x;
                    if (!found)
                    {
                        minX = x;
                        maxX = x;
                        found = true;
                    }
                    else
                    {
                        minX = Mathf.Min(minX, x);
                        maxX = Mathf.Max(maxX, x);
                    }
                }
            }

            width = Mathf.Max(1f, maxX - minX);
            return found;
        }

        private bool TryGetCharacterBuffers(
            TMP_TextInfo textInfo,
            int materialIndex,
            int vertexIndex,
            out Vector3[] sourceVertices,
            out Color32[] sourceColors,
            out Vector3[] targetVertices,
            out Color32[] targetColors)
        {
            sourceVertices = null;
            sourceColors = null;
            targetVertices = null;
            targetColors = null;

            if (materialIndex < 0 || materialIndex >= cachedMeshInfo.Length ||
                materialIndex >= textInfo.meshInfo.Length || vertexIndex < 0)
            {
                return false;
            }

            sourceVertices = cachedMeshInfo[materialIndex].vertices;
            sourceColors = cachedMeshInfo[materialIndex].colors32;
            targetVertices = textInfo.meshInfo[materialIndex].vertices;
            targetColors = textInfo.meshInfo[materialIndex].colors32;

            return sourceVertices != null && sourceColors != null &&
                   targetVertices != null && targetColors != null &&
                   vertexIndex + 3 < sourceVertices.Length &&
                   vertexIndex + 3 < sourceColors.Length &&
                   vertexIndex + 3 < targetVertices.Length &&
                   vertexIndex + 3 < targetColors.Length;
        }

        private static Vector3 CharacterCenter(Vector3[] vertices, int vertexIndex)
        {
            return (vertices[vertexIndex] + vertices[vertexIndex + 1] +
                    vertices[vertexIndex + 2] + vertices[vertexIndex + 3]) * 0.25f;
        }

        private void CharacterMotion(int characterIndex, out Vector3 offset, out float angleDegrees)
        {
            offset = Vector3.zero;
            angleDegrees = 0f;
            float time = AnimationTime;

            switch (preset)
            {
                case TmpTextAnimationPreset.LegacyCustom:
                    offset.y = TravelingPulse(
                        time,
                        characterIndex,
                        waveInterval,
                        waveTravelDuration,
                        perCharacterDelay) * waveHeight * intensity;
                    break;
                case TmpTextAnimationPreset.Food:
                    offset.y = TravelingPulse(time, characterIndex, 2.6f, 0.58f, 0.055f) * 3f * intensity;
                    break;
                case TmpTextAnimationPreset.Negative:
                {
                    float local = Mathf.Repeat(time, 3.6f);
                    if (local <= 0.34f)
                    {
                        float decay = 1f - local / 0.34f;
                        offset.x = Mathf.Sin(local * 78f + characterIndex * 0.37f) * 1.5f * decay * intensity;
                    }
                    break;
                }
                case TmpTextAnimationPreset.Event:
                {
                    float pulse = TravelingPulse(time, characterIndex, 3f, 0.82f, 0.045f);
                    float uneven = Mathf.Sin(characterIndex * 1.71f + pulse * Mathf.PI);
                    offset.y = pulse * uneven * 2f * intensity;
                    angleDegrees = pulse * Mathf.Sin(characterIndex * 2.17f) * 1.2f * intensity;
                    break;
                }
                case TmpTextAnimationPreset.Interest:
                    offset.y = TravelingPulse(time, characterIndex, 2.8f, 0.62f, 0.05f) * 3f * intensity;
                    break;
                case TmpTextAnimationPreset.Slot:
                {
                    float local = TravelingProgress(time, characterIndex, 3f, 0.72f, 0.05f);
                    if (local >= 0f)
                    {
                        offset.y = (-Mathf.Sin(local * Mathf.PI) + Mathf.Sin(local * Mathf.PI * 2f))
                            * 2f * intensity;
                    }
                    break;
                }
                case TmpTextAnimationPreset.Boss:
                {
                    float local = Mathf.Repeat(time, 3.2f);
                    if (local <= 0.52f)
                    {
                        float decay = 1f - local / 0.52f;
                        offset.x = Mathf.Sin(local * 68f + characterIndex * 0.29f) * 1.5f * decay * intensity;
                    }
                    break;
                }
            }
        }

        private static float TravelingPulse(
            float time,
            int characterIndex,
            float interval,
            float duration,
            float delay)
        {
            float progress = TravelingProgress(time, characterIndex, interval, duration, delay);
            return progress < 0f ? 0f : Mathf.Sin(progress * Mathf.PI);
        }

        private static float TravelingProgress(
            float time,
            int characterIndex,
            float interval,
            float duration,
            float delay)
        {
            float local = Mathf.Repeat(time, Mathf.Max(0.2f, interval)) - characterIndex * delay;
            if (local < 0f || local > duration)
            {
                return -1f;
            }

            return Mathf.Clamp01(local / Mathf.Max(0.05f, duration));
        }

        private Color32 ShimmerColor(Color32 source, float x, float minX, float width)
        {
            if (!ShimmerSettings(out float interval, out float bandWidth, out Color color, out float strength))
            {
                return source;
            }

            float cycle = Mathf.Repeat(AnimationTime / interval, 1f);
            float bandCenter = Mathf.Lerp(-0.22f, 1.22f, cycle);
            float normalizedX = (x - minX) / width;
            float distance = Mathf.Abs(normalizedX - bandCenter);
            float amount = Mathf.SmoothStep(1f, 0f, distance / bandWidth) * strength * intensity;

            Color mixed = Color.Lerp(source, color, Mathf.Clamp01(amount));
            mixed.a = source.a / 255f;
            return mixed;
        }

        private bool ShimmerSettings(
            out float interval,
            out float bandWidth,
            out Color color,
            out float strength)
        {
            interval = 3f;
            bandWidth = 0.16f;
            color = new Color(1f, 0.94f, 0.62f, 1f);
            strength = 0.25f;

            switch (preset)
            {
                case TmpTextAnimationPreset.LegacyCustom:
                    interval = shimmerInterval;
                    bandWidth = shimmerWidth;
                    color = shimmerColor;
                    strength = shimmerStrength;
                    return shimmerStrength > 0f;
                case TmpTextAnimationPreset.Food:
                    interval = 3.1f;
                    strength = 0.18f;
                    return true;
                case TmpTextAnimationPreset.Reward:
                    interval = 3f;
                    bandWidth = 0.14f;
                    strength = 0.5f;
                    return true;
                case TmpTextAnimationPreset.Event:
                    interval = 3.3f;
                    color = new Color(0.94f, 0.83f, 1f, 1f);
                    strength = 0.16f;
                    return true;
                case TmpTextAnimationPreset.Shop:
                    interval = 3.4f;
                    bandWidth = 0.15f;
                    strength = 0.38f;
                    return true;
                case TmpTextAnimationPreset.Interest:
                    interval = 2.8f;
                    color = new Color(0.82f, 1f, 0.63f, 1f);
                    strength = 0.34f;
                    return true;
                case TmpTextAnimationPreset.Slot:
                    interval = 3f;
                    color = new Color(1f, 0.78f, 0.48f, 1f);
                    strength = 0.32f;
                    return true;
                case TmpTextAnimationPreset.Boss:
                    interval = 3.2f;
                    color = new Color(1f, 0.48f, 0.32f, 1f);
                    strength = 0.3f;
                    return true;
                case TmpTextAnimationPreset.TipTitle:
                    interval = 4f;
                    bandWidth = 0.18f;
                    strength = 0.18f;
                    return true;
                default:
                    return false;
            }
        }

        private void RestoreOriginalMesh()
        {
            if (text == null || !hasCachedMeshInfo || cachedMeshInfo == null || text.textInfo == null)
            {
                return;
            }

            TMP_TextInfo textInfo = text.textInfo;
            bool restoreVertices = UsesVertexMotion(preset);
            for (int i = 0; i < textInfo.meshInfo.Length && i < cachedMeshInfo.Length; i++)
            {
                TMP_MeshInfo source = cachedMeshInfo[i];
                TMP_MeshInfo target = textInfo.meshInfo[i];
                if (source.vertices == null || source.colors32 == null ||
                    target.vertices == null || target.colors32 == null || target.mesh == null)
                {
                    continue;
                }

                int vertexCount = Mathf.Min(source.vertices.Length, target.vertices.Length);
                int colorCount = Mathf.Min(source.colors32.Length, target.colors32.Length);
                if (restoreVertices)
                {
                    for (int j = 0; j < vertexCount; j++)
                    {
                        target.vertices[j] = source.vertices[j];
                    }
                }

                for (int j = 0; j < colorCount; j++)
                {
                    target.colors32[j] = source.colors32[j];
                }
            }

            TMP_VertexDataUpdateFlags updateFlags = TMP_VertexDataUpdateFlags.Colors32;
            if (restoreVertices)
            {
                updateFlags |= TMP_VertexDataUpdateFlags.Vertices;
            }

            text.UpdateVertexData(updateFlags);
        }

        private void ResetScale()
        {
            if (rectTransformCache != null)
            {
                rectTransformCache.localScale = homeScale;
            }
        }

        private void OnValidate()
        {
            intensity = Mathf.Max(0f, intensity);
            speed = Mathf.Max(0.05f, speed);
            breathDuration = Mathf.Max(0.1f, breathDuration);
            breathScale = Mathf.Max(0f, breathScale);
            waveInterval = Mathf.Max(0.2f, waveInterval);
            waveTravelDuration = Mathf.Max(0.05f, waveTravelDuration);
            perCharacterDelay = Mathf.Max(0f, perCharacterDelay);
            waveHeight = Mathf.Max(0f, waveHeight);
            shimmerInterval = Mathf.Max(0.2f, shimmerInterval);
            shimmerWidth = Mathf.Max(0.02f, shimmerWidth);
            shimmerStrength = Mathf.Clamp01(shimmerStrength);
        }
    }
}
