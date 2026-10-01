using UnityEngine;

namespace EscapeAnalytics.Gameplay
{
    public enum ItemType
    {
        EnergyCore,   // Núcleo de energía
        Key,          // Llave
        Note,         // Nota / pista
        Consumable
    }

    /// <summary>
    /// Definición de un objeto del juego (un asset por tipo de objeto).
    /// Crear con: clic derecho en Project > Create > Escape Analytics > Item Data
    /// </summary>
    [CreateAssetMenu(fileName = "Item_", menuName = "Escape Analytics/Item Data")]
    public class ItemData : ScriptableObject
    {
        [Tooltip("ID único y estable (ej: energy_core, key_red). NO cambiarlo una vez en uso: " +
                 "es la clave que se guarda en disco y la que usará la telemetría.")]
        public string itemId;

        public string displayName = "Nuevo objeto";
        [TextArea] public string description;
        public ItemType type = ItemType.EnergyCore;

        [Header("Visual")]
        public Sprite icon;
        [Tooltip("Color del cuadro si no hay icono asignado.")]
        public Color fallbackColor = Color.white;

        [Header("Stack")]
        public bool stackable = true;
        [Min(1)] public int maxStack = 99;

        [Header("Uso")]
        [Tooltip("Si es true, se gasta 1 unidad al usarlo desde el inventario (tecla 1-8).")]
        public bool consumeOnUse = false;

        public int MaxStack => stackable ? Mathf.Max(1, maxStack) : 1;

#if UNITY_EDITOR
        void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(itemId))
                itemId = name.ToLowerInvariant().Replace(' ', '_');
        }
#endif
    }
}
