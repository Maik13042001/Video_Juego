using System;
using System.IO;
using UnityEngine;

namespace EscapeAnalytics.Gameplay
{
    /// <summary>
    /// Persistencia local del inventario en JSON dentro de Application.persistentDataPath.
    ///
    /// Escritura "segura": se escribe a un .tmp, se respalda el archivo anterior en .bak
    /// y luego se mueve el .tmp al definitivo. Si el juego se cierra a mitad de escritura
    /// o el archivo se corrompe, Load() cae automáticamente al .bak.
    /// </summary>
    public static class InventorySaveSystem
    {
        const string FilePrefix = "inventory_";

        public static string GetPath(string profileId)
        {
            string safe = Sanitize(profileId);
            return Path.Combine(Application.persistentDataPath, $"{FilePrefix}{safe}.json");
        }

        public static bool Save(string profileId, InventorySaveData data)
        {
            if (data == null) return false;

            try
            {
                string path = GetPath(profileId);
                string tmp = path + ".tmp";
                string bak = path + ".bak";

                data.version = InventorySaveData.CurrentVersion;
                data.savedAtUtc = DateTime.UtcNow.ToString("o");
                string json = JsonUtility.ToJson(data, true);

                File.WriteAllText(tmp, json);

                if (File.Exists(path))
                {
                    File.Copy(path, bak, true);
                    File.Delete(path);
                }
                File.Move(tmp, path);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[InventorySave] Error al guardar: {e.Message}");
                return false;
            }
        }

        /// <summary>Devuelve null si no existe guardado o si ambos archivos están dañados.</summary>
        public static InventorySaveData Load(string profileId)
        {
            string path = GetPath(profileId);

            var data = TryRead(path);
            if (data != null) return data;

            data = TryRead(path + ".bak");
            if (data != null)
                Debug.LogWarning("[InventorySave] Archivo principal dañado o ausente; se restauró desde .bak");

            return data;
        }

        public static bool Exists(string profileId) => File.Exists(GetPath(profileId));

        public static void Delete(string profileId)
        {
            string path = GetPath(profileId);
            foreach (var p in new[] { path, path + ".bak", path + ".tmp" })
            {
                try { if (File.Exists(p)) File.Delete(p); }
                catch (Exception e) { Debug.LogWarning($"[InventorySave] No se pudo borrar {p}: {e.Message}"); }
            }
        }

        static InventorySaveData TryRead(string path)
        {
            if (!File.Exists(path)) return null;

            try
            {
                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json)) return null;

                var data = JsonUtility.FromJson<InventorySaveData>(json);
                if (data == null) return null;

                // Punto de extensión para migraciones futuras:
                // if (data.version < 2) { ... }
                data.slots ??= new System.Collections.Generic.List<InventorySlotData>();
                data.collectedPickupIds ??= new System.Collections.Generic.List<string>();
                return data;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[InventorySave] No se pudo leer {path}: {e.Message}");
                return null;
            }
        }

        static string Sanitize(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "default";
            foreach (char c in Path.GetInvalidFileNameChars())
                value = value.Replace(c, '_');
            return value;
        }
    }
}
