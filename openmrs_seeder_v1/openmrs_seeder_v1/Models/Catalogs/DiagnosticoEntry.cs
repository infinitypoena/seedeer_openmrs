namespace OpenmrsSeeder.Models.Catalogs;

public class DiagnosticoEntry
{
    public string CielUuid { get; set; } = "";
    public string NombreEs { get; set; } = "";
    public string Categoria { get; set; } = "";
    public string Severidad { get; set; } = "";
    public bool Aplica0_14 { get; set; }
    public bool Aplica15_29 { get; set; }
    public bool Aplica30_44 { get; set; }
    public bool Aplica45_64 { get; set; }
    public bool Aplica65mas { get; set; }
    public int PesoM { get; set; }
    public int PesoF { get; set; }
    public bool RequiereLab { get; set; }
    public bool RequiereRx { get; set; }
    public bool RequiereExamenClinico { get; set; }
    /// <summary>Estaciones (en minúscula) bajo las que la enfermedad es más frecuente. Vacío = sin efecto estacional.</summary>
    public HashSet<string> Clima { get; set; } = [];
    /// <summary>Si es una condición crónica → se agrega a la lista de problemas del paciente (POST /condition).</summary>
    public bool EsCronica { get; set; }
    /// <summary>Si es una enfermedad común/frecuente. El factor inicial de selección apunta mayormente a estas.</summary>
    public bool EsComun { get; set; }
    /// <summary>Override opcional de vitales: fuerza fiebre aunque la categoría no lo haría (col. <c>vital_fiebre</c>). Default false = neutro.</summary>
    public bool VitalFiebre { get; set; }
    /// <summary>Override opcional de IMC: <c>"alto"</c> (sobrepeso/obesidad) | <c>"bajo"</c> (desnutrición/caquexia) | <c>""</c> neutro (col. <c>vital_imc</c>).</summary>
    public string VitalImc { get; set; } = "";
    /// <summary>Override opcional de presión arterial: <c>"alta"</c> fuerza banda hipertensiva aunque no sea cardiovascular (preeclampsia, ERC…) | <c>""</c> neutro (col. <c>vital_pa</c>).</summary>
    public string VitalPa { get; set; } = "";
    /// <summary>Override opcional de frecuencia cardíaca: <c>"alta"</c> taquicardia (hipertiroidismo, anemia grave) | <c>"baja"</c> bradicardia (hipotiroidismo, bloqueos) | <c>""</c> neutro (col. <c>vital_fc</c>).</summary>
    public string VitalFc { get; set; } = "";
    /// <summary>Override opcional de SpO2: <c>"baja"</c> desaturación fuera de respiratorio (insuf. cardíaca, anemia grave) | <c>""</c> neutro (col. <c>vital_spo2</c>).</summary>
    public string VitalSpo2 { get; set; } = "";
    /// <summary>Sexo al que aplica el diagnóstico: <c>"M"</c> | <c>"F"</c> | <c>""</c> (ambos). Excluye duro el sexo contrario (embarazo→F, próstata→M).</summary>
    public string Sexo { get; set; } = "";

    /// <summary>
    /// Qué puede hacer la clínica con este cuadro (col. <c>ambito</c>):
    /// <c>"clinica"</c> (o vacío) = lo trata ella misma · <c>"referencia"</c> = lo detecta, lo estabiliza
    /// y lo <b>refiere al hospital</b> (apendicitis, IAM, sepsis, eclampsia…).
    ///
    /// <para>⚠️ <b>No es lo mismo que <see cref="Severidad"/> = grave.</b> Muchas graves las maneja el
    /// primer nivel: VIH, tuberculosis (el DOTS es de primer nivel), pie diabético o trastorno bipolar
    /// son graves y tienen sus programas de atención. Referirlas sería un error clínico. La referencia es
    /// para lo <b>quirúrgico y lo agudo de emergencia</b>, y se cura a mano en el catálogo.</para>
    /// </summary>
    public string Ambito { get; set; } = "";

    /// <summary>La clínica no puede resolverlo: lo estabiliza y lo manda al hospital.</summary>
    public bool EsReferencia => Ambito.Equals("referencia", StringComparison.OrdinalIgnoreCase);
}
