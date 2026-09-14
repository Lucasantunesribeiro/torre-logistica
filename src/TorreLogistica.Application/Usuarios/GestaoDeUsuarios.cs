using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TorreLogistica.Application.Abstracoes.Identidade;
using TorreLogistica.Application.Abstracoes.Persistencia;
using TorreLogistica.Application.Abstracoes.Seguranca;
using TorreLogistica.Application.Auditoria;
using TorreLogistica.Application.Comum;
using TorreLogistica.Domain.Abstracoes.Erros;
using TorreLogistica.Domain.Abstracoes.Identificadores;
using TorreLogistica.Domain.Abstracoes.Tempo;
using TorreLogistica.Domain.Auditoria;
using TorreLogistica.Domain.Identidade;

namespace TorreLogistica.Application.Usuarios;

/// <summary>Conta como exposta pela API. Nunca inclui hash, contador de falha ou bloqueio.</summary>
public sealed record UsuarioResumo(
    Guid Id,
    string Nome,
    string Email,
    Perfil Perfil,
    bool Ativo,
    DateTimeOffset CriadoEm);

/// <summary>Dados para criar uma conta. A organização vem da sessão, nunca daqui.</summary>
public sealed record ComandoDeCriacaoDeUsuario(string? Nome, string? Email, string? Senha, Perfil Perfil);

/// <summary>Regras de senha.</summary>
public static class PoliticaDeSenha
{
    /// <summary>Tamanho mínimo. Comprimento pesa mais que regra de símbolo.</summary>
    public const int TamanhoMinimo = 12;

    /// <summary>
    /// Tamanho máximo. Sem teto, uma senha de megabytes custa CPU desproporcional no hash
    /// lento — negação de serviço barata contra o login.
    /// </summary>
    public const int TamanhoMaximo = 128;

    /// <summary>Lança erro de regra se a senha não atender à política.</summary>
    public static void Validar(string? senha) =>
        ExcecaoDeDominio.LancarSe(
            senha is null || senha.Length < TamanhoMinimo || senha.Length > TamanhoMaximo,
            "senha_fora_da_politica",
            $"A senha deve ter entre {TamanhoMinimo} e {TamanhoMaximo} caracteres.");
}

/// <summary>
/// Casos de uso de gestão de contas da organização autenticada.
/// </summary>
/// <remarks>
/// <para>
/// Nenhum método recebe identificador de organização: todos operam sobre o tenant da
/// sessão, e as consultas já chegam filtradas por ele. Conta de outra organização não
/// "é negada" — ela não existe para esta consulta, e a resposta é a mesma de um
/// identificador inventado.
/// </para>
/// <para>
/// Alterar perfil e desativar travam a linha da organização. Sem isso, dois
/// administradores rebaixando um ao outro ao mesmo tempo passariam, cada um, pela
/// verificação "ainda existe outro administrador" — e a organização terminaria sem nenhum.
/// </para>
/// </remarks>
public sealed class GestaoDeUsuarios(
    IContextoDePersistencia contexto,
    IContextoDoUsuario usuarioAtual,
    IHasherDeSenha hasher,
    IGeradorDeIdentificador identificadores,
    IRelogio relogio,
    ILogger<GestaoDeUsuarios> log)
{
    /// <summary>Cria uma conta na organização autenticada.</summary>
    public async Task<UsuarioResumo> CriarAsync(ComandoDeCriacaoDeUsuario comando, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(comando);

        var organizacaoId = usuarioAtual.OrganizacaoIdAutenticada;
        var email = EnderecoDeEmail.Criar(comando.Email);
        PoliticaDeSenha.Validar(comando.Senha);
        ExcecaoDeDominio.LancarSe(!comando.Perfil.EhDefinido(), "perfil_invalido", "Perfil inválido.");

        // A consulta já é restrita ao tenant: um e-mail usado em outra organização não
        // conflita e não pode ser descoberto por aqui.
        var jaExiste = await contexto.Usuarios
            .AnyAsync(usuario => usuario.EmailNormalizado == email.Normalizado, cancelamento)
            .ConfigureAwait(false);

        if (jaExiste)
        {
            throw EmailJaCadastrado();
        }

        var agora = relogio.AgoraUtc;
        var usuario = Usuario.Criar(
            identificadores.Novo(),
            organizacaoId,
            comando.Nome,
            email,
            hasher.GerarHash(comando.Senha!),
            comando.Perfil,
            agora);

        contexto.Usuarios.Add(usuario);
        contexto.EventosDeAuditoria.Add(RegistroDeAuditoria.Criar(
            identificadores,
            agora,
            organizacaoId,
            TiposDeEventoDeAuditoria.UsuarioCriado,
            usuarioAtual.UsuarioId,
            "usuario",
            usuario.Id,
            new { perfil = usuario.Perfil.ToString() }));

        try
        {
            await contexto.SaveChangesAsync(cancelamento).ConfigureAwait(false);
        }
        catch (DbUpdateException excecao)
            when (contexto.EhViolacaoDeUnicidade(excecao, NomesDeRestricoes.EmailDoUsuarioPorOrganizacao))
        {
            // Duas criações simultâneas com o mesmo e-mail passaram juntas pela checagem
            // acima; a restrição do banco decidiu. Mesmo erro, mesma resposta.
            throw EmailJaCadastrado();
        }

        log.LogInformation(
            "Usuário {UsuarioCriadoId} criado com perfil {Perfil} por {AutorId} na organização {OrganizacaoId}.",
            usuario.Id,
            usuario.Perfil,
            usuarioAtual.UsuarioId,
            organizacaoId);

        return ParaResumo(usuario);
    }

    /// <summary>Lista as contas da organização, ordenadas por nome.</summary>
    public async Task<PaginaDeResultados<UsuarioResumo>> ListarAsync(
        int pagina,
        int tamanhoDaPagina,
        CancellationToken cancelamento)
    {
        ExcecaoDeDominio.LancarSe(pagina < 1, "pagina_invalida", "A página começa em 1.");
        ExcecaoDeDominio.LancarSe(
            tamanhoDaPagina is < 1 or > LimitesDePaginacao.TamanhoMaximo,
            "tamanho_de_pagina_invalido",
            $"O tamanho da página deve estar entre 1 e {LimitesDePaginacao.TamanhoMaximo}.");

        var consulta = contexto.Usuarios.AsNoTracking();

        var total = await consulta.CountAsync(cancelamento).ConfigureAwait(false);
        var itens = await consulta
            .OrderBy(usuario => usuario.Nome)
            .ThenBy(usuario => usuario.Id)
            .Skip((pagina - 1) * tamanhoDaPagina)
            .Take(tamanhoDaPagina)
            .Select(usuario => new UsuarioResumo(
                usuario.Id,
                usuario.Nome,
                usuario.Email,
                usuario.Perfil,
                usuario.Ativo,
                usuario.CriadoEm))
            .ToListAsync(cancelamento)
            .ConfigureAwait(false);

        return new PaginaDeResultados<UsuarioResumo>(itens, pagina, tamanhoDaPagina, total);
    }

    /// <summary>Obtém uma conta da organização.</summary>
    public async Task<UsuarioResumo> ObterAsync(Guid usuarioId, CancellationToken cancelamento)
    {
        var usuario = await contexto.Usuarios
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == usuarioId, cancelamento)
            .ConfigureAwait(false);

        return usuario is null ? throw UsuarioNaoEncontrado() : ParaResumo(usuario);
    }

    /// <summary>Altera o perfil de uma conta e encerra as sessões dela.</summary>
    public Task<UsuarioResumo> AlterarPerfilAsync(Guid usuarioId, Perfil novoPerfil, CancellationToken cancelamento)
    {
        var organizacaoId = usuarioAtual.OrganizacaoIdAutenticada;

        return contexto.ExecutarEmTransacaoAsync(
            async cancelamentoDaTransacao =>
            {
                await contexto.BloquearOrganizacaoAsync(organizacaoId, cancelamentoDaTransacao)
                    .ConfigureAwait(false);

                var usuario = await CarregarParaAlteracaoAsync(usuarioId, cancelamentoDaTransacao)
                    .ConfigureAwait(false);

                var perfilAnterior = usuario.Perfil;

                if (perfilAnterior == Perfil.Administrador
                    && novoPerfil != Perfil.Administrador
                    && usuario.Ativo)
                {
                    await GarantirOutroAdministradorAtivoAsync(usuario.Id, cancelamentoDaTransacao)
                        .ConfigureAwait(false);
                }

                var agora = relogio.AgoraUtc;

                if (!usuario.AlterarPerfil(novoPerfil, agora))
                {
                    return ParaResumo(usuario);
                }

                // O token de acesso em circulação carrega o perfil antigo. Encerrar as
                // sessões obriga um login novo, com o perfil certo.
                await RevogarSessoesAsync(usuario.Id, MotivoDeRevogacao.PerfilAlterado, agora, cancelamentoDaTransacao)
                    .ConfigureAwait(false);

                contexto.EventosDeAuditoria.Add(RegistroDeAuditoria.Criar(
                    identificadores,
                    agora,
                    organizacaoId,
                    TiposDeEventoDeAuditoria.PerfilAlterado,
                    usuarioAtual.UsuarioId,
                    "usuario",
                    usuario.Id,
                    new { de = perfilAnterior.ToString(), para = novoPerfil.ToString() }));

                await contexto.SaveChangesAsync(cancelamentoDaTransacao).ConfigureAwait(false);

                log.LogInformation(
                    "Perfil do usuário {UsuarioAlteradoId} alterado de {PerfilAnterior} para {PerfilNovo} por {AutorId}.",
                    usuario.Id,
                    perfilAnterior,
                    novoPerfil,
                    usuarioAtual.UsuarioId);

                return ParaResumo(usuario);
            },
            cancelamento);
    }

    /// <summary>Desativa uma conta e encerra as sessões dela.</summary>
    public Task<UsuarioResumo> DesativarAsync(Guid usuarioId, CancellationToken cancelamento)
    {
        var organizacaoId = usuarioAtual.OrganizacaoIdAutenticada;

        ExcecaoDeDominio.LancarSe(
            usuarioId == usuarioAtual.UsuarioId,
            "nao_pode_desativar_a_propria_conta",
            "Não é possível desativar a própria conta.");

        return contexto.ExecutarEmTransacaoAsync(
            async cancelamentoDaTransacao =>
            {
                await contexto.BloquearOrganizacaoAsync(organizacaoId, cancelamentoDaTransacao)
                    .ConfigureAwait(false);

                var usuario = await CarregarParaAlteracaoAsync(usuarioId, cancelamentoDaTransacao)
                    .ConfigureAwait(false);

                if (usuario.Ativo && usuario.Perfil == Perfil.Administrador)
                {
                    await GarantirOutroAdministradorAtivoAsync(usuario.Id, cancelamentoDaTransacao)
                        .ConfigureAwait(false);
                }

                var agora = relogio.AgoraUtc;

                if (!usuario.Desativar(agora))
                {
                    return ParaResumo(usuario);
                }

                await RevogarSessoesAsync(usuario.Id, MotivoDeRevogacao.UsuarioDesativado, agora, cancelamentoDaTransacao)
                    .ConfigureAwait(false);

                contexto.EventosDeAuditoria.Add(RegistroDeAuditoria.Criar(
                    identificadores,
                    agora,
                    organizacaoId,
                    TiposDeEventoDeAuditoria.UsuarioDesativado,
                    usuarioAtual.UsuarioId,
                    "usuario",
                    usuario.Id,
                    new { perfil = usuario.Perfil.ToString() }));

                await contexto.SaveChangesAsync(cancelamentoDaTransacao).ConfigureAwait(false);

                log.LogInformation(
                    "Usuário {UsuarioDesativadoId} desativado por {AutorId}.",
                    usuario.Id,
                    usuarioAtual.UsuarioId);

                return ParaResumo(usuario);
            },
            cancelamento);
    }

    private async Task<Usuario> CarregarParaAlteracaoAsync(Guid usuarioId, CancellationToken cancelamento) =>
        await contexto.Usuarios
            .SingleOrDefaultAsync(item => item.Id == usuarioId, cancelamento)
            .ConfigureAwait(false)
        ?? throw UsuarioNaoEncontrado();

    private async Task GarantirOutroAdministradorAtivoAsync(Guid usuarioId, CancellationToken cancelamento)
    {
        var existeOutro = await contexto.Usuarios
            .AnyAsync(
                usuario => usuario.Id != usuarioId && usuario.Ativo && usuario.Perfil == Perfil.Administrador,
                cancelamento)
            .ConfigureAwait(false);

        ExcecaoDeDominio.LancarSe(
            !existeOutro,
            "ultimo_administrador",
            "A organização precisa manter ao menos um administrador ativo.");
    }

    private async Task RevogarSessoesAsync(
        Guid usuarioId,
        MotivoDeRevogacao motivo,
        DateTimeOffset agora,
        CancellationToken cancelamento)
    {
        var sessoes = await contexto.Sessoes
            .Where(sessao => sessao.UsuarioId == usuarioId && sessao.RevogadaEm == null)
            .ToListAsync(cancelamento)
            .ConfigureAwait(false);

        foreach (var sessao in sessoes)
        {
            sessao.Revogar(motivo, agora);
        }
    }

    private static UsuarioResumo ParaResumo(Usuario usuario) =>
        new(usuario.Id, usuario.Nome, usuario.Email, usuario.Perfil, usuario.Ativo, usuario.CriadoEm);

    private static ExcecaoDeDominio UsuarioNaoEncontrado() =>
        ExcecaoDeDominio.NaoEncontrado("usuario_nao_encontrado", "Usuário não encontrado.");

    private static ExcecaoDeDominio EmailJaCadastrado() =>
        ExcecaoDeDominio.Conflito("email_ja_cadastrado", "Já existe uma conta com este e-mail na organização.");
}
