# Plan: Dispensación en farmacia + enriquecimiento de catálogos clínicos

> Estado: **PLANTEADO** — sin implementar. Diseñado 24-ago-2026 sobre datos reales de la instancia.
> Alcance: (A) que cada enfermedad que **necesita** medicamento lo tenga (investigado, no asumido) y
> (B) modelar la **dispensación en farmacia** con el módulo `stockmanagement`.

---

## 1. Contexto verificado (24-ago-2026)

### Instancia OpenMRS
- `stockmanagement` 3.0.0 **activo**: 7 recursos REST responden (`stockitem`, `stockoperation`,
  `stockoperationtype`, `party`, `stocksource`, `userrolescope`, `stockrule`) — HTTP 200.
- **9 tipos de operación** preconfigurados, incluido **"Stock Issue"** (`stockissue`) = **dispensación**
  de medicamento al paciente.
- Existe la location **"Farmacia"** (`c1000000-…`), junto a Consultorio 1-4, Laboratorio, Recepción, Sala 1-4.
- **0 stock items**: no hay fármacos cargados como inventario.
- `billing/cashier` **instalado** (tablas `cashier_*`) — fuera del alcance de este plan (caja).
- Pipeline actual: `PrescriptionSeeder` (el médico **escribe** la `DrugOrder`) → `VisitCloseSeeder`. **No
  hay dispensación.**

### Catálogos (datos reales)
| Catálogo | Filas | Estado |
|---|---|---|
| `diagnosticos.csv` | 604 dx | ✅ **OK (no tocar)** — 208 crónicos, 308 `comun`, 82 `ambito=referencia` |
| `medicamentos.csv` | 67 fármacos | ⚠️ a ampliar + bug de columna |
| `tratamientos.csv` | 617 reglas (13 políticas + 604 por dx) | ⚠️ a enriquecer |

**Acciones de las 604 reglas por dx:**
| Acción | Nº | Prescribe | Nota |
|---|---|---|---|
| `tratamiento_especifico` | 81 | sí | regla investigada con fármaco |
| `manejo_sintomatico` | 37 | sí | sintomático (analgésico/antipirético…) |
| `tratamiento_condicionado` | 2 | no (aún) | lista fármaco pero no receta (contexto) |
| `seguimiento` | 167 | **no** | ⚠️ **casi todos crónicos** — placeholder conservador |
| `sin_receta` | 235 | **no** | agudos sintomáticos / autolimitados |
| `referencia` | 82 | no | correcto: lo pauta el hospital |

**El gap central:** de los **208 dx crónicos**, **167 están en `seguimiento` (80 % sin medicamento)**. Un
hipertenso, un diabético o un asmático debería tener su fármaco de primer nivel; hoy no lo tiene porque
solo 33 crónicos tienen `tratamiento_especifico` + 6 `manejo_sintomatico`.

**Bug detectado:** 4 fármacos de `medicamentos.csv` tienen `grupo_terapeutico = "true"` (columna
desalineada: el valor de `requiere_contexto` cayó en `grupo_terapeutico`).

---

## 2. Principio rector (igual que en `plan-homologacion.md`)

1. **Medicamento solo cuando está clínicamente indicado, investigado y verificado.** No se asume que toda
   enfermedad lo necesita: las virales/autolimitadas, las de seguimiento puro y las `referencia` **no**
   recetan.
2. **No romper las 9 leyes.** La dispensación es **contenido** de la visita (como labs, condiciones o
   recetas), no volumen/recurrencia/crecimiento → las leyes L1–L9 **no cambian**. Si algo las tocara,
   antes se escribe una ley (nunca se reescriben para "que pase").
3. Un cambio se da por bueno solo si compila **y** pasa los tests **y** una corrida real corta sale limpia.

---

## 3. Fase 1 — Enriquecimiento de catálogos (medicamentos + tratamientos)

### 1.1 Corregir el bug de datos
- `medicamentos.csv`: las 4 filas con `grupo_terapeutico="true"` → realinear la columna (el `true` es de
  `requiere_contexto`). Verificar el CSV con un parser de columnas.

### 1.2 Auditoría de las reglas sin medicamento (402 filas: 167 `seguimiento` + 235 `sin_receta`)
Clasificar cada una en **"necesita medicamento"** vs **"no necesita"**, con criterio clínico explícito:
- **No necesita (queda como está):** virales autolimitadas (resfriado, gastroenteritis viral), cuadros de
  solo observación (seguimiento de un hallazgo), y todo `referencia`.
- **Necesita (se enriquece):** crónicos tratables en primer nivel (HTA, DM2, asma, EPOC, hipotiroidismo,
  epilepsia, dislipidemia, ERC, gota, migraña, anemia, ERGE, etc.) y agudos con tratamiento sintomático
  específico (dolor, alergia, micosis, parasitosis, ITU…).

### 1.3 Investigar por categoría (prioridad = crónicos comunes y lo más frecuente en corridas)
- Fuentes: las ya usadas en `investigacion-tratamientos.md` (WHO EML 2023, WHO PEN, NICE NG109, CDC STI,
  mhGAP, OPS dengue). Cada regla nueva lleva `fuente` + `version_guia`.
- Orden de trabajo por categoría: cardiovascular → diabetes → respiratorio → endocrino → digestivo →
  osteomuscular → neurologico → dermatologico → infeccioso → urologico → salud_mental →
  ginecoobstetrico → trauma.

### 1.4 Ampliar `medicamentos.csv`
- Añadir los fármacos que falten para cubrir las reglas nuevas (p.ej. hipolipemiantes, antidepresivos,
  antiepilépticos, antiasmáticos inhalados, más antibióticos). **Verificar cada `drug_uuid`/`concept_uuid`
  contra la instancia** (`scripts/verificar_uuids.ps1` + `GET /drug`).
- Poblar `grupo_terapeutico` (evita duplicidad), `requiere_contexto` (solo vía regla investigada) y la
  posología (`dosis`, `unidad_dosis_uuid`, `frecuencia_uuid`, `dias_tratamiento`).

### 1.5 Completar `tratamientos.csv`
- Convertir las reglas "necesita" de `seguimiento`/`sin_receta` a `tratamiento_especifico` o
  `manejo_sintomatico` con su `medicamento_uuid` + `fuente`.
- Mantener las 13 políticas de categoría conservadoras (fallback: **nunca** recetar al azar).

### 1.6 Validación de catálogos
- `CatalogValidator` (cruces, UUIDs huérfanos, categorías) + `CatalogValidatorTests` (incluye el humo
  sobre los CSV reales) + `dotnet test` completo.

---

## 4. Fase 2 — Dispensación en farmacia (`stockmanagement`)

### 2.1 Sembrar stock (inventario)
- Crear **stock items** para los fármacos de `medicamentos.csv` (referenciando su `concept_uuid`), en la
  location "Farmacia", con una **cantidad inicial** y unidad de medida (comprimido/mL). Idempotente
  (reutilizable entre corridas; no se anula en `clear`, como los médicos).
- **Política de reabastecimiento** (decisión abierta): cantidad inicial holgada vs. barrido de
  reabastecimiento periódico.

### 2.2 Proveedor de farmacia
- Catálogo nuevo `catalogs/farmacia.csv` (o ampliar `personal_laboratorio.csv`): `SIM-FARMA-*` con
  `identifier`/`nombre`/`genero`. Asegurar el provider (mismo patrón `ProviderEnsurer` de los médicos/lab).

### 2.3 Nuevo `PharmacySeeder` (seam puro + async)
- Tras `PrescriptionSeeder`, por cada prescripción creada: registrar una **operación "Stock Issue"**
  (dispensación) — stock item del fármaco, cantidad = `quantity` de la receta, destino = el paciente
  (`party`), en "Farmacia".
- **Si no hay stock** → no se dispensa (y se registra el evento, sin romper la visita).

### 2.4 Encuentro de dispensación
- Encuentro en la location "Farmacia", firmado por `SIM-FARMA-*` (fecha = consulta + un desfase),
  con una obs que ligue la dispensación a la orden del médico. Reutilizar el tipo de encuentro "Order"
  o crear "Dispensing" (verificar contra la instancia).

### 2.5 Integración en el pipeline (sin romper)
- Orden en `ProcesarVisitaAsync`: … → `PrescriptionSeeder` → **`PharmacySeeder`** → `VisitCloseSeeder`.
- **Timing**: `VisitCloseSeeder.HoraCierre` debe quedar **después** del encuentro de dispensación
  (reusar `SimulatedPatient.UltimoEncuentroDatetime`, mismo patrón que la toma de laboratorio).
- **Errores**: un fallo del POST de stock se marca como ítem perdido (`ErrorTally` + `errores.csv`),
  no aborta la visita.
- **Reproducibilidad**: si hay aleatoriedad (p.ej. reabastecimiento), RNG propio (`RandomSeed + N`).
- **`clear`**: `DataCleaner` debe anular/retirar los encuentros y operaciones de farmacia de `SIM-`.

---

## 5. Fase 3 — Encaje con las leyes (sin romper, pero bien)

- **Las 9 leyes no cambian.** La dispensación añade contenido (un encuentro + una operación de stock por
  receta), no toca volumen/recurrencia/crecimiento, ni agenda, ni captación, ni referencia. Ninguna ley
  se ve afectada por construcción.
- **Riesgos de integración (que no son "leyes" pero sí corrección):**
  1. *Timing*: no cerrar la visita antes de dispensar (patrón `UltimoEncuentroDatetime`).
  2. *Coherencia farmacéutica*: nunca dispensar sin receta, y dispensar exactamente lo prescrito.
  3. *Inventario*: el stock no puede quedar negativo (la operación se rechaza si no alcanza).
- **Guardarraíl opcional (QA, no una ley de `Invariantes`):** `querys/farmacia.sql` que audite
  `0 dispensaciones sin receta`, `0 recetas sin Stock Issue cuando hay stock`, y stock ≥ 0. Mismo patrón
  que `querys/laboratorio.sql` / `querys/coherencia_seguimiento.sql`.
- **Si en el futuro** se quiere que la farmacia *gobierne* algo del volumen (no es el caso), **entonces**
  se escribirá una ley nueva (L10) antes de tocarlo.

---

## 6. Fase 4 — Tests y validación

1. Tests por seam: siembra de stock, selección de stock, `PharmacySeeder` (payload JSON), timing del
   cierre, error→ítem perdido, `clear`.
2. `CatalogValidatorTests` + humo sobre los CSV reales (catálogos enriquecidos válidos).
3. **Corrida real corta** (modo validación): verificar que cada receta tiene su Stock Issue, el encuentro
   en "Farmacia" firmado por `SIM-FARMA-*`, 0 errores, exit 0.
4. `querys/farmacia.sql` → 0 inconsistencias.

---

## 7. Decisiones de diseño abiertas (confirmar al implementar)

| # | Decisión | Propuesta por defecto |
|---|---|---|
| 1 | ¿Dispensación siempre o probabilística? | **Siempre** que haya stock; sin stock → no se dispensa (evento registrado) |
| 2 | Stock inicial y reabastecimiento | Cantidad holgada al sembrar + reabastecimiento periódico (o solo inicial; según duración de la corrida) |
| 3 | Encuentro de farmacia | Reutilizar "Order" o crear "Dispensing" (verificar contra la instancia) |
| 4 | ¿Un farmacéutico o varios? | 1-2 `SIM-FARMA-*` en "Farmacia" |
| 5 | ¿Incluir caja (`cashier`) en este plan? | **No** (fuera de alcance; fase futura) |

---

## 8. Regla de oro

Primero verificar que lo que está en el código cumple las leyes; las leyes se reescriben **solo
objetivamente**. Un catálogo se enriquece con **fuente y verificación de UUID**, nunca "rellenando por
cubrir". Y ninguna corrida se da por buena solo porque compila: tests + corrida real corta + QA.
