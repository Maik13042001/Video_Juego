using System.Globalization;
using UnityEngine;

namespace EscapeAnalytics.Gameplay
{
    /// <summary>
    /// Objeto recogible en el mundo (núcleo de energía, llave, etc.).
    /// Requiere un Collider con "Is Trigger" activado (se activa solo al añadir el componente).
    ///
    /// Persistencia: cada pickup tiene un ID. Si no escribes uno manual, se genera de forma
    /// determinista con escena + itemId + posición inicial, así que duplicar el objeto (Ctrl+D)
    /// y moverlo no crea IDs repetidos. Si un pickup se instancia por código o se mueve
    /// como hijo de otro objeto, asígnale un "Manual Id" propio.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class Pickup : MonoBehaviour
    {
        [Header("Objeto")]
        [SerializeField] ItemData item;
        [SerializeField, Min(1)] int quantity = 1;

        [Header("Recolección")]
        [Tooltip("true: se recoge al acercarse (fluido bajo persecución). false: hay que presionar E.")]
        [SerializeField] bool autoCollect = true;
        [Tooltip("Opcional. Déjalo vacío para usar el ID automático.")]
        [SerializeField] string manualId;
        [SerializeField] AudioClip collectSfx;
        [SerializeField, Range(0f, 1f)] float sfxVolume = 1f;

        [Header("Animación (feedback visual)")]
        [SerializeField] bool rotate = true;
        [SerializeField] float rotateSpeed = 70f;
        [SerializeField] float bobHeight = 0.12f;
        [SerializeField] float bobSpeed = 2f;

        Vector3 _basePosition;
        bool _collected;

        public ItemData Item => item;
        public int Quantity => quantity;
        public bool AutoCollect => autoCollect;
        public bool IsCollected => _collected;
        public string PickupId { get; private set; }

        void Reset()
        {
            var col = GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
        }

        void Awake()
        {
            _basePosition = transform.position;
            PickupId = string.IsNullOrWhiteSpace(manualId) ? BuildAutoId() : manualId.Trim();

            if (item == null)
                Debug.LogWarning($"[Pickup] '{name}' no tiene ItemData asignado.", this);

            var col = GetComponent<Collider>();
            if (col != null && !col.isTrigger)
                Debug.LogWarning($"[Pickup] '{name}': el Collider debería ser Trigger.", this);
        }

        void Start()
        {
            // Si ya se recogió en una sesión anterior, no debe reaparecer.
            var inv = PlayerInventory.Instance;
            if (inv != null && inv.IsPickupCollected(PickupId))
            {
                _collected = true;
                Destroy(gameObject);
            }
        }

        void Update()
        {
            if (_collected) return;

            if (rotate) transform.Rotate(0f, rotateSpeed * Time.deltaTime, 0f, Space.World);
            if (bobHeight > 0f)
                transform.position = _basePosition + Vector3.up * (Mathf.Sin(Time.time * bobSpeed) * bobHeight);
        }

        /// <summary>
        /// Intenta pasar el objeto al inventario. Devuelve true si se recogió por completo.
        /// Si solo cupo una parte, el resto queda en el mundo (y reaparece completo al recargar la escena).
        /// </summary>
        public bool TryCollect(PlayerInventory inventory)
        {
            if (_collected || item == null || inventory == null) return false;

            int added = inventory.AddItem(item, quantity);
            if (added <= 0) return false;

            if (added < quantity)
            {
                quantity -= added;
                return false;
            }

            _collected = true;
            inventory.MarkPickupCollected(PickupId);

            if (collectSfx != null)
                AudioSource.PlayClipAtPoint(collectSfx, transform.position, sfxVolume);

            Destroy(gameObject);
            return true;
        }

        string BuildAutoId()
        {
            string itemKey = item != null ? item.itemId : "none";
            string x = _basePosition.x.ToString("F2", CultureInfo.InvariantCulture);
            string y = _basePosition.y.ToString("F2", CultureInfo.InvariantCulture);
            string z = _basePosition.z.ToString("F2", CultureInfo.InvariantCulture);
            return $"{gameObject.scene.name}/{itemKey}@{x}_{y}_{z}";
        }
    }
}
