# Escape Analytics 3D — Esquema de base de datos

**Tareas:** SCRUM-36 (esquema de sesiones) y SCRUM-37 (esquema de eventos) · Historia US-05 · Sprint 2
**Motor:** MongoDB · **Base de datos:** `escape_analytics` · **Colecciones:** `sessions`, `events`

> En MongoDB las "tablas" se llaman **colecciones** y las "filas" son **documentos**.
> Los nombres de campos usan `snake_case`, igual que el contrato JSON del GDD (sección 4).

---

## 1. Decisiones de diseño

| Decisión | Por qué |
|---|---|
| **Dos colecciones separadas** (`sessions` y `events`) con referencia por `session_id`, en vez de guardar los eventos dentro de la sesión | Una sesión genera miles de eventos. Un documento de Mongo tiene un límite de 16 MB y un arreglo que crece sin parar degrada las escrituras. Además el backend debe soportar 500 RPS de inserción (RNF Escalabilidad). |
| **`event_id` (UUID) generado en el cliente, con índice único** | El cliente reintenta el envío tras perder conexión y guarda hasta 1,000 eventos en caché (RNF Confiabilidad). Sin un ID único, un reintento duplicaría eventos y ensuciaría las métricas. |
| **`seq`: contador por sesión generado en el cliente** | Permite reconstruir el orden exacto de los eventos aunque lleguen desordenados en lotes o desde la caché. |
| **`occurred_at` y `received_at` separados** | El tiempo real del evento no es el de llegada al servidor. La diferencia sirve para detectar eventos que vinieron de la caché offline. |
| **`payload` con esquema distinto por `event_type`** | Cada evento mide cosas diferentes. El validador de Mongo (`$jsonSchema` + `oneOf`) revisa el payload según el tipo, sin crear una colección por evento. |
| **Colección normal, no *time series*** | Las colecciones *time series* de Mongo no admiten índices únicos, y los necesitamos para `event_id`. |
| **Sin datos secretos** | Nunca se guarda el token JWT ni la clave HMAC. Solo el `jti` y su expiración. |

### Relación entre colecciones

```
sessions                                 events
────────────────────                     ──────────────────────────
session_id  (único)   ◄────── 1 : N ──── session_id
player_id                                player_id
level_id                                 level_id
status, started_at...                    event_type, seq, payload...
metrics (agregados)
```

MongoDB no impone llaves foráneas. El backend debe comprobar que la sesión exista y esté `active` antes de aceptar eventos.

---

## 2. Colección `sessions` (SCRUM-36)

Un documento por partida/sesión del jugador.

| Campo | Tipo | Obligatorio | Descripción |
|---|---|---|---|
| `_id` | ObjectId | auto | Identificador interno de Mongo |
| `session_id` | string | Sí | ID del contrato (ej. `ses_9901_exp`). **Único** |
| `player_id` | string | Sí | Jugador (ej. `usr_402`). Referencia lógica a usuarios (US-03) |
| `level_id` | string | Sí | Nivel con el que inicia la sesión |
| `status` | string | Sí | `active`, `completed` o `abandoned` |
| `started_at` | date | Sí | Inicio de la sesión |
| `ended_at` | date / null | No | Fin de la sesión |
| `last_event_at` | date / null | No | Último evento recibido (sirve para marcar sesiones `abandoned`) |
| `client` | objeto | Sí | `app_version` y `platform` obligatorios; `os`, `device_model`, `unity_version` opcionales |
| `experiment` | objeto / null | No | `group` y `condition` para comparar condiciones del estudio |
| `auth` | objeto | No | `jti` y `expires_at` del JWT. **Nunca el token** |
| `outcome` | objeto / null | No | `result` (`escaped`, `caught`, `timeout`, `quit`) y `duration_seconds` |
| `metrics` | objeto | No | Agregados para el dashboard: `events_count`, `route_decisions`, `optimal_routes`, `hints_read`, `panic_high_count`, `iec_score` |
| `schema_version` | int | Sí | Versión del esquema (hoy `1`) |
| `created_at` / `updated_at` | date | Sí | Auditoría |

### Índices de `sessions`

| Nombre | Campos | Para qué |
|---|---|---|
| `uniq_session_id` | `{ session_id: 1 }` único | Evitar sesiones duplicadas y buscar por ID |
| `by_player_recent` | `{ player_id: 1, started_at: -1 }` | Historial de un jugador |
| `by_status_recent` | `{ status: 1, started_at: -1 }` | Encontrar sesiones activas o abandonadas |

### Ejemplo

```json
{
  "session_id": "ses_9901_exp",
  "player_id": "usr_402",
  "level_id": "level_01_industrial",
  "status": "active",
  "started_at": "2026-09-04T00:35:15Z",
  "ended_at": null,
  "last_event_at": "2026-09-04T00:40:15Z",
  "client": { "app_version": "0.1.0", "platform": "windows", "unity_version": "6000.0" },
  "experiment": { "group": "A", "condition": "chaser_on" },
  "outcome": null,
  "metrics": { "events_count": 1, "route_decisions": 1, "optimal_routes": 0,
               "hints_read": 0, "panic_high_count": 1, "iec_score": null },
  "schema_version": 1,
  "created_at": "2026-09-04T00:35:15Z",
  "updated_at": "2026-09-04T00:40:15Z"
}
```

---

## 3. Colección `events` (SCRUM-37)

Un documento por evento de telemetría. Es la colección que más crece.

| Campo | Tipo | Obligatorio | Origen | Descripción |
|---|---|---|---|---|
| `_id` | ObjectId | auto | Mongo | Identificador interno |
| `event_id` | string (UUID) | Sí | Cliente* | Identificador único del evento. **Índice único** |
| `session_id` | string | Sí | Cliente | Sesión a la que pertenece |
| `player_id` | string | Sí | Cliente | Jugador |
| `level_id` | string | Sí | Cliente | Nivel donde ocurrió |
| `event_type` | string | Sí | Cliente | Uno de los tipos de la sección 3.1 |
| `seq` | número | Sí | Cliente* | Contador incremental por sesión |
| `occurred_at` | date | Sí | Cliente | Momento real (convertido desde `timestamp` del contrato) |
| `received_at` | date | Sí | Servidor | Momento de recepción |
| `payload` | objeto | Sí | Cliente | Datos propios del tipo de evento |
| `ingest.batch_id` | string | Sí | Servidor | Lote en el que llegó |
| `ingest.from_offline_cache` | bool | Sí | Cliente | `true` si se reenvió desde la caché local |
| `schema_version` | int | Sí | Servidor | Versión del esquema (hoy `1`) |

\* Campos nuevos respecto al contrato del GDD. Ver sección 5.

### 3.1 Tipos de evento y su `payload`

| `event_type` | Qué mide (RF-04) | Campos del payload (obligatorios en negrita) |
|---|---|---|
| `route_decision` | Rutas elegidas | **`fork_id`**, **`chosen_path`**, **`is_optimal_path`**, **`has_seen_hint`**, **`chaser_distance_meters`**, **`panic_index_level`** (`LOW`/`MEDIUM`/`HIGH`) |
| `item_collected` | Uso de recursos (recolección) | **`item_id`**, **`quantity`**, `pickup_id`, `chaser_distance_meters` |
| `item_used` | Uso de recursos (inventario) | **`item_id`**, **`slot_index`**, `consumed`, `chaser_distance_meters` |
| `hint_read` | Tiempos de lectura de pistas | **`hint_id`**, **`read_time_ms`**, `hint_type` (`monitor`/`wall_sign`/`note`/`puzzle`), `chaser_distance_meters` |
| `panic_direction_change` | Cambios de dirección por pánico | **`angle_delta_deg`**, **`window_ms`**, `changes_in_window`, `chaser_distance_meters`, `panic_index_level` |

`route_decision` sale tal cual del contrato del GDD. Los otros cuatro son **propuestas** que cubren lo que pide RF-04, y `item_collected` / `item_used` se conectan con los eventos del inventario que ya implementaste en Unity (`OnItemAdded`, `OnItemUsed`).

### 3.2 Índices de `events`

| Nombre | Campos | Para qué |
|---|---|---|
| `uniq_event_id` | `{ event_id: 1 }` único | Idempotencia: un reintento no duplica el evento |
| `by_session_seq` | `{ session_id: 1, seq: 1 }` | Leer los eventos de una sesión en orden |
| `by_session_type_time` | `{ session_id: 1, event_type: 1, occurred_at: 1 }` | Filtros del dashboard por tipo y tiempo |
| `by_player_time` | `{ player_id: 1, occurred_at: -1 }` | Historial de un jugador |

Se dejaron solo cuatro índices a propósito: cada índice extra hace más lentas las inserciones, y la meta es 500 RPS.

### 3.3 Ejemplo (contrato del GDD ya convertido a documento)

```json
{
  "event_id": "3f6c1a52-9d0e-4c1b-8a47-2b9e6d1f0c33",
  "session_id": "ses_9901_exp",
  "player_id": "usr_402",
  "level_id": "level_01_industrial",
  "event_type": "route_decision",
  "seq": 1,
  "occurred_at": "2026-09-04T00:40:15Z",
  "received_at": "2026-09-04T00:40:16Z",
  "payload": {
    "fork_id": "corridor_junction_01",
    "chosen_path": "path_locked_door",
    "is_optimal_path": false,
    "has_seen_hint": true,
    "chaser_distance_meters": 4.2,
    "panic_index_level": "HIGH"
  },
  "ingest": { "batch_id": "bat_0001", "from_offline_cache": false },
  "schema_version": 1
}
```

---

## 4. Cómo viaja un evento desde el juego

1. Unity genera el evento con `event_id`, `seq` y los campos del contrato.
2. Se envía en lote a `POST /api/v1/telemetry/events`, con el JWT y la firma HMAC-SHA256 en la cabecera.
3. El backend verifica JWT y firma. Si fallan, rechaza el lote completo y **no guarda nada**.
4. Comprueba que `session_id` exista y esté `active`.
5. Convierte `timestamp` a `occurred_at`, añade `received_at`, `ingest` y `schema_version`.
6. Inserta con `insertMany(..., { ordered: false })`. Así, un evento inválido o duplicado no bloquea al resto del lote (RNF Robustez). Los errores de clave duplicada en `event_id` se ignoran, porque significan que ya se había guardado.
7. Actualiza `last_event_at` y los contadores de `metrics` en la sesión.

---

## 5. Cambios propuestos al contrato JSON (hay que acordarlos con el equipo)

Estos puntos son decisiones de diseño mías. Conviene confirmarlos con el Data/Telemetry Engineer antes de implementar la telemetría (Sprint 4):

1. **Agregar `event_id` (UUID) y `seq` al contrato.** Sin ellos no hay idempotencia ni orden confiable.
2. **Enviar `timestamp` en milisegundos.** El ejemplo del GDD usa segundos, y para medir tiempos de lectura de pistas y cambios de dirección por pánico un segundo es demasiado grueso. Si se decide mantener segundos, el esquema funciona igual (`occurred_at` queda con precisión de segundos).
3. **Valores de `panic_index_level`.** El GDD solo muestra `HIGH`. Asumí `LOW`, `MEDIUM` y `HIGH`.
4. **Tipos de evento nuevos.** `item_collected`, `item_used`, `hint_read` y `panic_direction_change` son propuestas basadas en RF-04.
5. **Fórmula del IEC.** El GDD no la define. Por eso `metrics.iec_score` queda en `null` hasta que se acuerde (US-15).

---

## 6. Agregar un tipo de evento nuevo

Edita el objeto `payloadSchemas` en `setup_mongodb.js` y vuelve a ejecutar el script. Como actualiza el validador con `collMod`, los datos existentes no se tocan.

---

## 7. Consultas de ejemplo para el dashboard

```js
// Eventos de una sesión, en orden
db.events.find({ session_id: "ses_9901_exp" }).sort({ seq: 1 })

// ¿Se eligen peor las rutas cuando el pánico sube?
db.events.aggregate([
  { $match: { session_id: "ses_9901_exp", event_type: "route_decision" } },
  { $group: {
      _id: "$payload.panic_index_level",
      decisiones: { $sum: 1 },
      optimas: { $sum: { $cond: ["$payload.is_optimal_path", 1, 0] } }
  } }
])

// Tiempo promedio de lectura de pistas por tipo
db.events.aggregate([
  { $match: { event_type: "hint_read" } },
  { $group: { _id: "$payload.hint_type", promedio_ms: { $avg: "$payload.read_time_ms" } } }
])
```

---

## 8. Cómo verificar el esquema (criterios de aceptación)

1. Ejecutar `mongosh "<cadena de conexión>" setup_mongodb.js`.
2. La salida debe mostrar: colecciones creadas, índices asegurados, ejemplos insertados y **tres líneas con ✔ "rechazado"** (payload inválido, tipo de evento desconocido y `event_id` duplicado).
3. Revisar con `db.sessions.getIndexes()` y `db.events.getIndexes()` que existan los índices de las secciones 2 y 3.2.
4. Revisar con `db.getCollectionInfos({ name: "events" })` que el validador esté activo.
