using NightWorld.Input;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NightWorld.Player
{
    /// <summary>
    /// Third-person orbit camera. Sits on the hero next to PlayerController and drives Camera.main:
    /// the camera orbits a pivot cameraPivotHeight above the hero's feet at cameraDistance and looks
    /// at it. Input comes from the shared <see cref="GameControls"/> (see <see cref="SetControls"/>):
    /// a stick on Look writes <see cref="LookInput"/> (tests set it directly); a mouse on Look turns
    /// the camera while the cursor is locked. The cursor locks when play starts and frees on Menu.
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

        /// <summary>The shared controls this camera listens to, or null (tests drive LookInput by hand).</summary>
        public GameControls Controls { get; private set; }

        /// <summary>True from Start (which locks the cursor) until Menu frees it. The mouse turns the camera
        /// only while this is true. Tracked here rather than read back from Cursor.lockState, because a
        /// headless test player can't lock the OS cursor.</summary>
        public bool CursorLocked { get; private set; }

        private PlayerController player;

        private void Awake()
        {
            player = GetComponent<PlayerController>();
        }

        /// <summary>Listens to <paramref name="controls"/>' Look and Menu actions instead of the previous
        /// controls (null = none). Whoever created the controls enables and disposes them.</summary>
        public void SetControls(GameControls controls)
        {
            if (Controls != null)
            {
                Controls.Look.performed -= OnLook;
                Controls.Look.canceled -= OnLook;
                Controls.Menu.performed -= OnMenu;
            }

            Controls = controls;

            // Callbacks only fire when a device changes, so a LookInput set by a test isn't overwritten.
            if (Controls != null)
            {
                Controls.Look.performed += OnLook;
                Controls.Look.canceled += OnLook;
                Controls.Menu.performed += OnMenu;
            }
        }

        private void OnDestroy() => SetControls(null);

        // Mouse and stick share the Look action but scale differently, so they are told apart by the
        // device of the control that drives the action: a Pointer (the mouse) sends a per-frame delta,
        // read in LateUpdate; anything else (a stick) sends a rate, kept in LookInput.
        private void OnLook(InputAction.CallbackContext context)
        {
            // While the mouse drives Look the stick isn't, so its old rate must not keep turning the camera.
            LookInput = IsPointer(context.control) ? Vector2.zero : context.ReadValue<Vector2>();
        }

        // A callback, not WasPressedThisFrame in LateUpdate: it fires the moment Menu is pressed,
        // whichever input update that happens in.
        private void OnMenu(InputAction.CallbackContext context) => SetCursorLocked(false);

        private void SetCursorLocked(bool locked)
        {
            CursorLocked = locked;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        /// <summary>How far the camera turns this frame, in degrees (x = yaw, y = pitch up).
        /// <paramref name="stickRate"/> is a rate: × orbitSpeed × deltaTime. <paramref name="mouseDelta"/> is
        /// already "this frame": × mouseSensitivity, and only while <paramref name="cursorLocked"/>.</summary>
        public static Vector2 Turn(Vector2 stickRate, Vector2 mouseDelta, bool cursorLocked, PlayerSettings settings, float deltaTime)
        {
            Vector2 turn = stickRate * (settings.orbitSpeed * deltaTime);
            if (cursorLocked) turn += mouseDelta * settings.mouseSensitivity;
            return turn;
        }

        private static bool IsPointer(InputControl control) => control != null && control.device is Pointer;

        private void Start()
        {
            // Start behind the hero.
            Yaw = transform.eulerAngles.y;
            var settings = player.settings;
            if (settings != null) Pitch = Mathf.Clamp(settings.cameraStartPitch, settings.pitchMin, settings.pitchMax);

            SetCursorLocked(true);
        }

        private void LateUpdate()
        {
            var settings = player.settings;
            if (settings == null) return;

            // The stick is a rate, so it scales with frame time; the mouse delta is already "this frame".
            Vector2 mouseDelta = Vector2.zero;
            if (Controls != null && IsPointer(Controls.Look.activeControl)) mouseDelta = Controls.Look.ReadValue<Vector2>();
            Vector2 turn = Turn(LookInput, mouseDelta, CursorLocked, settings, Time.deltaTime);

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
