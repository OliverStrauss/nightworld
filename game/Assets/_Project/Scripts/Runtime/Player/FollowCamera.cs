using UnityEngine;
using UnityEngine.InputSystem;

namespace NightWorld.Player
{
    /// <summary>
    /// Third-person orbit camera. Sits on the hero next to PlayerController and drives Camera.main:
    /// the camera orbits a pivot cameraPivotHeight above the hero's feet at cameraDistance and looks
    /// at it. The right stick writes <see cref="LookInput"/> (tests set it directly); the mouse turns
    /// the camera while the cursor is locked. The cursor locks when play starts and frees on Escape.
    /// While this component exists, PlayerController walks relative to <see cref="Yaw"/>.
    /// Every number comes from the hero's PlayerSettings.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public class FollowCamera : MonoBehaviour
    {
        /// <summary>Stick units: x = 1 turns right at settings.orbitSpeed degrees per second;
        /// y = 1 tilts the view up (the camera drops) at the same speed.</summary>
        public Vector2 LookInput { get; set; }

        /// <summary>Degrees around world up. 0 = camera south of the hero, looking north (+z).</summary>
        public float Yaw { get; private set; }

        /// <summary>Degrees the camera sits above the pivot, always within settings.pitchMin..pitchMax.</summary>
        public float Pitch { get; private set; }

        private PlayerController player;
        private InputAction lookAction;

        private void Awake()
        {
            player = GetComponent<PlayerController>();

            lookAction = new InputAction("Look", InputActionType.Value, expectedControlType: "Vector2");
            lookAction.AddBinding("<Gamepad>/rightStick");

            // Callbacks only fire when the stick changes, so a LookInput set by a test isn't overwritten.
            lookAction.performed += OnLook;
            lookAction.canceled += OnLook;
        }

        private void OnEnable() => lookAction.Enable();

        private void OnDisable() => lookAction.Disable();

        private void OnDestroy() => lookAction?.Dispose();

        private void OnLook(InputAction.CallbackContext context) => LookInput = context.ReadValue<Vector2>();

        private void Start()
        {
            // Start behind the hero.
            Yaw = transform.eulerAngles.y;
            var settings = player.settings;
            if (settings != null) Pitch = Mathf.Clamp(settings.cameraStartPitch, settings.pitchMin, settings.pitchMax);

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void LateUpdate()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            var settings = player.settings;
            if (settings == null) return;

            // The stick is a rate, so it scales with frame time; the mouse delta is already "this frame".
            Vector2 turn = LookInput * (settings.orbitSpeed * Time.deltaTime);
            var mouse = Mouse.current;
            if (mouse != null && Cursor.lockState == CursorLockMode.Locked)
                turn += mouse.delta.ReadValue() * settings.mouseSensitivity;

            Yaw += turn.x;
            Pitch = Mathf.Clamp(Pitch - turn.y, settings.pitchMin, settings.pitchMax);

            var cam = Camera.main;
            if (cam == null) return;

            // Snappy: no smoothing, the camera is exactly where the orbit says every frame.
            var rotation = Quaternion.Euler(Pitch, Yaw, 0f);
            Vector3 pivot = transform.position + Vector3.up * settings.cameraPivotHeight;
            cam.transform.SetPositionAndRotation(pivot - rotation * Vector3.forward * settings.cameraDistance, rotation);
        }
    }
}
