# Detalle del Proyecto: OpenMRS Seeder API

## Descripción General

API REST en **C# ASP.NET Core 10** que pobla una base de datos **OpenMRS** (MariaDB 10.11) con datos clínicos realistas en español, simulando la operación de una clínica real. Se conecta directamente a la base de datos del Docker local, controlada por parámetros de configuración.

---

## Stack Tecnológico

| Componente | Tecnología |
|---|---|
| Lenguaje | C# (.NET 10) |
| Framework | ASP.NET Core 10 Minimal API + Controllers |
| Base de Datos | MariaDB 10.11 (OpenMRS schema) |
| Acceso a datos | MySqlConnector (sin ORM, SQL directo) |
| Generación de datos | Bogus (locale `es`) |
| Documentación API | Swashbuckle / Swagger UI |

---

## Infraestructura Docker

### Servicios en docker-compose.yml

| Servicio | Imagen | Puerto |
|---|---|---|
| gateway | openmrs-reference-application-3-gateway:3.6.0 | 80:80 |
| frontend | openmrs-reference-application-3-frontend:3.6.0 | interno |
| backend | openmrs-reference-application-3-backend:3.6.0 | interno (8080) |
| db | mariadb:10.11.7 | **3306:3306** ← agregar |

### Variables de entorno Docker (defaults)
```
OMRS_DB_USER=openmrs
OMRS_DB_PASSWORD=openmrs
MYSQL_ROOT_PASSWORD=openmrs
MYSQL_DATABASE=openmrs
```

### Modificación requerida en docker-compose.yml
Agregar al servicio `db`:
```yaml
ports:
  - "3306:3306"
```

---

## Configuración de la API (appsettings.json)

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "OpenMRS": {
    "Server": "localhost",
    "Port": 3306,
    "Database": "openmrs",
    "User": "openmrs",
    "Password": "openmrs",
    "AdminUserId": 1,
    "DefaultLocationUuid": ""
  },
  "Seed": {
    "StartDate": "2023-01-01",
    "EndDate": "2024-12-31",
    "PatientCount": 200,
    "VisitsPerPatient": 8,
    "Locale": "es",
    "RandomSeed": 42
  }
}
```

> **Nota:** `DefaultLocationUuid` debe completarse con el UUID de la location por defecto de la instancia OpenMRS antes de ejecutar el seeder.

---

## Estructura de Archivos del Proyecto C#

```
openmrs_seeder_v1/openmrs_seeder_v1/
├── Configuration/
│   ├── OpenMrsSettings.cs       # Mapea sección "OpenMRS" del appsettings
│   └── SeedSettings.cs          # Mapea sección "Seed" del appsettings
├── Models/
│   ├── PersonRow.cs             # Mapea tabla person
│   ├── PatientRow.cs            # Mapea tabla patient
│   ├── VisitRow.cs              # Mapea tabla visit
│   ├── EncounterRow.cs          # Mapea tabla encounter
│   └── ObsRow.cs                # Mapea tabla obs
├── Services/
│   ├── ConceptResolver.cs       # Resuelve UUID → ID de concepts al startup
│   └── SeedProgressTracker.cs  # Rastrea progreso del run en memoria
├── Seeders/
│   ├── SeedOrchestrator.cs      # Coordina la pipeline completa de seeders
│   ├── PatientSeeder.cs         # Inserta person + patient + person_name + person_address
│   ├── VisitSeeder.cs           # Crea visitas distribuidas en el rango de fechas
│   ├── EncounterSeeder.cs       # Crea encounters VITALS + ADULTINITIAL por visita
│   └── ObservationSeeder.cs     # Inserta obs de signos vitales y motivo de consulta
├── Controllers/
│   └── SeedController.cs        # Endpoints REST
├── Program.cs                   # Entry point, DI, middleware
├── appsettings.json
└── appsettings.Development.json
```

---

## Endpoints REST

| Método | Ruta | Descripción | Respuesta |
|---|---|---|---|
| `GET` | `/api/seed/status` | Verifica conectividad DB y muestra config + conteo de datos seeded | 200 con objeto de estado |
| `POST` | `/api/seed/run` | Inicia el seeding en background | 202 Accepted con `{ runId }` |
| `GET` | `/api/seed/progress/{runId}` | Estado del run: porcentaje, etapa actual, errores | 200 con objeto de progreso |
| `DELETE` | `/api/seed/clear` | Elimina todos los registros marcados con `SEEDED_BY_API` | 200 con conteo eliminado |

El `POST /api/seed/run` retorna inmediatamente (202) y el seeding corre en un background task para no bloquear el HTTP request.

---

## Datos Clínicos a Generar

### 1. Personas y Pacientes (PatientSeeder)
- Nombre completo (Bogus locale `es`): nombre, apellido paterno, apellido materno
- Género: M/F aleatorio
- Fecha de nacimiento: entre 5 y 85 años atrás
- Dirección: ciudad, departamento/estado, Colombia/México
- Identificador de paciente (tipo "OpenMRS ID")

### 2. Visitas (VisitSeeder)
- Tipo: OUTPATIENT
- Fechas: distribuidas aleatoriamente dentro de `StartDate..EndDate`
- Location: `DefaultLocationUuid`
- Cantidad: `PatientCount × VisitsPerPatient`

### 3. Encuentros (EncounterSeeder)
Por cada visita, dos encounters:
- **VITALS**: signos vitales, ocurre al inicio de la visita
- **ADULTINITIAL**: consulta de adultos, ocurre minutos después del VITALS

### 4. Observaciones (ObservationSeeder)
Por encounter **VITALS**:
| Concepto | Rango realista |
|---|---|
| Peso (kg) | 45–120 |
| Talla (cm) | 145–195 |
| Presión sistólica (mmHg) | 90–180 |
| Presión diastólica (mmHg) | 60–120 |
| Temperatura (°C) | 36.0–38.5 |
| Pulso (lpm) | 50–110 |
| Saturación O2 (%) | 92–100 |

Por encounter **ADULTINITIAL**:
- Motivo de consulta (texto libre en español): frases predefinidas realistas como "Dolor de cabeza intenso", "Fiebre y malestar general", "Control de presión arterial", etc.

---

## Convenciones del Schema OpenMRS

### Columnas de auditoría requeridas en todas las tablas
| Columna | Valor |
|---|---|
| `uuid` | `Guid.NewGuid().ToString()` |
| `creator` | `AdminUserId` (del appsettings) |
| `date_created` | Fecha del registro simulado |
| `voided` | `0` |

### Jerarquía de inserción (orden obligatorio)
```
person → patient → person_name
                 → person_address
                 → patient_identifier
                 → visit → encounter → obs
```

### Idempotencia
Todos los registros insertados llevan una marca identificable para poder eliminarlos con `DELETE /api/seed/clear`. Se usa el campo disponible en cada tabla (e.g., `description`, `comment`, o un campo de texto libre).

### ConceptResolver
- Al arrancar la API, resuelve los UUIDs de concepts requeridos a sus IDs enteros (`SELECT concept_id FROM concept WHERE uuid = ?`)
- Si falta algún concept requerido en la DB, el seeder falla con error claro antes de insertar cualquier dato
- Los IDs se cachean en memoria para toda la vida del proceso

---

## Flujo de Uso

```
1. docker compose up -d
2. Esperar ~10 min a que el backend OpenMRS esté healthy
3. Acceder a localhost → OpenMRS UI → obtener UUID de la Location por defecto
4. Pegar el UUID en appsettings.json → "DefaultLocationUuid"
5. dotnet run (desde openmrs_seeder_v1/openmrs_seeder_v1/)
6. Abrir Swagger UI en http://localhost:5197/swagger
7. GET /api/seed/status → verificar conexión OK
8. POST /api/seed/run → obtener runId
9. GET /api/seed/progress/{runId} → monitorear hasta 100%
10. Verificar datos en la UI de OpenMRS (localhost)
```

---

## Comandos de Desarrollo

```bash
# Compilar
dotnet build openmrs_seeder_v1/openmrs_seeder_v1/openmrs_seeder_v1.csproj

# Ejecutar (desde la carpeta del proyecto)
dotnet run --project openmrs_seeder_v1/openmrs_seeder_v1/openmrs_seeder_v1.csproj

# Ejecutar tests
dotnet test

# Agregar paquetes NuGet
dotnet add package MySqlConnector
dotnet add package Bogus
dotnet add package Swashbuckle.AspNetCore
```

---

## Decisiones de Diseño

| Decisión | Elección | Razón |
|---|---|---|
| Lenguaje | C# (no F#) | El proyecto estaba en F# por defecto del scaffold; C# es más apropiado para este tipo de trabajo con DB |
| ORM | Ninguno (SQL directo con MySqlConnector) | Más eficiente para bulk inserts; el schema OpenMRS tiene quirks que complican EF |
| Conectividad DB | Exponer puerto 3306 en docker-compose | El seeder corre en el host, no dentro de Docker |
| Generación de datos | Bogus locale `es` | Datos realistas en español para una clínica latinoamericana |
| Progreso del run | In-memory con `runId` GUID | Suficiente para uso local; no se necesita persistencia entre reinicios |
