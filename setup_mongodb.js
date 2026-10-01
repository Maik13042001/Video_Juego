// =============================================================================
// Escape Analytics 3D — Esquema MongoDB (SCRUM-36 sessions, SCRUM-37 events)
//
// Uso:
//   mongosh "mongodb://localhost:27017" setup_mongodb.js
//   (o con Atlas: mongosh "mongodb+srv://<usuario>@<cluster>/" setup_mongodb.js)
//
// El script es idempotente: se puede ejecutar varias veces. Si la colección ya
// existe, actualiza el validador (collMod) y asegura los índices.
// =============================================================================

const database = db.getSiblingDB("escape_analytics");

// Todos los números se aceptan como int/long/double/decimal porque cada driver
// (Node, C#, Python) serializa distinto los enteros. El backend debe validar
// además que seq, slot_index, etc. sean enteros.
const num = ["int", "long", "double", "decimal"];
const str = { bsonType: "string" };
const bool = { bsonType: "bool" };

// -----------------------------------------------------------------------------
// 1) Colección sessions  (SCRUM-36)
// -----------------------------------------------------------------------------
const sessionsValidator = {
  $jsonSchema: {
    bsonType: "object",
    title: "Session",
    required: [
      "session_id", "player_id", "level_id", "status", "started_at",
      "client", "schema_version", "created_at", "updated_at"
    ],
    additionalProperties: false,
    properties: {
      _id: { bsonType: "objectId" },
      session_id: { bsonType: "string", minLength: 3, maxLength: 64,
        description: "ID de sesión del contrato JSON (ej. ses_9901_exp). Único." },
      player_id: { bsonType: "string", minLength: 1, maxLength: 64,
        description: "Referencia lógica al jugador (ej. usr_402)." },
      level_id: { bsonType: "string", description: "Nivel con el que inicia la sesión." },
      status: { enum: ["active", "completed", "abandoned"] },
      started_at: { bsonType: "date" },
      ended_at: { bsonType: ["date", "null"] },
      last_event_at: { bsonType: ["date", "null"],
        description: "Lo actualiza el backend al ingerir eventos; sirve para detectar sesiones abandonadas." },
      client: {
        bsonType: "object",
        required: ["app_version", "platform"],
        additionalProperties: false,
        properties: {
          app_version: str,
          platform: { enum: ["windows", "macos", "linux", "android", "ios", "webgl", "editor"] },
          os: str,
          device_model: str,
          unity_version: str
        }
      },
      experiment: {
        bsonType: ["object", "null"],
        properties: { group: str, condition: str }
      },
      auth: {
        bsonType: "object",
        description: "Metadatos del JWT de la sesión. NUNCA se guarda el token en sí.",
        properties: { jti: str, expires_at: { bsonType: "date" } }
      },
      outcome: {
        bsonType: ["object", "null"],
        properties: {
          result: { enum: ["escaped", "caught", "timeout", "quit"] },
          duration_seconds: { bsonType: num, minimum: 0 }
        }
      },
      metrics: {
        bsonType: "object",
        description: "Agregados calculados por el backend para el dashboard.",
        properties: {
          events_count: { bsonType: num, minimum: 0 },
          route_decisions: { bsonType: num, minimum: 0 },
          optimal_routes: { bsonType: num, minimum: 0 },
          hints_read: { bsonType: num, minimum: 0 },
          panic_high_count: { bsonType: num, minimum: 0 },
          iec_score: { bsonType: ["double", "decimal", "null"],
            description: "Índice de Eficiencia Crítica. Fórmula pendiente de definir (US-15)." }
        }
      },
      schema_version: { bsonType: num, minimum: 1 },
      created_at: { bsonType: "date" },
      updated_at: { bsonType: "date" }
    }
  }
};

// -----------------------------------------------------------------------------
// 2) Colección events  (SCRUM-37)
// -----------------------------------------------------------------------------
const panicLevels = ["LOW", "MEDIUM", "HIGH"];
const distance = { bsonType: num, minimum: 0 };

// Un esquema de payload por tipo de evento. Para agregar un tipo nuevo:
// añade una entrada aquí y vuelve a ejecutar el script (hace collMod).
const payloadSchemas = {
  route_decision: {
    required: ["fork_id", "chosen_path", "is_optimal_path", "has_seen_hint",
               "chaser_distance_meters", "panic_index_level"],
    properties: {
      fork_id: str,
      chosen_path: str,
      is_optimal_path: bool,
      has_seen_hint: bool,
      chaser_distance_meters: distance,
      panic_index_level: { enum: panicLevels }
    }
  },
  item_collected: {
    required: ["item_id", "quantity"],
    properties: {
      item_id: str,
      quantity: { bsonType: num, minimum: 1 },
      pickup_id: str,
      chaser_distance_meters: distance
    }
  },
  item_used: {
    required: ["item_id", "slot_index"],
    properties: {
      item_id: str,
      slot_index: { bsonType: num, minimum: 0 },
      consumed: bool,
      chaser_distance_meters: distance
    }
  },
  hint_read: {
    required: ["hint_id", "read_time_ms"],
    properties: {
      hint_id: str,
      hint_type: { enum: ["monitor", "wall_sign", "note", "puzzle"] },
      read_time_ms: { bsonType: num, minimum: 0 },
      chaser_distance_meters: distance
    }
  },
  panic_direction_change: {
    required: ["angle_delta_deg", "window_ms"],
    properties: {
      angle_delta_deg: { bsonType: num },
      window_ms: { bsonType: num, minimum: 1 },
      changes_in_window: { bsonType: num, minimum: 1 },
      chaser_distance_meters: distance,
      panic_index_level: { enum: panicLevels }
    }
  }
};

const eventTypes = Object.keys(payloadSchemas);

const payloadBranches = Object.entries(payloadSchemas).map(([type, s]) => ({
  properties: {
    event_type: { enum: [type] },
    payload: { bsonType: "object", required: s.required, properties: s.properties }
  }
}));

const eventsValidator = {
  $jsonSchema: {
    bsonType: "object",
    title: "TelemetryEvent",
    required: [
      "event_id", "session_id", "player_id", "level_id", "event_type", "seq",
      "occurred_at", "received_at", "payload", "ingest", "schema_version"
    ],
    additionalProperties: false,
    properties: {
      _id: { bsonType: "objectId" },
      event_id: { bsonType: "string", minLength: 8, maxLength: 64,
        description: "UUID generado en el cliente. Garantiza idempotencia ante reintentos." },
      session_id: str,
      player_id: str,
      level_id: str,
      event_type: { enum: eventTypes },
      seq: { bsonType: num, minimum: 0,
        description: "Contador incremental por sesión generado en el cliente (orden fiable)." },
      occurred_at: { bsonType: "date", description: "Momento real del evento en el cliente." },
      received_at: { bsonType: "date", description: "Momento en que el servidor lo recibió." },
      payload: { bsonType: "object" },
      ingest: {
        bsonType: "object",
        required: ["batch_id", "from_offline_cache"],
        additionalProperties: false,
        properties: {
          batch_id: str,
          from_offline_cache: bool
        }
      },
      schema_version: { bsonType: num, minimum: 1 }
    },
    oneOf: payloadBranches
  }
};

// -----------------------------------------------------------------------------
// Creación / actualización
// -----------------------------------------------------------------------------
function ensureCollection(name, validator) {
  const options = { validator, validationLevel: "strict", validationAction: "error" };
  if (!database.getCollectionNames().includes(name)) {
    database.createCollection(name, options);
    print(`✔ colección creada: ${name}`);
  } else {
    database.runCommand({ collMod: name, ...options });
    print(`✔ validador actualizado: ${name}`);
  }
}

ensureCollection("sessions", sessionsValidator);
ensureCollection("events", eventsValidator);

// -----------------------------------------------------------------------------
// Índices
// -----------------------------------------------------------------------------
database.sessions.createIndex({ session_id: 1 }, { unique: true, name: "uniq_session_id" });
database.sessions.createIndex({ player_id: 1, started_at: -1 }, { name: "by_player_recent" });
database.sessions.createIndex({ status: 1, started_at: -1 }, { name: "by_status_recent" });

database.events.createIndex({ event_id: 1 }, { unique: true, name: "uniq_event_id" });
database.events.createIndex({ session_id: 1, seq: 1 }, { name: "by_session_seq" });
database.events.createIndex({ session_id: 1, event_type: 1, occurred_at: 1 }, { name: "by_session_type_time" });
database.events.createIndex({ player_id: 1, occurred_at: -1 }, { name: "by_player_time" });
print("✔ índices asegurados");

// -----------------------------------------------------------------------------
// Datos de ejemplo (basados en el contrato JSON del GDD) y pruebas del validador
// -----------------------------------------------------------------------------
const ts = 1788482415;                         // timestamp del ejemplo del GDD (segundos)
const occurred = new Date(ts * 1000);
const now = new Date();

database.sessions.updateOne(
  { session_id: "ses_9901_exp" },
  { $setOnInsert: {
      session_id: "ses_9901_exp",
      player_id: "usr_402",
      level_id: "level_01_industrial",
      status: "active",
      started_at: new Date((ts - 300) * 1000),
      ended_at: null,
      last_event_at: occurred,
      client: { app_version: "0.1.0", platform: "windows", unity_version: "6000.0" },
      experiment: { group: "A", condition: "chaser_on" },
      outcome: null,
      metrics: { events_count: 1, route_decisions: 1, optimal_routes: 0,
                 hints_read: 0, panic_high_count: 1, iec_score: null },
      schema_version: 1,
      created_at: now,
      updated_at: now
  } },
  { upsert: true }
);

const sampleEvent = {
  event_id: "3f6c1a52-9d0e-4c1b-8a47-2b9e6d1f0c33",
  session_id: "ses_9901_exp",
  player_id: "usr_402",
  level_id: "level_01_industrial",
  event_type: "route_decision",
  seq: 1,
  occurred_at: occurred,
  received_at: now,
  payload: {
    fork_id: "corridor_junction_01",
    chosen_path: "path_locked_door",
    is_optimal_path: false,
    has_seen_hint: true,
    chaser_distance_meters: 4.2,
    panic_index_level: "HIGH"
  },
  ingest: { batch_id: "bat_0001", from_offline_cache: false },
  schema_version: 1
};

database.events.updateOne(
  { event_id: sampleEvent.event_id },
  { $setOnInsert: sampleEvent },
  { upsert: true }
);
print("✔ datos de ejemplo insertados (si no existían)");

// Prueba negativa 1: panic_index_level inválido debe ser rechazado
try {
  database.events.insertOne({
    ...sampleEvent, _id: new ObjectId(),
    event_id: "00000000-0000-4000-8000-000000000001",
    payload: { ...sampleEvent.payload, panic_index_level: "EXTREME" }
  });
  print("✘ ERROR: el validador debió rechazar panic_index_level inválido");
} catch (e) {
  print("✔ rechazado payload inválido (" + e.codeName + ")");
}

// Prueba negativa 2: event_type desconocido debe ser rechazado
try {
  database.events.insertOne({
    ...sampleEvent, _id: new ObjectId(),
    event_id: "00000000-0000-4000-8000-000000000002",
    event_type: "tipo_inventado"
  });
  print("✘ ERROR: el validador debió rechazar event_type desconocido");
} catch (e) {
  print("✔ rechazado event_type desconocido (" + e.codeName + ")");
}

// Prueba negativa 3: event_id duplicado debe ser rechazado (idempotencia)
try {
  database.events.insertOne({ ...sampleEvent, _id: new ObjectId() });
  print("✘ ERROR: debió rechazar event_id duplicado");
} catch (e) {
  print("✔ rechazado event_id duplicado (" + e.codeName + ")");
}

print("\nListo. Colecciones: " + database.getCollectionNames().join(", "));
