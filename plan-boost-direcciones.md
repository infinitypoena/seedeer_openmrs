# Plan de mejora — boost diario de departamento no principal (diversidad de direcciones)

**Objetivo**: hoy las direcciones se concentran en San Salvador (anillo 0 ~45 %, colindantes ~28 %,
AMSS ~18 %, interior ~9 %). Se añade un **boost diario determinista**: algunos días de la corrida se
elige un departamento que **no sea el dominante** (el de mayor peso total) y se le asigna una **cuota**
del volumen de ese día, de modo que ese día aparecen más pacientes de una zona no principal. Es
**aleatorio por fecha pero reproducible** (semilla = `RandomSeed + 25 + fecha.DayNumber`, memoizado por
día), sobre el catálogo existente `direcciones.csv`, sin catálogo nuevo.

**No toca volumen/recurrencia/crecimiento** → ninguna ley (L1–L9) lo protege ni lo amenaza (es mezcla
de direcciones, no cuántas visitas ni quién vuelve). **Feature activa por defecto** (`Enabled=true`).

## Config

`Simulation:Direcciones`:

| Clave | Default | Significado |
|-------|---------|-------------|
| `Enabled` | `true` | Interruptor. `false` = cero consumo de RNG y flujo histórico bit a bit intacto. |
| `ProbabilidadBoostDia` | `0.15` | Prob. de que un día tenga boost (≈2 días de cada 14). |
| `Share` | `0.30` | Fracción (0–1) del día que capta el departamento potenciado (independiente de su tamaño). |

## Checklist de puntos

| # | Punto | Estado |
|---|-------|--------|
| 1 | `plan-boost-direcciones.md` (este archivo) | ✅ |
| 2 | `SimulationSettings.cs`: `DireccionesSettings` + propiedad `Direcciones` | ✅ |
| 3 | `Services/DireccionBoost.cs`: seam puro determinista | ✅ |
| 4 | `PatientProfileGenerator.cs`: boost memoizado + fecha en `GenerateAddress` | ✅ |
| 5 | `SettingsValidator.cs`: validar `Direcciones` | ✅ |
| 6 | `appsettings.json`: sección `Simulation:Direcciones` | ✅ |
| 7 | Tests (`DireccionBoostTests`, `PatientProfileGeneratorTests`, `SettingsValidatorTests`) | ✅ |
| 8 | Docs (`parametrizacion_archivos.md` §10b, `CLAUDE.md`) | ✅ |
| 9 | `dotnet build` + `dotnet test` (540 tests, 0 fallos) | ✅ |
| 10 | Corrida de validación 14 días (2026-10-01 → 2026-10-14) + limpieza de ventana | ✅ |

## Resultado de la validación (corrida real, 14 días)

- **Limpieza previa**: 163 pacientes SIM- con visitas en la ventana borrados (`sim_ventana_restantes=0`).
- **Corrida**: `completado`, **150 pacientes**, 0 errores, exit 0. Leyes L3/L5/L6/L9 ✓; L1/L2/L4/L7/L8
  "no procede" (ventana corta, como corresponde).
- **Efecto del boost visible**: el 2026-10-08 **La Unión (5) superó a San Salvador (4)** — un
  departamento con peso 0,4 % del catálogo captó la mayoría del día. Distribución de la ventana: 12
  departamentos distintos; San Salvador 116, La Libertad 12, La Unión 6, Sonsonate 4, San Miguel 3,
  Chalatenango/Cuscatlán 2, y 5 más con 1.
- ⚠️ El `AuditDateFixer` (etapa 5/5, `CorregirFechas=true` preexistente) loguea
  "Verificación FALLIDA … fuera de la ventana" porque la instancia ya tiene ~28.000 SIM- de corridas
  históricas cuyas fechas caen fuera de esta ventana de 14 días. Es benigno (0 pendientes) y ajeno a
  esta mejora. Tras la etapa 5/5 conviene `docker compose restart backend` (caché de Hibernate).

## Validación (ventana: 1–14 oct 2026)

- `appsettings.json` ya apunta a `StartDate=2026-10-01`, `EndDate=2026-10-14`, `AltasPorDiaArranque=2`.
- Limpieza previa y posterior con `querys/borrar_simulacion_ventana.sql`
  (`@v_inicio='2026-10-01'`, `@v_fin='2026-10-15'` — ⚠️ `@v_fin` es EXCLUSIVO).
- Verificación de diversidad: query sobre `person_address.state_province` unida a visitas de la ventana.
