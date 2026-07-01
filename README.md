# OpenMRS Clinical Simulator

API REST en C# .NET 10 que actúa como **simulador clínico** para una instancia [OpenMRS 3.x](https://openmrs.org/). Genera datos clínicos realistas en español siguiendo un modelo probabilístico: volumen diario de pacientes con variación estadística, enfermedades distribuidas por edad y género, y coherencia entre diagnóstico, vitales, laboratorios, exámenes en consultorio, prescripciones y alergias.

---

## Requisitos previos

| Herramienta | Versión mínima |
|-------------|---------------|
| [.NET SDK](https://dotnet.microsoft.com/download) | 10.0 |
| OpenMRS 3.x con REST API activa | cualquier despliegue |

El simulador se conecta exclusivamente a través de la **REST API de OpenMRS** (`/ws/rest/v1`). No importa cómo esté desplegada tu instancia (Docker, servidor local, servidor remoto) — solo necesitas tener la API accesible y sus credenciales.

---

## Configuración inicial

### 1. Verificar que OpenMRS está accesible

Confirma que la REST API responde antes de correr el simulador:

```
GET http://<tu-host>/openmrs/ws/rest/v1
```

Debe devolver un JSON con información de la instancia. Si usas la instalación por defecto en local, la URL base es `http://localhost/openmrs/ws/rest/v1`.

### 2. Configurar credenciales del simulador

Copiar el archivo de ejemplo y editarlo con tus credenciales:

```bash
cp openmrs_seeder_v1/openmrs_seeder_v1/appsettings.example.json \
   openmrs_seeder_v1/openmrs_seeder_v1/appsettings.json
```

Editar `appsettings.json` — al menos cambiar la contraseña:

```json
"RestApi": {
  "BaseUrl": "http://localhost/openmrs/ws/rest/v1",
  "Username": "admin",
  "Password": "TU_PASSWORD_AQUI"
}
```

> `appsettings.json` está en `.gitignore` y nunca se sube al repositorio.

### 3. Ajustar parámetros de simulación (opcional)

En `appsettings.json`, sección `Simulation`:

| Parámetro | Descripción | Validación | Producción |
|-----------|-------------|------------|------------|
| `StartDate` | Fecha de inicio | `2023-01-01` | `2023-01-01` |
| `EndDate` | Fecha de fin | `2023-01-08` | `2024-12-31` |
| `PacientesPorDiaMedio` | Volumen diario promedio | `8` | `40` |
| `PorcentajeRecurrentes` | % pacientes que regresan | `30` | `30` |

---

## Opción A: ejecutar con .NET SDK

```bash
# Compilar
dotnet build openmrs_seeder_v1/openmrs_seeder_v1/openmrs_seeder_v1.csproj

# Ejecutar
dotnet run --project openmrs_seeder_v1/openmrs_seeder_v1/openmrs_seeder_v1.csproj
```

La API queda disponible en `http://localhost:5197`.  
Swagger UI en `http://localhost:5197/swagger`.

---

## Opción B: ejecutar con Docker

Si no tienes el SDK de .NET instalado, puedes correr el simulador como contenedor. Requisito: [Docker Desktop](https://www.docker.com/products/docker-desktop/) (Windows/Mac) o Docker Engine (Linux).

### 1. Preparar credenciales

```bash
cd docker
cp .env.example .env
```

Editar `docker/.env` con la URL de tu OpenMRS y la contraseña:

```
OPENMRS_URL=http://host.docker.internal/openmrs/ws/rest/v1
OPENMRS_PASSWORD=tu_password
```

> `docker/.env` está en `.gitignore` — nunca se sube al repositorio.

### 2. Construir y levantar

```bash
docker compose -f docker/docker-compose.yml up --build
```

La API queda disponible en `http://localhost:5197`.  
Swagger UI en `http://localhost:5197/swagger`.

### 3. Detener

```bash
docker compose -f docker/docker-compose.yml down
```

> **Nota sobre la URL de OpenMRS:**  
> - **Windows / Mac**: usa `host.docker.internal` para llegar a `localhost` del host.  
> - **Linux**: reemplaza con la IP del gateway Docker, habitualmente `172.17.0.1`.

---

## Uso de la API

### Verificar conexión con OpenMRS

```
GET /api/seed/status
```

Devuelve estado de conectividad, configuración activa y cantidad de registros en cada catálogo.

### Iniciar simulación

```
POST /api/seed/run
```

Respuesta inmediata `202 Accepted` con el `runId`:

```json
{ "runId": "073f50a2-f583-45d1-95f4-63fefc63111d" }
```

### Monitorear progreso

```
GET /api/seed/progress/{runId}
```

```json
{
  "porcentaje": 65,
  "etapa": "procesando",
  "pacientesCreados": 184,
  "diasProcesados": 13,
  "totalDias": 20,
  "completado": false,
  "errores": []
}
```

### Limpiar datos simulados

```
DELETE /api/seed/clear
```

Elimina todos los pacientes con prefijo `SIM-` y sus visitas asociadas.

---

## Pipeline por paciente

Para cada paciente generado, el simulador ejecuta en orden:

```
PatientSeeder → AllergySeeder → VisitSeeder → VitalsSeeder → ConsultaSeeder
            → ConditionSeeder → LabOrderSeeder → PrescriptionSeeder → VisitCloseSeeder
```

Todos los datos quedan vinculados en OpenMRS como si hubieran sido ingresados manualmente.

---

## Catálogos CSV

En `openmrs_seeder_v1/openmrs_seeder_v1/catalogs/`:

| Archivo | Descripción |
|---------|-------------|
| `epidemiology-profile.csv` | Pesos de categorías diagnósticas por edad y género |
| `diagnosticos.csv` | Diagnósticos CIEL con columnas de grupo de edad |
| `medicamentos.csv` | Medicamentos con `drug_uuid`, `concept_uuid` y ruta de administración |
| `laboratorios.csv` | Pruebas de laboratorio por categoría clínica |
| `alergenos.csv` | Alérgenos (DRUG/FOOD/ENVIRONMENT) con UUIDs verificados |
| `examenes_clinicos.csv` | Exámenes físicos registrados como observaciones |
| `motivos_consulta.csv` | Frases de motivo de consulta en español por categoría |
| `nombres.csv` / `apellidos.csv` | Nombres de pila (por sexo) y apellidos centroamericanos para nombres realistas y únicos |
| `clima.csv` *(opcional)* | Estación por semana ISO — sesga enfermedades estacionales |
| `consultorios.csv` *(opcional)* | Consultorios + su médico (ubicación/personal dinámicos) |
| `comorbilidad_afinidades.csv` *(opcional)* | Clusters de comorbilidad afines por categoría |

Ver `parametrizacion_archivos.md` para el esquema completo de columnas.

---

## Endpoints

| Método | Ruta | Descripción |
|--------|------|-------------|
| `GET` | `/api/seed/status` | Estado de conexión + config + catálogos |
| `POST` | `/api/seed/run` | Inicia simulación → `{ runId }` |
| `GET` | `/api/seed/progress/{runId}` | Progreso en tiempo real |
| `DELETE` | `/api/seed/clear` | Elimina todos los datos `SIM-*` |

---

## Ejecutar tests

```bash
dotnet test openmrs_seeder_v1/openmrs_seeder_v1.Tests/openmrs_seeder_v1.Tests.csproj
```

---

## Documentación adicional

| Archivo | Contenido |
|---------|-----------|
| `parametrizacion_archivos.md` | Referencia completa de parámetros y columnas CSV |
| `detalle-seeder.md` | Arquitectura, stack, decisiones de diseño |
| `fases_implementacion.md` | Plan de implementación por fases |
| `CLAUDE.md` | Guía para Claude Code — UUIDs verificados, restricciones de la API |

---

## Stack

- **C# .NET 10** — ASP.NET Core 10
- **OpenMRS 3.6.0** via REST API (`/ws/rest/v1`) — sin acceso directo a base de datos
- Nombres de pacientes desde catálogos CSV (`nombres.csv`/`apellidos.csv`, 2 nombres + 2 apellidos centroamericanos); **Bogus** (locale `es`) solo como fallback y para direcciones
- **Swashbuckle** para Swagger UI
