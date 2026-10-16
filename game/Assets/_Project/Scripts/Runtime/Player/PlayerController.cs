using NightWorld.Input;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NightWorld.Player
{
    /// <summary>
    /// Walks the hero around with a CharacterController. The Move action of the shared
    /// <see cref="GameControls"/> (see <see cref="SetControls"/>) writes <see cref="MoveInput"/>;
    /// without controls, tests set it directly. With a <see cref="FollowCamera"/> on the hero,
    /// input (0, 1) walks away from the camera; without one it walks along world +z.
    /// Create the hero with <see cref="PlayerSpawner.Spawn"/>.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        public PlayerSettings settings;

        [Tooltip("Where the hero goes back to after falling below settings.fallResetY.")]
        public Transform spawnPoint;

        /// <summary>x walks right, y forward (camera-relative with a FollowCamera, else world +x and +z).
        /// Longer than 1 is clamped to 1.</summary>
        public Vector2 MoveInput { get; set; }

        /// <summary>The shared controls this hero listens to, or null (tests drive MoveInput by hand).</summary>
        public GameControls Controls { get; private set; }

        /// <summary>Downward speed while standing, so isGrounded stays true on flat ground and gentle slopes.</summary>
        private const float GroundedPush = -2f;

        private CharacterController body;
        private float verticalSpeed;

        private void Awake()
        {
            body = GetComponent<CharacterController>();
        }

        /// <summary>Listens to <paramref name="controls"/>' Move action instead of the previous controls (null = none).
        /// Whoever created the controls enables and disposes them.</summary>
        public void SetControls(GameControls controls)
        {
            if (Controls != null)
            {
                Controls.Move.performed -= OnMove;
                Controls.Move.canceled -= OnMove;
            }

            Controls = controls;

            // Callbacks only fire when a device changes, so a MoveInput set by a test isn't overwritten.
            if (Controls != null)
            {
                Controls.Move.performed += OnMove;
                Controls.Move.canceled += OnMove;
            }
        }

        private void OnDestroy() => SetControls(null);

        private void OnMove(InputAction.CallbackContext context) => MoveInput = context.ReadValue<Vector2>();

        private void Update()
        {
            if (settings == null) return;

            Vector2 input = Vector2.ClampMagnitude(MoveInput, 1f);
            var direction = new Vector3(input.x, 0f, input.y);
            // W walks away from the camera on the XZ plane.
            if (TryGetComponent(out FollowCamera follow)) direction = Quaternion.Euler(0f, follow.Yaw, 0f) * direction;

            if (body.isGrounded && verticalSpeed < 0f) verticalSpeed = GroundedPush;
            verticalSpeed += settings.gravity * Time.deltaTime;

            // Snappy: full speed from the first frame, no acceleration curve.
            var velocity = direction * settings.moveSpeed + Vector3.up * verticalSpeed;
            body.Move(velocity * Time.deltaTime);

            if (direction.sqrMagnitude > 0.0001f)
            {
                var facing = Quaternion.LookRotation(direction);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, facing, settings.turnSpeed * Time.deltaTime);
            }
        }

        private void FixedUpdate()
        {
            if (settings != null && spawnPoint != null && transform.position.y < settings.fallResetY)
                Teleport(spawnPoint.position + Vector3.up * PlayerSpawner.SpawnLift);
        }

        /// <summary>Moves the hero instantly and stops its fall. Use this instead of setting transform.position,
        /// which a CharacterController overwrites on its next Move.</summary>
        public void Teleport(Vector3 position)
        {
            body.enabled = false;
            transform.position = position;
            body.enabled = true;
            verticalSpeed = 0f;
        }
    }
}
