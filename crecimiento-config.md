# Configuración del crecimiento de la clínica (estado actual)

Cómo está parametrizado hoy el modelo de crecimiento en `openmrs_seeder_v1/appsettings.json`,
qué significa cada valor y cómo interactúan entre sí. El modelo vive en
`Services/BassGrowthModel.cs` (difusión de Bass) + `Services/RecurrentSelector.cs` (retornos)
+ `Services/SatisfaccionPolicy.cs` (freno de satisfacción), y lo vigilan las leyes L1–L8 de
`leyes_simulacion.md` / `Services/Invariantes.cs`.

---

## 1. La idea en una frase

**Bass gobierna solo LAS ALTAS** (pacientes nuevos captados); los retornos son la demanda
real del panel de pacientes ya captados. El volumen del día es una **suma**, no un cociente:

```
altas    = Poisson(λ(d) · peso_dia)          λ(d) = [p + q·S(d)/M] · (M − A(d))
retornos = RecurrentSelector.Seleccionar(…)  ← citas de control + retornos espontáneos
visitas  = altas + retornos
si visitas > aforo → se recortan LAS ALTAS   (jamás los retornos: ley L3)
```

---

## 2. Ventana y volumen de arranque

| Parámetro | Valor actual | Significado |
|---|---|---|
| `StartDate` / `EndDate` | `2023-01-01` → `2023-12-31` | **12 meses**. ⚠️ Con menos de 18 meses las leyes L1/L4/L7 **no se pronuncian** ("no procede"): esta ventana sirve para sembrar/probar, pero no juzga del todo el modelo de crecimiento. Para una corrida "de verdad", ≥18 meses. |
| `PacientesPorDiaMedio` | `5` | **Altas/día con las que ARRANCA la clínica** (día 0, pool vacío). No es el volumen total: es el suelo de captación. De aquí se deriva `p` (ver §3). |
| `WeekdayWeights` | L-M 1.20, X-J 1.00, V 0.90, S 0.50, D 0.00 | Forma semanal del volumen. El domingo la clínica cierra. Escala tanto λ (las altas) como el aforo del día. |

## 3. El bloque `Crecimiento`

```json
"Crecimiento": {
  "Enabled": true,
  "PoblacionCaptacion": 60000,
  "PacientesPorDiaObjetivo": 25,
  "VentanaActividadDias": 365,
  "RecurrentesPorPacienteExtra": 0,
  "PacientesPorDiaMax": 45,
  "MinVisitasRecurrente": 2,
  "AsistenciaProbInsatisfecho": 0.20
}
```

| Parámetro | Valor | Qué hace |
|---|---|---|
| `Enabled` | `true` | Modo producción: la clínica arranca en 5 altas/día y **crece** por difusión de Bass hacia el objetivo. Con `false`, las altas salen del plan estático (media fija + pesos + normal), pero **los retornos los sigue poniendo el panel** — hay un solo modelo de recurrencia, no dos. |
| `PoblacionCaptacion` (`M`) | `60000` | El mercado: cuánta gente vive en el área de captación. Es el techo `(M − A(d))` de λ — con `A` = **pacientes ACTIVOS** (visitaron dentro de la ventana), no captados-desde-siempre, para que el área no "se agote". Vigilado por la ley **L6**: captados ≤ 50 % de `M`. |
| `PacientesPorDiaObjetivo` | `25` | **VISITAS TOTALES/día en la meseta** (altas + controles) — y es a la vez el **aforo** de la consulta, escalado por el peso del día. Cuando la demanda lo supera, la clínica **deja de captar** (recorta altas); nunca da plantón a una cita (L3). Debe valer lo mismo que `Satisfaccion.CapacidadComodaPorDia` (lo exige `SettingsValidator`). Ley **L8**: la meseta debe quedar a ±20 % de este valor. |
| `VentanaActividadDias` | `365` | Qué es un paciente "activo": última visita dentro de esta ventana. Define `A(d)` (el techo), `S(d)` (el boca a boca) y la elegibilidad del retorno espontáneo. |
| `RecurrentesPorPacienteExtra` | `0` | Sin efecto adicional (parámetro de reserva). |
| `PacientesPorDiaMax` | `45` | **Red de seguridad por ENCIMA del aforo**, no el aforo (el validador exige ≥1,3× el objetivo; 45 = 1,8×25). En la corrida rota histórica era este tope quien gobernaba la clínica 33 de 42 meses — hoy solo debería tocarse en picos raros. Ley **L5**: ≤10 % de los días pueden chocar con él. |
| `MinVisitasRecurrente` | `2` | Umbral de "recurrente" para `S(d)` y para las métricas (un paciente con ≥2 **visitas** cuenta; se usa `p.Visitas`, no el nº de calificaciones, para que funcione aun con la satisfacción apagada). |
| `AsistenciaProbInsatisfecho` | `0.20` | El paciente que quedó descontento acude a su cita de control solo el 20 % de las veces (contra `Appointments.AsistenciaProb` = 0.75 del satisfecho, default del código) y **no** genera retornos espontáneos: es el churn. |

### Los dos coeficientes de Bass NO se configuran — se derivan

- **`p` (innovación, el suelo)**: `CoeficienteInnovacion(PacientesPorDiaMedio=5, M=60000)` lo
  fija para que el **día 0** (pool vacío → cero retornos posibles) la clínica atienda
  exactamente sus 5 altas/día.
- **`q` (imitación, el boca a boca)**: lo calibra por **bisección** `CalibrarImitacion` para
  que la **media móvil de 30 días abiertos** de la proyección alcance en su **pico** las 25
  visitas/día objetivo. Con `A ≪ M`, `q` se lee como "+1 alta/día por cada `1/q` recurrentes
  satisfechos". La calibración corre sobre el MISMO modelo de cohortes que luego se ejecuta
  (cada visita engendra, con prob. `k` ≈ 0.70 de retorno, la siguiente del mismo paciente;
  clientela activa por ley de Little).
- La etapa 2/5 de la corrida imprime la **proyección determinista** resultante (etiquetada
  como *estimación* — con crecimiento el volumen ya no es precalculable exacto).

**El suelo real no son 5 visitas/día**: cada paciente vuelve ~`1/(1−k)` ≈ 3,3 veces, así que
5 altas/día ya producen ~12–16 visitas/día sin boca a boca. El objetivo (25) debe superar ese
suelo o `CalibrarImitacion` devuelve `q=0` y la etapa 2/5 avisa.

## 4. El motor de retornos (la otra mitad del volumen)

`Services/RecurrentSelector.cs` — dos vías que **no compiten** entre sí:

1. **La cita de control** (±`Appointments.ToleranciaDias`=3): el citado acude con 0.75
   (o 0.20 si insatisfecho). **Se atienden TODAS las que asisten** — el aforo jamás las
   desplaza (tripwire `RetornosDesplazadosPorAforo` = 0, ley L3). El que no acude es un
   no-show de verdad (su cita acaba `Missed`, ley L2: ≤25 %).
2. **El retorno espontáneo**: el paciente activo que ya cumplió su intervalo vuelve por su
   cuenta a razón de `Recurrence.VisitasEspontaneasPorPacienteAno` = `0.5`/año
   (tasa diaria `1 − e^(−0.5/365)` **por paciente** → los retornos crecen con el panel).

Los intervalos los pone `Recurrence`: agudo 7–21 d, crónico 30–120 d, control post-referencia
15–30 d. Tras cada visita, `FijarProximaVisita` fija `ProximoElegibleDesde` (y `ProximaCita`
si la consulta agendó control — FollowUp crónico 0.90 / grave 0.80 / referido 0.95 / resto 0.30).

⚠️ **Historia**: aquí vivió el bug más caro del proyecto. Antes los retornos eran un **cupo**
(30 % del volumen del día) y las ~20 citas/día competían por 14 huecos: 14.025 citas `Missed`,
65 % de crónicos sin volver jamás, mix recurrente plano 42 meses. `PorcentajeRecurrentes` se
borró; el volumen es suma, nunca cociente.

## 5. El segundo freno: la satisfacción

```json
"Satisfaccion": { "Enabled": true, "MediaBase": 4.0, "Desviacion": 0.8,
  "BonusMedicoCabecera": 0.4, "BonusCitaCumplida": 0.2, "PenalizacionCuadroGrave": 0.3,
  "PenalizacionSaturacion": 1.2, "CapacidadComodaPorDia": 25, "UmbralSatisfaccion": 3.0 }
```

Cada visita creada recibe una nota entera 1–5 (`SatisfaccionPolicy`, seam puro):
`4.0 + 0.4·(le atendió su médico de cabecera) + 0.2·(vino a su cita) − 0.3·(cuadro grave)
− 1.2·saturación(visitasDelDía/25) + N(0, 0.8)`, acotada a [1,5].

- Promedio ≤ `3.0` ⇒ **insatisfecho**: casi no acude a sus citas (0.20) y no vuelve solo.
- La media de satisfechos activos con ≥2 visitas es `S(d)` — **el combustible del boca a
  boca**: clínica saturada → notas bajas → menos `S` → menos altas. Un lazo de
  retroalimentación negativo que estabiliza la meseta.
- `CapacidadComodaPorDia` **= 25 = el objetivo/aforo** (obligatorio: mismo número).
- La nota **no se escribe en OpenMRS** (no existe concepto de encuesta): es estado de
  simulación y se persiste en los CSV de salida.

## 6. Cableado y RNG

- RNG independientes: satisfacción = `RandomSeed+19`, llegadas Poisson = `RandomSeed+20`,
  roster diario = `+18+fecha`, chequeo = `+21`/`+24` (con `RandomSeed`=42 todo es reproducible).
- **Los retornos se eligen ANTES de crear las altas del día** → un paciente no puede ser
  nuevo y recurrente el mismo día por construcción.
- El total del día (altas+retornos) alimenta la saturación de las notas de ese mismo día.

## 7. La evidencia: `output/`

- **`crecimiento_diario.csv`** — la curva lista para graficar: `activos_A`, `captados_total`,
  `satisfechos_activos_S`, `lambda_altas`, `media_movil_30d` (de lo **atendido**, no de λ),
  `atendidos`, `nuevos`, `recurrentes`, **`pct_recurrentes`** (la columna a mirar: **tiene
  que subir** — ley L4; plana = el volumen volvió a derivarse de las altas), `aforo`,
  `altas_no_captadas`, `calificacion_media_dia`, `saturacion`, `pct_mercado`.
- **`clientes_recurrentes.csv`** — el padrón al cierre (visitas, notas, satisfecho, activo,
  última visita, crónicas).

## 8. Qué esperar con estos valores

Con la parametrización actual (5 altas/día → objetivo 25, M=60.000, ventana 2023):
la clínica arranca en ~5–6 visitas/día y la curva de Bass la lleva hacia ~18/día al cierre
del año (la referencia de 3 años: 6,2 → 17,9 → 23,8 → 25,2, mix recurrente 4 % → 60 %).
Con la ventana de solo 12 meses **no llega a la meseta** — L8 evalúa lo alcanzable y
L1/L4/L7 no se pronuncian. Si el objetivo es validar el crecimiento completo, extender
`EndDate` a ≥ mediados de 2024.

## 9. Reglas de oro antes de tocar esto

1. Leer `leyes_simulacion.md`: cualquier cambio a volumen/recurrencia/crecimiento lo juzgan
   las leyes en cada corrida (ley rota ⇒ exit code 3, con los datos ya sembrados).
2. Si un cambio no lo protege ninguna ley, **falta una ley: escribirla primero**.
3. No dar una corrida por buena porque compile y pasen los tests — correr ≥18 meses y
   mirar que `pct_recurrentes` suba.
4. `PacientesPorDiaObjetivo` = `CapacidadComodaPorDia`, y `PacientesPorDiaMax` ≥ 1,3× el
   objetivo (los exige el validador de arranque).
