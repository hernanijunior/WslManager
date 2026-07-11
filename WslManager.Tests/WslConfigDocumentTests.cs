using WslManager.Core;
using Xunit;

namespace WslManager.Tests;

public class WslConfigDocumentTests
{
    // Arquivo com comentários (# e ;), espaçamento variado, seção conhecida e
    // uma seção DESCONHECIDA. CRLF e newline final, como um .wslconfig real.
    private const string Sample =
        "# WSL global config\r\n" +
        "; segundo comentário\r\n" +
        "[wsl2]\r\n" +
        "memory=8GB\r\n" +
        "processors = 4\r\n" +
        "\r\n" +
        "[experimental]\r\n" +
        "sparseVhd=true\r\n" +
        "\r\n" +
        "[custom]\r\n" +
        "minhaChave=valor\r\n";

    [Fact]
    public void RoundTrip_SemAlteracao_PreservaTudoByteAByte()
    {
        var doc = WslConfigDocument.Parse(Sample);
        Assert.Equal(Sample, doc.ToText());
    }

    [Fact]
    public void Set_AlteraSomenteAChaveAlvo_PreservaComentariosESecaoDesconhecida()
    {
        var doc = WslConfigDocument.Parse(Sample);

        doc.Set("wsl2", "memory", "4GB");
        var output = doc.ToText();

        Assert.Contains("memory=4GB", output);
        Assert.DoesNotContain("memory=8GB", output);

        // resto intacto
        Assert.Contains("# WSL global config", output);
        Assert.Contains("; segundo comentário", output);
        Assert.Contains("processors = 4", output);   // espaçamento preservado
        Assert.Contains("[experimental]", output);
        Assert.Contains("sparseVhd=true", output);
        Assert.Contains("[custom]", output);          // seção desconhecida sobrevive
        Assert.Contains("minhaChave=valor", output);
    }

    [Fact]
    public void Set_PreservaEspacamentoDaChaveAoAtualizar()
    {
        var doc = WslConfigDocument.Parse(Sample);
        doc.Set("wsl2", "processors", "8");
        Assert.Contains("processors = 8", doc.ToText());
    }

    [Fact]
    public void Set_ChaveNova_VaiParaDentroDaSecaoExistente()
    {
        var doc = WslConfigDocument.Parse(Sample);
        doc.Set("wsl2", "swap", "2GB");
        var output = doc.ToText();

        Assert.Contains("swap=2GB", output);
        Assert.True(output.IndexOf("swap=2GB", System.StringComparison.Ordinal)
                    < output.IndexOf("[experimental]", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Set_SecaoInexistente_CriaSecaoEChave()
    {
        var doc = WslConfigDocument.Parse(Sample);
        doc.Set("novaSecao", "foo", "bar");
        var output = doc.ToText();

        Assert.Contains("[novaSecao]", output);
        Assert.Contains("foo=bar", output);
        // não estraga o que já existia
        Assert.Contains("[custom]", output);
        Assert.Contains("minhaChave=valor", output);
    }

    [Fact]
    public void Remove_ApagaChave_MantemSecaoEResto()
    {
        var doc = WslConfigDocument.Parse(Sample);
        doc.Remove("experimental", "sparseVhd");
        var output = doc.ToText();

        Assert.DoesNotContain("sparseVhd", output);
        Assert.Contains("[experimental]", output);
        Assert.Contains("[custom]", output);
    }

    [Fact]
    public void SetOrRemove_ValorVazio_RemoveAChave()
    {
        var doc = WslConfigDocument.Parse(Sample);
        doc.SetOrRemove("wsl2", "memory", null);
        Assert.DoesNotContain("memory", doc.ToText());
    }

    [Fact]
    public void Get_LeValorComEspacamento()
    {
        var doc = WslConfigDocument.Parse(Sample);
        Assert.Equal("8GB", doc.Get("wsl2", "memory"));
        Assert.Equal("4", doc.Get("wsl2", "processors"));
        Assert.Null(doc.Get("wsl2", "inexistente"));
    }

    [Fact]
    public void DocumentoVazio_RecebeSecaoEChave()
    {
        var doc = WslConfigDocument.Parse(string.Empty);
        doc.Set("wsl2", "memory", "4GB");
        var output = doc.ToText();

        Assert.Contains("[wsl2]", output);
        Assert.Contains("memory=4GB", output);
    }

    [Fact]
    public void CicloCompleto_LoadSetSave_PreservaComentariosESecaoDesconhecida()
    {
        // o cenário exato pedido: load → set → save mantém comentários e seção desconhecida
        var doc = WslConfigDocument.Parse(Sample);
        doc.Set("wsl2", "memory", "16GB");
        doc.Set("experimental", "autoMemoryReclaim", "gradual");

        var output = doc.ToText();

        Assert.Contains("# WSL global config", output);
        Assert.Contains("; segundo comentário", output);
        Assert.Contains("[custom]", output);
        Assert.Contains("minhaChave=valor", output);
        Assert.Contains("memory=16GB", output);
        Assert.Contains("autoMemoryReclaim=gradual", output);
    }
}
