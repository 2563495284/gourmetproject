using System;
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

        private readonly SoundComponent _sound;

        public AudioService(SoundComponent sound)
        {
            _sound = sound ?? throw new ArgumentNullException(nameof(sound));
        }

        public int PlaySound(string soundAssetName)
        {
            return _sound.PlaySound(soundAssetName, GroupSound);
        }

        public int PlayMusic(string musicAssetName)
        {
            return _sound.PlaySound(musicAssetName, GroupMusic);
        }

        public bool Stop(int serialId)
        {
            return _sound.StopSound(serialId);
        }
    }
}
