using System;
using GameFramework.Sound;
using UnityEngine;
using UnityGameFramework.Runtime;

namespace GourmetProject.Runtime.Audio
{
    /// <summary>
    /// 音频封装（开箱即用）。约定两个声音组：背景音乐 Music 与音效 Sound。
    /// 注意：需在 SoundComponent 上配置同名声音组后才能真正播放；这里只提供统一调用入口，
    /// 与玩法无关。
    /// </summary>
    public sealed class AudioService
    {
        public const string GroupMusic = "Music";
        public const string GroupSound = "Sound";

        private const string SoundRoot = "Assets/GameMain/Content/Resources/Sounds/";
        private const string ResourceSoundRoot = "Sounds/";
        private const string ButtonSound = SoundRoot + "button.ogg";
        private const string CancelSound = SoundRoot + "cancel.ogg";
        private const string PlacementSound = SoundRoot + "Thud.wav";
        private const string PickupSound = SoundRoot + "Pop.wav";
        private const string LossFanfareSound = SoundRoot + "Fanfare_loss.mp3";
        private const string SingleSettlementHitSound = SoundRoot + "multhit1.ogg";
        private const string MultipleSettlementHitSound = SoundRoot + "multhit2.ogg";

        private static readonly string[] CoinSounds =
        {
            SoundRoot + "coin1.ogg",
            SoundRoot + "coin2.ogg",
            SoundRoot + "coin3.ogg",
            SoundRoot + "coin4.ogg",
            SoundRoot + "coin5.ogg",
            SoundRoot + "coin6.ogg",
            SoundRoot + "coin7.ogg",
        };

        private static readonly string[] BattleMusic =
        {
            SoundRoot + "甜品店bgm.ogg",
            SoundRoot + "甜品店bgm2.ogg",
            SoundRoot + "甜品店bgm3.ogg",
            SoundRoot + "甜品店bgm4.ogg",
        };

        private readonly SoundComponent _sound;
        private readonly Action _prepareForPlayback;
        private readonly System.Random _random = new System.Random();
        private AudioSource _settlementHitSource;
        private AudioClip _singleSettlementHitClip;
        private AudioClip _multipleSettlementHitClip;
        private int _lastBattleMusicIndex = -1;

        public AudioService(SoundComponent sound, Action prepareForPlayback = null)
        {
            _sound = sound ?? throw new ArgumentNullException(nameof(sound));
            _prepareForPlayback = prepareForPlayback;
            PreloadSettlementHits();
        }

        public int PlaySound(string soundAssetName)
        {
            _prepareForPlayback?.Invoke();
            return _sound.PlaySound(soundAssetName, GroupSound);
        }

        public int PlayMusic(string musicAssetName)
        {
            _prepareForPlayback?.Invoke();
            return _sound.PlaySound(musicAssetName, GroupMusic);
        }

        public int PlayButtonClick()
        {
            return PlaySound(ButtonSound);
        }

        public int PlayCancelClick()
        {
            return PlaySound(CancelSound);
        }

        public int PlayPlacement()
        {
            return PlaySound(PlacementSound);
        }

        public int PlayPickup()
        {
            return PlaySound(PickupSound);
        }

        public int PlayLossFanfare()
        {
            return PlaySound(LossFanfareSound);
        }

        public int PlayRandomCoin()
        {
            return PlaySound(CoinSounds[_random.Next(CoinSounds.Length)]);
        }

        /// <summary>播放一次结算命中音效；同一批命中多个食物时使用更强的版本。</summary>
        public void PlaySettlementHit(int simultaneousTargetCount, float pitch = 1f)
        {
            if (simultaneousTargetCount <= 0)
            {
                return;
            }

            _prepareForPlayback?.Invoke();
            AudioClip clip = simultaneousTargetCount > 1
                ? _multipleSettlementHitClip
                : _singleSettlementHitClip;
            if (_settlementHitSource == null || clip == null)
            {
                _sound.PlaySound(
                    simultaneousTargetCount > 1
                        ? MultipleSettlementHitSound
                        : SingleSettlementHitSound,
                    GroupSound);
                return;
            }

            ISoundGroup soundGroup = _sound.GetSoundGroup(GroupSound);
            _settlementHitSource.mute = soundGroup?.Mute ?? false;
            _settlementHitSource.volume = soundGroup?.Volume ?? 1f;
            _settlementHitSource.pitch = Mathf.Clamp(pitch, 0.75f, 1.35f);
            _settlementHitSource.PlayOneShot(clip);
        }

        private void PreloadSettlementHits()
        {
            const string playerName = "SettlementHitAudioPlayer";
            Transform playerTransform = _sound.transform.Find(playerName);
            GameObject player = playerTransform != null
                ? playerTransform.gameObject
                : new GameObject(playerName);
            if (playerTransform == null)
            {
                player.transform.SetParent(_sound.transform, false);
            }

            AudioSource existingSource = player.GetComponent<AudioSource>();
            _settlementHitSource = existingSource != null
                ? existingSource
                : player.AddComponent<AudioSource>();
            _settlementHitSource.playOnAwake = false;
            _settlementHitSource.loop = false;
            _settlementHitSource.spatialBlend = 0f;

            _singleSettlementHitClip = Resources.Load<AudioClip>(ResourceSoundRoot + "multhit1");
            _multipleSettlementHitClip = Resources.Load<AudioClip>(ResourceSoundRoot + "multhit2");
            _singleSettlementHitClip?.LoadAudioData();
            _multipleSettlementHitClip?.LoadAudioData();
        }

        /// <summary>随机播放一首经营挑战音乐；连续两场不会选择同一首。</summary>
        public int PlayRandomBattleMusic(float fadeInSeconds = 0.35f)
        {
            int index;
            if (_lastBattleMusicIndex < 0)
            {
                index = _random.Next(BattleMusic.Length);
            }
            else
            {
                index = _random.Next(BattleMusic.Length - 1);
                if (index >= _lastBattleMusicIndex)
                {
                    index++;
                }
            }

            _lastBattleMusicIndex = index;
            var playParams = PlaySoundParams.Create();
            playParams.Loop = true;
            playParams.FadeInSeconds = Math.Max(0f, fadeInSeconds);
            playParams.SpatialBlend = 0f;

            _prepareForPlayback?.Invoke();
            return _sound.PlaySound(BattleMusic[index], GroupMusic, playParams);
        }

        public bool Stop(int serialId)
        {
            return _sound.StopSound(serialId);
        }

        public bool Stop(int serialId, float fadeOutSeconds)
        {
            return _sound.StopSound(serialId, Math.Max(0f, fadeOutSeconds));
        }
    }
}
