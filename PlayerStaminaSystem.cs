using System;
using UnityEngine;

namespace EscapeAnalytics.Player
{
    /// <summary>
    /// Sistema de estamina y carrera (SCRUM-6).
    ///
    /// Máquina de estados implícita:
    ///
    ///   Disponible --(se mantiene Shift y hay movimiento)--> Corriendo
    ///   Corriendo  --(se suelta Shift o se deja de mover)--> Disponible (tras regenDelay empieza a regenerar)
    ///   Corriendo  --(estamina llega a 0)--> Agotado (bloqueado: la velocidad vuelve a caminata)
    ///   Agotado    --(estamina >= recoveryThreshold)--> Disponible
    ///
    /// Cumple el criterio de aceptación de US-01: "Al agotarse la estamina corriendo, el personaje reduce
    /// automáticamente su velocidad a caminar".
    ///
    /// Se integra con el controlador de movimiento por dos interfaces, sin acoplarse a su lógica interna:
    ///  - <see cref="IMovementSpeedModifier"/> para aplicar el multiplicador de carrera.
    ///  - <see cref="IGroundedStateProvider"/> para reportar el estado Running.
    /// </summary>
    [RequireComponent(typeof(PlayerMovementController))]
    [RequireComponent(typeof(PlayerInputReader))]
    [DisallowMultipleComponent]
    public class PlayerStaminaSystem : MonoBehaviour, IMovementSpeedModifier, IGroundedStateProvider
    {
        [Header("Referencias")]
        [Tooltip("Asset con los parámetros de estamina. Se puede editar durante Play Mode y los cambios persisten.")]
        [SerializeField] private PlayerStaminaSettings settings;

        [Tooltip("Componente de agacharse, si existe. Estando agachado no se puede correr. " +
                 "Si se deja vacío se busca en este mismo GameObject.")]
        [SerializeField] private PlayerCrouchController crouchController;

        // ----------------------------------------------------------------------------------------
        // Eventos
        // ----------------------------------------------------------------------------------------

        /// <summary>Estamina actual normalizada (0 a 1). Se emite solo cuando el valor cambia.</summary>
        public event Action<float> StaminaChanged;

        /// <summary>El jugador empezó a correr.</summary>
        public event Action SprintStarted;

        /// <summary>El jugador dejó de correr (por soltar el botón, detenerse o agotar la estamina).</summary>
        public event Action SprintStopped;

        /// <summary>
        /// La estamina llegó a cero y la carrera quedó bloqueada. Señal relevante para el Índice de Pánico
        /// del Sprint 5: agotarse mientras el perseguidor está cerca es un indicador de mala gestión de recursos.
        /// </summary>
        public event Action StaminaDepleted;

        /// <summary>La estamina se recuperó por encima del umbral y se puede volver a correr.</summary>
        public event Action StaminaRecovered;

        // ----------------------------------------------------------------------------------------
        // Estado público
        // ----------------------------------------------------------------------------------------

        /// <summary>Estamina actual normalizada, de 0 a 1.</summary>
        public float Stamina01 { get; private set; }

        /// <summary>Si el jugador está corriendo en este momento.</summary>
        public bool IsSprinting { get; private set; }

        /// <summary>Si la carrera está bloqueada por agotamiento.</summary>
        public bool IsExhausted { get; private set; }

        /// <summary>Si en este momento se cumplen las condiciones para poder correr.</summary>
        public bool CanSprint
        {
            get { return !IsExhausted && Stamina01 > 0f && !IsCrouching; }
        }

        /// <summary>Configuración activa.</summary>
        public PlayerStaminaSettings Settings
        {
            get { return settings; }
        }

        /// <summary>Multiplicador aplicado por este sistema a la velocidad de desplazamiento.</summary>
        public float SpeedMultiplier
        {
            get { return IsSprinting ? settings.runSpeedMultiplier : 1f; }
        }

        private bool IsCrouching
        {
            get { return crouchController != null && crouchController.IsCrouching; }
        }

        // ----------------------------------------------------------------------------------------
        // Estado interno
        // ----------------------------------------------------------------------------------------

        private PlayerMovementController movement;
        private PlayerInputReader input;
        private float lastSprintTime = float.NegativeInfinity;

        // ----------------------------------------------------------------------------------------
        // API pública
        // ----------------------------------------------------------------------------------------

        /// <summary>Consume estamina desde otro sistema (por ejemplo, un empujón del perseguidor).</summary>
        public void ConsumeStamina(float amount01)
        {
            SetStamina(Stamina01 - Mathf.Max(0f, amount01));
            if (Stamina01 <= 0f) EnterExhausted();
        }

        /// <summary>Restaura estamina desde otro sistema (por ejemplo, un consumible).</summary>
        public void RestoreStamina(float amount01)
        {
            SetStamina(Stamina01 + Mathf.Max(0f, amount01));
        }

        /// <summary>Devuelve la estamina al máximo y levanta el bloqueo. Útil al reaparecer.</summary>
        public void ResetStamina()
        {
            StopSprint();
            IsExhausted = false;
            SetStamina(1f);
        }

        // ----------------------------------------------------------------------------------------
        // Ciclo de vida
        // ----------------------------------------------------------------------------------------

        private void Awake()
        {
            movement = GetComponent<PlayerMovementController>();
            input = GetComponent<PlayerInputReader>();
            if (crouchController == null) crouchController = GetComponent<PlayerCrouchController>();

            if (settings == null)
            {
                Debug.LogWarning("[PlayerStaminaSystem] No hay PlayerStaminaSettings asignado. " +
                                 "Se usan valores por defecto en memoria (los cambios no se guardarán).", this);
                settings = ScriptableObject.CreateInstance<PlayerStaminaSettings>();
            }

            Stamina01 = Mathf.Clamp01(settings.initialStamina);
        }

        private void Start()
        {
            // Emitir el valor inicial para que la interfaz arranque sincronizada.
            if (StaminaChanged != null) StaminaChanged(Stamina01);
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;
            if (deltaTime <= 0f) return;

            UpdateSprintState();

            if (IsSprinting) DrainStamina(deltaTime);
            else RegenerateStamina(deltaTime);
        }

        // ----------------------------------------------------------------------------------------
        // Lógica
        // ----------------------------------------------------------------------------------------

        private void UpdateSprintState()
        {
            // Se exige intención de movimiento y no solo velocidad: al chocar contra una pared la velocidad
            // real es casi cero, pero el jugador sigue empujando y debe seguir gastando estamina.
            bool wantsToMove = movement.DesiredMoveDirection.sqrMagnitude > 0.0001f;
            bool wantsToSprint = input.SprintHeld && (!settings.requireMovementToDrain || wantsToMove);

            if (wantsToSprint && CanSprint) StartSprint();
            else StopSprint();
        }

        private void StartSprint()
        {
            if (IsSprinting) return;
            IsSprinting = true;
            lastSprintTime = Time.time;
            if (SprintStarted != null) SprintStarted();
        }

        private void StopSprint()
        {
            if (!IsSprinting) return;
            IsSprinting = false;
            lastSprintTime = Time.time;
            if (SprintStopped != null) SprintStopped();
        }

        private void DrainStamina(float deltaTime)
        {
            lastSprintTime = Time.time;

            if (!settings.drainWhileAirborne && !movement.IsGrounded) return;

            SetStamina(Stamina01 - settings.DrainPerSecond * deltaTime);

            if (Stamina01 <= 0f)
            {
                StopSprint();
                EnterExhausted();
            }
        }

        private void RegenerateStamina(float deltaTime)
        {
            if (Stamina01 >= 1f && !IsExhausted) return;
            if (Time.time - lastSprintTime < settings.regenDelaySeconds) return;

            SetStamina(Stamina01 + settings.RegenPerSecond * deltaTime);

            if (IsExhausted && Stamina01 >= settings.recoveryThreshold)
            {
                IsExhausted = false;
                if (StaminaRecovered != null) StaminaRecovered();
            }
        }

        private void EnterExhausted()
        {
            if (IsExhausted) return;
            IsExhausted = true;
            if (StaminaDepleted != null) StaminaDepleted();
        }

        private void SetStamina(float value)
        {
            float clamped = Mathf.Clamp01(value);
            if (Mathf.Approximately(clamped, Stamina01)) return;

            Stamina01 = clamped;
            if (StaminaChanged != null) StaminaChanged(Stamina01);
        }

        // ----------------------------------------------------------------------------------------
        // IGroundedStateProvider
        // ----------------------------------------------------------------------------------------

        public bool TryGetGroundedState(bool isMoving, out LocomotionState state)
        {
            if (IsSprinting && isMoving)
            {
                state = LocomotionState.Running;
                return true;
            }

            state = LocomotionState.Idle;
            return false;
        }
    }
}
