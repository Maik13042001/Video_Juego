// -----------------------------------------------------------------------
// ESTE ARCHIVO ES UN EJEMPLO de cómo llamar al endpoint de telemetría
// DESDE UNITY, usando Newtonsoft.Json para poder mandar un Payload
// libre (Dictionary<string, object>) igual que lo espera el servidor.
//
// PASO PREVIO EN UNITY (una sola vez):
// 1. Ve a Window > Package Manager.
// 2. Click en "+" > "Add package by name..."
// 3. Escribe: com.unity.nuget.newtonsoft-json
// 4. Click en "Add". Esto instala Json.NET dentro de Unity.
//
// Cómo usarlo:
// 1. Crea un GameObject vacío en la escena (ej. "TelemetryManager").
// 2. Crea este script como "TelemetryClient.cs" dentro de Assets/Scripts.
// 3. Arrástralo como componente sobre ese GameObject.
// 4. Desde cualquier otro script, llama por ejemplo a:
//    var payload = new Dictionary<string, object> { { "nivel", 3 }, { "causa", "caida" } };
//    FindObjectOfType<TelemetryClient>().EnviarEvento("player_death", "jugador123", payload);
// -----------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

public class TelemetryClient : MonoBehaviour
{
    // Cambia esto por la URL real de tu servidor cuando lo despliegues.
    private const string BaseUrl = "https://localhost:5001/api/v1/telemetry/events";

    // Esta clase representa el evento con la MISMA forma que espera
    // el servidor (EventType, PlayerId, Timestamp, Payload).
    // Con Newtonsoft sí podemos usar un Dictionary<string, object> libre.
    private class TelemetryEventDTO
    {
        public string EventType { get; set; }
        public string PlayerId { get; set; }
        public DateTime Timestamp { get; set; }
        public Dictionary<string, object> Payload { get; set; }
    }

    public void EnviarEvento(string eventType, string playerId, Dictionary<string, object> payload = null)
    {
        var evento = new TelemetryEventDTO
        {
            EventType = eventType,
            PlayerId = playerId,
            Timestamp = DateTime.UtcNow,
            Payload = payload
        };

        StartCoroutine(EnviarEventoCoroutine(evento));
    }

    private IEnumerator EnviarEventoCoroutine(TelemetryEventDTO evento)
    {
        // JsonConvert.SerializeObject sí sabe convertir el diccionario
        // Payload a JSON anidado correctamente, a diferencia de JsonUtility.
        string json = JsonConvert.SerializeObject(evento);
        byte[] bodyBytes = Encoding.UTF8.GetBytes(json);

        using (var request = new UnityWebRequest(BaseUrl, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(bodyBytes);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                Debug.Log($"Telemetría enviada: {request.downloadHandler.text}");
            }
            else
            {
                Debug.LogWarning($"Error al enviar telemetría ({request.responseCode}): {request.error}");
                Debug.LogWarning($"Respuesta del servidor: {request.downloadHandler.text}");
            }
        }
    }
}

// -----------------------------------------------------------------------
// EJEMPLO DE USO dentro de otro script del juego, cuando el jugador muere:
//
// var payload = new Dictionary<string, object>
// {
//     { "nivel", 3 },
//     { "causa", "caida" },
//     { "posicion", new { x = 10.5f, y = 2.0f, z = -3.2f } }
// };
//
// FindObjectOfType<TelemetryClient>().EnviarEvento("player_death", "jugador123", payload);
// -----------------------------------------------------------------------
