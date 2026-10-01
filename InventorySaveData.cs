using System;
using System.Collections.Generic;

namespace EscapeAnalytics.Gameplay
{
    [Serializable]
    public class InventorySlotData
    {
        public string itemId;   // vacío = slot libre
        public int quantity;
    }

    /// <summary>
    /// Lo que se escribe en disco (JSON). Guardamos solo IDs y cantidades,
    /// nunca referencias a assets, para que el formato sea estable y
    /// luego pueda sincronizarse con MongoDB sin cambios.
    /// </summary>
    [Serializable]
    public class InventorySaveData
    {
        public const int CurrentVersion = 1;

        public int version = CurrentVersion;
        public string savedAtUtc;
        public List<InventorySlotData> slots = new List<InventorySlotData>();

        /// <summary>IDs de pickups ya recogidos, para que no reaparezcan al recargar.</summary>
        public List<string> collectedPickupIds = new List<string>();
    }
}
