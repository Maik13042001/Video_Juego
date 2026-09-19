using System;

[Serializable]
public class LoginRequest
{
    public string username;
    public string password;
}

[Serializable]
public class LoginResponse
{
    public string message;
    public string token;
    public string session_id;
}

[Serializable]
public class TelemetryEvent
{
    public string session_id;
    public string player_id;
    public long timestamp;
    public string level_id;
    public string event_type;
    public string payload_json; // Se envia serializado
}