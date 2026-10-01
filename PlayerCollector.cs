using System;
using UnityEngine;

namespace EscapeAnalytics.Gameplay
{
    /// <summary>
    /// Va en el GameObject del jugador. Escanea pickups cercanos (sin depender de Rigidbody ni
    /// de callbacks de trigger), recoge automáticamente los "autoCollect", y con la tecla E
    /// recoge el más cercano de los manuales. Las teclas 1-8 usan el item del slot.
    /// </summary>
    public class PlayerCollector : MonoBehaviour
    {
        [SerializeField] PlayerInventory inventory;
        [SerializeField, Min(0.1f)] float collectRadius = 1.8f;
        [Tooltip("Capas donde viven los pickups. Por rendimiento, crea una capa 'Pickup' y selecciónala.")]
        [SerializeField] LayerMask pickupLayers = ~0;
        [SerializeField, Min(0.02f)] float scanInterval = 0.1f;
        [SerializeField] bool enableHotkeys = true;

        /// <summary>Pickup manual más cercano (null si no hay). La UI lo usa para mostrar "[E] Recoger".</summary>
        public Pickup CurrentTarget { get; private set; }
        public event Action<Pickup> OnTargetChanged;

        readonly Collider[] _buffer = new Collider[16];
        float _nextScan;

        void Start()
        {
            if (inventory == null) inventory = PlayerInventory.Instance;
        }

        void Update()
        {
            if (inventory == null)
            {
                inventory = PlayerInventory.Instance;
                if (inventory == null) return;
            }

            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + scanInterval;
                Scan();
            }

            if (CurrentTarget != null && InputCompat.InteractPressed())
            {
                if (CurrentTarget.TryCollect(inventory)) SetTarget(null);
            }

            if (enableHotkeys)
            {
                int idx = InputCompat.PressedHotbarIndex();
                if (idx >= 0) inventory.UseSlot(idx);
            }
        }

        void Scan()
        {
            int hits = Physics.OverlapSphereNonAlloc(
                transform.position, collectRadius, _buffer, pickupLayers, QueryTriggerInteraction.Collide);

            Pickup nearest = null;
            float nearestSqr = float.MaxValue;

            for (int i = 0; i < hits; i++)
            {
                var pickup = _buffer[i].GetComponentInParent<Pickup>();
                if (pickup == null || pickup.IsCollected || !pickup.isActiveAndEnabled) continue;

                // Auto-recolección: solo si cabe completo; si no, queda como objetivo manual
                // para que la UI avise "Inventario lleno" sin spamear intentos cada 0.1 s.
                if (pickup.AutoCollect && inventory.CanAdd(pickup.Item, pickup.Quantity))
                {
                    pickup.TryCollect(inventory);
                    continue;
                }

                float sqr = (pickup.transform.position - transform.position).sqrMagnitude;
                if (sqr < nearestSqr)
                {
                    nearestSqr = sqr;
                    nearest = pickup;
                }
            }

            SetTarget(nearest);
        }

        void SetTarget(Pickup target)
        {
            if (CurrentTarget == target) return;
            CurrentTarget = target;
            OnTargetChanged?.Invoke(target);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.9f, 0.4f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, collectRadius);
        }
    }
}
