# OpenMRS Clinical Simulator

Aplicación de consola en **C# .NET 10** que actúa como **simulador clínico** para una instancia [OpenMRS 3.x](https://openmrs.org/): se ejecuta una vez, genera el historial completo de una clínica de consulta externa durante el período configurado, y termina. Todo por la **REST API** de OpenMRS (`/ws/rest/v1`) — sin acceso directo a base de datos.

Lo que genera (en español, epidemiológica y clínicamente coherente):

- **Pacientes** con nombres centroamericanos realistas, edad y género según distribución configurable.
- **Visitas completas**: vitales coherentes con la enfermedad, consulta con diagnóstico (por edad/sexo/estación, con comorbilidades), órdenes de laboratorio **con resultado**, prescripciones, alergias y cierre de visita.
- **Continuidad asistencial**: crónicos que vuelven a control de su enfermedad con su médico de cabecera, problem list, inscripción en programas (VIH, diabetes) y **citas reales en la agenda de O3** con no-shows.

## Inicio rápido

Requisitos: [.NET SDK 10+](https://dotnet.microsoft.com/download) y una instancia OpenMRS 3.x con su REST API accesible.

```bash
# 1. Configurar credenciales y ventana de simulación
cp openmrs_seeder_v1/openmrs_seeder_v1/appsettings.example.json \
   openmrs_seeder_v1/openmrs_seeder_v1/appsettings.json
# Editar: OpenMRS.RestApi (URL/usuario/contraseña) y Simulation.StartDate/EndDate

# 2. Ejecutar — corre la simulación completa y termina
dotnet run --project openmrs_seeder_v1/openmrs_seeder_v1/openmrs_seeder_v1.csproj

# 3. Limpiar los datos simulados (pide confirmación s/N)
dotnet run --project openmrs_seeder_v1/openmrs_seeder_v1/openmrs_seeder_v1.csproj -- clear
```

La consola muestra un resumen inicial (conexión + catálogos), el progreso cada ~15 s y un resumen final con el conteo preciso de errores. **Exit codes**: `0` completado · `1` fallo del proceso · `2` OpenMRS inaccesible (no se toca ningún dato).

> `appsettings.json` y `docker/.env` están en `.gitignore` — las credenciales nunca se suben al repositorio.

### Con Docker (sin instalar .NET)

```bash
cd docker && cp .env.example .env   # editar URL y contraseña
docker compose -f docker/docker-compose.yml run --rm seeder          # simulación
docker compose -f docker/docker-compose.yml run --rm -i seeder clear # limpieza
```

> OpenMRS en el mismo host: usar `host.docker.internal` (Windows/Mac) o `172.17.0.1` (Linux) en `OPENMRS_URL`.

## Tests

```bash
dotnet test openmrs_seeder_v1/openmrs_seeder_v1.Tests/openmrs_seeder_v1.Tests.csproj
```

## Documentación

| Documento | Contenido |
|-----------|-----------|
| [`manual_usuario.md`](manual_usuario.md) | **Empieza aquí** — tour de capacidades con ejemplos, configuración, casos de uso, solución de problemas |
| [`parametrizacion_archivos.md`](parametrizacion_archivos.md) | Referencia completa de parámetros (`appsettings.json`) y esquemas de los catálogos CSV |
| [`CLAUDE.md`](CLAUDE.md) | Notas técnicas: arquitectura, UUIDs verificados de la instancia, restricciones de la REST API |
| [`fases_implementacion.md`](fases_implementacion.md) | Historia del proyecto: fases y registro de cambios |
| [`enfermedades-centroamerica.md`](enfermedades-centroamerica.md) | Investigación: 300 enfermedades de Centroamérica con metodología y referencias (insumo del catálogo) |

## Identificación de los datos simulados

Todo paciente generado lleva el identificador **`SIM-XXXXXXXX`** y sus visitas la marca `SEEDED_BY_SIMULATOR` — los datos reales de la instancia nunca se tocan, y el subcomando `clear` solo anula lo simulado.
