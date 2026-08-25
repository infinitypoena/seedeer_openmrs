# Plan de homologación de las leyes + de-parametrización

> Estado: **PLANTEADO** — sin implementar. Se retoma por fases.
> Diagnóstico del 24-ago-2026 cruzando código (`Invariantes`/`RunStats`), ejecución real
> (logs/CSV de `bin/Debug/net10.0/output/`) y catálogos/config.

## Regla de oro (orden de trabajo obligatorio)

1. **Primero verificar que el código actual cumple las leyes.** Nunca dar un cambio por bueno porque
   compile y los tests pasen: medirlo contra las leyes (corrida ≥18 meses para L1/L4/L7).
2. **Las leyes se pueden reescribir SOLO objetivamente** — cuando una ley tal como está impide medir el
   funcionamiento correcto (ej. L1 con umbral de 90 d cuando la recurrencia crónica va de 30 a 120 d).
   **No** se reescriben para que una corrida "pase": se reescriben para que midan lo que dicen medir.
3. Si un cambio toca volumen/recurrencia/crecimiento y ninguna ley lo protege, **escribir la ley primero**.

---

## Diagnóstico (verificación 24-ago-2026)

### A. Discrepancia central — L1 rompe en corridas cortas (falso positivo)
Corrida real de 3 meses (`corrida-3-meses-2026`, 377 pacientes, 0 errores, 0 ítems perdidos):

| Ley | Resultado | Medido |
|---|---|---|
| L1 | ❌ ROTA (exit 3) | 24,8 % de 109 crónicos volvió (umbral 60 %) |
| L2 | ✓ | 7,5 % citas perdidas (≤25 %) |
| L3 | ✓ | 0 desplazados |
| L9 | ✓ | 0 remisiones en control |
| L4–L8 | — no procede | ventana corta (correcto) |

**Causa**: `Invariantes.DiasParaJuzgarCronicos = 90` < `Recurrence.MaxDiasCronico = 120`. Un crónico
diagnosticado el día 1 puede volver; uno del día 60 no (su control cae fuera de la ventana). Toda corrida
de 3 meses rompe L1 **sin estar rota**: es un falso positivo del umbral, no un fallo del modelo.

### B. Drift post-de-parametrización
`Satisfaccion.CapacidadComodaPorDia` **ya no existe** (deriva de `VisitasPorDiaMeseta`,
`SeedOrchestrator.cs:586`), pero CLAUDE.md / crecimiento-config / parametrizacion lo citan como
"requerido por SettingsValidator". Igual `PorcentajeRecurrentes` (ya borrado). Los docs aún arrastran
esos restos.

### C. Sobre-parametrización
~100 valores en 18 sub-secciones de `Simulation`. La mayoría son diales finos con default sano.

### D. Toggles que contradicen "catálogo existe → se aplica"
`Climate.Enabled=false` ignora un `clima.csv` que existe. El contrato buscado: **si el catálogo está,
se aplica**; si no está, se usa un estándar interno.

### E. Parámetros muertos/vestigiales
`Crecimiento.RecurrentesPorPacienteExtra` (override "no se puede apuntar a ojo", 0 = derivar) y
`DemographicProfile.PediatricMinAgeMonths` (solo con `PediatricClinic`).

### F. Obligatorio vs opcional no legible
17 catálogos; `CatalogValidator` exige 9 y el resto son opcionales, pero el contrato no es explícito.

### G. Observación (vigilar, no es una ley)
Una corrida de las 19:23 dejó 1.302 ítems perdidos por `POST encounter → 400 Invalid Submission`; la de
las 20:00 salió limpia (0 errores). Transitorio (estado de OpenMRS durante un `clear`), no bug de código.
Si reaparece en una corrida limpia, investigar antes de proseguir.

---

## Fase A — Homologar L1 — `[x]` DONE (commit 967e679)

**Objetivo**: que la ley mida "el crónico vuelve a su control" y no "la ventana es lo bastante larga".

**Lo que se hizo (distinto de lo estimado aquí):** medido empíricamente con la MiniClinica, la fracción de
crónicos que vuelve cruza el 60 % **alrededor del año**, no a los ~180-210 d estimados:

| Ventana | % crónicos que vuelven |
|---|---|
| 90 d | 12 % |
| 180 d | 39 % |
| 270 d | 52 % |
| **365 d** | **65 %** ✓ |
| 540 d | 72 % |

Así que el umbral de L1 se subió de 90 d a **365 d** (misma constante `DiasParaJuzgarContinuidad` que L7),
no se derivó de `MaxDiasCronico` ni se midieron "crónicos elegibles" (haría falta trackear la fecha de la
primera crónica, más invasivo y sin ganancia objetiva).

1. `[x]` `Services/Invariantes.cs`: `DiasParaJuzgarCronicos` (90) y `DiasParaJuzgarVisitasPorPaciente`
   (365) unificados en `DiasParaJuzgarContinuidad = 365`.
2. `[x]` `leyes_simulacion.md` + `manual_usuario.md`: "L1 y L7 necesitan ≥365 días".
3. `[x]` Tests: `VentanasCortasTests` — 120 d ⇒ L1 "no procede"; 365 d ⇒ L1 se juzga y cumple (~65 %).
4. `[x]` L2/L3/L9 siguen pronunciándose en cortas (tripwires por conteo, no por ventana) — ya verificado
   en corridas reales cortas.
5. `[x]` Suite completa: 524 tests en verde.

## Fase B — De-parametrizar `appsettings.json`

**Objetivo**: núcleo esencial en JSON + un puñado de **diales clínicos** configurables; el resto, constantes
internas con los valores actuales. Nivel elegido: **"Núcleo + diales clínicos"**.

### Núcleo (queda en JSON — imprescindible / instance-specific)
- `OpenMRS.RestApi.*` (BaseUrl, Username, Password)
- `OpenMRS.Database.*` (CorregirFechas, ConnectionString, PrefijoPaciente, TamanoLote, PedirConfirmacion)
- `OpenMRS.Defaults.*` (los ~19 UUIDs de la instancia)
- `Simulation.StartDate` / `EndDate`
- `Simulation.AltasPorDiaArranque` · `Crecimiento.VisitasPorDiaMeseta` · `Crecimiento.PoblacionCaptacion`
- `Simulation.UtcOffset` (TZ de la instancia)
- `Simulation.RandomSeed` · `Simulation.Salida.*`

### Diales clínicos (opcionales en JSON, con default estándar — el "recetario" de config-diagnostico.md)
- **Sesgo común↔raro**: `CommonProbMin` / `CommonProbMax`
- **Concentración↔variedad**: `Variedad.RepeticionDamping`
- **Estacionalidad**: `Climate.SeasonalBoost` (sin `Enabled` — ver Fase C)
- **Multimorbilidad**: `Comorbidity.*` (BaseProbability, MaxAdditional, SecondExtraProbability, AffinityBoost, AgeScaling)
- **Repetición en retornos**: `SeguimientoCronicoProb`, `SeguimientoAgudoProb`, `VentanaSeguimientoAgudoDias`
- **Cuándo vuelve**: `Recurrence.*` (bandas agudo/crónico/post-referencia, `VisitasEspontaneasPorPacienteAno`)
- **Cuándo se cita control**: `ReferralProbabilities.FollowUp*` (FollowUp, FollowUpCronico, FollowUpGrave, FollowUpReferido)

### Constantes internas (se quitan del JSON — valores actuales pasan a `const` en código)
- `ReferralProbabilities.LabOrder / ClinicalExam / DrugOrder / Urgent`
- `Laboratorio.*` (ProbRechazo, ProbResultadoLlega, MinutosHastaToma*)
- `Satisfaccion.*` (MediaBase, Desviacion, Bonus*, Penalizacion*, Umbral) — `Enabled` se mantiene como dial
  de apagado de la feature de calificación, o desaparece si "siempre satisfacción" (decidir en impl.)
- `Allergy.*` (prevalencia + conteos)
- `WeekdayWeights` · `HorarioAtencion` · `DemographicProfile.AgeGroups/GenderRatio/MinPatientAgeMonths`
- `MedicoCabeceraProb*` · `Min/MaxMedicosPorDia` · `Orders.LabVigenciaDias` · `Chequeo.*` · `Appointments.*`
- `Crecimiento.PacientesPorDiaMax` (derivar = `VisitasPorDiaMeseta × 1,8`), `MinVisitasRecurrente`,
  `AsistenciaProbInsatisfecho`

### Limpieza
- Eliminar `Crecimiento.RecurrentesPorPacienteExtra` y `DemographicProfile.PediatricMinAgeMonths`.
- Quitar `Climate.Enabled` (pasa a Fase C).
- Mantener `SettingsValidator.FindUnknownKeys` para avisar de claves JSON huérfanas tras cada recorte.

## Fase C — Catálogos obligatorios vs opcionales

1. Codificar el contrato en `CatalogLoader`/`CatalogValidator` con una lista explícita:
   - **Obligatorios** (9, abortan si vacíos): epidemiology-profile, diagnosticos, tratamientos,
     medicamentos, laboratorios, examenes_clinicos, alergenos, motivos_consulta, nombres+apellidos.
   - **Opcionales** (8, con estándar interno si faltan): clima, consultorios, programas, direcciones,
     paneles, comorbilidad_afinidades, personal_laboratorio, (apellidos si nombres presente).
2. **"Catálogo presente → se aplica"**: eliminar los toggles `Enabled` que apagan un catálogo que existe
   (`Climate.Enabled`). La presencia del archivo es el interruptor.
3. Cada opcional documenta su comportamiento sin archivo (feature apagada o estándar interno) en
   `parametrizacion_archivos.md`.

## Fase D — Sincronizar documentación

- `CLAUDE.md`: quitar la mención a `Satisfaccion.CapacidadComodaPorDia`; actualizar el bloque
  `appsettings.json` de ejemplo y la lista de catálogos (oblig/opc); reflejar el nuevo umbral de L1.
- `crecimiento-config.md` y `parametrizacion_archivos.md`: lo mismo (aforo = `VisitasPorDiaMeseta` directo;
  sin toggles `Enabled` de catálogo).
- `manual_usuario.md` §5/§8: configuración reducida + contrato obligatorio/opcional.

---

## Orden de ejecución y verificación por fase

| Fase | Verificación |
|---|---|
| A (L1) | `dotnet test` (InvariantesTests); corrida de 3 meses ya NO rompe L1 (queda "no procede"); corrida ≥18 meses juzga L1 en verde con la config sana |
| B (config) | `dotnet build` + `dotnet test`; arrancar con el `appsettings.json` recortado y verificar que los defaults internos reproducen la corrida de referencia (misma `RandomSeed` → mismo volumen) |
| C (catálogos) | `dotnet test` (CatalogValidatorTests); quitar un catálogo opcional → arranca con feature apagada/estándar; quitar uno obligatorio → aborta con exit 2 |
| D (docs) | relectura de los 4 docs: ninguna referencia a claves/parámetros eliminados |

Cada fase se cierra con: código+tests → build limpio → corrida corta real → docs → commit por fase.
