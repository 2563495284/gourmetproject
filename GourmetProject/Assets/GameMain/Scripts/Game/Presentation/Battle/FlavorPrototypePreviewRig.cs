using System;
using System.Collections.Generic;
using UnityEngine;

namespace GourmetProject.Game.Presentation.Battle
{
    internal enum FlavorPrototypeMode
    {
        Legacy,
        OrganicRegions,
        SegmentedRim,
        FlavorAura,
    }

    /// <summary>
    /// flavor-lab 专用隐藏实时舞台。四个相机只渲染各自的食物样机到 UI RenderTexture，
    /// 不读取对局、不进入正式食物渲染链路。
    /// </summary>
    internal sealed class FlavorPrototypePreviewRig : MonoBehaviour
    {
        private const int PreviewSize = 384;
        private const int FallbackLayer = 31;
        private const float StageSpacing = 8f;
        private const float StageOrigin = 24000f;
        private const float Seed = 17.314f;
        private const float CameraSize = 1.7f;

        private static readonly int FlavorMaskId = Shader.PropertyToID("_FlavorMask");
        private static readonly int FlavorCountId = Shader.PropertyToID("_FlavorCount");
        private static readonly int SeedId = Shader.PropertyToID("_Seed");
        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        private static readonly int AspectId = Shader.PropertyToID("_Aspect");
        private static readonly int AnimationEnabledId = Shader.PropertyToID("_AnimationEnabled");
        private static readonly int RimWidthId = Shader.PropertyToID("_RimWidth");
        private static readonly int MotionTimeId = Shader.PropertyToID("_MotionTime");

        private sealed class Stage
        {
            public Transform Root;
            public Camera Camera;
            public RenderTexture Texture;
            public SpriteRenderer Dish;
            public MaterialPropertyBlock Block;
        }

        private readonly Stage[] _stages = new Stage[4];
        private readonly List<string> _legacyFlavorIds = new(FlavorVisualCatalog.Capacity);
        private readonly ParticleSystem[] _flavorParticles = new ParticleSystem[FlavorVisualCatalog.Capacity];
        private readonly Material[] _glyphMaterials = new Material[FlavorVisualCatalog.Capacity];
        private readonly Material[] _prototypeMaterials = new Material[3];
        private int _previewLayer;
        private int _flavorMask;
        private float _intensity = 0.72f;
        private bool _paused;
        private bool _disposed;
        private float _motionTime;

        public static int ActiveRigCount { get; private set; }

        public IReadOnlyList<RenderTexture> Textures
        {
            get
            {
                var result = new RenderTexture[_stages.Length];
                for (int i = 0; i < _stages.Length; i++)
                {
                    result[i] = _stages[i]?.Texture;
                }

                return result;
            }
        }

        public int FlavorMask => _flavorMask;

        public float MotionTime => _motionTime;

        public int ActiveParticleSystemCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _flavorParticles.Length; i++)
                {
                    if (_flavorParticles[i] != null && _flavorParticles[i].gameObject.activeSelf)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public int PlayingParticleSystemCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _flavorParticles.Length; i++)
                {
                    if (_flavorParticles[i] != null && _flavorParticles[i].isPlaying)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public static FlavorPrototypePreviewRig Create()
        {
            var root = new GameObject("FlavorPrototypeLabRuntime")
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            root.transform.position = new Vector3(StageOrigin, StageOrigin, 0f);
            FlavorPrototypePreviewRig rig = root.AddComponent<FlavorPrototypePreviewRig>();
            rig.Build();
            ActiveRigCount++;
            return rig;
        }

        public void Bind(Sprite sprite, IReadOnlyList<string> flavorIds)
        {
            if (_disposed)
            {
                return;
            }

            _flavorMask = FlavorVisualCatalog.BuildMask(flavorIds);
            FlavorVisualCatalog.FlavorIdsForMask(_flavorMask, _legacyFlavorIds);
            for (int i = 0; i < _stages.Length; i++)
            {
                Stage stage = _stages[i];
                stage.Dish.sprite = sprite;
                FitSprite(stage.Dish, sprite);
            }

            ApplyLegacy();
            ApplyPrototype(1, _prototypeMaterials[0]);
            ApplyPrototype(2, _prototypeMaterials[1]);
            ApplyPrototype(3, _prototypeMaterials[2]);
            RefreshParticles(restart: true);
        }

        public void SetIntensity(float intensity)
        {
            _intensity = Mathf.Clamp01(intensity);
            for (int i = 1; i < _stages.Length; i++)
            {
                ApplyPrototype(i, _prototypeMaterials[i - 1]);
            }

            for (int i = 0; i < _glyphMaterials.Length; i++)
            {
                if (_glyphMaterials[i] != null)
                {
                    _glyphMaterials[i].SetFloat(IntensityId, Mathf.Lerp(0.45f, 1f, _intensity));
                }
            }
        }

        public void SetPaused(bool paused)
        {
            _paused = paused;
            for (int i = 1; i < _stages.Length; i++)
            {
                ApplyPrototype(i, _prototypeMaterials[i - 1]);
            }

            for (int i = 0; i < _flavorParticles.Length; i++)
            {
                ParticleSystem particles = _flavorParticles[i];
                if (particles == null || !particles.gameObject.activeSelf)
                {
                    continue;
                }

                if (paused)
                {
                    particles.Pause(true);
                }
                else
                {
                    particles.Play(true);
                }
            }
        }

        public void Burst()
        {
            for (int i = 0; i < _flavorParticles.Length; i++)
            {
                ParticleSystem particles = _flavorParticles[i];
                if (particles == null || !particles.gameObject.activeSelf)
                {
                    continue;
                }

                particles.Clear(true);
                var emit = new ParticleSystem.EmitParams
                {
                    startSize = 0.38f,
                    startLifetime = 1.1f,
                    startColor = Color.white,
                };
                particles.Emit(emit, 2);
                if (_paused)
                {
                    particles.Pause(true);
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            for (int i = 0; i < _stages.Length; i++)
            {
                Stage stage = _stages[i];
                if (stage?.Camera != null)
                {
                    stage.Camera.targetTexture = null;
                }

                if (stage?.Texture != null)
                {
                    stage.Texture.Release();
                    DestroyObject(stage.Texture);
                }
            }

            for (int i = 0; i < _prototypeMaterials.Length; i++)
            {
                DestroyObject(_prototypeMaterials[i]);
            }

            for (int i = 0; i < _glyphMaterials.Length; i++)
            {
                DestroyObject(_glyphMaterials[i]);
            }

            ActiveRigCount = Mathf.Max(0, ActiveRigCount - 1);
            DestroyObject(gameObject);
        }

        private void OnDestroy()
        {
            if (!_disposed)
            {
                _disposed = true;
                ActiveRigCount = Mathf.Max(0, ActiveRigCount - 1);
            }
        }

        private void Update()
        {
            if (_disposed || _paused)
            {
                return;
            }

            _motionTime += Time.unscaledDeltaTime;
            Stage organicStage = _stages[1];
            if (organicStage?.Dish == null)
            {
                return;
            }

            organicStage.Dish.GetPropertyBlock(organicStage.Block);
            organicStage.Block.SetFloat(MotionTimeId, _motionTime);
            organicStage.Dish.SetPropertyBlock(organicStage.Block);
        }

        private void Build()
        {
            _previewLayer = LayerMask.NameToLayer("DishIconPreview");
            if (_previewLayer < 0)
            {
                _previewLayer = FallbackLayer;
            }

            gameObject.layer = _previewLayer;
            BuildMaterials();
            for (int i = 0; i < _stages.Length; i++)
            {
                _stages[i] = BuildStage(i, (FlavorPrototypeMode)i);
            }

            BuildParticleField(_stages[3].Root);
        }

        private void BuildMaterials()
        {
            _prototypeMaterials[0] = CreateMaterial("Shaders/FlavorOrganicRegions", "GourmetProject/FlavorOrganicRegions");
            _prototypeMaterials[1] = CreateMaterial("Shaders/FlavorSegmentedRim", "GourmetProject/FlavorSegmentedRim");
            _prototypeMaterials[2] = CreateMaterial("Shaders/FlavorAura", "GourmetProject/FlavorAura");
        }

        private Stage BuildStage(int index, FlavorPrototypeMode mode)
        {
            var stageObject = new GameObject($"Stage_{mode}")
            {
                hideFlags = HideFlags.HideAndDontSave,
                layer = _previewLayer,
            };
            stageObject.transform.SetParent(transform, false);
            stageObject.transform.localPosition = new Vector3(index * StageSpacing, 0f, 0f);

            var dishObject = new GameObject("Dish")
            {
                hideFlags = HideFlags.HideAndDontSave,
                layer = _previewLayer,
            };
            dishObject.transform.SetParent(stageObject.transform, false);
            SpriteRenderer dish = dishObject.AddComponent<SpriteRenderer>();
            SpriteRenderStyle.ApplyUnlitMaterial(dish);

            var cameraObject = new GameObject("Camera")
            {
                hideFlags = HideFlags.HideAndDontSave,
                layer = _previewLayer,
            };
            cameraObject.transform.SetParent(stageObject.transform, false);
            cameraObject.transform.localPosition = new Vector3(0f, 0f, -10f);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = CameraSize;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 30f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear;
            camera.allowHDR = false;
            camera.allowMSAA = true;
            camera.cullingMask = 1 << _previewLayer;
            camera.depth = -100 + index;

            // URP 2D 在 MSAA 目标上需要真实的深度/模板附件；无深度 RT 会被判定为 fake surface。
            var texture = new RenderTexture(PreviewSize, PreviewSize, 24, RenderTextureFormat.ARGB32)
            {
                name = $"FlavorLab_{mode}",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                antiAliasing = 4,
                hideFlags = HideFlags.HideAndDontSave,
            };
            texture.Create();
            camera.targetTexture = texture;

            return new Stage
            {
                Root = stageObject.transform,
                Camera = camera,
                Texture = texture,
                Dish = dish,
                Block = new MaterialPropertyBlock(),
            };
        }

        private void BuildParticleField(Transform parent)
        {
            Shader glyphShader = Resources.Load<Shader>("Shaders/FlavorGlyphParticle")
                                 ?? Shader.Find("GourmetProject/FlavorGlyphParticle");
            if (glyphShader == null)
            {
                Debug.LogError("Flavor lab is missing FlavorGlyphParticle shader.", this);
                return;
            }

            IReadOnlyList<FlavorVisualDescriptor> descriptors = FlavorVisualCatalog.Descriptors;
            for (int i = 0; i < descriptors.Count; i++)
            {
                FlavorVisualDescriptor descriptor = descriptors[i];
                var material = new Material(glyphShader)
                {
                    name = $"FlavorLabGlyph_{descriptor.Kind}",
                    hideFlags = HideFlags.HideAndDontSave,
                };
                material.SetColor("_FlavorColor", descriptor.Color);
                material.SetFloat("_FlavorIndex", (int)descriptor.Kind);
                material.SetFloat(IntensityId, 1f);
                _glyphMaterials[i] = material;

                var go = new GameObject($"Particles_{descriptor.Kind}")
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    layer = _previewLayer,
                };
                go.transform.SetParent(parent, false);
                ParticleSystem particles = go.AddComponent<ParticleSystem>();
                ConfigureParticleSystem(particles, i, material);
                go.SetActive(false);
                _flavorParticles[i] = particles;
            }
        }

        private static void ConfigureParticleSystem(ParticleSystem particles, int index, Material material)
        {
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = particles.main;
            main.loop = true;
            main.playOnAwake = false;
            main.duration = 2.6f;
            main.startLifetime = 2.8f;
            main.startSpeed = 0f;
            main.startSize = 0.27f;
            main.maxParticles = 2;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.enabled = true;
            emission.rateOverTime = 0.72f;

            ParticleSystem.ShapeModule shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 1.06f + index * 0.025f;
            shape.radiusThickness = 0f;
            shape.arcMode = ParticleSystemShapeMultiModeValue.Random;

            ParticleSystem.VelocityOverLifetimeModule velocity = particles.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.orbitalZ = new ParticleSystem.MinMaxCurve(index % 2 == 0 ? 0.85f : -0.85f);
            velocity.radial = new ParticleSystem.MinMaxCurve(0.015f);

            ParticleSystem.ColorOverLifetimeModule colorOverLifetime = particles.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 1f),
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.16f),
                    new GradientAlphaKey(0.85f, 0.72f),
                    new GradientAlphaKey(0f, 1f),
                });
            colorOverLifetime.color = gradient;

            ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = material;
            renderer.sortingOrder = 20 + index;
        }

        private void ApplyLegacy()
        {
            Stage stage = _stages[0];
            var settings = new FlavorStainPalette.Settings(8f, 0.62f, 0.12f, 0.12f, Seed);
            FlavorStainPalette.ApplyToSpriteRenderer(stage.Dish, _legacyFlavorIds, ref stage.Block, settings);
        }

        private void ApplyPrototype(int stageIndex, Material material)
        {
            Stage stage = _stages[stageIndex];
            if (stage == null || stage.Dish == null)
            {
                return;
            }

            if (material == null || _flavorMask == 0)
            {
                stage.Dish.SetPropertyBlock(null);
                SpriteRenderStyle.ApplyUnlitMaterial(stage.Dish);
                return;
            }

            stage.Dish.sharedMaterial = material;
            stage.Dish.GetPropertyBlock(stage.Block);
            stage.Block.SetFloat(FlavorMaskId, _flavorMask);
            stage.Block.SetFloat(FlavorCountId, FlavorVisualCatalog.CountBits(_flavorMask));
            stage.Block.SetFloat(SeedId, Seed);
            stage.Block.SetFloat(IntensityId, _intensity);
            stage.Block.SetFloat(AspectId, SpriteAspect(stage.Dish.sprite));
            stage.Block.SetFloat(AnimationEnabledId, _paused ? 0f : 1f);
            stage.Block.SetFloat(MotionTimeId, _motionTime);
            if (stageIndex == 2)
            {
                stage.Block.SetFloat(RimWidthId, 18f);
            }
            stage.Dish.SetPropertyBlock(stage.Block);
        }

        private void RefreshParticles(bool restart)
        {
            for (int i = 0; i < _flavorParticles.Length; i++)
            {
                ParticleSystem particles = _flavorParticles[i];
                if (particles == null)
                {
                    continue;
                }

                bool active = (_flavorMask & (1 << i)) != 0;
                particles.gameObject.SetActive(active);
                if (!active)
                {
                    particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    continue;
                }

                if (restart)
                {
                    particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    particles.randomSeed = (uint)(1009 + i * 977);
                    particles.Simulate(0.7f + i * 0.11f, true, false, true);
                    particles.Play(true);
                }

                if (_paused)
                {
                    particles.Pause(true);
                }
            }
        }

        private static void FitSprite(SpriteRenderer renderer, Sprite sprite)
        {
            if (renderer == null)
            {
                return;
            }

            if (sprite == null)
            {
                renderer.transform.localScale = Vector3.one;
                return;
            }

            Vector2 size = sprite.bounds.size;
            float largest = Mathf.Max(size.x, size.y, 0.001f);
            float scale = 2.32f / largest;
            renderer.transform.localScale = Vector3.one * scale;
        }

        private static float SpriteAspect(Sprite sprite)
        {
            if (sprite == null || sprite.bounds.size.y <= 0.0001f)
            {
                return 1f;
            }

            return sprite.bounds.size.x / sprite.bounds.size.y;
        }

        private static Material CreateMaterial(string resourcePath, string shaderName)
        {
            Shader shader = Resources.Load<Shader>(resourcePath) ?? Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogError($"Flavor lab is missing shader '{shaderName}'.");
                return null;
            }

            return new Material(shader)
            {
                name = $"Runtime{shader.name.Replace('/', '_')}",
                hideFlags = HideFlags.HideAndDontSave,
            };
        }

        private static void DestroyObject(UnityEngine.Object value)
        {
            if (value == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(value);
            }
            else
            {
                DestroyImmediate(value);
            }
        }
    }
}
