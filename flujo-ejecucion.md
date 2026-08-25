# Flujo de ejecución — la mecánica del simulador, paso a paso

> Cómo se ejecuta el proceso, como reglas claras y en orden. Es el espejo del código:
> `Program.cs` (las 5 etapas) → `SeedOrchestrator.RunAsync` (el día) → `ProcesarVisitaAsync` (la
> visita) → contabilidad post-visita → cierre. Las **9 leyes de la simulación** (`leyes_simulacion.md`)
> juzgan el resultado al final; este documento describe **cómo se produce** ese resultado.

---

## 1. Arranque — `Program.cs`, cinco etapas

1. **1/5 Validar catálogos.** Carga los 17 CSV y los valida: los **10 obligatorios** abortan si están
   vacíos; los **7 opcionales** caen a su estándar interno. **Con errores → aborta (exit 2) antes de
   tocar OpenMRS.**
2. **2/5 Proyectar días.** Ventana, días con atención vs. cerrados, y la **proyección determinista** de
   Bass (altas + retornos). Aquí se juzgan **L5 y L6** *antes* de sembrar: aviso temprano, no esperar a
   una corrida de horas.
3. **3/5 Ejecutar.** Pausa de 5 s para abortar con Ctrl+C; luego el **loop día a día** (reglas 2–5 abajo).
4. **4/5 Resumen final.** Lo realmente sembrado (`RunStats`) + el veredicto de las **9 leyes**.
   **Una ley rota ⇒ exit code 3** (los datos ya están escritos, pero la corrida no se declara buena).
5. **5/5 Fechas de auditoría** *(opcional, desactivada por defecto).* Retrofecha `date_created` por SQL
   (la única vía no-REST; ver `correccion_fechas.md`).

---

## 2. El día — `SeedOrchestrator.RunAsync` (una vez por cada día abierto)

1. **Estado del pool.** Calcula los activos `A(d)`, los satisfechos `S(d)` y el `λ(d)` de Bass.
2. **Retornos PRIMERO.** `RecurrentSelector` elige quién vuelve hoy: las **citas de control** que asisten
   (±`ToleranciaDias`, prob. `AsistenciaProb`) + los **retornos espontáneos** del panel (tasa
   `VisitasEspontaneasPorPacienteAno`). Se elige sobre el pool **tal como está** — sin las altas de hoy,
   así un paciente **nunca** es nuevo y recurrente el mismo día.
3. **Altas DESPUÉS.** Bass pide los nuevos (Poisson sobre `λ(d) · peso_del_día`) y se recortan **solo a lo
   que quede de aforo** (`CapacidadDelDia`). El aforo **jamás** desplaza un retorno — **ley L3**.
4. **Total y contexto del día.** `total = altas + retornos` → saturación; roster de médicos que abren
   consultorio; entregas de laboratorio que tocaban hoy.
5. **Procesar cada paciente** con el pipeline de la regla 3:
   - **Nuevo**: se genera el perfil (`PatientProfileGenerator`) → `PatientSeeder` (POST /patient) →
     `AllergySeeder` (solo la primera vez) → se elige dx/categoría → pipeline de visita.
   - **Recurrente**: se construye una copia que **hereda** del pool (crónicas, órdenes, problem list,
     citas, calificaciones, médico de cabecera, talla/IMC…) y se elige el motivo (`DxDeControl`).
6. **Registrar el día.** `RunStats.RegistrarDiaSimulado` + una fila en `crecimiento_diario.csv`.

---

## 3. La visita — `ProcesarVisitaAsync` (pipeline de seeders, en este orden)

1. **Consultorio y médico** (`ClinicResourceAssigner`): honra la cita reservada si viene a su control.
2. **Crear la visita** (`VisitSeeder`, `POST /visit`).
3. **Vitales** (`VitalsSeeder`): 8 obs derivadas de la enfermedad y de la peor severidad.
4. **Resolver citas** (`AppointmentSeeder`): la cita a la que vino → `Completed`; las vencidas → `Missed`.
5. **Consulta** (`ConsultaSeeder`): motivo, **diagnóstico(s)** (primario + comorbilidades), exámenes en
   consultorio, **remisión al hospital si el dx es `ambito=referencia`** (obs de remisión) y decisión de
   seguimiento (obs "Return visit date").
6. **Condiciones** (`ConditionSeeder`): dx `cronica=true` → problem list (`POST /condition`, dedup por
   `ProblemListConcepts`).
7. **Programas** (`ProgramEnrollmentSeeder`): inscripción si el dx/categoría dispara un programa.
8. **Laboratorio** (`LabOrderSeeder` + `LabWorkflowSeeder`): la orden y su ciclo (toma → resultado o
   rechazo/derivación externa).
9. **Prescripción** (`PrescriptionSeeder`): fármacos de `TreatmentSelector` (reglas investigadas de
   `tratamientos.csv`; sin regla → no se receta al azar).
10. **Agendar cita** de control (`AppointmentSeeder`, si hubo seguimiento), con el médico reservado.
11. **Cerrar la visita** (`VisitCloseSeeder`, `stopDatetime` 1–4 h después de la llegada).

---

## 4. Post-visita — contabilidad por paciente (objeto del pool)

Tras cada visita, sobre el objeto **persistente** del pool (no sobre la copia de la visita):

- **Crónicas**: acumula los dx `cronica=true` en `CronicasActivas` → el paciente volverá por ellos.
- **Episodio agudo**: abre/renueva `UltimoDxAgudo`; su visita de control lo **cierra** (evita bucles del
  mismo dx).
- **Próxima visita**: fija `ProximoElegibleDesde` (y `ProximaCita` + médico/consultorio si hubo control).
- **Calificación** (1–5): alimenta `S(d)` (el boca a boca) y el churn; no se escribe en OpenMRS.

---

## 5. Al final — cierre de la corrida

1. **Laboratorio**: entregar los resultados externos cuya fecha cae dentro de la ventana.
2. **Agenda**: barrido final — citas vencidas de quien nunca volvió → `Missed`.
3. **Evidencia**: escribir `crecimiento_diario.csv` + `clientes_recurrentes.csv` (pase lo que pase,
   también si se cancela con Ctrl+C).
4. **Veredicto**: `Invariantes.Evaluar` juzga las **9 leyes** sobre lo sembrado → exit code 3 si alguna
   se rompe.

---

## Resumen en una línea

**Validar catálogos → proyectar → por cada día (retornos primero, altas con el aforo que queda) → por
cada paciente (visita → vitales → consulta → condiciones → labs → receta → cita → cierre) → post-visita
(crónicas, episodio, próxima cita, nota) → al final (cerrar lab/agenda, escribir CSVs, juzgar las leyes).**
