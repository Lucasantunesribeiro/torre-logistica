using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TorreLogistica.Application.Abstracoes.Persistencia;
using TorreLogistica.Domain.Alertas;
using TorreLogistica.Domain.Entregas;
using TorreLogistica.Domain.Frota;
using TorreLogistica.Domain.Identidade;
using TorreLogistica.Domain.Rotas;

namespace TorreLogistica.Infrastructure.Persistencia.Configuracoes;

/// <summary>Mapeamento de <see cref="AlertaOperacional"/> — tabela <c>alertas_operacionais</c>.</summary>
internal sealed class AlertaOperacionalConfiguracao : IEntityTypeConfiguration<AlertaOperacional>
{
    public void Configure(EntityTypeBuilder<AlertaOperacional> builder)
    {
        builder.ToTable("alertas_operacionais", tabela =>
        {
            tabela.HasCheckConstraint("ck_alertas_operacionais_reaberturas_nao_negativas", "reaberturas >= 0");
            tabela.HasCheckConstraint(
                "ck_alertas_operacionais_resolucao_coerente",
                "(estado = 'Aberto' AND resolvido_em IS NULL AND forma_de_resolucao IS NULL) "
                + "OR (estado = 'Resolvido' AND resolvido_em IS NOT NULL AND forma_de_resolucao IS NOT NULL)");
            tabela.HasCheckConstraint(
                "ck_alertas_operacionais_alvo",
                "entrega_id IS NOT NULL OR (motorista_id IS NOT NULL AND rota_id IS NOT NULL)");
        });

        builder.HasKey(alerta => alerta.Id);
        builder.Property(alerta => alerta.Id).ValueGeneratedNever();
        builder.Property(alerta => alerta.Versao).IsRowVersion();

        builder.Property(alerta => alerta.Tipo).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(alerta => alerta.Severidade).HasConversion<string>().HasMaxLength(10).IsRequired();
        builder.Property(alerta => alerta.Estado).HasConversion<string>().HasMaxLength(10).IsRequired();
        builder.Property(alerta => alerta.FormaDeResolucao).HasConversion<string>().HasMaxLength(20);
        builder.Property(alerta => alerta.Chave).HasMaxLength(160).IsRequired();
        builder.Property(alerta => alerta.EvidenciaDeAbertura).HasColumnType("jsonb").IsRequired();
        builder.Property(alerta => alerta.UltimaEvidencia).HasColumnType("jsonb").IsRequired();
        builder.Property(alerta => alerta.ObservacaoDaResolucao).HasMaxLength(AlertaOperacional.TamanhoMaximoDaObservacao);

        builder.HasOne<Organizacao>().WithMany().HasForeignKey(alerta => alerta.OrganizacaoId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Entrega>().WithMany().HasForeignKey(alerta => alerta.EntregaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Motorista>().WithMany().HasForeignKey(alerta => alerta.MotoristaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Rota>().WithMany().HasForeignKey(alerta => alerta.RotaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Usuario>().WithMany().HasForeignKey(alerta => alerta.ResolvidoPorUsuarioId).OnDelete(DeleteBehavior.Restrict);

        // Deduplicação no banco: um alerta aberto por problema, mesmo com duas avaliações ao mesmo tempo.
        builder.HasIndex(alerta => new { alerta.OrganizacaoId, alerta.Chave })
            .IsUnique()
            .HasFilter("estado = 'Aberto'")
            .HasDatabaseName(NomesDeRestricoes.AlertaAbertoPorChave);

        // O alerta anterior da mesma chave, para reabrir.
        builder.HasIndex(alerta => new { alerta.OrganizacaoId, alerta.Chave, alerta.AbertoEm });

        // A lista do console: abertos da organização.
        builder.HasIndex(alerta => new { alerta.OrganizacaoId, alerta.Estado, alerta.AbertoEm });

        builder.HasIndex(alerta => alerta.RotaId).HasDatabaseName("ix_alertas_operacionais_rota_id_aberto").HasFilter("estado = 'Aberto'");
        builder.HasIndex(alerta => alerta.EntregaId);
        builder.HasIndex(alerta => alerta.MotoristaId);
    }
}

/// <summary>Mapeamento de <see cref="EventoDoAlerta"/> — tabela <c>eventos_de_alerta</c>.</summary>
internal sealed class EventoDoAlertaConfiguracao : IEntityTypeConfiguration<EventoDoAlerta>
{
    public void Configure(EntityTypeBuilder<EventoDoAlerta> builder)
    {
        builder.ToTable("eventos_de_alerta", tabela =>
            tabela.HasCheckConstraint("ck_eventos_de_alerta_sequencia_positiva", "sequencia > 0"));

        builder.HasKey(evento => evento.Id);
        builder.Property(evento => evento.Id).ValueGeneratedNever();
        builder.Property(evento => evento.Tipo).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(evento => evento.Evidencia).HasColumnType("jsonb").IsRequired();
        builder.Property(evento => evento.Observacao).HasMaxLength(AlertaOperacional.TamanhoMaximoDaObservacao);

        builder.HasOne<AlertaOperacional>().WithMany().HasForeignKey(evento => evento.AlertaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Organizacao>().WithMany().HasForeignKey(evento => evento.OrganizacaoId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Usuario>().WithMany().HasForeignKey(evento => evento.UsuarioId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(evento => new { evento.AlertaId, evento.Sequencia })
            .IsUnique()
            .HasDatabaseName(NomesDeRestricoes.SequenciaDoEventoDoAlerta);
        builder.HasIndex(evento => evento.OrganizacaoId);
    }
}
