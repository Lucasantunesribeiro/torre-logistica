using Microsoft.EntityFrameworkCore;
using TorreLogistica.Application.Abstracoes.Identidade;
using TorreLogistica.Application.Abstracoes.Persistencia;
using TorreLogistica.Application.Identidade;
using TorreLogistica.Domain.Abstracoes.Erros;

namespace TorreLogistica.Application.Organizacoes;

/// <summary>Organização como exposta pela API.</summary>
public sealed record OrganizacaoResumo(Guid Id, string Nome, string Slug);

/// <summary>Consultas sobre a organização e a conta da sessão autenticada.</summary>
public sealed class ConsultaDeOrganizacao(IContextoDePersistencia contexto, IContextoDoUsuario usuarioAtual)
{
    /// <summary>Organização da sessão.</summary>
    public async Task<OrganizacaoResumo> ObterAtualAsync(CancellationToken cancelamento)
    {
        var organizacaoId = usuarioAtual.OrganizacaoIdAutenticada;

        return await contexto.Organizacoes
            .AsNoTracking()
            .Where(organizacao => organizacao.Id == organizacaoId)
            .Select(organizacao => new OrganizacaoResumo(organizacao.Id, organizacao.Nome, organizacao.Slug))
            .SingleOrDefaultAsync(cancelamento)
            .ConfigureAwait(false)
            ?? throw ExcecaoDeDominio.NaoEncontrado("organizacao_nao_encontrada", "Organização não encontrada.");
    }

    /// <summary>Conta da sessão.</summary>
    public async Task<UsuarioAutenticado> ObterUsuarioAtualAsync(CancellationToken cancelamento)
    {
        var usuarioId = usuarioAtual.UsuarioId;

        return await contexto.Usuarios
            .AsNoTracking()
            .Where(usuario => usuario.Id == usuarioId)
            .Join(
                contexto.Organizacoes,
                usuario => usuario.OrganizacaoId,
                organizacao => organizacao.Id,
                (usuario, organizacao) => new UsuarioAutenticado(
                    usuario.Id,
                    usuario.Nome,
                    usuario.Email,
                    usuario.Perfil,
                    organizacao.Id,
                    organizacao.Nome,
                    organizacao.Slug))
            .SingleOrDefaultAsync(cancelamento)
            .ConfigureAwait(false)
            ?? throw ExcecaoDeDominio.NaoEncontrado("usuario_nao_encontrado", "Usuário não encontrado.");
    }
}
