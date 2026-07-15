# Las leyes de la simulación

> Propiedades que **toda corrida tiene que cumplir** para que los datos que deja en OpenMRS describan una
> clínica, y no un montón de filas plausibles.
>
> No son documentación: son **código que se ejecuta en cada corrida** (`Services/Invariantes.cs`), imprime
> su veredicto en la etapa 4/5 y **cambia el exit code a 3** si alguna se rompe. Tests en
> `InvariantesTests`, con la corrida rota de 3,5 años como caso de regresión.

---

## Por qué existen

En julio de 2026 se añadió el modelo de crecimiento (difusión de Bass). Compilaba, pasaba los 380 tests,
la corrida de 3,5 años terminó en `completado` **sin una sola advertencia** y el exit code fue 0.

Y había roto el corazón del simulador.

Se descubrió tres meses después, mirando a mano los CSV de salida:

| Lo que hacía la clínica simulada | Medido |
|---|---|
| Pacientes que vinieron **una sola vez** y nunca más | **24.989 de 31.610 (79 %)** |
| Crónicos (HTA, DM2, VIH, EPOC) que **jamás volvieron a un control** | **7.686 de 11.808 (65 %)** |
| Citas de control **tiradas a la basura** | **14.025 `Missed`** contra 14.115 `Completed` |
| Visitas por paciente en tres años y medio | **1,45** |
| Fracción de visitas recurrentes, mes 1 → mes 42 | **31 % → 31 %** (constante por construcción) |
| Meses con la media diaria clavada en el techo de seguridad | **33 de 42** |
| Pacientes distintos registrados, sobre un área de 30.000 habitantes | **31.610 (el 105 % del barrio)** |

**Todos los tests seguían en verde, porque todas las piezas estaban bien.** Lo que estaba roto era el
sistema: una línea de `BassGrowthModel.CupoDelDia` derivaba el volumen del día de las altas
(`total = nuevos / (1 − PorcentajeRecurrentes)`) y dejaba a los recurrentes como un residuo fijo del 30 %.
La demanda real del panel —qué crónicos tocaba controlar hoy, qué citas vencían hoy— **no entraba en la
ecuación**. La clínica agendaba ~20 controles al día contra 14 huecos, y el sobrante vencía solo.

Estas leyes son el **test del sistema**, y corren sobre datos reales. Son lo que habría gritado el primer
día.

---

## Las ocho leyes

| # | Ley | Umbral | La corrida rota |
|---|---|---|---|
| **L1** | **El crónico vuelve a su control** | ≥ 60 % de los pacientes con ≥1 dx crónico tienen ≥2 visitas | 34,9 % ❌ |
| **L2** | **La agenda se honra** | citas `Missed` ≤ 25 % de las resueltas | 49,8 % ❌ |
| **L3** | **Ninguna cita se pierde por falta de aforo** | `RetornosDesplazadosPorAforo` = 0 | (no existía) ❌ |
| **L4** | **El panel madura** | % de visitas recurrentes del último año > el del primero | 31 % vs 31 % ❌ |
| **L5** | **La curva es una curva, no una pared** | ≤ 10 % de los días topando en `PacientesPorDiaMax` | 79 % ❌ |
| **L6** | **No se capta a más gente de la que vive en el área** | captados ≤ 50 % de `PoblacionCaptacion` | 105 % ❌ |
| **L7** | **Un paciente no es un ticket** | media ≥ 2,0 visitas/paciente | 1,45 ❌ |
| **L8** | **La clínica llega a donde se le pidió (y no más)** | meseta a ±20 % de `PacientesPorDiaObjetivo` | 45 contra 25 ❌ |

### L1 · El crónico vuelve a su control
Un hipertenso que pasa por la clínica y **no vuelve nunca** no es un paciente crónico: es una anécdota.
Es la ley que más duele romper, porque de ella cuelga toda la historia clínica longitudinal — la problem
list, los programas de atención, los controles, la re-orden de la HbA1c. Sin L1, el simulador no produce
historias clínicas: produce altas.

### L2 · La agenda se honra
Si se cita a alguien y no se le atiende, la cita es decorado. El no-show real de una consulta externa
ronda el 15-25 % (aquí lo fija `Appointments.AsistenciaProb`). **Perder la mitad de la agenda significa
que algo se la está comiendo**, y ese algo fue el cupo.

### L3 · Ninguna cita se pierde por falta de aforo
El **tripwire**. Una consulta llena recorta **las altas** (deja de captar), nunca a quien tenía cita.
El contador es 0 por construcción; si deja de serlo, es que alguien reintrodujo un cupo de recurrentes.
Es la ley que existe *específicamente* para que este bug no pueda volver.

### L4 · El panel madura
Una clínica con tres años de historia **no puede** tener el mismo mix nuevos/recurrentes que el día que
abrió: su panel de pacientes ha crecido y le genera consulta sola. Si el mix sale plano, el volumen no lo
está gobernando el panel — lo está gobernando una fórmula.

### L5 · La curva es una curva, no una pared
`PacientesPorDiaMax` es una **red de seguridad**. Si la clínica vive pegada a ella, no la gobierna el
objetivo: la gobierna el techo, y la "curva de crecimiento" es una rampa contra un muro. El validador de
configuración exige además `PacientesPorDiaMax ≥ 1,3 × PacientesPorDiaObjetivo`, para que la ley tenga
margen donde distinguir una cosa de la otra.

### L6 · No se capta a más gente de la que vive en el área
Registrar 31.610 personas distintas en un barrio de 30.000 no es un error de redondeo: es una clínica que
ha atendido al 105 % de sus vecinos. O λ está desbocada, o `PoblacionCaptacion` es de mentira.

### L7 · Un paciente no es un ticket
Si la media de visitas por paciente ronda 1, no hay evolución, ni control, ni continuidad: nada que un
ETL o un estudio longitudinal pueda explotar. Es el síntoma agregado de L1.

### L8 · La clínica llega a donde se le pidió (y no más)
`PacientesPorDiaObjetivo` es una petición que el modelo tiene que **cumplir**. Terminar en 45 cuando se
pidieron 25 significa que la calibración del boca a boca no aterriza — y que el usuario no tiene ningún
mando real sobre el tamaño de su clínica.

---

## Cuándo se juzgan

- **Antes de sembrar (etapa 2/5)** — `Invariantes.EvaluarProyeccion` comprueba **L5 y L6** sobre la
  proyección determinista. Vale más una advertencia aquí que descubrir a las seis horas que la clínica
  lleva dos años contra el techo.
- **Al terminar (etapa 4/5)** — `Invariantes.Evaluar(stats, pool, sim)` mide **las ocho** sobre lo que de
  verdad se sembró (`RunStats` + el padrón de pacientes). Imprime ✓ / ✗ / — con el número medido y una
  pista de dónde mirar. **Una ley rota ⇒ exit code 3**: los datos están escritos (no se revierte nada),
  pero la corrida **no se declara buena**.
- **En los tests** — `InvariantesTests` reproduce la corrida rota con sus números reales y exige que
  L1, L2, L4, L5 y L7 se pongan **rojas**. Si alguien reintroduce el cociente, el build se cae.

### "No procede" no es un aprobado
Una ley que la corrida no puede juzgar se marca `—` y **no cuenta**. Una ventana de una semana no puede
decir nada sobre el control trimestral de un hipertenso (L1 necesita ≥ 90 días; L7, ≥ 365; L4, dos años
naturales distintos).

---

## Cómo se cumplen ahora

El volumen del día dejó de ser un cociente y pasó a ser una **suma**:

```
altas    = Poisson(λ · peso_del_día)          ← Bass gobierna la CAPTACIÓN, y nada más
retornos = citas_de_hoy(asisten)              ← la demanda REAL del panel
         + activos_elegibles × tasa_espontánea   (RecurrentSelector)
visitas  = altas + retornos

si visitas > aforo:  se recortan LAS ALTAS
   (una consulta llena deja de captar gente nueva; jamás le da plantón al crónico que tenía cita)
```

Tres consecuencias que valen más que el arreglo:

1. **La fracción de recurrentes emerge**, no se configura: crece con el panel (proyectado 4 % el primer
   mes → 60 % en el tercer año). `PorcentajeRecurrentes` **se borró** — no gobernaba nada bueno.
2. **El aforo es el freno del crecimiento**, y es el más honesto que tiene el modelo: una clínica llena no
   capta.
3. **El padrón de pacientes se usa**: `Recurrence.VisitasEspontaneasPorPacienteAno` (def. 0,5) es la tasa
   a la que un paciente activo vuelve *por su cuenta*, con algo nuevo, meses después. Es "coger a
   cualquiera de la lista al azar", pero como tasa por paciente — así el número de retornos crece con el
   panel en vez de quedarse clavado en un porcentaje.

---

## Antes de tocar el modelo de volumen, recurrencia o crecimiento

1. ¿Qué ley protege lo que voy a cambiar? Si ninguna, **es que falta una ley** — escríbela primero.
2. Corre una ventana de ≥ 18 meses. Con menos, L1/L4/L7 no se pronuncian y el cambio pasa sin ser juzgado.
   ⚠️ Y hay una trampa peor: **el boca a boca se calibra sobre la ventana que configures**, porque el
   objetivo significa "dónde quiero la clínica al final de lo que simulo". Pedirle ir de 5 a 25 visitas/día
   en **3 meses** exige un `q` ~100 veces mayor que en 3 años: la clínica se planta en su aforo en semanas
   y la curva sale plana. Una corrida corta **prueba el pipeline, no la forma de la curva**.
3. Mira `output/crecimiento_diario.csv` → la columna **`pct_recurrentes`**. Tiene que **subir**. Si sale
   plana, el volumen ha vuelto a derivarse de las altas.
4. Mira `output/clientes_recurrentes.csv` → la columna **`visitas`**. Si la moda es 1, los pacientes se
   están abandonando.
5. Exit code 3 = el cambio rompió la simulación. **No lo des por bueno porque compile y los tests pasen**:
   eso es exactamente lo que pasó la primera vez.
