using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class TelemetryManager : MonoBehaviour
{
    [Header("Configuración del Servidor")]
    public string baseUrl = "http://127.0.0.1:3000";
    
    private string jwtToken = "";
    private string currentSessionId = "";
    private string playerId = "";

    void Start()
    {
        // Iniciar sesión automáticamente al arrancar la escena
        StartCoroutine(StartSessionRoutine("admin", "1234"));
    }

    // --- US-02: Petición de Autenticación a FastAPI ---
    public IEnumerator StartSessionRoutine(string username, string password)
    {
        string url = baseUrl + "/api/v1/sessions/start";
        
        LoginRequest reqData = new LoginRequest { username = username, password = password };
        string jsonBody = JsonUtility.ToJson(reqData);

        using (UnityWebRequest www = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
            www.uploadHandler = new UploadHandlerRaw(bodyRaw);
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/json");

            yield return www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.Success)
            {
                LoginResponse res = JsonUtility.FromJson<LoginResponse>(www.downloadHandler.text);
                jwtToken = res.token;
                currentSessionId = res.session_id;
                playerId = "usr_402";

                Debug.Log($"[Analytics] Sesión iniciada con éxito. SessionID: {currentSessionId}");
            }
            else
            {
                Debug.LogError($"[Analytics] Error al iniciar sesión: {www.error}");
            }
        }
    }

    // --- US-03: Envío de Eventos JSON con Token JWT ---
    public void TrackEvent(string eventType, string levelId, string jsonPayload)
    {
        if (string.IsNullOrEmpty(jwtToken))
        {
            Debug.LogWarning("[Analytics] Intento de envío sin token activo.");
            return;
        }

        StartCoroutine(SendEventRoutine(eventType, levelId, jsonPayload));
    }

    private IEnumerator SendEventRoutine(string eventType, string levelId, string jsonPayload)
    {
        string url = baseUrl + "/api/v1/telemetry/events";

        // Estructurar el cuerpo según el modelo de FastAPI
        string fullJson = $@"{{
            ""session_id"": ""{currentSessionId}"",
            ""player_id"": ""{playerId}"",
            ""timestamp"": {System.DateTimeOffset.UtcNow.ToUnixTimeSeconds()},
            ""level_id"": ""{levelId}"",
            ""event_type"": ""{eventType}"",
            ""payload"": {jsonPayload}
        }}";

        using (UnityWebRequest www = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(fullJson);
            www.uploadHandler = new UploadHandlerRaw(bodyRaw);
            www.downloadHandler = new DownloadHandlerBuffer();
            
            // Cabeceras exigidas por RNF-Seguridad e Interoperabilidad
            www.SetRequestHeader("Content-Type", "application/json");
            www.SetRequestHeader("Authorization", "Bearer " + jwtToken);

            yield return www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.Success)
            {
                Debug.Log($"[Analytics] Evento '{eventType}' enviado correctamente (Status 202).");
            }
            else
            {
                Debug.LogError($"[Analytics] Error enviando evento: {www.error}");
            }
        }
    }
}