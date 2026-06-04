using System;
using System.Collections.Generic;
using System.IO;
using GourmetProject.Game.Platformer;
using GourmetProject.Game.Platformer.Chunks;
using GourmetProject.Game.Platformer.Monsters;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace GourmetProject.GameEditor
{
    /// <summary>
    /// 动画资源作者工具：把 *_sheet.png 切成多帧、生成 AnimationClip / AnimatorController，
    /// 并构建 Player.prefab 与 Monster_*.prefab（带 Animator），最后把旧 chunk 内的 MonsterMarker
    /// 迁移成 Monster_*.prefab 实例。菜单：Tools/光影/重建动画资源。可重复运行（覆盖同名资源）。
    /// </summary>
    public static class AnimationAuthoring
    {
        private const string SpriteRoot = "Assets/GameMain/Resources/Sprites";
        private const string AnimDir = "Assets/GameMain/Animations";
        private const string PlayerAnimDir = AnimDir + "/Player";
        private const string MonsterAnimDir = AnimDir + "/Monsters";
        private const string PrefabDir = "Assets/GameMain/Resources/Prefabs";
        private const string MonsterPrefabDir = PrefabDir + "/Monsters";
        private const string ChunkDir = "Assets/GameMain/Resources/Chunks";
        private const string MatPath = "Assets/GameMain/Art/Materials/ChunkLit.mat";

        private const float Ppu = 32f;

        [MenuItem("Tools/光影/重建动画资源")]
        public static void RebuildAnimationAssets()
        {
            EnsureFolder(AnimDir);
            EnsureFolder(PlayerAnimDir);
            EnsureFolder(MonsterAnimDir);
            EnsureFolder(PrefabDir);
            EnsureFolder(MonsterPrefabDir);

            Material litMat = LoadOrCreateLitMaterial();

            BuildPlayer(litMat);
            BuildMonsters(litMat);
            MigrateChunks();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[AnimationAuthoring] 动画资源 + 预制体重建完成。");
        }

        // ============================ 玩家 ============================

        private static void BuildPlayer(Material litMat)
        {
            const string dir = "Sprites/Characters/";
            const string assetDir = SpriteRoot + "/Characters/";

            Sprite[] idle = SliceAndLoad(assetDir + "player_idle_sheet.png", 32, 64, 4);
            Sprite[] run = SliceAndLoad(assetDir + "player_run_sheet.png", 32, 64, 6);
            Sprite[] jump = SliceAndLoad(assetDir + "player_jump_sheet.png", 32, 64, 2);
            Sprite[] wall = SliceAndLoad(assetDir + "player_wallslide_sheet.png", 32, 64, 2);
            Sprite[] lIdle = SliceAndLoad(assetDir + "player_lighter_idle_sheet.png", 32, 64, 4);
            Sprite[] lRun = SliceAndLoad(assetDir + "player_lighter_run_sheet.png", 32, 64, 6);
            _ = dir;

            var idleClip = MakeClip(PlayerAnimDir + "/Player_Idle.anim", idle, 8f, true);
            var runClip = MakeClip(PlayerAnimDir + "/Player_Run.anim", run, 10f, true);
            var jumpUpClip = MakeClip(PlayerAnimDir + "/Player_JumpUp.anim", Sub(jump, 0, 1), 1f, false);
            var jumpDownClip = MakeClip(PlayerAnimDir + "/Player_JumpDown.anim", Sub(jump, 1, 1), 1f, false);
            var wallClip = MakeClip(PlayerAnimDir + "/Player_Wallslide.anim", wall, 4f, true);
            var lIdleClip = MakeClip(PlayerAnimDir + "/Player_LighterIdle.anim", lIdle, 8f, true);
            var lRunClip = MakeClip(PlayerAnimDir + "/Player_LighterRun.anim", lRun, 10f, true);

            string ctrlPath = PlayerAnimDir + "/Player.controller";
            var ctrl = MakeController(ctrlPath);
            var sm = ctrl.layers[0].stateMachine;
            AddState(sm, PlayerAnimator.StateIdle, idleClip, isDefault: true);
            AddState(sm, PlayerAnimator.StateRun, runClip);
            AddState(sm, PlayerAnimator.StateJumpUp, jumpUpClip);
            AddState(sm, PlayerAnimator.StateJumpDown, jumpDownClip);
            AddState(sm, PlayerAnimator.StateWallslide, wallClip);
            AddState(sm, PlayerAnimator.StateLighterIdle, lIdleClip);
            AddState(sm, PlayerAnimator.StateLighterRun, lRunClip);

            // —— 组装 Player.prefab ——
            var root = new GameObject("Player");
            var sr = root.AddComponent<SpriteRenderer>();
            sr.sprite = idle.Length > 0 ? idle[0] : null;
            sr.sharedMaterial = litMat;
            sr.sortingOrder = 10;

            var rb = root.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.gravityScale = 0f;
            rb.useFullKinematicContacts = true;

            var box = root.AddComponent<BoxCollider2D>();
            box.size = new Vector2(GameConst.PlayerWidth, GameConst.PlayerHeight);

            var anim = root.AddComponent<Animator>();
            anim.runtimeAnimatorController = ctrl;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            root.AddComponent<PlayerController>();
            root.AddComponent<PlayerAnimator>();

            int playerLayer = LayerMask.NameToLayer(WorldRender.LayerPlayer);
            if (playerLayer >= 0) root.layer = playerLayer;

            PrefabUtility.SaveAsPrefabAsset(root, PrefabDir + "/Player.prefab");
            UnityEngine.Object.DestroyImmediate(root);
        }

        // ============================ 怪物 ============================

        private sealed class MonsterSpec
        {
            public MonsterKind Kind;
            public Type Type;
            public string Sheet;     // _sheet 资源文件名（不含路径/扩展名）；为空表示静态怪物
            public int Fw, Fh, Count;
            public float Fps;
            public bool IsAttract;
            public Vector2 BodyScale = Vector2.one;
            public string StaticSprite; // 静态怪物用（Vine）：Resources 路径
        }

        private static void BuildMonsters(Material litMat)
        {
            string md = SpriteRoot + "/Characters/Monsters/";

            var specs = new List<MonsterSpec>
            {
                new MonsterSpec { Kind = MonsterKind.Mosquito,   Type = typeof(Mosquito),   Sheet = "mosquito_sheet",        Fw = 16, Fh = 16, Count = 4, Fps = 12f, IsAttract = true },
                new MonsterSpec { Kind = MonsterKind.Moth,       Type = typeof(Moth),       Sheet = "moth_sheet",            Fw = 32, Fh = 32, Count = 4, Fps = 8f,  IsAttract = true,  BodyScale = new Vector2(1.3f, 1.3f) },
                new MonsterSpec { Kind = MonsterKind.LightEater, Type = typeof(LightEater), Sheet = "light_eater_sheet",     Fw = 32, Fh = 32, Count = 4, Fps = 8f,  IsAttract = true },
                new MonsterSpec { Kind = MonsterKind.LightScale, Type = typeof(LightScale), Sheet = "light_scale_sheet",     Fw = 32, Fh = 32, Count = 4, Fps = 8f,  IsAttract = true },
                new MonsterSpec { Kind = MonsterKind.Firefly,    Type = typeof(Firefly),    Sheet = "firefly_sheet",         Fw = 16, Fh = 16, Count = 4, Fps = 8f,  IsAttract = true,  BodyScale = new Vector2(0.5f, 0.5f) },
                new MonsterSpec { Kind = MonsterKind.Shadow,     Type = typeof(Shadow),     Sheet = "shadow_sheet",          Fw = 32, Fh = 64, Count = 4, Fps = 8f,  IsAttract = false },
                new MonsterSpec { Kind = MonsterKind.AmbushSpider, Type = typeof(AmbushSpider), Sheet = "ambush_spider_sheet", Fw = 32, Fh = 64, Count = 4, Fps = 6f, IsAttract = false },
                new MonsterSpec { Kind = MonsterKind.FogWraith,  Type = typeof(FogWraith),  Sheet = "mist_spirit_sheet",     Fw = 96, Fh = 96, Count = 4, Fps = 4f,  IsAttract = false, BodyScale = new Vector2(1.2f, 1.2f) },
                new MonsterSpec { Kind = MonsterKind.EchoBat,    Type = typeof(EchoBat),    Sheet = "echo_bat_sheet",        Fw = 32, Fh = 32, Count = 4, Fps = 10f, IsAttract = false },
                new MonsterSpec { Kind = MonsterKind.LightShadowBug, Type = typeof(LightShadowBug), Sheet = "light_shadow_bug_sheet", Fw = 32, Fh = 32, Count = 4, Fps = 8f, IsAttract = true },
                new MonsterSpec { Kind = MonsterKind.Vine,       Type = typeof(Vine),       Sheet = null, IsAttract = false, StaticSprite = Art.Vine },
            };

            foreach (MonsterSpec s in specs)
            {
                RuntimeAnimatorController ctrl = null;
                Sprite bodySprite = null;

                if (!string.IsNullOrEmpty(s.Sheet))
                {
                    Sprite[] frames = SliceAndLoad(md + s.Sheet + ".png", s.Fw, s.Fh, s.Count);
                    bodySprite = frames.Length > 0 ? frames[0] : null;
                    var clip = MakeClip(MonsterAnimDir + "/" + s.Kind + "_Idle.anim", frames, s.Fps, true);
                    string cp = MonsterAnimDir + "/" + s.Kind + ".controller";
                    var c = MakeController(cp);
                    AddState(c.layers[0].stateMachine, "Idle", clip, isDefault: true);
                    ctrl = c;
                }
                else if (!string.IsNullOrEmpty(s.StaticSprite))
                {
                    bodySprite = Resources.Load<Sprite>(s.StaticSprite);
                    var clip = MakeClip(MonsterAnimDir + "/" + s.Kind + "_Idle.anim",
                        bodySprite != null ? new[] { bodySprite } : Array.Empty<Sprite>(), 1f, true);
                    string cp = MonsterAnimDir + "/" + s.Kind + ".controller";
                    var c = MakeController(cp);
                    AddState(c.layers[0].stateMachine, "Idle", clip, isDefault: true);
                    ctrl = c;
                }

                BuildMonsterPrefab(s, ctrl, bodySprite, litMat);
            }

            // 石瞳特例：石化(单帧静态 closed) / 苏醒(sheet 4 帧) 两态。
            BuildStoneEye(litMat);
        }

        private static void BuildStoneEye(Material litMat)
        {
            string md = SpriteRoot + "/Characters/Monsters/";
            Sprite[] awakeFrames = SliceAndLoad(md + "stone_eye_sheet.png", 32, 64, 4);
            Sprite stoneSprite = Resources.Load<Sprite>(Art.StoneEyeClosed);

            var stoneClip = MakeClip(MonsterAnimDir + "/StoneEye_Stone.anim",
                stoneSprite != null ? new[] { stoneSprite } : awakeFrames.Length > 0 ? Sub(awakeFrames, 0, 1) : Array.Empty<Sprite>(), 1f, false);
            var awakeClip = MakeClip(MonsterAnimDir + "/StoneEye_Awake.anim", awakeFrames, 4f, true);

            string cp = MonsterAnimDir + "/StoneEye.controller";
            var ctrl = MakeController(cp);
            var sm = ctrl.layers[0].stateMachine;
            AddState(sm, StoneEye.StateStone, stoneClip, isDefault: true);
            AddState(sm, StoneEye.StateAwake, awakeClip);

            var spec = new MonsterSpec
            {
                Kind = MonsterKind.StoneEye,
                Type = typeof(StoneEye),
                IsAttract = false,
                BodyScale = Vector2.one,
            };
            Sprite body = stoneSprite != null ? stoneSprite : awakeFrames.Length > 0 ? awakeFrames[0] : null;
            BuildMonsterPrefab(spec, ctrl, body, litMat);
        }

        private static void BuildMonsterPrefab(MonsterSpec spec, RuntimeAnimatorController ctrl, Sprite bodySprite, Material litMat)
        {
            var root = new GameObject("Monster_" + spec.Kind);

            var bodyGo = new GameObject("Body");
            bodyGo.transform.SetParent(root.transform, false);
            var bsr = bodyGo.AddComponent<SpriteRenderer>();
            bsr.sprite = bodySprite;
            bsr.sharedMaterial = litMat;
            bsr.sortingOrder = 5;
            bsr.color = new Color(1f, 1f, 1f, 0f); // 默认隐于黑暗，运行时按需点亮
            bodyGo.transform.localScale = new Vector3(spec.BodyScale.x, spec.BodyScale.y, 1f);

            Animator banim = null;
            if (ctrl != null)
            {
                banim = bodyGo.AddComponent<Animator>();
                banim.runtimeAnimatorController = ctrl;
                banim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }

            var dotGo = new GameObject("Dot");
            dotGo.transform.SetParent(root.transform, false);
            var dsr = dotGo.AddComponent<SpriteRenderer>();
            dsr.sprite = Resources.Load<Sprite>(spec.IsAttract ? Art.RedDot : Art.BlueDot);
            dsr.sortingOrder = 50;
            dsr.enabled = false;

            var comp = root.AddComponent(spec.Type);

            var so = new SerializedObject(comp);
            SetRef(so, "Body", bsr);
            SetRef(so, "Dot", dsr);
            SetRef(so, "BodyAnimator", banim);
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, MonsterPrefabDir + "/Monster_" + spec.Kind + ".prefab");
            UnityEngine.Object.DestroyImmediate(root);
        }

        private static void SetRef(SerializedObject so, string field, UnityEngine.Object value)
        {
            SerializedProperty p = so.FindProperty(field);
            if (p != null) p.objectReferenceValue = value;
        }

        // ============================ chunk 迁移 ============================

        private static void MigrateChunks()
        {
            // 加载所有怪物 prefab。
            var prefabByKind = new Dictionary<MonsterKind, GameObject>();
            foreach (MonsterKind k in Enum.GetValues(typeof(MonsterKind)))
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(MonsterPrefabDir + "/Monster_" + k + ".prefab");
                if (go != null) prefabByKind[k] = go;
            }

            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { ChunkDir });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject contents = PrefabUtility.LoadPrefabContents(path);
                bool changed = false;
                try
                {
                    var markers = contents.GetComponentsInChildren<MonsterMarker>(true);
                    foreach (MonsterMarker mm in markers)
                    {
                        if (!prefabByKind.TryGetValue(mm.Kind, out GameObject src) || src == null) continue;

                        var inst = (GameObject)PrefabUtility.InstantiatePrefab(src, contents.transform);
                        inst.transform.localPosition = mm.transform.localPosition;
                        inst.transform.localRotation = mm.transform.localRotation;
                        UnityEngine.Object.DestroyImmediate(mm.gameObject);
                        changed = true;
                    }

                    if (changed)
                    {
                        PrefabUtility.SaveAsPrefabAsset(contents, path);
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }

                if (changed) Debug.Log("[AnimationAuthoring] 迁移 chunk 怪物: " + Path.GetFileName(path));
            }
        }

        // ============================ 通用工具 ============================

        private static Sprite[] SliceAndLoad(string assetPath, int fw, int fh, int count)
        {
            if (AssetDatabase.LoadMainAssetAtPath(assetPath) == null)
            {
                Debug.LogWarning("[AnimationAuthoring] 缺少图集: " + assetPath);
                return Array.Empty<Sprite>();
            }

#pragma warning disable 0618
            var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = Ppu;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;

            string baseName = Path.GetFileNameWithoutExtension(assetPath);
            var metas = new SpriteMetaData[count];
            for (int i = 0; i < count; i++)
            {
                metas[i] = new SpriteMetaData
                {
                    name = baseName + "_" + i,
                    rect = new Rect(i * fw, 0, fw, fh),
                    alignment = (int)SpriteAlignment.Center,
                    pivot = new Vector2(0.5f, 0.5f),
                };
            }
            importer.spritesheet = metas;
            EditorUtility.SetDirty(importer);
            importer.SaveAndReimport();
#pragma warning restore 0618

            UnityEngine.Object[] reps = AssetDatabase.LoadAllAssetRepresentationsAtPath(assetPath);
            var sprites = new List<Sprite>();
            foreach (UnityEngine.Object o in reps)
            {
                if (o is Sprite sp) sprites.Add(sp);
            }
            sprites.Sort((a, b) => SuffixIndex(a.name).CompareTo(SuffixIndex(b.name)));
            return sprites.ToArray();
        }

        private static int SuffixIndex(string name)
        {
            int us = name.LastIndexOf('_');
            if (us >= 0 && int.TryParse(name.Substring(us + 1), out int idx)) return idx;
            return 0;
        }

        private static Sprite[] Sub(Sprite[] src, int start, int len)
        {
            var r = new List<Sprite>();
            for (int i = start; i < start + len && i < src.Length; i++) r.Add(src[i]);
            return r.ToArray();
        }

        private static AnimationClip MakeClip(string path, Sprite[] frames, float fps, bool loop)
        {
            if (AssetDatabase.LoadAssetAtPath<AnimationClip>(path) != null) AssetDatabase.DeleteAsset(path);

            var clip = new AnimationClip { frameRate = fps <= 0f ? 1f : fps };

            if (frames != null && frames.Length > 0)
            {
                var binding = new EditorCurveBinding
                {
                    type = typeof(SpriteRenderer),
                    path = "",
                    propertyName = "m_Sprite",
                };
                float step = 1f / clip.frameRate;
                var keys = new ObjectReferenceKeyframe[frames.Length];
                for (int i = 0; i < frames.Length; i++)
                {
                    keys[i] = new ObjectReferenceKeyframe { time = i * step, value = frames[i] };
                }
                AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);
            }

            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            AssetDatabase.CreateAsset(clip, path);
            return clip;
        }

        private static AnimatorController MakeController(string path)
        {
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(path) != null) AssetDatabase.DeleteAsset(path);
            return AnimatorController.CreateAnimatorControllerAtPath(path);
        }

        private static void AddState(AnimatorStateMachine sm, string name, Motion motion, bool isDefault = false)
        {
            AnimatorState state = sm.AddState(name);
            state.motion = motion;
            state.writeDefaultValues = false;
            if (isDefault) sm.defaultState = state;
        }

        private static Material LoadOrCreateLitMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
            if (mat != null) return mat;

            EnsureFolder("Assets/GameMain/Art/Materials");
            Shader sh = Shader.Find("Universal Render Pipeline/2D/Sprite-Lit-Default");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            mat = new Material(sh) { name = "ChunkLit" };
            AssetDatabase.CreateAsset(mat, MatPath);
            return mat;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            string parent = path.Substring(0, slash);
            string leaf = path.Substring(slash + 1);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
