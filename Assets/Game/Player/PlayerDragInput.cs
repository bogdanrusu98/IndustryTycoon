using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace IndustryTycoon.Player
{
    /// <summary>
    /// Converts a primary pointer drag into a floating virtual joystick value.
    /// Pointer input covers both the first touchscreen contact and the Editor mouse.
    /// </summary>
    public sealed class PlayerDragInput : MonoBehaviour
    {
        [Header("Virtual Joystick")]
        [SerializeField, Min(1f)] private float joystickRadiusPixels = 180f;
        [SerializeField, Min(0.1f)] private float dragSensitivity = 1f;
        [SerializeField, Range(0f, 0.5f)] private float deadZone = 0.05f;

        private InputAction _mousePressAction;
        private InputAction _touchPressAction;
        private InputAction _positionAction;
        private InputControl _activePressControl;
        private Vector2Control _activePositionControl;
        private Vector2 _dragOrigin;
        private Vector2 _lastPointerPosition;
        private Vector2 _latestMousePosition;
        private Vector2 _lastDragInput;
        private Vector2 _releasedDragInput;
        private bool _isPressed;
        private bool _hasReleasedDragSample;
        private bool _hasReadDuringCurrentPress;
        private bool _hasMousePosition;

        public Vector2 ReadMoveInput()
        {
            if (_isPressed && _activePositionControl != null)
            {
                _lastPointerPosition = _activePositionControl.ReadValue();
                _lastDragInput = CalculateDragInput(_lastPointerPosition);
                if (_lastDragInput.sqrMagnitude > 0f)
                {
                    _hasReadDuringCurrentPress = true;
                }

                return _lastDragInput;
            }

            if (_hasReleasedDragSample)
            {
                _hasReleasedDragSample = false;
                return _releasedDragInput;
            }

            return Vector2.zero;
        }

        private void OnEnable()
        {
            EnsureActions();
            _positionAction.Enable();
            _mousePressAction.Enable();
            _touchPressAction.Enable();
        }

        private void OnDisable()
        {
            _mousePressAction?.Disable();
            _touchPressAction?.Disable();
            _positionAction?.Disable();
            ResetDrag();
        }

        private void OnDestroy()
        {
            if (_mousePressAction == null)
            {
                return;
            }

            _mousePressAction.started -= OnMousePressStarted;
            _mousePressAction.canceled -= OnMousePressCanceled;
            _mousePressAction.Dispose();
            _touchPressAction.performed -= OnTouchPressChanged;
            _touchPressAction.Dispose();
            _positionAction.performed -= OnPointerPosition;
            _positionAction.Dispose();
        }

        private void OnValidate()
        {
            joystickRadiusPixels = Mathf.Max(1f, joystickRadiusPixels);
            dragSensitivity = Mathf.Max(0.1f, dragSensitivity);
        }

        private void EnsureActions()
        {
            if (_mousePressAction != null)
            {
                return;
            }

            _mousePressAction = new InputAction(
                "Move Mouse Press",
                InputActionType.Button,
                "<Mouse>/leftButton");
            _mousePressAction.started += OnMousePressStarted;
            _mousePressAction.canceled += OnMousePressCanceled;

            _touchPressAction = new InputAction(
                "Move Touch Press",
                InputActionType.PassThrough,
                "<Touchscreen>/touch*/press");
            _touchPressAction.performed += OnTouchPressChanged;

            _positionAction = new InputAction("Move Pointer Position", InputActionType.PassThrough);
            _positionAction.AddBinding("<Mouse>/position");
            _positionAction.AddBinding("<Touchscreen>/touch*/position");
            _positionAction.performed += OnPointerPosition;
        }

        private void OnMousePressStarted(InputAction.CallbackContext context)
        {
            BeginDrag(context.control);
        }

        private void OnMousePressCanceled(InputAction.CallbackContext context)
        {
            EndDrag(context.control);
        }

        private void OnTouchPressChanged(InputAction.CallbackContext context)
        {
            if (context.ReadValueAsButton())
            {
                BeginDrag(context.control);
                return;
            }

            EndDrag(context.control);
        }

        private void BeginDrag(InputControl pressControl)
        {
            if (_isPressed || !TryGetPositionControl(pressControl, out Vector2Control positionControl))
            {
                return;
            }

            _activePressControl = pressControl;
            _activePositionControl = positionControl;
            _lastPointerPosition = positionControl.ReadValue();
            if (pressControl.device is Mouse && _lastPointerPosition == Vector2.zero && _hasMousePosition)
            {
                _lastPointerPosition = _latestMousePosition;
            }

            _dragOrigin = pressControl.parent is TouchControl touch
                ? touch.startPosition.ReadValue()
                : _lastPointerPosition;
            _lastDragInput = Vector2.zero;
            _releasedDragInput = Vector2.zero;
            _hasReleasedDragSample = false;
            _hasReadDuringCurrentPress = false;
            _isPressed = true;
        }

        private void EndDrag(InputControl pressControl)
        {
            if (!_isPressed || pressControl != _activePressControl)
            {
                return;
            }

            if (_activePositionControl != null)
            {
                _lastPointerPosition = _activePositionControl.ReadValue();
            }

            _releasedDragInput = CalculateDragInput(_lastPointerPosition);
            _hasReleasedDragSample = !_hasReadDuringCurrentPress && _releasedDragInput.sqrMagnitude > 0f;
            _activePressControl = null;
            _activePositionControl = null;
            _isPressed = false;
            _hasReadDuringCurrentPress = false;
            _lastDragInput = Vector2.zero;
        }

        private void OnPointerPosition(InputAction.CallbackContext context)
        {
            if (context.control is not Vector2Control positionControl)
            {
                return;
            }

            Vector2 pointerPosition = context.ReadValue<Vector2>();
            if (positionControl.device is Mouse)
            {
                _latestMousePosition = pointerPosition;
                _hasMousePosition = true;
            }

            if (!_isPressed || positionControl != _activePositionControl)
            {
                return;
            }

            _lastPointerPosition = pointerPosition;
            _lastDragInput = CalculateDragInput(pointerPosition);
        }

        private static bool TryGetPositionControl(InputControl pressControl, out Vector2Control positionControl)
        {
            if (pressControl.parent is TouchControl touch)
            {
                positionControl = touch.position;
                return true;
            }

            if (pressControl.device is Mouse mouse)
            {
                positionControl = mouse.position;
                return true;
            }

            positionControl = null;
            return false;
        }

        private void ResetDrag()
        {
            _activePressControl = null;
            _activePositionControl = null;
            _isPressed = false;
            _lastPointerPosition = Vector2.zero;
            _lastDragInput = Vector2.zero;
            _releasedDragInput = Vector2.zero;
            _hasReleasedDragSample = false;
            _hasReadDuringCurrentPress = false;
        }

        private Vector2 CalculateDragInput(Vector2 pointerPosition)
        {
            float effectiveRadius = joystickRadiusPixels / dragSensitivity;
            Vector2 input = Vector2.ClampMagnitude((pointerPosition - _dragOrigin) / effectiveRadius, 1f);
            if (input.magnitude <= deadZone)
            {
                return Vector2.zero;
            }

            float remappedMagnitude = Mathf.InverseLerp(deadZone, 1f, input.magnitude);
            return input.normalized * remappedMagnitude;
        }
    }
}
