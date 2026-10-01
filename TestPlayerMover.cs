using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace EscapeAnalytics.Gameplay
{
    /// <summary>
    /// SOLO PARA PRUEBAS: mueve al jugador con WASD (en ejes del mundo, sin rotar).
    /// No reemplaza el movimiento 3D en 3.ª persona de US-01; bórralo cuando tengas el real.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class TestPlayerMover : MonoBehaviour
    {
        [SerializeField] float speed = 5f;

        CharacterController _controller;
        float _verticalVelocity;

        void Awake()
        {
            _controller = GetComponent<CharacterController>();
        }

        void Update()
        {
            Vector2 input = ReadMove();
            Vector3 move = new Vector3(input.x, 0f, input.y) * speed;

            if (_controller.isGrounded && _verticalVelocity < 0f) _verticalVelocity = -2f;
            _verticalVelocity += Physics.gravity.y * Time.deltaTime;
            move.y = _verticalVelocity;

            _controller.Move(move * Time.deltaTime);
        }

        static Vector2 ReadMove()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb == null) return Vector2.zero;
            float x = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
            float y = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
            return Vector2.ClampMagnitude(new Vector2(x, y), 1f);
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Vector2.ClampMagnitude(
                new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")), 1f);
#else
            return Vector2.zero;
#endif
        }
    }
}
