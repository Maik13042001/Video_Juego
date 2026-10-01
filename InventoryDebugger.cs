using System.Collections.Generic;
using UnityEngine;

namespace EscapeAnalytics.Gameplay
{
    /// <summary>
    /// Panel de depuración (solo Editor y Development Build) para probar el inventario y la
    /// persistencia sin necesidad de tener pickups en la escena. Útil para QA.
    /// Usa IMGUI, así que funciona con cualquier configuración de Input.
    /// </summary>
    public class InventoryDebugger : MonoBehaviour
    {
        [SerializeField] List<ItemData> testItems = new List<ItemData>();
        [SerializeField] bool visible = true;

        void OnGUI()
        {
            // Solo Editor y Development Build (se comprueba en runtime)
            if (!visible || !(Application.isEditor || Debug.isDebugBuild)) return;
            var inv = PlayerInventory.Instance;
            if (inv == null) return;

            GUILayout.BeginArea(new Rect(10, 10, 300, 360), GUI.skin.box);
            GUILayout.Label("DEBUG INVENTARIO");

            foreach (var it in testItems)
                if (it != null && GUILayout.Button($"+1 {it.displayName}"))
                    inv.AddItem(it);

            GUILayout.Space(6);
            if (GUILayout.Button("Guardar ahora")) inv.SaveNow();
            if (GUILayout.Button("Cargar del disco")) inv.LoadNow();
            if (GUILayout.Button("Borrar guardado y vaciar")) inv.ResetProgress(true);

            GUILayout.Space(6);
            GUILayout.Label($"Guardado existe: {InventorySaveSystem.Exists(inv.ProfileId)}");
            GUILayout.Label(InventorySaveSystem.GetPath(inv.ProfileId));
            GUILayout.EndArea();
        }
    }
}
