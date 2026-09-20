using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TorreLogistica.Application.Abstracoes.Persistencia;
using TorreLogistica.Domain.Entregas;
using TorreLogistica.Domain.Frota;
using TorreLogistica.Domain.Identidade;
using TorreLogistica.Domain.Operacao;
using TorreLogistica.Domain.Rotas;

namespace TorreLogistica.Infrastructure.Persistencia.Configuracoes;

/// <summary>Mapeamento de <see cref="Rota"/>.</summary>
internal sealed class RotaConfiguracao : IEntityTypeConfiguration<Rota>
{
    /// <summary>
    /// Condição de rota ativa em SQL, montada a partir do enum para não divergir dele.
    /// </summary>
    internal static readonly string CondicaoDeRotaAtiva =
        $"status IN ('{nameof(StatusDaRota.EmMontagem)}', '{nameof(StatusDaRota.Planejada)}', '{nameof(StatusDaRota.EmAndamento)}')";

    public void Configure(EntityTypeBuilder<Rota> builder)
    {
        builder.ToTable("rotas", tabela =>
        {
            tabela.HasCheckConstraint("ck_rotas_versao_da_ordem_nao_negativa", "versao_da_ordem >= 0");
            tabela.HasCheckConstraint("ck_rotas_ultima_sequencia_de_evento_positiva", "ultima_sequencia_de_evento > 0");
        });

        builder.HasKey(rota => rota.Id);
        builder.Property(rota => rota.Id).ValueGeneratedNever();
        builder.Property(rota => rota.Codigo).HasMaxLength(CodigoDaRota.TamanhoMaximo).IsRequired();
        builder.Property(rota => rota.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(rota => rota.Versao).IsRowVersion();

        builder.HasMany(rota => rota.Paradas)
            .WithOne()
            .HasForeignKey(parada => parada.RotaId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(rota => rota.Paradas).HasField("_paradas").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasOne<Organizacao>().WithMany().HasForeignKey(rota => rota.OrganizacaoId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Hub>().WithMany().HasForeignKey(rota => rota.HubId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Motorista>().WithMany().HasForeignKey(rota => rota.MotoristaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Veiculo>().WithMany().HasForeignKey(rota => rota.VeiculoId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(rota => new { rota.OrganizacaoId, rota.Codigo })
            .IsUnique()
            .HasDatabaseName(NomesDeRestricoes.CodigoDaRotaPorOrganizacao);

        // Motorista e veículo em no máximo uma rota ativa por dia. Parcial: rota cancelada ou
        // concluída libera; rota sem motorista não conta.
        builder.HasIndex(rota => new { rota.OrganizacaoId, rota.MotoristaId, rota.Data })
            .IsUnique()
            .HasFilter($"motorista_id IS NOT NULL AND {CondicaoDeRotaAtiva}")
            .HasDatabaseName(NomesDeRestricoes.MotoristaEmRotaAtivaNoDia);

        builder.HasIndex(rota => new { rota.OrganizacaoId, rota.VeiculoId, rota.Data })
            .IsUnique()
            .HasFilter($"veiculo_id IS NOT NULL AND {CondicaoDeRotaAtiva}")
            .HasDatabaseName(NomesDeRestricoes.VeiculoEmRotaAtivaNoDia);

        builder.HasIndex(rota => new { rota.OrganizacaoId, rota.Data, rota.Status });
    }
}

/// <summary>Mapeamento de <see cref="Parada"/>.</summary>
internal sealed class ParadaConfiguracao : IEntityTypeConfiguration<Parada>
{
    public void Configure(EntityTypeBuilder<Parada> builder)
    {
        builder.ToTable("paradas", tabela =>
            tabela.HasCheckConstraint("ck_paradas_sequencia_positiva", "sequencia > 0"));

        builder.HasKey(parada => parada.Id);
        builder.Property(parada => parada.Id).ValueGeneratedNever();
        builder.Property(parada => parada.MotivoDaRemocao).HasConversion<string>().HasMaxLength(40);

        builder.HasOne<Organizacao>().WithMany().HasForeignKey(parada => parada.OrganizacaoId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Entrega>().WithMany().HasForeignKey(parada => parada.EntregaId).OnDelete(DeleteBehavior.Restrict);

        // A regra "entrega em no máximo uma rota ativa" atravessa rotas: nenhuma rota sozinha a
        // garante. Este índice decide a corrida entre duas inclusões simultâneas.
        builder.HasIndex(parada => parada.EntregaId)
            .IsUnique()
            .HasFilter("ativa")
            .HasDatabaseName(NomesDeRestricoes.ParadaAtivaDaEntrega);

        builder.HasIndex(parada => new { parada.RotaId, parada.Ativa, parada.Sequencia });

        // Indicadores por rota: a pergunta é "em que rota esta entrega estava quando foi concluída", e a
        // resposta pode estar numa parada já desativada — concluir a rota desativa todas as dela. O índice
        // acima não serve, porque cobre só a parada ativa.
        builder.HasIndex(parada => new { parada.EntregaId, parada.AdicionadaEm })
            .HasDatabaseName("ix_paradas_entrega_adicionada_em");
    }
}

/// <summary>Mapeamento de <see cref="EventoDaRota"/>.</summary>
internal sealed class EventoDaRotaConfiguracao : IEntityTypeConfiguration<EventoDaRota>
{
    public void Configure(EntityTypeBuilder<EventoDaRota> builder)
    {
        builder.ToTable("eventos_da_rota", tabela =>
            tabela.HasCheckConstraint("ck_eventos_da_rota_sequencia_positiva", "sequencia > 0"));

        builder.HasKey(evento => evento.Id);
        builder.Property(evento => evento.Id).ValueGeneratedNever();
        builder.Property(evento => evento.Tipo).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(evento => evento.StatusResultante).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(evento => evento.Dados).HasColumnType("jsonb").IsRequired();

        builder.HasOne<Organizacao>().WithMany().HasForeignKey(evento => evento.OrganizacaoId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Rota>().WithMany().HasForeignKey(evento => evento.RotaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Usuario>().WithMany().HasForeignKey(evento => evento.AutorUsuarioId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(evento => new { evento.RotaId, evento.Sequencia })
            .IsUnique()
            .HasDatabaseName(NomesDeRestricoes.SequenciaDoEventoDaRota);
    }
}
