namespace TorreLogistica.Domain.Abstracoes.Cadastro;

/// <summary>
/// Cadastro operacional pertencente a uma organização, com ciclo ativo/inativo e versão.
/// </summary>
/// <remarks>
/// <para>
/// Inativar é a forma de retirar um cadastro de uso. Não há exclusão física: a partir da
/// Fase 3, entregas passam a apontar para motoristas, veículos, hubs, clientes e
/// destinatários, e apagar o registro reescreveria o histórico dessas entregas.
/// </para>
/// <para>
/// <see cref="Versao"/> é a versão de linha do banco. Alteração de dados exige a versão lida:
/// se outra pessoa gravou antes, a segunda gravação é recusada com conflito em vez de
/// sobrescrever em silêncio.
/// </para>
/// </remarks>
public interface IRecursoAtivavel
{
    /// <summary>Identificador.</summary>
    Guid Id { get; }

    /// <summary>Organização dona do cadastro.</summary>
    Guid OrganizacaoId { get; }

    /// <summary>Cadastro em uso.</summary>
    bool Ativo { get; }

    /// <summary>Versão da linha, para concorrência otimista.</summary>
    uint Versao { get; }

    /// <summary>Instante da última alteração.</summary>
    DateTimeOffset AtualizadoEm { get; }

    /// <summary>Ativa. Repetir não tem efeito.</summary>
    /// <returns><see langword="true"/> se estava inativo.</returns>
    bool Ativar(DateTimeOffset agora);

    /// <summary>Inativa. Repetir não tem efeito.</summary>
    /// <returns><see langword="true"/> se estava ativo.</returns>
    bool Inativar(DateTimeOffset agora);
}

/// <summary>
/// Acumula os nomes dos campos que uma alteração realmente mudou.
/// </summary>
/// <remarks>
/// A trilha de auditoria registra <b>quais</b> campos mudaram, nunca os valores: vários
/// cadastros guardam dado pessoal (nome, telefone, endereço de destinatário), e a
/// auditoria é somente-inserção — um valor pessoal gravado ali não poderia mais ser apagado.
/// </remarks>
public sealed class RegistroDeAlteracoes
{
    private readonly List<string> _campos = [];

    /// <summary>Campos alterados, na ordem em que foram aplicados.</summary>
    public IReadOnlyList<string> Campos => _campos;

    /// <summary>Houve alguma mudança.</summary>
    public bool HouveAlteracao => _campos.Count > 0;

    /// <summary>Aplica o valor novo se ele for diferente do atual.</summary>
    public void Aplicar<T>(string campo, T atual, T novo, Action<T> aplicar)
    {
        ArgumentNullException.ThrowIfNull(aplicar);

        if (EqualityComparer<T>.Default.Equals(atual, novo))
        {
            return;
        }

        aplicar(novo);
        _campos.Add(campo);
    }
}
