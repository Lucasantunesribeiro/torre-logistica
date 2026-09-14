using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TorreLogistica.Application.Abstracoes.Persistencia;
using TorreLogistica.Application.Abstracoes.Seguranca;
using TorreLogistica.Application.Usuarios;
using TorreLogistica.Domain.Abstracoes.Identificadores;
using TorreLogistica.Domain.Abstracoes.Tempo;
using TorreLogistica.Domain.Identidade;

namespace TorreLogistica.Application.Identidade;

/// <summary>
/// Login: confere credenciais e abre uma sessão.
/// </summary>
/// <remarks>
/// <para>
/// Toda falha devolve o mesmo resultado — <see langword="null"/> — e a borda HTTP
/// responde exatamente igual para organização inexistente, e-mail inexistente, senha
/// errada, conta bloqueada, conta inativa ou canal errado. A causa real vai só para o log.
/// </para>
/// <para>
/// O hash de senha é calculado em <b>todos</b> os caminhos, inclusive quando a conta
/// não existe, para que o tempo de resposta também não denuncie quais contas existem.
/// </para>
/// </remarks>
public sealed class AutenticarUsuario(
    IContextoDePersistencia contexto,
    IHasherDeSenha hasher,
    IEmissorDeTokenDeAcesso emissor,
    IGeradorDeIdentificador identificadores,
    IRelogio relogio,
    IOptions<OpcoesDeAutenticacao> opcoes,
    ILogger<AutenticarUsuario> log)
{
    /// <summary>Executa o login.</summary>
    /// <returns>A sessão emitida, ou <see langword="null"/> em qualquer falha.</returns>
    public async Task<SessaoEmitida?> ExecutarAsync(
        ComandoDeAutenticacao comando,
        CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(comando);

        var configuracao = opcoes.Value;
        var slug = Organizacao.NormalizarSlug(comando.Organizacao);
        var email = EnderecoDeEmail.Normalizar(comando.Email);
        var senhaInformada = comando.Senha ?? string.Empty;

        if (!Organizacao.SlugEhValido(slug)
            || email.Length is 0 or > EnderecoDeEmail.TamanhoMaximo
            || senhaInformada.Length is 0 or > PoliticaDeSenha.TamanhoMaximo)
        {
            hasher.VerificarContraHashFicticio(senhaInformada.Length > PoliticaDeSenha.TamanhoMaximo
                ? senhaInformada[..PoliticaDeSenha.TamanhoMaximo]
                : senhaInformada);
            RegistrarFalha("entrada_invalida", comando.Canal, slug, usuarioId: null);
            return null;
        }

        // Sem sessão ainda, não há tenant: a busca atravessa organizações de propósito,
        // e é restrita ao par (organização ativa, e-mail) informado no login.
        var candidato = await contexto.Usuarios
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(usuario => usuario.EmailNormalizado == email)
            .Join(
                contexto.Organizacoes.Where(organizacao => organizacao.Slug == slug && organizacao.Ativa),
                usuario => usuario.OrganizacaoId,
                organizacao => organizacao.Id,
                (usuario, organizacao) => new { Usuario = usuario, Organizacao = organizacao })
            .SingleOrDefaultAsync(cancelamento)
            .ConfigureAwait(false);

        if (candidato is null)
        {
            hasher.VerificarContraHashFicticio(senhaInformada);
            RegistrarFalha("conta_inexistente", comando.Canal, slug, usuarioId: null);
            return null;
        }

        var usuario = candidato.Usuario;
        var agora = relogio.AgoraUtc;

        if (usuario.EstaBloqueado(agora))
        {
            // Confere mesmo assim, só para gastar o mesmo tempo. O resultado é ignorado:
            // acertar a senhaInformada durante o bloqueio não libera nada.
            hasher.Verificar(usuario.HashDaSenha, senhaInformada);
            RegistrarFalha("conta_bloqueada", comando.Canal, slug, usuario.Id);
            return null;
        }

        var verificacao = hasher.Verificar(usuario.HashDaSenha, senhaInformada);

        if (verificacao == ResultadoDaVerificacaoDeSenha.Invalida)
        {
            await RegistrarFalhaDeSenhaAsync(usuario.Id, agora, configuracao, cancelamento)
                .ConfigureAwait(false);
            RegistrarFalha("senha_incorreta", comando.Canal, slug, usuario.Id);
            return null;
        }

        if (!usuario.Ativo || usuario.Canal != comando.Canal)
        {
            RegistrarFalha(
                usuario.Ativo ? "canal_incorreto" : "conta_inativa",
                comando.Canal,
                slug,
                usuario.Id);
            return null;
        }

        await ZerarFalhasAsync(usuario.Id, verificacao, senhaInformada, cancelamento).ConfigureAwait(false);

        var duracaoDaSessao = configuracao.DuracaoDaSessao(comando.Canal);
        var sessao = Sessao.Abrir(identificadores.Novo(), usuario, agora, duracaoDaSessao);
        var (tokenCru, hash) = SegredosDeRenovacao.Gerar();
        var tokenDeRenovacao = TokenDeRenovacao.Emitir(
            identificadores.Novo(), sessao, hash, agora, duracaoDaSessao);

        contexto.Sessoes.Add(sessao);
        contexto.TokensDeRenovacao.Add(tokenDeRenovacao);
        await contexto.SaveChangesAsync(cancelamento).ConfigureAwait(false);

        var acesso = emissor.Emitir(usuario, sessao);

        log.LogInformation(
            "Login concluído: usuário {UsuarioId}, organização {OrganizacaoId}, sessão {SessaoId}, canal {Canal}.",
            usuario.Id,
            usuario.OrganizacaoId,
            sessao.Id,
            sessao.Canal);

        return new SessaoEmitida(
            new UsuarioAutenticado(
                usuario.Id,
                usuario.Nome,
                usuario.Email,
                usuario.Perfil,
                candidato.Organizacao.Id,
                candidato.Organizacao.Nome,
                candidato.Organizacao.Slug),
            acesso.Token,
            acesso.ExpiraEm,
            tokenCru,
            tokenDeRenovacao.ExpiraEm,
            sessao.Canal);
    }

    /// <summary>
    /// Incrementa as falhas e bloqueia ao atingir o limite, num único <c>UPDATE</c>.
    /// </summary>
    /// <remarks>
    /// Ler o contador, somar e gravar em passos separados deixaria tentativas paralelas
    /// sobrescreverem umas às outras — e um ataque em rajada ganharia tentativas extras
    /// justamente por ser rápido. No <c>UPDATE</c>, todas as expressões enxergam os
    /// valores antigos da mesma linha, então as duas colunas mudam de forma coerente.
    /// </remarks>
    private Task<int> RegistrarFalhaDeSenhaAsync(
        Guid usuarioId,
        DateTimeOffset agora,
        OpcoesDeAutenticacao configuracao,
        CancellationToken cancelamento)
    {
        var limite = configuracao.LimiteDeFalhasDeLogin;
        DateTimeOffset? bloqueioAte = agora + configuracao.DuracaoDoBloqueio;

        return contexto.Usuarios
            .IgnoreQueryFilters()
            .Where(usuario => usuario.Id == usuarioId)
            .ExecuteUpdateAsync(
                atualizacao => atualizacao
                    .SetProperty(
                        usuario => usuario.BloqueadoAte,
                        usuario => usuario.TentativasDeLoginFalhas + 1 >= limite
                            ? bloqueioAte
                            : usuario.BloqueadoAte)
                    .SetProperty(
                        usuario => usuario.TentativasDeLoginFalhas,
                        usuario => usuario.TentativasDeLoginFalhas + 1 >= limite
                            ? 0
                            : usuario.TentativasDeLoginFalhas + 1),
                cancelamento);
    }

    private Task<int> ZerarFalhasAsync(
        Guid usuarioId,
        ResultadoDaVerificacaoDeSenha verificacao,
        string senha,
        CancellationToken cancelamento)
    {
        var consulta = contexto.Usuarios.IgnoreQueryFilters().Where(usuario => usuario.Id == usuarioId);

        if (verificacao == ResultadoDaVerificacaoDeSenha.ValidaComRecalculo)
        {
            var novoHash = hasher.GerarHash(senha);

            return consulta.ExecuteUpdateAsync(
                atualizacao => atualizacao
                    .SetProperty(usuario => usuario.TentativasDeLoginFalhas, 0)
                    .SetProperty(usuario => usuario.BloqueadoAte, (DateTimeOffset?)null)
                    .SetProperty(usuario => usuario.HashDaSenha, novoHash),
                cancelamento);
        }

        return consulta.ExecuteUpdateAsync(
            atualizacao => atualizacao
                .SetProperty(usuario => usuario.TentativasDeLoginFalhas, 0)
                .SetProperty(usuario => usuario.BloqueadoAte, (DateTimeOffset?)null),
            cancelamento);
    }

    // E-mail e senha nunca entram no log: o primeiro é dado pessoal, a segunda dispensa
    // explicação. O slug da organização é identificador público.
    private void RegistrarFalha(string motivo, CanalDeAcesso canal, string slug, Guid? usuarioId) =>
        log.LogWarning(
            "Login recusado: motivo {Motivo}, canal {Canal}, organização {OrganizacaoSlug}, usuário {UsuarioId}.",
            motivo,
            canal,
            slug,
            usuarioId);
}
