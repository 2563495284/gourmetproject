using TMPro;
using UnityEngine;

namespace GameStartStudio.UI
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TMP_Text))]
    public sealed class TmpTextVertexAnimator : MonoBehaviour
    {
        [Header("Breath Scale")]
        [SerializeField] private float breathDuration = 1.9f;
        [SerializeField] private float breathScale = 0.045f;

        [Header("Character Wave")]
        [SerializeField] private float waveInterval = 2.35f;
        [SerializeField] private float waveTravelDuration = 0.75f;
        [SerializeField] private float perCharacterDelay = 0.055f;
        [SerializeField] private float waveHeight = 8f;

        [Header("Shimmer")]
        [SerializeField] private float shimmerInterval = 3.2f;
        [SerializeField] private float shimmerWidth = 0.16f;
        [SerializeField] private Color shimmerColor = new Color(1f, 0.96f, 0.58f, 1f);
        [SerializeField, Range(0f, 1f)] private float shimmerStrength = 0.42f;

        private TMP_Text text;
        private RectTransform rectTransformCache;
        private Vector3 homeScale;
        private float elapsed;

        private TMP_MeshInfo[] cachedMeshInfo;
        private bool hasCachedMeshInfo;

        private void Awake()
        {
            text = GetComponent<TMP_Text>();
            rectTransformCache = transform as RectTransform;
            homeScale = rectTransformCache != null ? rectTransformCache.localScale : Vector3.one;
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

        private void Update()
        {
            if (!Application.isPlaying || text == null || !isActiveAndEnabled)
            {
                ResetScale();
                return;
            }

            if (!hasCachedMeshInfo)
            {
                CacheOriginalMeshInfo();
            }

            elapsed += Time.unscaledDeltaTime;

            ApplyBreathScale();
            ApplyTextAnimation();
        }

        /// <summary>
        /// 如果运行时修改了 text.text，可以在修改后主动调用这个方法重新缓存文字网格。
        /// </summary>
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
            homeScale = rectTransformCache != null ? rectTransformCache.localScale : Vector3.one;
        }

        private void CacheOriginalMeshInfo()
        {
            hasCachedMeshInfo = false;

            if (text == null)
            {
                return;
            }

            text.ForceMeshUpdate();

            TMP_TextInfo textInfo = text.textInfo;

            if (!TryCreateMeshSnapshot(textInfo, out TMP_MeshInfo[] meshSnapshot))
            {
                return;
            }

            cachedMeshInfo = meshSnapshot;
            hasCachedMeshInfo = true;
        }

        private static bool TryCreateMeshSnapshot(
            TMP_TextInfo textInfo,
            out TMP_MeshInfo[] meshSnapshot)
        {
            meshSnapshot = null;

            if (textInfo == null ||
                textInfo.meshInfo == null ||
                textInfo.meshInfo.Length == 0 ||
                textInfo.meshInfo[0].vertices == null ||
                textInfo.meshInfo[0].colors32 == null)
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

                if (materialIndex < 0 ||
                    materialIndex >= textInfo.meshInfo.Length ||
                    vertexIndex < 0)
                {
                    return false;
                }

                TMP_MeshInfo sourceMeshInfo = textInfo.meshInfo[materialIndex];

                if (sourceMeshInfo.vertices == null ||
                    sourceMeshInfo.colors32 == null ||
                    vertexIndex + 3 >= sourceMeshInfo.vertices.Length ||
                    vertexIndex + 3 >= sourceMeshInfo.colors32.Length)
                {
                    return false;
                }
            }

            meshSnapshot = new TMP_MeshInfo[textInfo.meshInfo.Length];

            for (int i = 0; i < textInfo.meshInfo.Length; i++)
            {
                TMP_MeshInfo sourceMeshInfo = textInfo.meshInfo[i];

                if (sourceMeshInfo.vertices != null)
                {
                    meshSnapshot[i].vertices = (Vector3[])sourceMeshInfo.vertices.Clone();
                }

                if (sourceMeshInfo.colors32 != null)
                {
                    meshSnapshot[i].colors32 = (Color32[])sourceMeshInfo.colors32.Clone();
                }
            }

            return true;
        }

        private void ApplyBreathScale()
        {
            if (rectTransformCache == null)
            {
                return;
            }

            float phase = Mathf.Repeat(elapsed / Mathf.Max(0.1f, breathDuration), 1f);
            float softened = Mathf.SmoothStep(0f, 1f, Mathf.Sin(phase * Mathf.PI));
            float scale = 1f + softened * breathScale;

            rectTransformCache.localScale = homeScale * scale;
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

            float minX = 0f;
            float maxX = 0f;
            bool hasVisibleCharacter = false;

            for (int i = 0; i < textInfo.characterCount; i++)
            {
                TMP_CharacterInfo characterInfo = textInfo.characterInfo[i];

                if (!characterInfo.isVisible)
                {
                    continue;
                }

                int materialIndex = characterInfo.materialReferenceIndex;
                int vertexIndex = characterInfo.vertexIndex;

                if (materialIndex >= cachedMeshInfo.Length)
                {
                    continue;
                }

                Vector3[] sourceVertices = cachedMeshInfo[materialIndex].vertices;

                if (sourceVertices == null || vertexIndex + 3 >= sourceVertices.Length)
                {
                    continue;
                }

                for (int j = 0; j < 4; j++)
                {
                    float x = sourceVertices[vertexIndex + j].x;

                    if (!hasVisibleCharacter)
                    {
                        minX = x;
                        maxX = x;
                        hasVisibleCharacter = true;
                    }
                    else
                    {
                        minX = Mathf.Min(minX, x);
                        maxX = Mathf.Max(maxX, x);
                    }
                }
            }

            if (!hasVisibleCharacter)
            {
                return;
            }

            float width = Mathf.Max(1f, maxX - minX);
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

                if (materialIndex >= cachedMeshInfo.Length ||
                    materialIndex >= textInfo.meshInfo.Length)
                {
                    continue;
                }

                Vector3[] sourceVertices = cachedMeshInfo[materialIndex].vertices;
                Color32[] sourceColors = cachedMeshInfo[materialIndex].colors32;

                Vector3[] targetVertices = textInfo.meshInfo[materialIndex].vertices;
                Color32[] targetColors = textInfo.meshInfo[materialIndex].colors32;

                if (sourceVertices == null || sourceColors == null ||
                    targetVertices == null || targetColors == null)
                {
                    continue;
                }

                if (vertexIndex + 3 >= sourceVertices.Length ||
                    vertexIndex + 3 >= sourceColors.Length ||
                    vertexIndex + 3 >= targetVertices.Length ||
                    vertexIndex + 3 >= targetColors.Length)
                {
                    continue;
                }

                float yOffset = GetCharacterWaveOffset(visibleCharacterIndex);

                for (int j = 0; j < 4; j++)
                {
                    int index = vertexIndex + j;

                    Vector3 vertex = sourceVertices[index] + Vector3.up * yOffset;
                    targetVertices[index] = vertex;

                    Color32 baseColor = sourceColors[index];
                    targetColors[index] = GetShimmerColor(baseColor, sourceVertices[index].x, minX, width);
                }

                visibleCharacterIndex++;
            }

            for (int i = 0; i < textInfo.meshInfo.Length; i++)
            {
                TMP_MeshInfo meshInfo = textInfo.meshInfo[i];

                if (meshInfo.mesh == null ||
                    meshInfo.vertices == null ||
                    meshInfo.colors32 == null)
                {
                    continue;
                }

                meshInfo.mesh.vertices = meshInfo.vertices;
                meshInfo.mesh.colors32 = meshInfo.colors32;

                text.UpdateGeometry(meshInfo.mesh, i);
            }
        }

        private float GetCharacterWaveOffset(int characterIndex)
        {
            float cycleTime = Mathf.Repeat(elapsed, Mathf.Max(0.2f, waveInterval));
            float characterTime = cycleTime - characterIndex * perCharacterDelay;

            if (characterTime < 0f || characterTime > waveTravelDuration)
            {
                return 0f;
            }

            float t = Mathf.Clamp01(characterTime / Mathf.Max(0.05f, waveTravelDuration));
            return Mathf.Sin(t * Mathf.PI) * waveHeight;
        }

        private Color32 GetShimmerColor(Color32 source, float x, float minX, float width)
        {
            float cycle = Mathf.Repeat(elapsed / Mathf.Max(0.2f, shimmerInterval), 1f);
            float bandCenter = Mathf.Lerp(-0.22f, 1.22f, cycle);

            float normalizedX = (x - minX) / width;
            float distance = Mathf.Abs(normalizedX - bandCenter);

            float strength = Mathf.SmoothStep(
                1f,
                0f,
                distance / Mathf.Max(0.02f, shimmerWidth)
            ) * shimmerStrength;

            Color mixed = Color.Lerp(source, shimmerColor, strength);
            mixed.a = source.a / 255f;

            return mixed;
        }

        private void RestoreOriginalMesh()
        {
            if (text == null || !hasCachedMeshInfo || cachedMeshInfo == null)
            {
                return;
            }

            TMP_TextInfo textInfo = text.textInfo;

            if (textInfo == null)
            {
                return;
            }

            for (int i = 0; i < textInfo.meshInfo.Length; i++)
            {
                if (i >= cachedMeshInfo.Length)
                {
                    continue;
                }

                TMP_MeshInfo sourceMeshInfo = cachedMeshInfo[i];
                TMP_MeshInfo targetMeshInfo = textInfo.meshInfo[i];

                if (sourceMeshInfo.vertices == null ||
                    sourceMeshInfo.colors32 == null ||
                    targetMeshInfo.vertices == null ||
                    targetMeshInfo.colors32 == null ||
                    targetMeshInfo.mesh == null)
                {
                    continue;
                }

                int vertexCount = Mathf.Min(sourceMeshInfo.vertices.Length, targetMeshInfo.vertices.Length);
                int colorCount = Mathf.Min(sourceMeshInfo.colors32.Length, targetMeshInfo.colors32.Length);

                for (int j = 0; j < vertexCount; j++)
                {
                    targetMeshInfo.vertices[j] = sourceMeshInfo.vertices[j];
                }

                for (int j = 0; j < colorCount; j++)
                {
                    targetMeshInfo.colors32[j] = sourceMeshInfo.colors32[j];
                }

                targetMeshInfo.mesh.vertices = targetMeshInfo.vertices;
                targetMeshInfo.mesh.colors32 = targetMeshInfo.colors32;

                text.UpdateGeometry(targetMeshInfo.mesh, i);
            }
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
