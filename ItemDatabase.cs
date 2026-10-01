using System.Collections.Generic;
using UnityEngine;

namespace EscapeAnalytics.Gameplay
{
    /// <summary>
    /// Catálogo de todos los ItemData del juego. Permite reconstruir el inventario
    /// desde el archivo de guardado (que solo almacena itemId + cantidad).
    /// Crear con: Create > Escape Analytics > Item Database
    /// </summary>
    [CreateAssetMenu(fileName = "ItemDatabase", menuName = "Escape Analytics/Item Database")]
    public class ItemDatabase : ScriptableObject
    {
        public List<ItemData> items = new List<ItemData>();

        Dictionary<string, ItemData> _lookup;

        public ItemData Get(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return null;
            BuildLookup();
            _lookup.TryGetValue(itemId, out var data);
            return data;
        }

        void BuildLookup()
        {
            if (_lookup != null) return;
            _lookup = new Dictionary<string, ItemData>();
            foreach (var item in items)
            {
                if (item == null || string.IsNullOrEmpty(item.itemId)) continue;
                if (!_lookup.TryAdd(item.itemId, item))
                    Debug.LogWarning($"[ItemDatabase] itemId duplicado: '{item.itemId}' ({item.name})", this);
            }
        }

        void OnEnable() => _lookup = null;
        void OnValidate() => _lookup = null;
    }
}
