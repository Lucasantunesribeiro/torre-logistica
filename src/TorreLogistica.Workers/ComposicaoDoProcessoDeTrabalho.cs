using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TorreLogistica.Application;
using TorreLogistica.Infrastructure;
using TorreLogistica.Infrastructure.Observabilidade;
using TorreLogistica.Infrastructure.Previsao;
using TorreLogistica.Infrastructure.Retencao;
using TorreLogistica.Infrastructure.Webhooks;

namespace TorreLogistica.Workers;

/// <summary>
/// O composition root do processo de trabalho, num método que o teste também consegue chamar.
/// </summary>
/// <remarks>
/// Mora aqui, e não em <c>Program.cs</c>, por um motivo prático: um composition root escrito em
/// instruções de nível superior só existe quando o processo sobe, e um teste que o recriasse à mão
/// estaria testando a cópia — que sai de sincronia no primeiro registro esquecido. Com o registro num
/// método, o processo e o teste compõem exatamente o mesmo grafo.
/// </remarks>
public static class ComposicaoDoProcessoDeTrabalho
{
    /// <summary>Registra tudo o que o processo de trabalho precisa, e nada além.</summary>
    public static IServiceCollection AdicionarProcessoDeTrabalho(
        this IServiceCollection servicos,
        IConfiguration configuracao)
    {
        ArgumentNullException.ThrowIfNull(servicos);
        ArgumentNullException.ThrowIfNull(configuracao);

        // Só a metade da camada de aplicação que funciona sem ninguém autenticado. A outra metade — login,
        // cadastro, consultas do console — depende de IContextoDoUsuario e IEmissorDeTokenDeAcesso, que
        // são abstrações implementadas pela borda HTTP e que este processo não tem como satisfazer.
        //
        // Registrar a camada inteira, como era antes, arrastava quarenta casos de uso para dentro do grafo
        // deste processo só para obter os seis que ele executa. Em produção passava despercebido, porque a
        // validação na construção do contêiner fica desligada; em desenvolvimento, que a liga, o processo
        // não subia. Registrar só o que se usa resolve nos dois ambientes, e pelo mesmo motivo.
        servicos.AdicionarCasosDeUsoDaOperacao();
        servicos.AdicionarCamadaDeInfrastructure(configuracao);

        // Dependências dos laços: as mesmas que a API registra, porque ela lê o que eles produzem.
        servicos.AdicionarPrevisaoDeChegada(configuracao);
        servicos.AdicionarAlertasOperacionais(configuracao);
        servicos.AdicionarWebhooks(configuracao);
        servicos.AdicionarRetencao(configuracao);
        servicos.AdicionarMedidasDaOperacao(configuracao);

        // Os laços: só aqui. É esta ausência, na API, que separa os dois processos.
        servicos.AdicionarProcessamentoDePrevisoes();
        servicos.AdicionarProcessamentoDeWebhooks();
        servicos.AdicionarProcessamentoDeRetencao();
        servicos.AdicionarProcessamentoDeMedidas();

        servicos.AddHostedService<ServicoDeVerificacaoDeInfraestrutura>();

        return servicos;
    }
}
