using System;
using UnityEngine;

namespace EscapeAnalytics.Player
{
    /// <summary>
    /// Mecánica de agacharse (SCRUM-6), en modo alternado (toggle): cada pulsación cambia de postura.
    ///
    /// Cumple el criterio de aceptación de US-01 en su parte de colisión: al agacharse cambia el perfil de
    /// colisión, reduciendo la altura de la cápsula del CharacterController y manteniendo los pies en su
    /// sitio. Al intentar ponerse de pie se comprueba con una cápsula de prueba que haya espacio libre;
    /// si hay un techo encima, el personaje permanece agachado y se emite <see cref="StandUpBlocked"/>.
    ///
    /// Se integra con el controlador de movimiento por interfaces:
    ///  - <see cref="IMovementSpeedModifier"/> para reducir la velocidad.
    ///  - <see cref="IGroundedStateProvider"/> para reportar el estado Crouching.
    /// Además usa la propiedad JumpEnabled del controlador para impedir saltar estando agachado.
    ///
    /// Orden recomendado de componentes en el Inspector: este por encima de PlayerStaminaSystem, para que
    /// el estado Crouching tenga prioridad sobre Running.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(PlayerMovementController))]
    [RequireComponent(typeof(PlayerInputReader))]
    [DisallowMultipleComponent]
    public class PlayerCrouchController : MonoBehaviour, IMovementSpeedModifier, IGroundedStateProvider
    {
        private const int OverlapBufferSize = 8;

        [Header("Referencias")]
        [Tooltip("Asset con los parámetros de agacharse. Se puede editar durante Play Mode y los cambios persisten.")]
        [SerializeField] private PlayerCrouchSettings settings;

        [Tooltip("Objeto visual del jugador (la cápsula, o el modelo cuando exista). " +
                 "Se escala provisionalmente al agacharse. Opcional.")]
        [SerializeField] private Transform visual;

        [Header("Depuración")]
        [SerializeField] private bool drawGizmos = true;

        // ----------------------------------------------------------------------------------------
        // Eventos
        // ----------------------------------------------------------------------------------------

        /// <summary>Cambió la postura. El parámetro indica si quedó agachado.</summary>
        public event Action<bool> CrouchStateChanged;

        /// <summary>Se intentó poner de pie pero hay un obstáculo encima.</summary>
        public event Action StandUpBlocked;

        // ----------------------------------------------------------------------------------------
        // Estado público
        // ----------------------------------------------------------------------------------------

        /// <summary>Postura objetivo: true si el jugador está agachado o agachándose.</summary>
        public bool IsCrouching { get; private set; }

        /// <summary>True mientras la altura de la cápsula está cambiando.</summary>
        public bool IsTransitioning
        {
            get { return !Mathf.Approximately(controller.height, TargetHeight); }
        }

        /// <summary>Altura actual de la cápsula, en metros.</summary>
        public float CurrentHeight
        {
            get { return controller != null ? controller.height : 0f; }
        }

        /// <summary>Altura de pie detectada al iniciar, en metros.</summary>
        public float StandingHeight
        {
            get { return standingHeight; }
        }

        /// <summary>Configuración activa.</summary>
        public PlayerCrouchSettings Settings
        {
            get { return settings; }
        }

        /// <summary>Multiplicador aplicado por este componente a la velocidad de desplazamiento.</summary>
        public float SpeedMultiplier
        {
            get { return IsCrouching ? settings.crouchSpeedMultiplier : 1f; }
        }

        private float TargetHeight
        {
            get { return IsCrouching ? Mathf.Min(settings.crouchHeight, standingHeight) : standingHeight; }
        }

        // ----------------------------------------------------------------------------------------
        // Estado interno
        // ----------------------------------------------------------------------------------------

        private CharacterController controller;
        private PlayerMovementController movement;
        private PlayerInputReader input;
        private readonly Collider[] overlapResults = new Collider[OverlapBufferSize];

        private float standingHeight;
        private float centerHeightRatio = 0.5f;
        private Vector3 visualBaseScale = Vector3.one;
        private Vector3 visualBasePosition;

        // ----------------------------------------------------------------------------------------
        // API pública
        // ----------------------------------------------------------------------------------------

        /// <summary>Agacha al personaje. No hace nada si ya está agachado.</summary>
        public void Crouch()
        {
            if (IsCrouching) return;
            IsCrouching = true;
            if (settings.blockJumpWhileCrouched) movement.JumpEnabled = false;
            if (CrouchStateChanged != null) CrouchStateChanged(true);
        }

        /// <summary>
        /// Intenta ponerse de pie. Devuelve false si hay un obstáculo encima; en ese caso el personaje
        /// permanece agachado.
        /// </summary>
        public bool TryStandUp()
        {
            if (!IsCrouching) return true;

            if (HasCeilingAbove())
            {
                if (StandUpBlocked != null) StandUpBlocked();
                return false;
            }

            IsCrouching = false;
            if (CrouchStateChanged != null) CrouchStateChanged(false);
            return true;
        }

        /// <summary>Alterna la postura.</summary>
        public void Toggle()
        {
            if (IsCrouching) TryStandUp();
            else Crouch();
        }

        // ----------------------------------------------------------------------------------------
        // Ciclo de vida
        // ----------------------------------------------------------------------------------------

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            movement = GetComponent<PlayerMovementController>();
            input = GetComponent<PlayerInputReader>();

            if (settings == null)
            {
                Debug.LogWarning("[PlayerCrouchController] No hay PlayerCrouchSettings asignado. " +
                                 "Se usan valores por defecto en memoria (los cambios no se guardarán).", this);
                settings = ScriptableObject.CreateInstance<PlayerCrouchSettings>();
            }

            // La altura de pie es la configurada en el CharacterController: este componente no la impone.
            standingHeight = controller.height;
            centerHeightRatio = standingHeight > 0f ? controller.center.y / standingHeight : 0.5f;

            if (visual != null)
            {
                visualBaseScale = visual.localScale;
                visualBasePosition = visual.localPosition;
            }
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;
            if (deltaTime <= 0f) return;

            HandleInput();
            UpdateCapsuleHeight(deltaTime);
        }

        private void HandleInput()
        {
            if (input.CrouchPressedThisFrame) Toggle();

            // Estando agachado, el salto está bloqueado y la pulsación se reinterpreta como "ponerse de pie".
            if (IsCrouching && settings.blockJumpWhileCrouched && input.JumpPressedThisFrame) TryStandUp();
        }

        // ----------------------------------------------------------------------------------------
        // Perfil de colisión
        // ----------------------------------------------------------------------------------------

        private void UpdateCapsuleHeight(float deltaTime)
        {
            float target = TargetHeight;
            if (Mathf.Approximately(controller.height, target))
            {
                // Transición terminada: si está de pie, se vuelve a permitir el salto.
                if (!IsCrouching && settings.blockJumpWhileCrouched) movement.JumpEnabled = true;
                return;
            }

            float speed = Mathf.Abs(standingHeight - settings.crouchHeight) /
                          Mathf.Max(0.01f, settings.transitionDuration);
            float height = Mathf.MoveTowards(controller.height, target, speed * deltaTime);
            ApplyHeight(height);
        }

        /// <summary>
        /// Ajusta altura y centro de la cápsula manteniendo los pies en el mismo sitio.
        /// Si el centro se dejara fijo, al reducir la altura el personaje quedaría flotando.
        /// </summary>
        private void ApplyHeight(float height)
        {
            controller.height = height;

            Vector3 center = controller.center;
            center.y = height * centerHeightRatio;
            controller.center = center;

            if (settings.scaleVisualWhileCrouched && visual != null && standingHeight > 0f)
            {
                float factor = height / standingHeight;
                Vector3 scale = visualBaseScale;
                scale.y = visualBaseScale.y * factor;
                visual.localScale = scale;

                Vector3 position = visualBasePosition;
                position.y = visualBasePosition.y * factor;
                visual.localPosition = position;
            }
        }

        /// <summary>
        /// Comprueba con una cápsula de prueba, del tamaño del personaje de pie, si hay algo que impida
        /// incorporarse. Ignora los colliders del propio jugador y los triggers.
        /// </summary>
        private bool HasCeilingAbove()
        {
            float radius = Mathf.Max(0.01f, controller.radius * 0.95f);
            float height = standingHeight + settings.standUpClearance;
            Vector3 feet = GetFeetPosition();
            Vector3 bottom = feet + Vector3.up * radius;
            Vector3 top = feet + Vector3.up * Mathf.Max(radius, height - radius);

            int count = Physics.OverlapCapsuleNonAlloc(bottom, top, radius, overlapResults,
                settings.ceilingLayers, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                Collider hit = overlapResults[i];
                if (hit == null) continue;
                if (hit == controller || hit.transform.IsChildOf(transform)) continue;
                return true;
            }
            return false;
        }

        private Vector3 GetFeetPosition()
        {
            return transform.TransformPoint(controller.center) - Vector3.up * (controller.height * 0.5f);
        }

        // ----------------------------------------------------------------------------------------
        // IGroundedStateProvider
        // ----------------------------------------------------------------------------------------

        public bool TryGetGroundedState(bool isMoving, out LocomotionState state)
        {
            if (IsCrouching)
            {
                state = LocomotionState.Crouching;
                return true;
            }

            state = LocomotionState.Idle;
            return false;
        }

        // ----------------------------------------------------------------------------------------
        // Gizmos
        // ----------------------------------------------------------------------------------------

        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos || !Application.isPlaying || controller == null) return;

            Vector3 feet = GetFeetPosition();
            float radius = controller.radius * 0.95f;
            float height = standingHeight + (settings != null ? settings.standUpClearance : 0f);

            Gizmos.color = IsCrouching ? Color.yellow : Color.green;
            Gizmos.DrawWireSphere(feet + Vector3.up * radius, radius);
            Gizmos.DrawWireSphere(feet + Vector3.up * Mathf.Max(radius, height - radius), radius);
        }
    }
}
