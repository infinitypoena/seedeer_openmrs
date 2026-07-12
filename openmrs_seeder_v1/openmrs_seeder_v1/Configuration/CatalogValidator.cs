using OpenmrsSeeder.Models.Catalogs;
using OpenmrsSeeder.Services;

namespace OpenmrsSeeder.Configuration;

/// <summary>
/// Validación fail-fast de los catálogos CSV al arranque, hermana de <see cref="SettingsValidator"/>.
/// <para>
/// El <see cref="CatalogLoader"/> es deliberadamente mudo: un CSV ausente o vacío da lista vacía, un
/// booleano mal escrito da <c>false</c> y un número basura da <c>0</c> — sin una sola queja. Eso
/// convierte una errata (una categoría mal escrita, una banda invertida, un catálogo que no se copió)
/// en una feature apagada en silencio o en comportamiento raro a mitad de una corrida de horas.
/// Aquí se comprueba lo que el loader no comprueba, ANTES de tocar OpenMRS.
/// </para>
/// <para>
/// <b>Errores</b> abortan el arranque. <b>Advertencias</b> son situaciones válidas pero sospechosas
/// (una categoría sin frases de motivo de consulta, un panel sin componentes) y solo se loguean.
/// </para>
/// Clase estática pura (sin red ni DI) para poder testearla de forma determinista.
/// </summary>
public static class CatalogValidator
{
    /// <summary>Las 13 categorías clínicas del simulador. Fuente de verdad para validar los catálogos.</summary>
    public static readonly IReadOnlySet<string> CategoriasValidas = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "respiratorio", "cardiovascular", "diabetes", "digestivo", "osteomuscular", "urologico",
        "infeccioso", "endocrino", "neurologico", "dermatologico", "salud_mental",
        "ginecoobstetrico", "trauma"
    };

    public static readonly IReadOnlySet<string> GruposEdad = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "0-14", "15-29", "30-44", "45-64", "65+" };

    public static readonly IReadOnlySet<string> Estaciones = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "verano", "invierno", "lluvia", "seca" };

    private static readonly IReadOnlySet<string> Severidades = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "leve", "moderado", "grave" };

    private static readonly IReadOnlySet<string> TiposAlergeno = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "DRUG", "FOOD", "ENVIRONMENT" };

    private static readonly IReadOnlySet<string> Datatypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "numeric", "coded", "panel", "imagen" };

    public static (List<string> Errores, List<string> Advertencias) Validate(CatalogLoader c)
    {
        var errores = new List<string>();
        var avisos  = new List<string>();

        // ── Presencia: los obligatorios no pueden estar vacíos ────────────────────
        // Los opcionales (clima, consultorios, afinidades, programas, direcciones, paneles) sí:
        // vacío = feature desactivada, que es un modo de uso legítimo.
        void Obligatorio(int n, string archivo)
        {
            if (n == 0)
                errores.Add($"{archivo} está vacío o no se encontró (catálogo obligatorio)");
        }

        Obligatorio(c.EpidemiologyProfile.Count, "epidemiology-profile.csv");
        Obligatorio(c.Diagnosticos.Count,        "diagnosticos.csv");
        Obligatorio(c.Medicamentos.Count,        "medicamentos.csv");
        Obligatorio(c.Laboratorios.Count,        "laboratorios.csv");
        Obligatorio(c.ExamenesClinicos.Count,    "examenes_clinicos.csv");
        Obligatorio(c.Alergenos.Count,           "alergenos.csv");
        Obligatorio(c.MotivosConsulta.Count,     "motivos_consulta.csv");
        Obligatorio(c.Nombres.Count,             "nombres.csv");
        Obligatorio(c.Apellidos.Count,           "apellidos.csv");

        ValidarEpidemiologia(c, errores);
        ValidarDiagnosticos(c, errores);
        ValidarCruceCategorias(c, errores);
        ValidarLaboratorios(c, errores, avisos);
        ValidarPaneles(c, errores);
        ValidarExamenes(c, errores);
        ValidarMedicamentos(c, errores);
        ValidarAlergenos(c, errores);
        ValidarProgramas(c, errores);
        ValidarCategoriasSimples(c, errores, avisos);
        ValidarConsultorios(c, errores);
        ValidarClima(c, errores);
        ValidarNombresYDirecciones(c, errores);

        return (errores, avisos);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Ubicación legible de una fila: "laboratorios.csv fila 7 (Transaminasa AST)".</summary>
    private static string Fila(string archivo, int indice, string nombre) =>
        $"{archivo} fila {indice + 2}" + (string.IsNullOrWhiteSpace(nombre) ? "" : $" ({nombre})");

    private static bool EsCategoria(string cat) => CategoriasValidas.Contains(cat);

    /// <summary>Valor de una columna enumerada: debe estar vacío o pertenecer al dominio.</summary>
    private static void Enum_(string valor, IReadOnlySet<string> dominio, string donde, string columna,
                              List<string> errores, bool permiteVacio = true)
    {
        if (string.IsNullOrWhiteSpace(valor)) { if (!permiteVacio) errores.Add($"{donde}: {columna} no puede estar vacío"); return; }
        if (!dominio.Contains(valor))
            errores.Add($"{donde}: {columna}='{valor}' no es válido (esperado: {string.Join(" | ", dominio)})");
    }

    private static void Banda(double min, double max, string donde, string columnaMin, string columnaMax,
                             List<string> errores)
    {
        if (min > max)
            errores.Add($"{donde}: {columnaMin} ({min}) no puede ser mayor que {columnaMax} ({max})");
    }

    // ── Reglas por catálogo ───────────────────────────────────────────────────

    private static void ValidarEpidemiologia(CatalogLoader c, List<string> errores)
    {
        for (var i = 0; i < c.EpidemiologyProfile.Count; i++)
        {
            var e = c.EpidemiologyProfile[i];
            var donde = Fila("epidemiology-profile.csv", i, e.Categoria);

            if (!EsCategoria(e.Categoria))
                errores.Add($"{donde}: categoria='{e.Categoria}' no es una de las 13 categorías del simulador");
            Enum_(e.GrupoEdad, GruposEdad, donde, "grupo_edad", errores, permiteVacio: false);
            if (!string.Equals(e.Genero, "Ambos", StringComparison.OrdinalIgnoreCase) &&
                e.Genero is not ("M" or "F"))
                errores.Add($"{donde}: genero='{e.Genero}' no es válido (esperado: M | F | Ambos)");
            if (e.Peso < 0)
                errores.Add($"{donde}: peso no puede ser negativo (valor: {e.Peso})");
        }

        if (c.EpidemiologyProfile.Count == 0) return;

        // Cobertura: sin una fila con peso > 0 para su (grupo, género), EpidemiologySelector no puede
        // elegir categoría para ese perfil de paciente y la visita se queda sin diagnóstico.
        foreach (var grupo in GruposEdad)
            foreach (var genero in new[] { "M", "F" })
            {
                var hay = c.EpidemiologyProfile.Any(e =>
                    string.Equals(e.GrupoEdad, grupo, StringComparison.OrdinalIgnoreCase) &&
                    (string.Equals(e.Genero, genero, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(e.Genero, "Ambos", StringComparison.OrdinalIgnoreCase)) &&
                    e.Peso > 0);
                if (!hay)
                    errores.Add($"epidemiology-profile.csv: no hay ninguna categoría con peso > 0 para " +
                                $"({grupo}, {genero}) — esos pacientes no podrían recibir diagnóstico");
            }
    }

    private static void ValidarDiagnosticos(CatalogLoader c, List<string> errores)
    {
        var imc  = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "alto", "bajo" };
        var alta = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "alta" };
        var fc   = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "alta", "baja" };
        var baja = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "baja" };

        for (var i = 0; i < c.Diagnosticos.Count; i++)
        {
            var d = c.Diagnosticos[i];
            var donde = Fila("diagnosticos.csv", i, d.NombreEs);

            if (string.IsNullOrWhiteSpace(d.CielUuid))
                errores.Add($"{donde}: ciel_uuid vacío");
            if (!EsCategoria(d.Categoria))
                errores.Add($"{donde}: categoria='{d.Categoria}' no es una de las 13 categorías del simulador");
            Enum_(d.Severidad, Severidades, donde, "severidad", errores, permiteVacio: false);

            if (d.Sexo is not ("" or "M" or "F"))
                errores.Add($"{donde}: sexo='{d.Sexo}' no es válido (esperado: M | F | vacío = ambos)");

            Enum_(d.VitalImc,  imc,  donde, "vital_imc",  errores);
            Enum_(d.VitalPa,   alta, donde, "vital_pa",   errores);
            Enum_(d.VitalFc,   fc,   donde, "vital_fc",   errores);
            Enum_(d.VitalSpo2, baja, donde, "vital_spo2", errores);

            foreach (var estacion in d.Clima)
                if (!Estaciones.Contains(estacion))
                    errores.Add($"{donde}: clima='{estacion}' no es una estación válida " +
                                $"(esperado: {string.Join(" | ", Estaciones)})");

            if (d.PesoM < 0 || d.PesoF < 0)
                errores.Add($"{donde}: los pesos no pueden ser negativos (peso_M={d.PesoM}, peso_F={d.PesoF})");
            if (d.PesoM == 0 && d.PesoF == 0)
                errores.Add($"{donde}: peso_M y peso_F son 0 — el diagnóstico nunca podría elegirse");

            if (!d.Aplica0_14 && !d.Aplica15_29 && !d.Aplica30_44 && !d.Aplica45_64 && !d.Aplica65mas)
                errores.Add($"{donde}: no aplica a ningún grupo de edad — el diagnóstico nunca podría elegirse");
        }
    }

    /// <summary>
    /// Las dos mitades del motor epidemiológico tienen que encajar: el selector elige primero una
    /// categoría del perfil y luego un diagnóstico DE esa categoría. Una categoría del perfil sin
    /// diagnósticos deja la visita sin dx; un diagnóstico de una categoría ausente del perfil es
    /// inalcanzable (peso muerto en el catálogo).
    /// </summary>
    private static void ValidarCruceCategorias(CatalogLoader c, List<string> errores)
    {
        if (c.EpidemiologyProfile.Count == 0 || c.Diagnosticos.Count == 0) return;

        var enPerfil = c.EpidemiologyProfile.Where(e => e.Peso > 0)
            .Select(e => e.Categoria).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var enDx = c.Diagnosticos.Select(d => d.Categoria).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var cat in enPerfil.Where(cat => !enDx.Contains(cat)).OrderBy(x => x))
            errores.Add($"categoría '{cat}' tiene peso en epidemiology-profile.csv pero no tiene ningún " +
                        $"diagnóstico en diagnosticos.csv — esas visitas se quedarían sin diagnóstico");

        foreach (var cat in enDx.Where(cat => EsCategoria(cat) && !enPerfil.Contains(cat)).OrderBy(x => x))
            errores.Add($"categoría '{cat}' tiene diagnósticos en diagnosticos.csv pero no aparece con peso " +
                        $"en epidemiology-profile.csv — esos diagnósticos son inalcanzables");
    }

    private static void ValidarLaboratorios(CatalogLoader c, List<string> errores, List<string> avisos)
    {
        for (var i = 0; i < c.Laboratorios.Count; i++)
        {
            var l = c.Laboratorios[i];
            var donde = Fila("laboratorios.csv", i, l.NombreEs);

            if (string.IsNullOrWhiteSpace(l.CielUuid))
                errores.Add($"{donde}: ciel_uuid vacío");
            if (!AplicaAlgunaCategoria(l))
                errores.Add($"{donde}: no aplica a ninguna categoría — el laboratorio nunca podría ordenarse");
            Enum_(l.Datatype, Datatypes, donde, "datatype", errores);

            foreach (var t in l.ResTrigger.Where(t => !EsCategoria(t)))
                errores.Add($"{donde}: res_trigger='{t}' no es una de las 13 categorías del simulador");

            if (l.Datatype.Equals("numeric", StringComparison.OrdinalIgnoreCase))
            {
                Banda(l.ResMin, l.ResMax, donde, "res_min", "res_max", errores);
                Banda(l.ResMinAnormal, l.ResMaxAnormal, donde, "res_min_anormal", "res_max_anormal", errores);
                if (l.ResMax <= 0)
                    errores.Add($"{donde}: datatype=numeric exige una banda normal (res_min/res_max)");
            }
            else if (l.Datatype.Equals("coded", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(l.ResNormalUuid) || string.IsNullOrWhiteSpace(l.ResAnormalUuid))
                    errores.Add($"{donde}: datatype=coded exige res_normal_uuid y res_anormal_uuid");
            }
            else if (l.Datatype.Equals("panel", StringComparison.OrdinalIgnoreCase))
            {
                if (!c.Paneles.Any(p => p.PanelUuid == l.CielUuid))
                    avisos.Add($"{donde}: datatype=panel sin componentes en paneles.csv — la orden quedará sin resultado");
            }
        }
    }

    private static void ValidarPaneles(CatalogLoader c, List<string> errores)
    {
        var labs = c.Laboratorios.Select(l => l.CielUuid).ToHashSet(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < c.Paneles.Count; i++)
        {
            var p = c.Paneles[i];
            var donde = Fila("paneles.csv", i, p.Nombre);

            if (c.Laboratorios.Count > 0 && !labs.Contains(p.PanelUuid))
                errores.Add($"{donde}: panel_uuid '{p.PanelUuid}' no existe en laboratorios.csv — " +
                            $"el componente nunca se usaría");
            if (string.IsNullOrWhiteSpace(p.ComponenteUuid))
                errores.Add($"{donde}: componente_uuid vacío");
            Banda(p.ResMin, p.ResMax, donde, "res_min", "res_max", errores);
            Banda(p.ResMinAnormal, p.ResMaxAnormal, donde, "res_min_anormal", "res_max_anormal", errores);

            foreach (var t in p.ResTrigger.Where(t => !EsCategoria(t)))
                errores.Add($"{donde}: res_trigger='{t}' no es una de las 13 categorías del simulador");
        }
    }

    private static void ValidarExamenes(CatalogLoader c, List<string> errores)
    {
        for (var i = 0; i < c.ExamenesClinicos.Count; i++)
        {
            var e = c.ExamenesClinicos[i];
            var donde = Fila("examenes_clinicos.csv", i, e.NombreEs);

            if (string.IsNullOrWhiteSpace(e.CielUuid))
                errores.Add($"{donde}: ciel_uuid vacío");
            if (!AplicaAlgunaCategoria(e))
                errores.Add($"{donde}: no aplica a ninguna categoría — el examen nunca podría registrarse");

            if (e.TipoResultado is not ("numerico" or "categorico"))
            {
                errores.Add($"{donde}: tipo_resultado='{e.TipoResultado}' no es válido (esperado: numerico | categorico)");
                continue;
            }

            // Un examen numérico SIN banda no tiene de dónde sacar el valor (ya no hay fallback por unidad).
            if (e.TipoResultado == "numerico")
            {
                Banda(e.ResMin, e.ResMax, donde, "res_min", "res_max", errores);
                if (e.ResMax <= 0)
                    errores.Add($"{donde}: tipo_resultado=numerico exige una banda (res_min/res_max)");
            }
        }
    }

    private static void ValidarMedicamentos(CatalogLoader c, List<string> errores)
    {
        for (var i = 0; i < c.Medicamentos.Count; i++)
        {
            var m = c.Medicamentos[i];
            var donde = Fila("medicamentos.csv", i, m.NombreGenerico);

            if (string.IsNullOrWhiteSpace(m.DrugUuid))
                errores.Add($"{donde}: drug_uuid vacío");
            if (string.IsNullOrWhiteSpace(m.ConceptUuid))
                errores.Add($"{donde}: concept_uuid vacío");
            if (!AplicaAlgunaCategoria(m))
                errores.Add($"{donde}: no aplica a ninguna categoría — el fármaco nunca podría recetarse");
            if (m.Dosis < 0)
                errores.Add($"{donde}: dosis no puede ser negativa (valor: {m.Dosis})");
            if (m.DiasTratamiento < 0)
                errores.Add($"{donde}: dias_tratamiento no puede ser negativo (valor: {m.DiasTratamiento})");
        }
    }

    private static void ValidarAlergenos(CatalogLoader c, List<string> errores)
    {
        for (var i = 0; i < c.Alergenos.Count; i++)
        {
            var a = c.Alergenos[i];
            var donde = Fila("alergenos.csv", i, a.NombreEs);

            if (string.IsNullOrWhiteSpace(a.ConceptUuid))
                errores.Add($"{donde}: concept_uuid vacío");
            Enum_(a.TipoAlergeno, TiposAlergeno, donde, "tipo_alergeno", errores, permiteVacio: false);
        }
    }

    private static void ValidarProgramas(CatalogLoader c, List<string> errores)
    {
        for (var i = 0; i < c.Programas.Count; i++)
        {
            var p = c.Programas[i];
            var donde = Fila("programas.csv", i, p.Nombre);

            if (string.IsNullOrWhiteSpace(p.ProgramUuid))
                errores.Add($"{donde}: program_uuid vacío");
            if (p.TriggerDx.Count == 0 && p.TriggerCategoria.Count == 0)
                errores.Add($"{donde}: sin trigger_dx ni trigger_categoria — nadie se inscribiría nunca");

            foreach (var cat in p.TriggerCategoria.Where(cat => !EsCategoria(cat)))
                errores.Add($"{donde}: trigger_categoria='{cat}' no es una de las 13 categorías del simulador");
        }
    }

    private static void ValidarCategoriasSimples(CatalogLoader c, List<string> errores, List<string> avisos)
    {
        for (var i = 0; i < c.MotivosConsulta.Count; i++)
        {
            var m = c.MotivosConsulta[i];
            if (!EsCategoria(m.Categoria))
                errores.Add($"{Fila("motivos_consulta.csv", i, m.Texto)}: categoria='{m.Categoria}' " +
                            $"no es una de las 13 categorías del simulador");
        }

        // Una categoría sin frases deja la consulta sin obs de motivo, pero no rompe la corrida.
        if (c.MotivosConsulta.Count > 0)
        {
            var conMotivo = c.MotivosConsulta.Select(m => m.Categoria).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var cat in CategoriasValidas.Where(cat => !conMotivo.Contains(cat)).OrderBy(x => x))
                avisos.Add($"motivos_consulta.csv: la categoría '{cat}' no tiene frases — " +
                           $"esas consultas se registrarán sin motivo");
        }

        for (var i = 0; i < c.Afinidades.Count; i++)
        {
            var a = c.Afinidades[i];
            var donde = Fila("comorbilidad_afinidades.csv", i, a.Categoria);

            if (!EsCategoria(a.Categoria))
                errores.Add($"{donde}: categoria='{a.Categoria}' no es una de las 13 categorías del simulador");
            foreach (var afin in a.Afines.Where(afin => !EsCategoria(afin)))
                errores.Add($"{donde}: afines='{afin}' no es una de las 13 categorías del simulador");
        }
    }

    private static void ValidarConsultorios(CatalogLoader c, List<string> errores)
    {
        var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < c.Consultorios.Count; i++)
        {
            var co = c.Consultorios[i];
            var donde = Fila("consultorios.csv", i, co.MedicoNombre);

            if (string.IsNullOrWhiteSpace(co.LocationUuid))
                errores.Add($"{donde}: location_uuid vacío");
            if (string.IsNullOrWhiteSpace(co.MedicoIdentifier))
                errores.Add($"{donde}: medico_identifier vacío");
            else if (!vistos.Add(co.MedicoIdentifier))
                errores.Add($"{donde}: medico_identifier '{co.MedicoIdentifier}' está duplicado");
        }
    }

    private static void ValidarClima(CatalogLoader c, List<string> errores)
    {
        var semanas = new HashSet<int>();

        for (var i = 0; i < c.Clima.Count; i++)
        {
            var cl = c.Clima[i];
            var donde = Fila("clima.csv", i, cl.Estacion);

            if (cl.Semana is < 1 or > 53)
                errores.Add($"{donde}: semana={cl.Semana} fuera de rango (1-53)");
            else if (!semanas.Add(cl.Semana))
                errores.Add($"{donde}: la semana {cl.Semana} está duplicada");

            Enum_(cl.Estacion, Estaciones, donde, "estacion", errores, permiteVacio: false);
        }
    }

    private static void ValidarNombresYDirecciones(CatalogLoader c, List<string> errores)
    {
        if (c.Nombres.Count > 0)
            foreach (var genero in new[] { "M", "F" })
                if (!c.Nombres.Any(n => string.Equals(n.Genero, genero, StringComparison.OrdinalIgnoreCase)))
                    errores.Add($"nombres.csv: no hay ningún nombre de género '{genero}' — " +
                                $"los pacientes de ese sexo se quedarían sin nombre de pila");

        for (var i = 0; i < c.Direcciones.Count; i++)
        {
            var d = c.Direcciones[i];
            var donde = Fila("direcciones.csv", i, d.Municipio);

            if (string.IsNullOrWhiteSpace(d.Municipio))
                errores.Add($"{donde}: municipio vacío");
            if (d.Peso < 1)
                errores.Add($"{donde}: peso debe ser >= 1 (valor: {d.Peso})");
        }
    }

    // ── Cobertura de categorías (las 13 columnas aplica_*) ────────────────────

    private static bool AplicaAlgunaCategoria(LaboratorioEntry l) =>
        l.AplicaRespiratorio || l.AplicaCardiovascular || l.AplicaDiabetes || l.AplicaDigestivo ||
        l.AplicaOsteomuscular || l.AplicaUrologico || l.AplicaInfeccioso || l.AplicaEndocrino ||
        l.AplicaNeurologico || l.AplicaDermatologico || l.AplicaSaludMental ||
        l.AplicaGinecoobstetrico || l.AplicaTrauma;

    private static bool AplicaAlgunaCategoria(MedicamentoEntry m) =>
        m.AplicaRespiratorio || m.AplicaCardiovascular || m.AplicaDiabetes || m.AplicaDigestivo ||
        m.AplicaOsteomuscular || m.AplicaUrologico || m.AplicaInfeccioso || m.AplicaEndocrino ||
        m.AplicaNeurologico || m.AplicaDermatologico || m.AplicaSaludMental ||
        m.AplicaGinecoobstetrico || m.AplicaTrauma;

    private static bool AplicaAlgunaCategoria(ExamenClinicoEntry e) =>
        e.AplicaRespiratorio || e.AplicaCardiovascular || e.AplicaDiabetes || e.AplicaDigestivo ||
        e.AplicaOsteomuscular || e.AplicaUrologico || e.AplicaInfeccioso || e.AplicaEndocrino ||
        e.AplicaNeurologico || e.AplicaDermatologico || e.AplicaSaludMental ||
        e.AplicaGinecoobstetrico || e.AplicaTrauma;
}
