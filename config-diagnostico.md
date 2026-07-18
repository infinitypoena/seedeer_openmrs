# Ajustar la frecuencia de diagnósticos desde `appsettings.json`

Cómo reformar **qué enfermedades salen y con qué frecuencia** tocando solo la configuración,
**sin editar ningún catálogo CSV**. Todas las claves cuelgan de `Simulation` en
`openmrs_seeder_v1/openmrs_seeder_v1/appsettings.json`.

> ℹ️ El `appsettings.json` real es **local y está gitignored**; el que se versiona es
> `appsettings.example.json`. Los defaults de estas claves están en
> `Configuration/SimulationSettings.cs`. Si cambiás un default para todos, cambialo en ambos.

---

## 1. Primero: qué puede y qué NO puede hacer esta configuración

El diagnóstico de una visita se elige en **dos capas**:

```
Capa 1  ── ¿QUÉ CATEGORÍA?  (respiratorio, cardiovascular, digestivo, …)
            → la gobiernan los PESOS POR EDAD/SEXO de  epidemiology-profile.csv   ← CATÁLOGO
Capa 2  ── ¿QUÉ DIAGNÓSTICO dentro de la categoría?
            → lo gobiernan  peso_M/peso_F  y el flag  comun  de  diagnosticos.csv ← CATÁLOGO
```

Los **pesos base** (qué categoría domina en un adulto, cuánto pesa la gripe frente a la neumonía)
viven en los **catálogos**. Esta guía **no los toca**.

Lo que `appsettings.json` sí controla son **5 diales globales** que reforman la distribución
*por encima* de esos pesos, sin editar una sola fila:

| # | Dial | Qué reforma |
|---|------|-------------|
| A | **Sesgo común ↔ raro** | cuánto se inclina la clínica hacia lo banal vs lo infrecuente/grave |
| B | **Concentración ↔ variedad** | si unos pocos dx frecuentes dominan, o si aflora la cola larga del catálogo |
| C | **Estacionalidad** | cuánto suben gripe (invierno) o dengue/EDA (verano) en su temporada |
| D | **Multimorbilidad** | cuántos diagnósticos lleva cada visita (comorbilidades) |
| E | **Repetición en los retornos** | cuánto se repite el MISMO dx cuando el paciente vuelve a control |

> ⚠️ Si lo que querés es cambiar **qué categoría** predomina (p.ej. "más traumatología", "menos
> cardiología") o **qué diagnóstico concreto** es común, eso **sí** está en los catálogos
> (`epidemiology-profile.csv` y la columna `comun`/`peso_*` de `diagnosticos.csv`) y queda fuera de
> este documento. Ver `parametrizacion_archivos.md`.

---

## A. Sesgo común ↔ raro — `CommonProbMin` / `CommonProbMax`

```json
"CommonProbMin": 0.75,
"CommonProbMax": 0.95,
```

**Qué hacen.** Al empezar **cada corrida** se sortea una probabilidad `p` uniforme en
`[CommonProbMin, CommonProbMax]`. Luego, en **cada visita**, con probabilidad `p` el diagnóstico se
restringe al pool de enfermedades marcadas `comun=true` (gripe, EDA, IVU, lumbalgia…); con
probabilidad `1−p` se abre al pool `comun=false`, donde viven las infrecuentes y las graves agudas
(neumonía, apendicitis, TB, meningitis…). Así la proporción **varía entre corridas** pero siempre se
inclina a lo común.

| Movimiento | Efecto |
|---|---|
| **Subir** ambos (p.ej. `0.82`–`0.96`) | Más visitas de cuadros banales. **Achica el pool no-común** → la patología grave aguda cae. |
| **Bajar** ambos (p.ej. `0.60`–`0.80`) | Más variedad de cuadros raros/graves; la clínica se ve más "de hospital". |

**Ésta es la palanca para el efecto lateral que se vio en la validación:** con `0.75`–`0.95` la
corrida sorteó `p≈0.80` → el **20 %** de las visitas fue al pool no-común, y ahí se concentraron
**Neumonía 1.8 %** y **Apendicitis 1.6 %** (alto para primer nivel). Subir a `0.82`–`0.96`
(≈`p 0.89`) deja ~**11 %** en el pool no-común → **casi la mitad** de esos graves, **sin tocar** la
frecuencia de lo común.

---

## B. Concentración ↔ variedad — `Variedad.RepeticionDamping`

```json
"Variedad": {
  "RepeticionDamping": 0.10
}
```

**Qué hace.** Cada vez que un diagnóstico sale en la corrida, su peso efectivo baja para la siguiente
tirada: `peso_efectivo = peso / (1 + damping × veces_ya_usado)`. Sin esto, los dx de peso alto
(gripe, resfriado) monopolizan y solo ~30 % del catálogo aparece nunca. El contador se **reinicia
cada corrida**; no toca el perfil por edad/sexo/clima ni los controles crónicos/agudos (ésos saltan
el selector).

| Valor | Efecto | La gripe (peso 40) tras 20 usos pesa… |
|---|---|---|
| `0` | Apagado: los frecuentes monopolizan | 40 (sin cambio) |
| `0.10` (actual) | Lo común domina bastante, algo de cola larga | 40 / 3 = **13.3** |
| `0.25` (anterior) | Fuerte empuje a la variedad | 40 / 6 = **6.7** |
| `0.50` | Variedad agresiva; la clínica se ve muy dispersa | 40 / 11 = 3.6 |

**Regla práctica.** ¿Querés que gripe/EDA/lumbalgia **se repitan más** (clínica realista saturada de
lo banal)? → **bajá** el damping (`0.05`–`0.10`). ¿Querés **historias más variadas** para
demostraciones/ETL? → **subilo** (`0.25`–`0.40`). En la validación, bajarlo de `0.25→0.10` puso los
cuadros comunes al tope del ranking conservando 262 dx distintos de 407.

---

## C. Estacionalidad — `Climate.SeasonalBoost` / `Climate.Enabled`

```json
"Climate": {
  "Enabled": true,
  "SeasonalBoost": 2.5
}
```

**Qué hace.** Si `clima.csv` existe, cada visita cae en una estación (por semana ISO). Las categorías
y diagnósticos etiquetados con esa estación (columna `clima` en `diagnosticos.csv`: gripe→invierno,
dengue/EDA→verano,lluvia) **multiplican su peso** por `SeasonalBoost`.

| Movimiento | Efecto |
|---|---|
| `SeasonalBoost` **alto** (p.ej. `4.0`) | Picos estacionales marcados: mucha gripe en invierno, mucho dengue/EDA en verano |
| `SeasonalBoost` = `1.0` | Neutro aunque el clima esté activo (sin picos) |
| `Enabled: false` | Ignora la estación por completo; distribución plana todo el año |

Útil si el objetivo es **mostrar temporadas** (curva de dengue en la época lluviosa). No cambia la
mezcla anual promedio, solo **cuándo** se concentra.

---

## D. Multimorbilidad — `Comorbidity.*`

```json
"Comorbidity": {
  "BaseProbability": 0.20,
  "MaxAdditional": 2,
  "SecondExtraProbability": 0.25,
  "AffinityBoost": 4.0,
  "AgeScaling": { "0-14": 0.3, "15-29": 0.5, "30-44": 0.8, "45-64": 1.3, "65+": 1.8 }
}
```

**Qué hace.** Tras el diagnóstico primario, una visita puede sumar 1..N diagnósticos extra en el
mismo encuentro. Controla **cuántos diagnósticos por visita** hay (no cuál es el primario).

| Clave | Efecto al subirla |
|---|---|
| `BaseProbability` | Sube la probabilidad de que una visita tenga ≥1 comorbilidad |
| `MaxAdditional` | Techo de diagnósticos extra (2 = hasta triple diagnóstico) |
| `SecondExtraProbability` | Probabilidad de un 2.º extra (si ya hubo uno) |
| `AffinityBoost` | Cuánto se favorecen clusters clínicos afines (diabetes↔cardiovascular, vía `comorbilidad_afinidades.csv`) — más realismo, menos aleatorio |
| `AgeScaling` | Multiplicador por grupo de edad: la comorbilidad **escala con la edad** (un 65+ acumula más) |

**Regla práctica.** ¿Historias clínicas más "cargadas" (paciente polipatológico)? → subí
`BaseProbability` (`0.30`–`0.40`) y `AgeScaling` de los mayores. ¿Visitas de un solo motivo, más
limpias? → bajá `BaseProbability` o poné `MaxAdditional: 0` (apaga la comorbilidad). No afecta al
sesgo común/raro del primario.

---

## E. Repetición del mismo dx en los retornos — seguimiento

```json
"SeguimientoCronicoProb": 0.70,
"SeguimientoAgudoProb": 0.70,
"VentanaSeguimientoAgudoDias": 30,
```

**Qué hacen.** Cuando un paciente **vuelve**, deciden si trae el MISMO diagnóstico (visita de control)
o uno nuevo al azar:

| Clave | Efecto |
|---|---|
| `SeguimientoCronicoProb` | Prob. de que un recurrente con condición crónica venga a **control de esa crónica** (HTA, DM2…) en vez de un motivo agudo nuevo. Subir → más continuidad de crónicos (mismo dx repetido). |
| `SeguimientoAgudoProb` | Prob. de que un recurrente **no** crónico traiga el **mismo dx agudo abierto** (neumonía→control de neumonía) en vez de uno nuevo. Subir → episodios agudos coherentes. |
| `VentanaSeguimientoAgudoDias` | Días que un episodio agudo sigue "abierto" para poder repetirse. Alargar → más controles del mismo cuadro agudo. |

**Regla práctica.** Estas claves suben la **repetición del mismo diagnóstico** entre visitas de un
paciente (bueno para continuidad longitudinal). Bajarlas hace que cada visita parezca un motivo nuevo
e inconexo. En medición A/B, `SeguimientoAgudoProb=0.70` llevó el "mismo dx en visitas consecutivas"
de 2.3 % a 64.2 %.

> ⚠️ **No confundir con `Recurrence.*` ni `Crecimiento.*`.** Esas gobiernan **cuántos** pacientes
> vuelven y **cuándo** (volumen/recurrencia), no qué diagnóstico traen, y están **protegidas por las
> leyes de la simulación** (`leyes_simulacion.md`, L1–L8). Tocarlas es otra cosa: leé esas leyes
> antes.

---

## Recetario rápido

| Quiero… | Toco | Cómo |
|---|---|---|
| Que **lo común salga más** (gripe, EDA, IVU) | B + A | `RepeticionDamping` ↓ (`0.10`→`0.05`) y `CommonProbMin/Max` ↑ |
| Que **la patología grave aguda** (neumonía, apendicitis) **sea más rara** | A | `CommonProbMin/Max` ↑ (`0.82`–`0.96`) |
| Más **variedad** de enfermedades (demo/ETL) | B | `RepeticionDamping` ↑ (`0.25`–`0.40`) |
| **Picos estacionales** marcados (dengue en lluvia) | C | `Climate.SeasonalBoost` ↑ (`4.0`) |
| Pacientes **polipatológicos** (más dx por visita) | D | `Comorbidity.BaseProbability` ↑ y `AgeScaling` mayores ↑ |
| Visitas de **un solo motivo**, limpias | D | `Comorbidity.MaxAdditional: 0` |
| Más **controles del mismo dx** (continuidad) | E | `SeguimientoCronicoProb` / `SeguimientoAgudoProb` ↑ |

---

## Cómo verificar un cambio (corrida corta, no destructiva)

1. Poné una ventana corta en `appsettings.json` (1 mes basta) — p.ej. `StartDate`/`EndDate` de un
   mes, `Crecimiento.Enabled: false` y `PacientesPorDiaMedio` alto (~20) para una muestra robusta.
   **El volumen no afecta la mezcla de dx**, solo el tamaño de la muestra.
2. Corré el seeder redirigiendo la salida a un log (⚠️ nunca cortar la tubería del proceso;
   `< /dev/null` evita que espere una tecla):
   ```bash
   dotnet run --project openmrs_seeder_v1/openmrs_seeder_v1/openmrs_seeder_v1.csproj < /dev/null > corrida.log 2>&1
   ```
3. **Lo rápido** — el resumen final (etapa 4/5) ya trae `Top 5 diagnósticos de N distintos`.
4. **Lo completo** — el log registra el dx primario de cada consulta; agregá la distribución entera
   sin tocar la BD:
   ```bash
   # Reparto común vs no-común
   grep -oE "comun: (True|False)" corrida.log | sort | uniq -c
   # Ranking de diagnósticos primarios
   grep -oE "Dx: [^|]+\|" corrida.log | sed -E 's/^Dx: //; s/ *\|$//' | sort | uniq -c | sort -rn | head -20
   ```
5. **Revertí la ventana** (y `Crecimiento`/`PacientesPorDiaMedio`) al terminar. La corrida deja
   pacientes `SIM-` en la instancia: `dotnet run -- clear` los limpia.

> El resumen y el log son **por corrida** (en memoria), así que el análisis no se contamina con datos
> de corridas anteriores que ya estén en la base — filtrar la BD por ventana **sí** se contaminaría.

---

## Referencias

- `parametrizacion_archivos.md` — referencia completa de parámetros y esquemas de los CSV
- `leyes_simulacion.md` — las 8 leyes (antes de tocar volumen/recurrencia/crecimiento)
- `CLAUDE.md` — bullets *Diagnosis variety*, *Two-stage diagnosis selection*, *Rebalanceo de frecuencia*
