using OpenmrsSeeder.Services;
using Xunit;

namespace openmrs_seeder_v1.Tests;

public class CatalogLoaderTests
{
    private static string CreateTempDir(Dictionary<string, string> files)
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        foreach (var (name, content) in files)
            File.WriteAllText(Path.Combine(dir, name), content);
        return dir;
    }

    [Fact]
    public void Load_LeeEpidemiologyProfileCorrectamente()
    {
        var dir = CreateTempDir(new()
        {
            ["epidemiology-profile.csv"] =
                "categoria,grupo_edad,genero,peso\n" +
                "respiratorio,0-14,M,35\n" +
                "cardiovascular,45-64,Ambos,25\n",
            ["diagnosticos.csv"]    = "ciel_uuid,nombre_es,categoria,severidad,aplica_0_14,aplica_15_29,aplica_30_44,aplica_45_64,aplica_65mas,peso_M,peso_F,requiere_lab,requiere_rx,requiere_examen_clinico\n",
            ["medicamentos.csv"]    = "drug_uuid,nombre_generico,strength,via_uuid,aplica_respiratorio,aplica_cardiovascular,aplica_diabetes,aplica_digestivo,aplica_osteomuscular,aplica_urologico,aplica_infeccioso,aplica_endocrino\n",
            ["laboratorios.csv"]    = "ciel_uuid,nombre_es,clase,aplica_respiratorio,aplica_cardiovascular,aplica_diabetes,aplica_digestivo,aplica_osteomuscular,aplica_urologico,aplica_infeccioso,aplica_endocrino\n",
            ["examenes_clinicos.csv"] = "ciel_uuid,nombre_es,tipo_resultado,unidad,aplica_respiratorio,aplica_cardiovascular,aplica_diabetes,aplica_digestivo,aplica_osteomuscular,aplica_urologico,aplica_infeccioso,aplica_endocrino\n",
            ["alergenos.csv"]       = "concept_uuid,nombre_es,tipo_alergeno,severidad_tipica\n",
            ["motivos_consulta.csv"] = "categoria,texto\n",
        });

        try
        {
            var loader = new CatalogLoader();
            loader.Load(dir);

            Assert.Equal(2, loader.EpidemiologyProfile.Count);
            Assert.Equal("respiratorio",   loader.EpidemiologyProfile[0].Categoria);
            Assert.Equal("cardiovascular", loader.EpidemiologyProfile[1].Categoria);
            Assert.Equal(35, loader.EpidemiologyProfile[0].Peso);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Load_ArchivosAusentesDevuelvenListasVacias()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            var loader = new CatalogLoader();
            loader.Load(dir);

            Assert.Empty(loader.EpidemiologyProfile);
            Assert.Empty(loader.Diagnosticos);
            Assert.Empty(loader.Medicamentos);
            Assert.Empty(loader.Laboratorios);
            Assert.Empty(loader.ExamenesClinicos);
            Assert.Empty(loader.Alergenos);
            Assert.Empty(loader.MotivosConsulta);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Load_BooleanosTrueYFalseParseanCorrectamente()
    {
        var dir = CreateTempDir(new()
        {
            ["epidemiology-profile.csv"] = "categoria,grupo_edad,genero,peso\n",
            ["diagnosticos.csv"] =
                "ciel_uuid,nombre_es,categoria,severidad,aplica_0_14,aplica_15_29,aplica_30_44,aplica_45_64,aplica_65mas,peso_M,peso_F,requiere_lab,requiere_rx,requiere_examen_clinico\n" +
                "uuid1,Bronquitis,respiratorio,leve,true,true,false,false,false,10,10,false,true,false\n",
            ["medicamentos.csv"]    = "drug_uuid,nombre_generico,strength,via_uuid,aplica_respiratorio,aplica_cardiovascular,aplica_diabetes,aplica_digestivo,aplica_osteomuscular,aplica_urologico,aplica_infeccioso,aplica_endocrino\n",
            ["laboratorios.csv"]    = "ciel_uuid,nombre_es,clase,aplica_respiratorio,aplica_cardiovascular,aplica_diabetes,aplica_digestivo,aplica_osteomuscular,aplica_urologico,aplica_infeccioso,aplica_endocrino\n",
            ["examenes_clinicos.csv"] = "ciel_uuid,nombre_es,tipo_resultado,unidad,aplica_respiratorio,aplica_cardiovascular,aplica_diabetes,aplica_digestivo,aplica_osteomuscular,aplica_urologico,aplica_infeccioso,aplica_endocrino\n",
            ["alergenos.csv"]       = "concept_uuid,nombre_es,tipo_alergeno,severidad_tipica\n",
            ["motivos_consulta.csv"] = "categoria,texto\n",
        });

        try
        {
            var loader = new CatalogLoader();
            loader.Load(dir);

            var dx = loader.Diagnosticos[0];
            Assert.True(dx.Aplica0_14);
            Assert.True(dx.Aplica15_29);
            Assert.False(dx.Aplica30_44);
            Assert.False(dx.RequiereLab);
            Assert.True(dx.RequiereRx);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Load_DiagnosticoCronicaYCategoriaNuevaParsean()
    {
        var dir = CreateTempDir(new()
        {
            ["epidemiology-profile.csv"] = "categoria,grupo_edad,genero,peso\n",
            ["diagnosticos.csv"] =
                "ciel_uuid,nombre_es,categoria,severidad,aplica_0_14,aplica_15_29,aplica_30_44,aplica_45_64,aplica_65mas,peso_M,peso_F,requiere_lab,requiere_rx,requiere_examen_clinico,clima,cronica\n" +
                "uuid-mig,Migrana,neurologico,moderado,false,true,true,true,false,8,20,false,true,false,,true\n",
            ["medicamentos.csv"]    = "drug_uuid,concept_uuid,nombre_generico,strength,via_uuid,aplica_respiratorio,aplica_cardiovascular,aplica_diabetes,aplica_digestivo,aplica_osteomuscular,aplica_urologico,aplica_infeccioso,aplica_endocrino,aplica_neurologico,aplica_dermatologico,aplica_salud_mental,aplica_ginecoobstetrico,aplica_trauma\n",
            ["laboratorios.csv"] =
                "ciel_uuid,nombre_es,clase,aplica_respiratorio,aplica_cardiovascular,aplica_diabetes,aplica_digestivo,aplica_osteomuscular,aplica_urologico,aplica_infeccioso,aplica_endocrino,aplica_neurologico,aplica_dermatologico,aplica_salud_mental,aplica_ginecoobstetrico,aplica_trauma\n" +
                "uuid-tc,Tomografia de craneo,Test,false,false,false,false,false,false,false,false,true,false,false,false,true\n",
            ["examenes_clinicos.csv"] = "ciel_uuid,nombre_es,tipo_resultado,unidad,aplica_respiratorio,aplica_cardiovascular,aplica_diabetes,aplica_digestivo,aplica_osteomuscular,aplica_urologico,aplica_infeccioso,aplica_endocrino,aplica_neurologico,aplica_dermatologico,aplica_salud_mental,aplica_ginecoobstetrico,aplica_trauma\n",
            ["alergenos.csv"]       = "concept_uuid,nombre_es,tipo_alergeno,severidad_tipica\n",
            ["motivos_consulta.csv"] = "categoria,texto\n",
        });

        try
        {
            var loader = new CatalogLoader();
            loader.Load(dir);

            var dx = loader.Diagnosticos[0];
            Assert.Equal("neurologico", dx.Categoria);
            Assert.True(dx.EsCronica);

            var lab = loader.Laboratorios[0];
            Assert.True(lab.AplicaNeurologico);
            Assert.True(lab.AplicaTrauma);
            Assert.False(lab.AplicaRespiratorio);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Load_AlergenosTiposCorrectos()
    {
        var dir = CreateTempDir(new()
        {
            ["epidemiology-profile.csv"] = "categoria,grupo_edad,genero,peso\n",
            ["diagnosticos.csv"]         = "ciel_uuid,nombre_es,categoria,severidad,aplica_0_14,aplica_15_29,aplica_30_44,aplica_45_64,aplica_65mas,peso_M,peso_F,requiere_lab,requiere_rx,requiere_examen_clinico\n",
            ["medicamentos.csv"]         = "drug_uuid,nombre_generico,strength,via_uuid,aplica_respiratorio,aplica_cardiovascular,aplica_diabetes,aplica_digestivo,aplica_osteomuscular,aplica_urologico,aplica_infeccioso,aplica_endocrino\n",
            ["laboratorios.csv"]         = "ciel_uuid,nombre_es,clase,aplica_respiratorio,aplica_cardiovascular,aplica_diabetes,aplica_digestivo,aplica_osteomuscular,aplica_urologico,aplica_infeccioso,aplica_endocrino\n",
            ["examenes_clinicos.csv"]    = "ciel_uuid,nombre_es,tipo_resultado,unidad,aplica_respiratorio,aplica_cardiovascular,aplica_diabetes,aplica_digestivo,aplica_osteomuscular,aplica_urologico,aplica_infeccioso,aplica_endocrino\n",
            ["alergenos.csv"] =
                "concept_uuid,nombre_es,tipo_alergeno,severidad_tipica\n" +
                "uuid-pen,Penicilina,DRUG,moderada\n" +
                "uuid-mariscos,Mariscos,FOOD,grave\n" +
                "uuid-polvo,Polvo,ENVIRONMENT,leve\n",
            ["motivos_consulta.csv"] = "categoria,texto\n",
        });

        try
        {
            var loader = new CatalogLoader();
            loader.Load(dir);

            Assert.Equal(3, loader.Alergenos.Count);
            Assert.Equal("DRUG",        loader.Alergenos[0].TipoAlergeno);
            Assert.Equal("FOOD",        loader.Alergenos[1].TipoAlergeno);
            Assert.Equal("ENVIRONMENT", loader.Alergenos[2].TipoAlergeno);
            Assert.Equal("grave",       loader.Alergenos[1].SeveridadTipica);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
