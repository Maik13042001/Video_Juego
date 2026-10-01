using System;
using System.Collections.Generic;
using UnityEngine;

namespace EscapeAnalytics.Gameplay
{
    [Serializable]
    public class InventorySlot
    {
        public ItemData item;
        public int quantity;

        public bool IsEmpty => item == null || quantity <= 0;
        public void Clear() { item = null; quantity = 0; }
    }

    /// <summary>
    /// Inventario del jugador: lógica + eventos (patrón Observer) + persistencia local.
    ///
    /// La UI y, más adelante, el módulo de telemetría (Sprint 4) se suscriben a los eventos
    /// sin que esta clase sepa nada de ellos (cumple RNF "Modificabilidad").
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class PlayerInventory : MonoBehaviour
    {
        public static PlayerInventory Instance { get; private set; }

        [Header("Configuración")]
        [SerializeField] ItemDatabase database;
        [SerializeField, Min(1)] int capacity = 8;
        [Tooltip("Identifica el archivo de guardado. Más adelante puede ser el player_id.")]
        [SerializeField] string profileId = "default";

        [Header("Persistencia local")]
        [SerializeField] bool loadOnAwake = true;
        [SerializeField] bool autoSave = true;
        [Tooltip("Espera tras el último cambio antes de escribir a disco (agrupa varios cambios seguidos).")]
        [SerializeField, Min(0f)] float saveDelaySeconds = 0.5f;

        // ---------- Eventos (Observer) ----------
        /// <summary>Se dispara una vez por operación, después de cualquier cambio. La UI se refresca aquí.</summary>
        public event Action OnInventoryChanged;
        /// <summary>item, cantidad añadida a ese slot, índice del slot.</summary>
        public event Action<ItemData, int, int> OnItemAdded;
        /// <summary>item, cantidad removida.</summary>
        public event Action<ItemData, int> OnItemRemoved;
        /// <summary>item, índice del slot desde el que se usó.</summary>
        public event Action<ItemData, int> OnItemUsed;
        /// <summary>No cupo (todo o parte) del item que se intentó añadir.</summary>
        public event Action<ItemData> OnInventoryFull;

        readonly List<InventorySlot> _slots = new List<InventorySlot>();
        readonly HashSet<string> _collectedPickups = new HashSet<string>();
        bool _dirty;
        float _saveTimer;

        public IReadOnlyList<InventorySlot> Slots => _slots;
        public int Capacity => _slots.Count;
        public string ProfileId => profileId;
        public ItemDatabase Database => database;

        // =====================================================================
        // Ciclo de vida
        // =====================================================================
        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[PlayerInventory] Ya existe otra instancia; se ignora esta.", this);
                Destroy(this);
                return;
            }
            Instance = this;

            for (int i = 0; i < capacity; i++) _slots.Add(new InventorySlot());

            if (database == null)
                Debug.LogError("[PlayerInventory] Falta asignar el ItemDatabase: no se podrá cargar el guardado.", this);

            if (loadOnAwake) LoadNow();
        }

        void Update()
        {
            if (!_dirty || !autoSave) return;

            _saveTimer -= Time.unscaledDeltaTime;
            if (_saveTimer <= 0f) SaveNow();
        }

        void OnApplicationPause(bool paused)
        {
            if (paused && _dirty && autoSave) SaveNow();
        }

        void OnApplicationQuit()
        {
            if (_dirty && autoSave) SaveNow();
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            if (_dirty && autoSave) SaveNow();   // al cambiar de escena no se pierde nada
            Instance = null;
        }

        // =====================================================================
        // Consultas
        // =====================================================================
        public int Count(ItemData item)
        {
            if (item == null) return 0;
            int total = 0;
            foreach (var s in _slots)
                if (!s.IsEmpty && s.item == item) total += s.quantity;
            return total;
        }

        public int Count(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return 0;
            int total = 0;
            foreach (var s in _slots)
                if (!s.IsEmpty && s.item.itemId == itemId) total += s.quantity;
            return total;
        }

        public bool Has(string itemId, int quantity = 1) => Count(itemId) >= quantity;
        public bool Has(ItemData item, int quantity = 1) => Count(item) >= quantity;

        public int FreeSpaceFor(ItemData item)
        {
            if (item == null) return 0;
            int space = 0;
            foreach (var s in _slots)
            {
                if (s.IsEmpty) space += item.MaxStack;
                else if (item.stackable && s.item == item) space += item.MaxStack - s.quantity;
            }
            return space;
        }

        public bool CanAdd(ItemData item, int quantity = 1) =>
            item != null && quantity > 0 && FreeSpaceFor(item) >= quantity;

        // =====================================================================
        // Operaciones
        // =====================================================================
        /// <summary>Añade items. Devuelve cuántos se añadieron realmente (puede ser menos si se llena).</summary>
        public int AddItem(ItemData item, int quantity = 1)
        {
            if (item == null || quantity <= 0) return 0;

            int remaining = quantity;

            // 1) Completar stacks existentes
            if (item.stackable)
            {
                for (int i = 0; i < _slots.Count && remaining > 0; i++)
                {
                    var s = _slots[i];
                    if (s.IsEmpty || s.item != item) continue;

                    int space = item.MaxStack - s.quantity;
                    if (space <= 0) continue;

                    int add = Mathf.Min(space, remaining);
                    s.quantity += add;
                    remaining -= add;
                    OnItemAdded?.Invoke(item, add, i);
                }
            }

            // 2) Usar slots vacíos
            for (int i = 0; i < _slots.Count && remaining > 0; i++)
            {
                var s = _slots[i];
                if (!s.IsEmpty) continue;

                int add = Mathf.Min(item.MaxStack, remaining);
                s.item = item;
                s.quantity = add;
                remaining -= add;
                OnItemAdded?.Invoke(item, add, i);
            }

            int added = quantity - remaining;
            if (remaining > 0) OnInventoryFull?.Invoke(item);
            if (added > 0) NotifyChanged(persist: true);
            return added;
        }

        public bool RemoveItem(ItemData item, int quantity = 1)
        {
            if (item == null || quantity <= 0 || Count(item) < quantity) return false;

            int remaining = quantity;
            for (int i = _slots.Count - 1; i >= 0 && remaining > 0; i--)
            {
                var s = _slots[i];
                if (s.IsEmpty || s.item != item) continue;

                int take = Mathf.Min(s.quantity, remaining);
                s.quantity -= take;
                remaining -= take;
                if (s.quantity <= 0) s.Clear();
            }

            OnItemRemoved?.Invoke(item, quantity);
            NotifyChanged(persist: true);
            return true;
        }

        /// <summary>Útil para puertas: <c>if (inv.RemoveItem("key_red")) AbrirPuerta();</c></summary>
        public bool RemoveItem(string itemId, int quantity = 1)
        {
            ItemData item = FindItem(itemId);
            return item != null && RemoveItem(item, quantity);
        }

        public bool RemoveAt(int slotIndex, int quantity = 1)
        {
            if (slotIndex < 0 || slotIndex >= _slots.Count || quantity <= 0) return false;
            var s = _slots[slotIndex];
            if (s.IsEmpty) return false;

            var item = s.item;
            int take = Mathf.Min(s.quantity, quantity);
            s.quantity -= take;
            if (s.quantity <= 0) s.Clear();

            OnItemRemoved?.Invoke(item, take);
            NotifyChanged(persist: true);
            return true;
        }

        /// <summary>Usa el item del slot (hotkeys 1-8). Otros sistemas reaccionan vía OnItemUsed.</summary>
        public bool UseSlot(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= _slots.Count || _slots[slotIndex].IsEmpty) return false;

            var item = _slots[slotIndex].item;
            OnItemUsed?.Invoke(item, slotIndex);
            if (item.consumeOnUse) RemoveAt(slotIndex, 1);
            return true;
        }

        ItemData FindItem(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return null;
            foreach (var s in _slots)
                if (!s.IsEmpty && s.item.itemId == itemId) return s.item;
            return database != null ? database.Get(itemId) : null;
        }

        // =====================================================================
        // Pickups ya recogidos (para que no reaparezcan al recargar)
        // =====================================================================
        public bool IsPickupCollected(string pickupId) =>
            !string.IsNullOrEmpty(pickupId) && _collectedPickups.Contains(pickupId);

        public void MarkPickupCollected(string pickupId)
        {
            if (string.IsNullOrEmpty(pickupId)) return;
            if (_collectedPickups.Add(pickupId)) MarkDirty();
        }

        // =====================================================================
        // Persistencia (SCRUM-28)
        // =====================================================================
        public InventorySaveData CreateSaveData()
        {
            var data = new InventorySaveData();
            foreach (var s in _slots)
            {
                data.slots.Add(new InventorySlotData
                {
                    itemId = s.IsEmpty ? string.Empty : s.item.itemId,
                    quantity = s.IsEmpty ? 0 : s.quantity
                });
            }
            data.collectedPickupIds.AddRange(_collectedPickups);
            return data;
        }

        public bool SaveNow()
        {
            bool ok = InventorySaveSystem.Save(profileId, CreateSaveData());
            if (ok)
            {
                _dirty = false;
            }
            else
            {
                _dirty = true;
                _saveTimer = 2f;   // reintenta en 2 s
            }
            return ok;
        }

        /// <summary>Carga el guardado del disco. Devuelve false si no había archivo válido.</summary>
        public bool LoadNow()
        {
            var data = InventorySaveSystem.Load(profileId);
            if (data == null) return false;

            foreach (var s in _slots) s.Clear();
            _collectedPickups.Clear();

            if (data.slots.Count > _slots.Count)
                Debug.LogWarning($"[PlayerInventory] El guardado tiene {data.slots.Count} slots y la capacidad actual es {_slots.Count}: se descartan los sobrantes.");

            int n = Mathf.Min(data.slots.Count, _slots.Count);
            for (int i = 0; i < n; i++)
            {
                var sd = data.slots[i];
                if (sd == null || string.IsNullOrEmpty(sd.itemId) || sd.quantity <= 0) continue;

                var item = database != null ? database.Get(sd.itemId) : null;
                if (item == null)
                {
                    Debug.LogWarning($"[PlayerInventory] itemId '{sd.itemId}' no existe en el ItemDatabase; se omite.");
                    continue;
                }

                _slots[i].item = item;
                _slots[i].quantity = Mathf.Clamp(sd.quantity, 1, item.MaxStack);
            }

            foreach (var id in data.collectedPickupIds)
                if (!string.IsNullOrEmpty(id)) _collectedPickups.Add(id);

            _dirty = false;
            NotifyChanged(persist: false);
            return true;
        }

        /// <summary>Vacía el inventario y los pickups recogidos. Opcionalmente borra el archivo.</summary>
        public void ResetProgress(bool deleteSaveFile)
        {
            foreach (var s in _slots) s.Clear();
            _collectedPickups.Clear();

            if (deleteSaveFile)
            {
                InventorySaveSystem.Delete(profileId);
                _dirty = false;
                NotifyChanged(persist: false);
            }
            else
            {
                NotifyChanged(persist: true);
            }
        }

        void NotifyChanged(bool persist)
        {
            OnInventoryChanged?.Invoke();
            if (persist) MarkDirty();
        }

        void MarkDirty()
        {
            _dirty = true;
            _saveTimer = saveDelaySeconds;
            if (autoSave && saveDelaySeconds <= 0f) SaveNow();
        }

        // ---------- Atajos de depuración (clic derecho en el componente, en Play) ----------
        [ContextMenu("Debug/Guardar ahora")] void DebugSave() => SaveNow();
        [ContextMenu("Debug/Cargar del disco")] void DebugLoad() => LoadNow();
        [ContextMenu("Debug/Borrar guardado y vaciar")] void DebugReset() => ResetProgress(true);
        [ContextMenu("Debug/Mostrar ruta del archivo")]
        void DebugPath() => Debug.Log($"[PlayerInventory] Archivo: {InventorySaveSystem.GetPath(profileId)}");
    }
}
