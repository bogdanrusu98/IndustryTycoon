using IndustryTycoon.Persistence;
using IndustryTycoon.Player;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace IndustryTycoon.UI
{
    /// <summary>Single owner for modal navigation and Android Back semantics.</summary>
    public sealed class MobilePanelNavigation : MonoBehaviour
    {
        [SerializeField] private GameObject mainMenu;
        [SerializeField] private GameObject pauseMenu;
        [SerializeField] private GameObject settingsPanel;
        [SerializeField] private GameObject resetConfirmation;
        [SerializeField] private GameObject helpPanel;
        [SerializeField] private GameObject[] contentPanels;
        [SerializeField] private Button[] contentButtons;
        [SerializeField] private Button pauseButton;
        [SerializeField] private Button continueButton;
        [SerializeField] private Button resumeButton;
        [SerializeField] private Button mainSettingsButton;
        [SerializeField] private Button pauseSettingsButton;
        [SerializeField] private Button helpButton;
        [SerializeField] private Button newGameButton;
        [SerializeField] private Button confirmResetButton;
        [SerializeField] private Button cancelResetButton;
        [SerializeField] private Button[] closeButtons;
        [SerializeField] private LocalPersistenceService persistence;
        [SerializeField] private PlayerMovement playerMovement;
        [SerializeField] private PlayerDragInput playerDragInput;
        [SerializeField] private GameObject[] explorationHud;
        [SerializeField] private GameObject navigationRoot;

        private bool _movementWasEnabled;
        private bool _dragWasEnabled;
        private bool _inputCaptured;
        private GameObject _settingsReturnPanel;
        private UnityAction[] _contentActions;
        private UnityAction _mainSettingsAction;
        private UnityAction _pauseSettingsAction;

        public bool HasBlockingPanel => FindTopPanel() != null;

        private void OnEnable()
        {
            if (contentButtons != null)
            {
                if (_contentActions == null || _contentActions.Length != contentButtons.Length)
                    _contentActions = new UnityAction[contentButtons.Length];
                for (int i = 0; i < contentButtons.Length; i++)
                {
                    int index = i;
                    _contentActions[i] = () => ToggleContent(index);
                    contentButtons[i]?.onClick.AddListener(_contentActions[i]);
                }
            }
            pauseButton?.onClick.AddListener(OpenPause);
            continueButton?.onClick.AddListener(CloseAll);
            resumeButton?.onClick.AddListener(CloseAll);
            _mainSettingsAction = () => OpenSettings(mainMenu);
            _pauseSettingsAction = () => OpenSettings(pauseMenu);
            mainSettingsButton?.onClick.AddListener(_mainSettingsAction);
            pauseSettingsButton?.onClick.AddListener(_pauseSettingsAction);
            helpButton?.onClick.AddListener(OpenHelp);
            newGameButton?.onClick.AddListener(OpenResetConfirmation);
            confirmResetButton?.onClick.AddListener(ConfirmReset);
            cancelResetButton?.onClick.AddListener(CancelReset);
            if (closeButtons != null)
                for (int i = 0; i < closeButtons.Length; i++)
                    closeButtons[i]?.onClick.AddListener(Back);

            if (Application.isBatchMode) CloseAll();
            else SetExclusive(mainMenu);
        }

        private void OnDisable()
        {
            if (contentButtons != null && _contentActions != null)
                for (int i = 0; i < contentButtons.Length && i < _contentActions.Length; i++)
                    if (_contentActions[i] != null)
                        contentButtons[i]?.onClick.RemoveListener(_contentActions[i]);
            pauseButton?.onClick.RemoveListener(OpenPause);
            continueButton?.onClick.RemoveListener(CloseAll);
            resumeButton?.onClick.RemoveListener(CloseAll);
            if (_mainSettingsAction != null)
                mainSettingsButton?.onClick.RemoveListener(_mainSettingsAction);
            if (_pauseSettingsAction != null)
                pauseSettingsButton?.onClick.RemoveListener(_pauseSettingsAction);
            helpButton?.onClick.RemoveListener(OpenHelp);
            newGameButton?.onClick.RemoveListener(OpenResetConfirmation);
            confirmResetButton?.onClick.RemoveListener(ConfirmReset);
            cancelResetButton?.onClick.RemoveListener(CancelReset);
            if (closeButtons != null)
                for (int i = 0; i < closeButtons.Length; i++)
                    closeButtons[i]?.onClick.RemoveListener(Back);
            RestoreInput();
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                Back();
        }

        public void ToggleContent(int index)
        {
            if (contentPanels == null || index < 0 || index >= contentPanels.Length) return;
            GameObject target = contentPanels[index];
            if (target != null && target.activeSelf) CloseAll();
            else SetExclusive(target);
        }

        public void OpenPause() => SetExclusive(pauseMenu);
        public void OpenHelp() => SetExclusive(helpPanel);

        public void CloseAll()
        {
            SetExclusive(null);
            Time.timeScale = 1f;
        }

        public void Back()
        {
            if (resetConfirmation != null && resetConfirmation.activeSelf)
            {
                CancelReset();
                return;
            }
            if (settingsPanel != null && settingsPanel.activeSelf)
            {
                SetExclusive(_settingsReturnPanel != null ? _settingsReturnPanel : pauseMenu);
                return;
            }
            if (mainMenu != null && mainMenu.activeSelf)
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                Application.Quit();
#endif
                return;
            }
            if (FindTopPanel() != null)
            {
                CloseAll();
                return;
            }
            OpenPause();
        }

        private void OpenSettings(GameObject returnPanel)
        {
            _settingsReturnPanel = returnPanel;
            SetExclusive(settingsPanel);
        }

        private void OpenResetConfirmation()
        {
            if (resetConfirmation != null) resetConfirmation.SetActive(true);
        }

        private void CancelReset()
        {
            if (resetConfirmation != null) resetConfirmation.SetActive(false);
        }

        private void ConfirmReset()
        {
            if (persistence != null && persistence.ResetSaveAndReload())
            {
                CancelReset();
                SetExclusive(mainMenu);
            }
        }

        private void SetExclusive(GameObject target)
        {
            SetActive(mainMenu, target == mainMenu);
            SetActive(pauseMenu, target == pauseMenu);
            SetActive(settingsPanel, target == settingsPanel);
            SetActive(helpPanel, target == helpPanel);
            if (contentPanels != null)
                for (int i = 0; i < contentPanels.Length; i++)
                    SetActive(contentPanels[i], contentPanels[i] == target);
            if (resetConfirmation != null && target != mainMenu)
                resetConfirmation.SetActive(false);

            bool menu = target == mainMenu || target == pauseMenu || target == settingsPanel || target == helpPanel;
            if (explorationHud != null)
                foreach (GameObject item in explorationHud) SetActive(item, !menu);
            SetActive(navigationRoot, !menu);
            if (contentButtons != null && contentPanels != null)
                for (int i = 0; i < contentButtons.Length && i < contentPanels.Length; i++)
                {
                    Image surface = contentButtons[i] != null ? contentButtons[i].GetComponent<Image>() : null;
                    if (surface != null) surface.color = target == contentPanels[i]
                        ? MobileUiTheme.TealDark : MobileUiTheme.Navigation;
                }

            if (target != null) CaptureInput();
            else RestoreInput();
            Time.timeScale = target == mainMenu || target == pauseMenu || target == settingsPanel
                ? 0f
                : 1f;
        }

        private GameObject FindTopPanel()
        {
            if (settingsPanel != null && settingsPanel.activeSelf) return settingsPanel;
            if (helpPanel != null && helpPanel.activeSelf) return helpPanel;
            if (pauseMenu != null && pauseMenu.activeSelf) return pauseMenu;
            if (mainMenu != null && mainMenu.activeSelf) return mainMenu;
            if (contentPanels != null)
                for (int i = 0; i < contentPanels.Length; i++)
                    if (contentPanels[i] != null && contentPanels[i].activeSelf) return contentPanels[i];
            return null;
        }

        private void CaptureInput()
        {
            if (_inputCaptured) return;
            _movementWasEnabled = playerMovement != null && playerMovement.enabled;
            _dragWasEnabled = playerDragInput != null && playerDragInput.enabled;
            if (playerMovement != null) playerMovement.enabled = false;
            if (playerDragInput != null) playerDragInput.enabled = false;
            _inputCaptured = true;
        }

        private void RestoreInput()
        {
            if (!_inputCaptured) return;
            if (playerMovement != null) playerMovement.enabled = _movementWasEnabled;
            if (playerDragInput != null) playerDragInput.enabled = _dragWasEnabled;
            _inputCaptured = false;
        }

        private static void SetActive(GameObject panel, bool active)
        {
            if (panel != null && panel.activeSelf != active) panel.SetActive(active);
        }
    }
}
