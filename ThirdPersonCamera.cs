using EscapeAnalytics.Player;
using UnityEngine;

namespace EscapeAnalytics.Cameras
{
    /// <summary>
    /// Cámara en tercera persona sobre el hombro que sigue al jugador y orbita con el mouse o el joystick derecho.
    ///
    /// Modelo geométrico (calculado en cada LateUpdate):
    ///
    ///   pivote   = posición del jugador + (0, pivotHeight, 0), con suavizado
    ///   hombro   = pivote + derecha(yaw) * shoulderOffset         (ajustado si hay pared al costado)
    ///   cámara   = hombro - adelante(yaw, pitch) * distancia       (acortada si hay obstáculo)
    ///   rotación = Euler(pitch, yaw, 0)
    ///
    /// La colisión se resuelve con dos SphereCast: pivote -> hombro y hombro -> cámara. El primero evita
    /// que el desplazamiento lateral meta la cámara dentro de una pared cercana; el segundo acerca la
    /// cámara cuando algo se interpone entre ella y el jugador (criterio de aceptación "sin obstrucciones").
    ///
    /// Se ejecuta en LateUpdate para leer la posición final del jugador, que se mueve en Update.
    ///
    /// Por qué una cámara propia y no Cinemachine: el requisito del equipo es control total del código y
    /// ajuste en caliente. Esta clase concentra todo el comportamiento en un archivo legible, sin
    /// dependencias de versión. Si más adelante se migra a Cinemachine, el patrón es el mismo
    /// (seguir un pivote y rotarlo con la entrada), por lo que la migración es directa.
    /// </summary>
    [DisallowMultipleComponent]
    public class ThirdPersonCamera : MonoBehaviour
    {
        private const int CastBufferSize = 16;

        [Header("Referencias")]
        [Tooltip("Transform del jugador que la cámara sigue (el GameObject con el CharacterController).")]
        [SerializeField] private Transform target;

        [Tooltip("Lector de entrada del jugador. Si se deja vacío se busca en el objetivo.")]
        [SerializeField] private PlayerInputReader input;

        [Tooltip("Asset con los parámetros de la cámara. Se puede editar durante Play Mode y los cambios persisten.")]
        [SerializeField] private ThirdPersonCameraSettings settings;

        [Header("Depuración")]
        [Tooltip("Dibujar en la vista Scene el pivote, el punto del hombro y la esfera de colisión.")]
        [SerializeField] private bool drawGizmos = true;

        private readonly RaycastHit[] castResults = new RaycastHit[CastBufferSize];

        private float yaw;
        private float pitch;
        private Vector3 smoothedPivot;
        private Vector3 pivotVelocity;          // Referencias internas de Mathf.SmoothDamp por eje.
        private float currentDistance;
        private float targetShoulderSide = 1f;  // +1 hombro derecho, -1 hombro izquierdo.
        private float currentShoulderSide = 1f;

        private Vector3 debugShoulderPoint;
        private bool debugCollisionActive;

        // ----------------------------------------------------------------------------------------
        // API pública
        // ----------------------------------------------------------------------------------------

        /// <summary>Ángulo horizontal actual en grados (0 = mirando hacia +Z del mundo).</summary>
        public float Yaw
        {
            get { return yaw; }
        }

        /// <summary>Ángulo vertical actual en grados (positivo = mirando hacia abajo).</summary>
        public float Pitch
        {
            get { return pitch; }
        }

        /// <summary>Si la cámara está acortada por un obstáculo en este frame.</summary>
        public bool IsCollisionActive
        {
            get { return debugCollisionActive; }
        }

        /// <summary>Objetivo que la cámara sigue. Al cambiarlo, la cámara se reposiciona sin transición.</summary>
        public Transform Target
        {
            get { return target; }
            set
            {
                target = value;
                if (target != null) SnapToTarget();
            }
        }

        /// <summary>
        /// Coloca la cámara en su posición final sin suavizado. Llamar después de teletransportar al jugador
        /// para evitar un barrido de cámara por todo el nivel.
        /// </summary>
        public void SnapToTarget()
        {
            if (target == null) return;
            smoothedPivot = GetPivotGoal();
            pivotVelocity = Vector3.zero;
            currentDistance = settings.distance;
            UpdateCameraTransform(0f, true);
        }

        // ----------------------------------------------------------------------------------------
        // Ciclo de vida de Unity
        // ----------------------------------------------------------------------------------------

        private void Awake()
        {
            if (settings == null)
            {
                Debug.LogWarning("[ThirdPersonCamera] No hay ThirdPersonCameraSettings asignado. " +
                                 "Se usan valores por defecto en memoria (los cambios no se guardarán).", this);
                settings = ScriptableObject.CreateInstance<ThirdPersonCameraSettings>();
            }
        }

        private void Start()
        {
            if (target == null)
            {
                Debug.LogError("[ThirdPersonCamera] Falta asignar el Target (el jugador). La cámara se desactiva.", this);
                enabled = false;
                return;
            }

            if (input == null) input = target.GetComponent<PlayerInputReader>();
            if (input == null)
            {
                Debug.LogWarning("[ThirdPersonCamera] No se encontró PlayerInputReader. La cámara seguirá al jugador " +
                                 "pero no podrá girarse.", this);
            }

            // Arrancar detrás del jugador, mirando hacia donde él mira.
            yaw = target.eulerAngles.y;
            pitch = Mathf.Clamp(settings.initialPitch, settings.minPitch, settings.maxPitch);
            targetShoulderSide = currentShoulderSide = 1f;
            SnapToTarget();
        }

        private void LateUpdate()
        {
            if (target == null) return;

            float deltaTime = Time.deltaTime;
            ReadLookInput(deltaTime);
            UpdateShoulderSide(deltaTime);
            UpdateCameraTransform(deltaTime, false);
        }

        // ----------------------------------------------------------------------------------------
        // Entrada
        // ----------------------------------------------------------------------------------------

        private void ReadLookInput(float deltaTime)
        {
            if (input == null) return;

            // Mouse: el delta ya es una distancia (píxeles), NO se multiplica por deltaTime.
            // Joystick: es una velocidad normalizada, SÍ se multiplica por deltaTime.
            Vector2 look = input.LookMouseDelta * settings.mouseSensitivity
                           + input.LookGamepad * (settings.gamepadSensitivity * deltaTime);

            yaw = Mathf.Repeat(yaw + look.x, 360f);

            // En Unity, pitch positivo inclina la cámara hacia abajo. Por defecto, mover el mouse hacia
            // arriba (delta.y positivo) debe hacer mirar hacia arriba, es decir, restar pitch.
            float pitchDelta = settings.invertY ? look.y : -look.y;
            pitch = Mathf.Clamp(pitch + pitchDelta, settings.minPitch, settings.maxPitch);

            if (settings.allowShoulderSwitch && input.SwitchShoulderPressedThisFrame)
            {
                targetShoulderSide = -targetShoulderSide;
            }
        }

        private void UpdateShoulderSide(float deltaTime)
        {
            currentShoulderSide = Mathf.MoveTowards(currentShoulderSide, targetShoulderSide,
                settings.shoulderSwitchSpeed * deltaTime);
        }

        // ----------------------------------------------------------------------------------------
        // Posicionamiento
        // ----------------------------------------------------------------------------------------

        private Vector3 GetPivotGoal()
        {
            return target.position + Vector3.up * settings.pivotHeight;
        }

        private void UpdateCameraTransform(float deltaTime, bool snap)
        {
            // 1. Pivote suavizado por eje (horizontal y vertical con tiempos distintos).
            Vector3 goal = GetPivotGoal();
            if (snap || deltaTime <= 0f)
            {
                smoothedPivot = goal;
            }
            else
            {
                smoothedPivot.x = Mathf.SmoothDamp(smoothedPivot.x, goal.x, ref pivotVelocity.x,
                    settings.horizontalFollowSmoothTime, Mathf.Infinity, deltaTime);
                smoothedPivot.z = Mathf.SmoothDamp(smoothedPivot.z, goal.z, ref pivotVelocity.z,
                    settings.horizontalFollowSmoothTime, Mathf.Infinity, deltaTime);
                smoothedPivot.y = Mathf.SmoothDamp(smoothedPivot.y, goal.y, ref pivotVelocity.y,
                    settings.verticalFollowSmoothTime, Mathf.Infinity, deltaTime);
            }

            Quaternion yawRotation = Quaternion.Euler(0f, yaw, 0f);
            Quaternion viewRotation = Quaternion.Euler(pitch, yaw, 0f);

            // 2. Punto del hombro: desplazamiento lateral, recortado si hay una pared al costado.
            Vector3 lateral = yawRotation * Vector3.right * (settings.shoulderOffset * currentShoulderSide);
            Vector3 shoulderPoint = smoothedPivot;
            float lateralLength = lateral.magnitude;
            if (lateralLength > 0.0001f)
            {
                Vector3 lateralDirection = lateral / lateralLength;
                float allowedLateral = lateralLength;
                float hitDistance;
                if (settings.collisionEnabled &&
                    SphereCastIgnoringTarget(smoothedPivot, lateralDirection, lateralLength, out hitDistance))
                {
                    allowedLateral = hitDistance;
                }
                shoulderPoint = smoothedPivot + lateralDirection * allowedLateral;
            }

            // 3. Distancia hacia atrás, recortada si algo se interpone entre el hombro y la cámara.
            Vector3 backDirection = viewRotation * Vector3.back;
            float allowedDistance = settings.distance;
            debugCollisionActive = false;
            float backHitDistance;
            if (settings.collisionEnabled &&
                SphereCastIgnoringTarget(shoulderPoint, backDirection, settings.distance, out backHitDistance))
            {
                allowedDistance = backHitDistance;
                debugCollisionActive = true;
            }

            // Acercarse es inmediato (nunca mostrar el interior de una pared); alejarse es gradual
            // (evita saltos bruscos cuando el obstáculo desaparece).
            if (snap || allowedDistance < currentDistance)
            {
                currentDistance = allowedDistance;
            }
            else
            {
                currentDistance = Mathf.MoveTowards(currentDistance, allowedDistance,
                    settings.collisionRecoverySpeed * deltaTime);
            }

            // 4. Aplicar.
            transform.SetPositionAndRotation(shoulderPoint + backDirection * currentDistance, viewRotation);
            debugShoulderPoint = shoulderPoint;
        }

        /// <summary>
        /// SphereCast que ignora los colliders del jugador (el objetivo y sus hijos) y los triggers.
        /// Devuelve la distancia al impacto más cercano, que es la distancia segura para el centro de la esfera.
        /// </summary>
        private bool SphereCastIgnoringTarget(Vector3 origin, Vector3 direction, float maxDistance, out float hitDistance)
        {
            hitDistance = maxDistance;
            if (maxDistance <= 0f) return false;

            int count = Physics.SphereCastNonAlloc(origin, settings.collisionRadius, direction, castResults,
                maxDistance, settings.collisionLayers, QueryTriggerInteraction.Ignore);

            bool found = false;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = castResults[i];
                if (hit.collider == null) continue;
                if (hit.collider.transform.IsChildOf(target)) continue; // IsChildOf también es true para el propio target.

                // Un collider que ya se solapaba con la esfera al inicio se reporta con distancia 0.
                if (hit.distance < hitDistance)
                {
                    hitDistance = hit.distance;
                    found = true;
                }
            }
            return found;
        }

        // ----------------------------------------------------------------------------------------
        // Gizmos
        // ----------------------------------------------------------------------------------------

        private void OnDrawGizmos()
        {
            if (!drawGizmos || !Application.isPlaying || target == null || settings == null) return;

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(smoothedPivot, 0.05f);
            Gizmos.DrawLine(smoothedPivot, debugShoulderPoint);

            Gizmos.color = debugCollisionActive ? Color.red : Color.green;
            Gizmos.DrawLine(debugShoulderPoint, transform.position);
            Gizmos.DrawWireSphere(transform.position, settings.collisionRadius);
        }
    }
}
