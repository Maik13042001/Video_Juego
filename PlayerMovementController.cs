using System;
using System.Collections.Generic;
using UnityEngine;

namespace EscapeAnalytics.Player
{
    /// <summary>
    /// Controlador de movimiento del jugador en tercera persona (SCRUM-5).
    ///
    /// Responsabilidades:
    ///  - Desplazamiento horizontal relativo a la cámara, con aceleración y desaceleración.
    ///  - Rotación suave del personaje hacia la dirección de movimiento.
    ///  - Gravedad, salto (con coyote time y jump buffer) y detección de suelo.
    ///  - Publicación de eventos de movimiento para otros sistemas (patrón Observer).
    ///
    /// Fuera de alcance (SCRUM-6): estamina, correr, agacharse y animaciones. Los puntos de extensión
    /// para esas funciones son <see cref="IMovementSpeedModifier"/>, <see cref="LocomotionState"/>
    /// y las propiedades públicas de solo lectura.
    ///
    /// Por qué CharacterController y no Rigidbody:
    ///  - Movimiento determinista y controlado por código, sin fuerzas ni fricción que ajustar.
    ///  - Manejo integrado de pendientes (Slope Limit) y escalones (Step Offset).
    ///  - Se mueve en Update, igual que la cámara en LateUpdate: no hay desfase entre la frecuencia
    ///    de la física y la del render, y por tanto no hay tirones (jitter) al seguir al jugador.
    ///
    /// Supuesto: el GameObject del jugador tiene escala (1, 1, 1).
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(PlayerInputReader))]
    [DisallowMultipleComponent]
    public class PlayerMovementController : MonoBehaviour
    {
        private const float MinDirectionSqrMagnitude = 0.0001f;
        private const float MovingSpeedThreshold = 0.05f;
        private const int GroundProbeBufferSize = 8;

        [Header("Referencias")]
        [Tooltip("Asset con los parámetros de movimiento. Se puede editar durante Play Mode y los cambios persisten.")]
        [SerializeField] private PlayerMovementSettings settings;

        [Tooltip("Transform de la cámara que define 'adelante' para el movimiento. Si se deja vacío se usa Camera.main.")]
        [SerializeField] private Transform cameraTransform;

        [Header("Depuración")]
        [Tooltip("Si el jugador cae por debajo de esta altura, se reubica en su posición inicial. Útil en escenas de prueba.")]
        [SerializeField] private float fallResetHeight = -30f;

        [Tooltip("Dibujar en la vista Scene la sonda de suelo y la dirección deseada.")]
        [SerializeField] private bool drawGizmos = true;

        // ----------------------------------------------------------------------------------------
        // Eventos (Observer). Suscribirse en OnEnable y cancelar la suscripción en OnDisable.
        // ----------------------------------------------------------------------------------------

        /// <summary>Se emite al cambiar el estado de locomoción. Parámetros: (estado anterior, estado nuevo).</summary>
        public event Action<LocomotionState, LocomotionState> LocomotionStateChanged;

        /// <summary>Se emite cuando la dirección deseada cambia más que el umbral configurado.</summary>
        public event Action<DirectionChangeEvent> DirectionChanged;

        /// <summary>Se emite en el frame en que el jugador inicia un salto.</summary>
        public event Action Jumped;

        /// <summary>Se emite al aterrizar tras un tiempo en el aire mayor al mínimo configurado.</summary>
        public event Action<LandingEvent> Landed;

        // ----------------------------------------------------------------------------------------
        // Estado público de solo lectura (para animaciones, IA, telemetría y HUD de depuración).
        // ----------------------------------------------------------------------------------------

        /// <summary>Velocidad que el controlador intenta aplicar (horizontal + vertical), en m/s.</summary>
        public Vector3 Velocity
        {
            get { return horizontalVelocity + Vector3.up * verticalVelocity; }
        }

        /// <summary>
        /// Velocidad real resultante tras las colisiones, en m/s. Al caminar contra una pared es casi cero
        /// aunque haya entrada. Es la adecuada para alimentar el Animator en SCRUM-6.
        /// </summary>
        public Vector3 ActualVelocity
        {
            get { return controller != null ? controller.velocity : Vector3.zero; }
        }

        /// <summary>Rapidez horizontal pretendida, en m/s.</summary>
        public float HorizontalSpeed
        {
            get { return horizontalVelocity.magnitude; }
        }

        /// <summary>Velocidad vertical actual, en m/s (positiva hacia arriba).</summary>
        public float VerticalVelocity
        {
            get { return verticalVelocity; }
        }

        /// <summary>Dirección de movimiento deseada este frame (normalizada, plano XZ) o cero si no hay entrada.</summary>
        public Vector3 DesiredMoveDirection { get; private set; }

        /// <summary>Si el jugador está apoyado en el suelo.</summary>
        public bool IsGrounded { get; private set; }

        /// <summary>Estado de locomoción actual.</summary>
        public LocomotionState CurrentState { get; private set; }

        /// <summary>Producto de todos los modificadores de velocidad registrados este frame.</summary>
        public float CurrentSpeedMultiplier { get; private set; }

        /// <summary>Configuración activa.</summary>
        public PlayerMovementSettings Settings
        {
            get { return settings; }
        }

        // ----------------------------------------------------------------------------------------
        // Estado interno
        // ----------------------------------------------------------------------------------------

        private CharacterController controller;
        private PlayerInputReader input;
        private readonly List<IMovementSpeedModifier> speedModifiers = new List<IMovementSpeedModifier>();
        private readonly Collider[] groundProbeResults = new Collider[GroundProbeBufferSize];

        private Vector3 horizontalVelocity;
        private float verticalVelocity;
        private float rotationVelocity;          // Referencia interna de Mathf.SmoothDampAngle.

        private bool wasGrounded;
        private float lastGroundedTime = float.NegativeInfinity;
        private float lastJumpPressedTime = float.NegativeInfinity;
        private float airborneStartTime;

        private bool hasReportedDirection;
        private Vector3 lastReportedDirection;

        private Vector3 spawnPosition;
        private Quaternion spawnRotation;

        // ----------------------------------------------------------------------------------------
        // API pública
        // ----------------------------------------------------------------------------------------

        /// <summary>Registra un modificador de velocidad que no está en este GameObject o se creó en ejecución.</summary>
        public void RegisterSpeedModifier(IMovementSpeedModifier modifier)
        {
            if (modifier != null && !speedModifiers.Contains(modifier)) speedModifiers.Add(modifier);
        }

        /// <summary>Elimina un modificador de velocidad previamente registrado.</summary>
        public void UnregisterSpeedModifier(IMovementSpeedModifier modifier)
        {
            speedModifiers.Remove(modifier);
        }

        /// <summary>
        /// Reubica al jugador de forma segura. CharacterController sobrescribe transform.position,
        /// así que hay que desactivarlo durante el cambio. También reinicia las velocidades.
        /// Pensado para reapariciones (por ejemplo, cuando el perseguidor atrape al jugador).
        /// </summary>
        public void Teleport(Vector3 position, Quaternion rotation)
        {
            controller.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            controller.enabled = true;

            horizontalVelocity = Vector3.zero;
            verticalVelocity = 0f;
            rotationVelocity = 0f;
            hasReportedDirection = false;
        }

        // ----------------------------------------------------------------------------------------
        // Ciclo de vida de Unity
        // ----------------------------------------------------------------------------------------

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            input = GetComponent<PlayerInputReader>();

            if (settings == null)
            {
                Debug.LogWarning("[PlayerMovementController] No hay PlayerMovementSettings asignado. " +
                                 "Se usan valores por defecto en memoria (los cambios no se guardarán).", this);
                settings = ScriptableObject.CreateInstance<PlayerMovementSettings>();
            }

            // Detecta automáticamente modificadores de velocidad en este mismo GameObject (SCRUM-6).
            var found = new List<IMovementSpeedModifier>();
            GetComponents(found);
            for (int i = 0; i < found.Count; i++) RegisterSpeedModifier(found[i]);

            CurrentState = LocomotionState.Idle;
            CurrentSpeedMultiplier = 1f;
        }

        private void Start()
        {
            if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;
            if (cameraTransform == null)
            {
                Debug.LogWarning("[PlayerMovementController] No se encontró cámara. El movimiento usará los ejes del mundo.", this);
            }

            spawnPosition = transform.position;
            spawnRotation = transform.rotation;

            IsGrounded = wasGrounded = controller.isGrounded || ProbeGround();
            if (IsGrounded) lastGroundedTime = Time.time;
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;
            if (deltaTime <= 0f) return; // Juego en pausa (Time.timeScale = 0).

            // 1. Estado de suelo a partir del resultado del Move del frame anterior.
            UpdateGroundedState();

            // 2. Intención del jugador en espacio de mundo.
            float inputMagnitude;
            Vector3 desiredDirection = ComputeDesiredDirection(out inputMagnitude);
            DesiredMoveDirection = desiredDirection;
            ReportDirectionChangeIfNeeded(desiredDirection);

            // 3. Velocidades.
            UpdateHorizontalVelocity(desiredDirection, inputMagnitude, deltaTime);
            UpdateVerticalVelocity(deltaTime);

            // 4. Rotación visual del personaje.
            UpdateRotation(desiredDirection, deltaTime);

            // 5. Aplicar el movimiento. CharacterController resuelve colisiones, pendientes y escalones.
            Vector3 displacement = (horizontalVelocity + Vector3.up * verticalVelocity) * deltaTime;
            CollisionFlags flags = controller.Move(displacement);

            // Si el salto choca contra un techo, cortar la subida para no quedar "pegado".
            if ((flags & CollisionFlags.Above) != 0 && verticalVelocity > 0f) verticalVelocity = 0f;

            // 6. Estado de locomoción y red de seguridad de la escena de prueba.
            UpdateLocomotionState();
            if (transform.position.y < fallResetHeight) Teleport(spawnPosition, spawnRotation);
        }

        // ----------------------------------------------------------------------------------------
        // Suelo
        // ----------------------------------------------------------------------------------------

        private void UpdateGroundedState()
        {
            // controller.isGrounded solo es true si el último Move colisionó hacia abajo. La sonda adicional
            // mantiene el estado "en suelo" al bajar escalones o pendientes, pero solo si ya se estaba en el
            // suelo (histéresis), para no detectar el aterrizaje antes de tiempo al caer.
            bool touchingGround = controller.isGrounded || (wasGrounded && ProbeGround());
            bool grounded = touchingGround && verticalVelocity <= 0f;

            if (grounded)
            {
                lastGroundedTime = Time.time;

                if (!wasGrounded)
                {
                    float airTime = Time.time - airborneStartTime;
                    if (airTime >= settings.minAirTimeForLandingEvent && Landed != null)
                    {
                        // verticalVelocity todavía conserva la velocidad de impacto (la gravedad se aplica después).
                        Landed(new LandingEvent(airTime, Mathf.Abs(verticalVelocity), transform.position,
                            Time.time, MovementEventClock.UnixNowMilliseconds()));
                    }
                }
            }
            else if (wasGrounded)
            {
                airborneStartTime = Time.time; // Salió de un borde caminando.
            }

            IsGrounded = grounded;
            wasGrounded = grounded;
        }

        /// <summary>
        /// Comprueba si hay suelo justo debajo de la esfera inferior de la cápsula.
        /// Usa un radio ligeramente menor que el de la cápsula para no detectar paredes laterales.
        /// </summary>
        private bool ProbeGround()
        {
            float radius = controller.radius * 0.9f;
            Vector3 center = transform.TransformPoint(controller.center);
            float halfHeight = Mathf.Max(controller.height * 0.5f, controller.radius);
            Vector3 bottomSphereCenter = center + Vector3.down * (halfHeight - controller.radius);
            Vector3 probeCenter = bottomSphereCenter + Vector3.down * (settings.groundProbeDistance + controller.skinWidth);

            int count = Physics.OverlapSphereNonAlloc(probeCenter, radius, groundProbeResults,
                settings.groundLayers, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                Collider hit = groundProbeResults[i];
                if (hit == controller || hit.transform.IsChildOf(transform)) continue; // Ignorar al propio jugador.
                return true;
            }
            return false;
        }

        // ----------------------------------------------------------------------------------------
        // Movimiento horizontal
        // ----------------------------------------------------------------------------------------

        /// <summary>
        /// Convierte la entrada 2D en una dirección de mundo relativa a la cámara, proyectada en el plano XZ.
        /// </summary>
        private Vector3 ComputeDesiredDirection(out float inputMagnitude)
        {
            Vector2 raw = input.Move;
            float magnitude = raw.magnitude;

            if (magnitude < settings.inputDeadzone)
            {
                inputMagnitude = 0f;
                return Vector3.zero;
            }

            Vector3 forward;
            Vector3 right;

            if (cameraTransform != null)
            {
                forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up);
                if (forward.sqrMagnitude < MinDirectionSqrMagnitude)
                {
                    // La cámara mira casi vertical: se usa su eje "arriba" como referencia horizontal.
                    forward = Vector3.ProjectOnPlane(cameraTransform.up, Vector3.up);
                }
                forward.Normalize();
                right = Vector3.Cross(Vector3.up, forward); // En el sistema de Unity (mano izquierda) da la derecha.
            }
            else
            {
                forward = Vector3.forward;
                right = Vector3.right;
            }

            inputMagnitude = Mathf.Clamp01(magnitude);
            Vector3 direction = forward * raw.y + right * raw.x;
            return direction.normalized;
        }

        private void UpdateHorizontalVelocity(Vector3 desiredDirection, float inputMagnitude, float deltaTime)
        {
            CurrentSpeedMultiplier = ComputeSpeedMultiplier();

            // La magnitud de la entrada escala la velocidad: un joystick a medio recorrido camina más lento.
            Vector3 targetVelocity = desiredDirection * (settings.moveSpeed * CurrentSpeedMultiplier * inputMagnitude);

            bool hasTarget = targetVelocity.sqrMagnitude > MinDirectionSqrMagnitude;
            float rate = hasTarget ? settings.acceleration : settings.deceleration;
            if (!IsGrounded) rate *= settings.airControl;

            // MoveTowards sobre el vector completo: los cambios de dirección se resuelven con la misma
            // aceleración que los arranques, sin derrapes circulares.
            horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, targetVelocity, rate * deltaTime);
        }

        private float ComputeSpeedMultiplier()
        {
            float multiplier = 1f;
            for (int i = speedModifiers.Count - 1; i >= 0; i--)
            {
                IMovementSpeedModifier modifier = speedModifiers[i];

                // Un componente destruido no es null a través de la interfaz: se comprueba como UnityEngine.Object.
                UnityEngine.Object unityObject = modifier as UnityEngine.Object;
                if (modifier == null || (unityObject is object && unityObject == null))
                {
                    speedModifiers.RemoveAt(i);
                    continue;
                }

                multiplier *= Mathf.Max(0f, modifier.SpeedMultiplier);
            }
            return multiplier;
        }

        private void UpdateRotation(Vector3 desiredDirection, float deltaTime)
        {
            if (desiredDirection.sqrMagnitude < MinDirectionSqrMagnitude) return;

            float targetYaw = Mathf.Atan2(desiredDirection.x, desiredDirection.z) * Mathf.Rad2Deg;
            float yaw = Mathf.SmoothDampAngle(transform.eulerAngles.y, targetYaw, ref rotationVelocity,
                settings.rotationSmoothTime, Mathf.Infinity, deltaTime);
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        // ----------------------------------------------------------------------------------------
        // Salto y gravedad
        // ----------------------------------------------------------------------------------------

        private void UpdateVerticalVelocity(float deltaTime)
        {
            float now = Time.time;
            if (input.JumpPressedThisFrame) lastJumpPressedTime = now;

            bool jumpBuffered = now - lastJumpPressedTime <= settings.jumpBufferTime;
            bool canUseGround = IsGrounded || now - lastGroundedTime <= settings.coyoteTime;

            if (jumpBuffered && canUseGround && settings.jumpHeight > 0f)
            {
                PerformJump(now);
                return;
            }

            if (IsGrounded && verticalVelocity <= 0f)
            {
                // Pequeña velocidad hacia abajo para que controller.isGrounded siga siendo true en pendientes.
                verticalVelocity = -settings.groundStickForce;
            }
            else
            {
                verticalVelocity = Mathf.Max(verticalVelocity - settings.gravity * deltaTime, -settings.maxFallSpeed);
            }
        }

        private void PerformJump(float now)
        {
            verticalVelocity = settings.JumpVelocity;

            // Consumir el buffer y el coyote time para impedir saltos dobles.
            lastJumpPressedTime = float.NegativeInfinity;
            lastGroundedTime = float.NegativeInfinity;

            IsGrounded = false;
            wasGrounded = false;
            airborneStartTime = now;

            if (Jumped != null) Jumped();
        }

        // ----------------------------------------------------------------------------------------
        // Estados y eventos
        // ----------------------------------------------------------------------------------------

        private void UpdateLocomotionState()
        {
            LocomotionState newState;
            if (!IsGrounded)
            {
                newState = verticalVelocity > 0f ? LocomotionState.Jumping : LocomotionState.Falling;
            }
            else
            {
                newState = HorizontalSpeed > MovingSpeedThreshold ? LocomotionState.Moving : LocomotionState.Idle;
            }

            if (newState == CurrentState) return;

            LocomotionState previous = CurrentState;
            CurrentState = newState;
            if (LocomotionStateChanged != null) LocomotionStateChanged(previous, newState);
        }

        private void ReportDirectionChangeIfNeeded(Vector3 desiredDirection)
        {
            // Sin intención de movimiento no hay dirección que comparar. Se conserva la última reportada,
            // de modo que "detenerse y salir en sentido contrario" también cuenta como cambio.
            if (desiredDirection.sqrMagnitude < MinDirectionSqrMagnitude) return;

            if (!hasReportedDirection)
            {
                lastReportedDirection = desiredDirection;
                hasReportedDirection = true;
                return;
            }

            float angle = Vector3.SignedAngle(lastReportedDirection, desiredDirection, Vector3.up);
            if (Mathf.Abs(angle) < settings.directionChangeThresholdDegrees) return;

            if (DirectionChanged != null)
            {
                DirectionChanged(new DirectionChangeEvent(lastReportedDirection, desiredDirection, angle,
                    transform.position, IsGrounded, Time.time, MovementEventClock.UnixNowMilliseconds()));
            }
            lastReportedDirection = desiredDirection;
        }

        // ----------------------------------------------------------------------------------------
        // Gizmos
        // ----------------------------------------------------------------------------------------

        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos) return;

            CharacterController cc = controller != null ? controller : GetComponent<CharacterController>();
            if (cc == null) return;

            float probeDistance = settings != null ? settings.groundProbeDistance : 0.1f;
            Vector3 center = transform.TransformPoint(cc.center);
            float halfHeight = Mathf.Max(cc.height * 0.5f, cc.radius);
            Vector3 probeCenter = center + Vector3.down * (halfHeight - cc.radius + probeDistance + cc.skinWidth);

            Gizmos.color = Application.isPlaying && IsGrounded ? Color.green : Color.red;
            Gizmos.DrawWireSphere(probeCenter, cc.radius * 0.9f);

            if (Application.isPlaying && DesiredMoveDirection.sqrMagnitude > MinDirectionSqrMagnitude)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawRay(center, DesiredMoveDirection * 1.5f);
            }
        }
    }
}
