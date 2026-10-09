using System;
using IndustryTycoon.Feedback;
using UnityEngine;
using UnityEngine.UI;

namespace IndustryTycoon.UI
{
    /// <summary>
    /// Player-facing preferences only. These values intentionally live outside
    /// the gameplay save so Reset Save cannot unexpectedly reset device choices.
    /// </summary>
    public sealed class MobileSettingsService : MonoBehaviour
    {
        private const string Prefix = "industry_tycoon.settings.";
        private const string MasterKey = Prefix + "master";
        private const string MusicKey = Prefix + "music";
        private const string SfxKey = Prefix + "sfx";
        private const string HapticsKey = Prefix + "haptics";
        private const string GraphicsKey = Prefix + "graphics";

        [SerializeField] private Slider masterSlider;
        [SerializeField] private Slider musicSlider;
        [SerializeField] private Slider sfxSlider;
        [SerializeField] private Toggle hapticsToggle;
        [SerializeField] private Button graphicsButton;
        [SerializeField] private Text graphicsLabel;
        [SerializeField] private AudioSource musicSource;
        [SerializeField] private AudioFeedback audioFeedback;
        [SerializeField] private HapticFeedback hapticFeedback;

        private float _master = 1f;
        private float _music = 0.8f;
        private float _sfx = 1f;
        private bool _haptics = true;
        private int _graphicsPreset = 1;

        public float MasterVolume => _master;
        public float MusicVolume => _music;
        public float SfxVolume => _sfx;
        public bool HapticsEnabled => _haptics;
        public int GraphicsPreset => _graphicsPreset;

        private void Awake()
        {
            Load();
            SynchronizeControls();
            Apply();
        }

        private void OnEnable()
        {
            masterSlider?.onValueChanged.AddListener(SetMaster);
            musicSlider?.onValueChanged.AddListener(SetMusic);
            sfxSlider?.onValueChanged.AddListener(SetSfx);
            hapticsToggle?.onValueChanged.AddListener(SetHaptics);
            graphicsButton?.onClick.AddListener(CycleGraphicsPreset);
            SynchronizeControls();
        }

        private void OnDisable()
        {
            masterSlider?.onValueChanged.RemoveListener(SetMaster);
            musicSlider?.onValueChanged.RemoveListener(SetMusic);
            sfxSlider?.onValueChanged.RemoveListener(SetSfx);
            hapticsToggle?.onValueChanged.RemoveListener(SetHaptics);
            graphicsButton?.onClick.RemoveListener(CycleGraphicsPreset);
        }

        private void Load()
        {
            _master = Mathf.Clamp01(PlayerPrefs.GetFloat(MasterKey, 1f));
            _music = Mathf.Clamp01(PlayerPrefs.GetFloat(MusicKey, 0.8f));
            _sfx = Mathf.Clamp01(PlayerPrefs.GetFloat(SfxKey, 1f));
            _haptics = PlayerPrefs.GetInt(HapticsKey, 1) != 0;
            _graphicsPreset = Mathf.Clamp(PlayerPrefs.GetInt(GraphicsKey, 1), 0, 1);
        }

        private void SetMaster(float value) { _master = Mathf.Clamp01(value); SaveAndApply(); }
        private void SetMusic(float value) { _music = Mathf.Clamp01(value); SaveAndApply(); }
        private void SetSfx(float value) { _sfx = Mathf.Clamp01(value); SaveAndApply(); }
        private void SetHaptics(bool value) { _haptics = value; SaveAndApply(); }

        private void CycleGraphicsPreset()
        {
            _graphicsPreset = (_graphicsPreset + 1) % 2;
            SaveAndApply();
            RefreshGraphicsLabel();
        }

        private void SaveAndApply()
        {
            PlayerPrefs.SetFloat(MasterKey, _master);
            PlayerPrefs.SetFloat(MusicKey, _music);
            PlayerPrefs.SetFloat(SfxKey, _sfx);
            PlayerPrefs.SetInt(HapticsKey, _haptics ? 1 : 0);
            PlayerPrefs.SetInt(GraphicsKey, _graphicsPreset);
            PlayerPrefs.Save();
            Apply();
        }

        private void Apply()
        {
            AudioListener.volume = _master;
            if (musicSource != null) musicSource.volume = _music;
            if (audioFeedback != null) audioFeedback.OutputVolume = _sfx;
            if (hapticFeedback != null) hapticFeedback.Enabled = _haptics;

            bool low = _graphicsPreset == 0;
            Application.targetFrameRate = low ? 30 : 60;
            QualitySettings.shadows = low ? ShadowQuality.Disable : ShadowQuality.All;
            QualitySettings.shadowDistance = low ? 0f : 35f;
        }

        private void SynchronizeControls()
        {
            if (masterSlider != null) masterSlider.SetValueWithoutNotify(_master);
            if (musicSlider != null) musicSlider.SetValueWithoutNotify(_music);
            if (sfxSlider != null) sfxSlider.SetValueWithoutNotify(_sfx);
            if (hapticsToggle != null) hapticsToggle.SetIsOnWithoutNotify(_haptics);
            RefreshGraphicsLabel();
        }

        private void RefreshGraphicsLabel()
        {
            if (graphicsLabel != null)
                graphicsLabel.text = _graphicsPreset == 0 ? "GRAPHICS: BATTERY" : "GRAPHICS: BALANCED";
        }
    }
}
