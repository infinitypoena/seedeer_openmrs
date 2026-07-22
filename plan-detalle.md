# Plan: cerrar el bucle de referencias al hospital + birthdate a mediodía

> Al aprobar: **guardar una copia de este plan en `plan-detalle.md`** (raíz del repo) para revisión, como pidió el usuario. Es temporal: se borra al terminar.

## Contexto

La corrida completa del 17-jul (23.263 visitas, 3,5 años, 8 leyes verdes) reveló en el análisis de BD:

1. **El episodio de referencia nunca se resuelve.** Un dx `ambito=referencia` (apendicitis, IAM…) agenda control post-alta con `FollowUpReferido=0.95`; la cita transporta el dx (`MotivoProximaCita`); el control llega con el mismo dx primario y `ReferenciaPolicy.DebeReferir` — **sin estado** (`ConsultaSeeder.cs:72`) — vuelve a emitir las 4 obs de remisión, ordena labs STAT, no prescribe y re-agenda otro control al 0,95. Medido: un paciente con **47 encuentros "Apendicitis aguda"**; 5.039 remisiones sobre 737 pacientes (6,8 por referido, 21,7 % de las visitas); apendicitis = 2º dx más frecuente (451). Clínicamente imposible.
2. **1 paciente perdido por birthdate en hueco DST**: `1988-05-01` a medianoche no existe en `America/El_Salvador` (transición de horario de verano 1988). `PatientSeeder.cs:56` manda `yyyy-MM-dd` → el servidor lo parsea a medianoche → 400.

Decisiones ya tomadas con el usuario:
- Control post-alta: **mismo dx, máximo 1 control** (episodio + control = 2 apariciones), sin re-remisión, labs de rutina, prescripción permitida. Después el episodio queda resuelto.
- El punto de las fechas de entrega de labs externos **se omite** (decisión del usuario).
- Birthdate: **mediodía**.
- Regla de la casa (CLAUDE.md): esto toca la recurrencia y ninguna ley lo protege → **se escribe la ley primero**.

**No romper nada**: el episodio inicial de referencia se mantiene idéntico (remisión + STAT + no prescribir + cita 15-30 d al 0,95). Solo cambia el comportamiento de la visita de **control** de ese episodio. Un dx de referencia que salga como episodio NUEVO (sorteo fresco, incluso repetido meses después) sigue refiriendo — eso es correcto.

## Fase 1 — La ley primero (L9)

**`Services/Invariantes.cs`** + **`leyes_simulacion.md`** + **`Services/RunStats.cs`**

- `RunStats`: contador nuevo `RemisionesEnControl` (remisiones emitidas en una visita que era control post-referencia) + `RemisionesTotales`. Alimentados desde `ConsultaSeeder` al emitir la remisión (patrón de los contadores existentes; reset en `Reset()`).
- Nueva ley **L9 · "La referencia se resuelve"**: `RemisionesEnControl == 0` (tripwire exacto, como L3). No depende del volumen ni de la duración → se pronuncia también en corridas cortas. Documentarla en `leyes_simulacion.md` (tabla + racional con los números medidos: 47×, 6,8 remisiones/referido).
- `InvariantesTests`: L9 verde con 0, roja con >0.

## Fase 2 — Estado y flujo (el fix)

**`Models/SimulatedPatient.cs`**: propiedad nueva `bool EsControlPostReferencia` — **por visita** (no compartida con el pool, a diferencia de `ProblemListConcepts`): se pone en el objeto de la visita y no se copia de vuelta.

**`Seeders/SeedOrchestrator.cs`**:
- Tras `DxDeControl` (~línea 292): si `dxSeguimiento?.EsReferencia == true` → `recurrente.EsControlPostReferencia = true`. Cubre las DOS vías (cita con motivo y retorno espontáneo con episodio agudo vigente) porque ambas salen de `DxDeControl`. El camino del paciente nuevo (línea ~261) nunca marca el flag.
- `FijarProximaVisita` (línea ~553): si `visitPatient.EsControlPostReferencia` y el dx primario es el de referencia → `MotivoProximaCita = null` aunque haya cita (la cita puede existir, pero ya no transporta el dx resuelto; `DxDeControl` con motivo null cae a crónicas/sorteo — lógica existente intacta, `SeedOrchestrator.cs:457-462`). El episodio agudo ya se cierra solo (`RegistrarEpisodioAgudo(fueControlAgudo:true)`, línea 367 — no tocar).

**`Seeders/ConsultaSeeder.cs`** (línea ~72):
- `patient.Referido = ReferenciaPolicy.DebeReferir(dxsEvaluables)` donde `dxsEvaluables` excluye el dx primario si `EsControlPostReferencia` (una comorbilidad fresca de referencia en la misma visita SÍ refiere — episodio nuevo).
- Con `Referido=false` en el control, lo demás se corrige solo **si** `LabOrderSeeder` (STAT) y `PrescriptionSeeder` (no prescribir) condicionan por `patient.Referido` — **verificar en el código al implementar**; si alguno mira `EsReferencia` del dx directamente, condicionarlo igual que la remisión.
- `SeguimientoPolicy.Probabilidad(dxs, rp, referido)`: llamarla con la misma lista filtrada (`dxsEvaluables`) para que el control post-alta de una apendicitis (grave) no dispare `FollowUpGrave` 0,80 por un cuadro ya resuelto → cae a `FollowUpCronico`/`FollowUp` según lo que quede. `SeguimientoPolicy` **no cambia** (puro, ya recibe la lista).
- Al emitir la remisión: `RunStats.RegistrarRemision(esControl: patient.EsControlPostReferencia)` (tras el fix, `esControl=true` es estructuralmente inalcanzable — esa es la gracia del tripwire).

## Fase 3 — Arnés y tests (que el bug histórico no pueda volver)

- **`TestSupport/MiniClinica.cs`**: replicar el paso nuevo en la mecánica del día (flag de control post-referencia → sin re-remisión, motivo de próxima cita null) y **actualizar la tabla paso↔línea de la cabecera** (contrato con `SeedOrchestrator.RunAsync` — obligatorio según CLAUDE.md).
- **`Sistema/SistemaRotoTests`**: sabotaje nuevo — reintroducir la remisión sin estado (ignorar el flag) → la corrida de MiniClinica debe encender **L9**.
- **Corrida sana de MiniClinica** (3 años): L9 verde + afirmar que ningún paciente acumula >2-3 visitas con el mismo dx de referencia (la métrica que hoy da 47).
- Unit tests: `SeedOrchestratorTests` (flag puesto por ambas vías; `FijarProximaVisita` anula el motivo), `ConsultaSeederTests` (control → sin obs de remisión, `Referido=false`; comorbilidad de referencia fresca en control → sí refiere), `RunStatsTests` (contadores nuevos + reset).

## Fase 4 — Birthdate a mediodía

**`Seeders/PatientSeeder.cs:56`**: `birthdate = patient.BirthDate.ToString("yyyy-MM-dd") + "T12:00:00.000" + offset` usando `Simulation.UtcOffset` con el **mismo helper/patrón de formato** que ya usan los demás datetimes del seeder (visitas/encuentros — localizarlo y reusarlo, no inventar formato). Mediodía nunca cae en una transición DST. OpenMRS almacena solo la fecha, así que el dato resultante es idéntico.
- Test unitario del string generado (seam puro o test del payload, según el patrón existente en `PatientSeederTests`/`ConsultaSeederTests` de afirmar sobre el JSON).

## Fase 5 — Documentación

- `CLAUDE.md`: actualizar el bullet *Referencia al hospital* (control post-alta no re-refiere, flag `EsControlPostReferencia`, L9) y la tabla de leyes (añadir L9).
- `leyes_simulacion.md`: L9 con su racional.
- `querys/coherencia_seguimiento.sql`: sección QA nueva — remisiones repetidas por (paciente, dx) y máximo de encuentros con el mismo dx de referencia por paciente (la query del histograma que destapó el 47×).

## Verificación

1. `dotnet test` — los 448 existentes + los nuevos en verde.
2. **Corrida corta real** (modo validación: 1 semana, `Crecimiento.Enabled=false`) contra la instancia: humo de que la remisión inicial sigue saliendo (obs `1272…`), que un birthdate cualquiera entra, y exit code 0.
3. **MiniClinica 3 años** (ya es un test): L1-L9 verdes; sabotaje → L9 roja.
4. Tras la próxima corrida larga real, re-ejecutar las queries del análisis: histograma de apendicitis por paciente (máx. esperado ≤2-3), remisiones/referido ≈ 1,0-1,2 (hoy 6,8), y que apendicitis salga del top-5.
5. El fix del birthdate se da por verificado si la corrida corta crea pacientes y `errores.csv` queda vacío (el caso exacto `1988-05-01` es aleatorio, pero mediodía lo hace estructuralmente imposible).

## Qué NO se toca

- El episodio inicial de referencia (remisión, STAT, no prescribir, banda 15-30 d, prob 0,95): intacto.
- `ReferenciaPolicy` (puro, sin estado — la memoria del episodio vive en el paciente/orquestador, no en la policy).
- `RecurrentSelector`, Bass, satisfacción, bandas de recurrencia: sin cambios.
- Datos ya sembrados: no se corrigen retroactivamente (si más adelante se quiere, sería un script aparte).
