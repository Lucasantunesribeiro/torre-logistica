using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TorreLogistica.Application.Abstracoes.Persistencia;
using TorreLogistica.Domain.Clientes;
using TorreLogistica.Domain.Entregas;
using TorreLogistica.Domain.Identidade;

namespace TorreLogistica.Infrastructure.Persistencia.Configuracoes;

/// <summary>
/// Contador de código humano por organização, série e ano.
/// </summary>
/// <remarks>
/// Não é conceito de domínio — é o mecanismo que garante número sem repetição e sem lacuna
/// sob concorrência. Só é lido e escrito por
/// <see cref="TorreLogisticaDbContext.ReservarNumeroSequencialAsync"/>.
/// </remarks>
internal sealed class SequenciaDeCodigo
{
    public Guid OrganizacaoId { get; private set; }

    public string Serie { get; private set; } = string.Empty;

    public int Ano { get; private set; }

    public long UltimoNumero { get; private set; }
}

/// <summary>Mapeamento de <see cref="SequenciaDeCodigo"/>.</summary>
internal sealed class SequenciaDeCodigoConfiguracao : IEntityTypeConfiguration<SequenciaDeCodigo>
{
    public void Configure(EntityTypeBuilder<SequenciaDeCodigo> builder)
    {
        builder.ToTable("sequencias_de_codigo", tabela =>
            tabela.HasCheckConstraint("ck_sequencias_de_codigo_ultimo_numero_positivo", "ultimo_numero > 0"));

        builder.HasKey(sequencia => new { sequencia.OrganizacaoId, sequencia.Serie, sequencia.Ano });
        builder.Property(sequencia => sequencia.Serie).HasMaxLength(40);

        builder.HasOne<Organizacao>().WithMany().HasForeignKey(sequencia => sequencia.OrganizacaoId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Mapeamento de <see cref="Entrega"/>.</summary>
internal sealed class EntregaConfiguracao : IEntityTypeConfiguration<Entrega>
{
    public void Configure(EntityTypeBuilder<Entrega> builder)
    {
        builder.ToTable("entregas", tabela =>
        {
            tabela.HasCheckConstraint("ck_entregas_janela_prometida_ordenada", "prometida_ate > prometida_de");
            tabela.HasCheckConstraint("ck_entregas_ultima_sequencia_de_evento_positiva", "ultima_sequencia_de_evento > 0");
        });

        builder.HasKey(entrega => entrega.Id);
        builder.Property(entrega => entrega.Id).ValueGeneratedNever();
        builder.Property(entrega => entrega.Codigo).HasMaxLength(CodigoDaEntrega.TamanhoMaximo).IsRequired();
        builder.Property(entrega => entrega.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(entrega => entrega.MotivoDoCancelamento).HasConversion<string>().HasMaxLength(40);
        builder.Property(entrega => entrega.DescricaoDoCancelamento).HasMaxLength(Entrega.TamanhoMaximoDaDescricaoDoCancelamento);
        builder.Property(entrega => entrega.Observacoes).HasMaxLength(Entrega.TamanhoMaximoDasObservacoes);
        builder.Property(entrega => entrega.Versao).IsRowVersion();

        builder.ComplexProperty(entrega => entrega.Endereco, MapeamentoComum.MapearEndereco<Entrega>);

        builder.ComplexProperty(entrega => entrega.Janela, janela =>
        {
            janela.Property(item => item.Inicio).HasColumnName("prometida_de").IsRequired();
            janela.Property(item => item.Fim).HasColumnName("prometida_ate").IsRequired();
        });

        builder.Property(entrega => entrega.Localizacao)
            .HasConversion(MapeamentoComum.CoordenadaOpcional)
            .HasColumnType(MapeamentoComum.ColunaDePonto);

        builder.HasOne<Organizacao>().WithMany().HasForeignKey(entrega => entrega.OrganizacaoId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Cliente>().WithMany().HasForeignKey(entrega => entrega.ClienteId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Destinatario>().WithMany().HasForeignKey(entrega => entrega.DestinatarioId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(entrega => new { entrega.OrganizacaoId, entrega.Codigo })
            .IsUnique()
            .HasDatabaseName(NomesDeRestricoes.CodigoDaEntregaPorOrganizacao);

        // O índice por organização, status e fim da janela — a consulta da lista operacional —
        // está na migration: coluna de tipo complexo não entra em HasIndex.
        builder.HasIndex(entrega => entrega.Localizacao).HasMethod("gist");
    }
}

/// <summary>Mapeamento de <see cref="EventoDaEntrega"/>.</summary>
internal sealed class EventoDaEntregaConfiguracao : IEntityTypeConfiguration<EventoDaEntrega>
{
    public void Configure(EntityTypeBuilder<EventoDaEntrega> builder)
    {
        builder.ToTable("eventos_da_entrega", tabela =>
            tabela.HasCheckConstraint("ck_eventos_da_entrega_sequencia_positiva", "sequencia > 0"));

        builder.HasKey(evento => evento.Id);
        builder.Property(evento => evento.Id).ValueGeneratedNever();
        builder.Property(evento => evento.Tipo).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(evento => evento.StatusResultante).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(evento => evento.Dados).HasColumnType("jsonb").IsRequired();

        builder.HasOne<Organizacao>().WithMany().HasForeignKey(evento => evento.OrganizacaoId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Entrega>().WithMany().HasForeignKey(evento => evento.EntregaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Usuario>().WithMany().HasForeignKey(evento => evento.AutorUsuarioId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(evento => new { evento.EntregaId, evento.Sequencia })
            .IsUnique()
            .HasDatabaseName(NomesDeRestricoes.SequenciaDoEventoDaEntrega);
    }
}
