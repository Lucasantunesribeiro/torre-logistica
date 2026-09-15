using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TorreLogistica.Application.Abstracoes.Persistencia;
using TorreLogistica.Domain.Entregas;
using TorreLogistica.Domain.Identidade;
using TorreLogistica.Domain.Previsao;
using TorreLogistica.Domain.Rotas;

namespace TorreLogistica.Infrastructure.Persistencia.Configuracoes;

/// <summary>Mapeamento de <see cref="PrevisaoDaEntrega"/> — tabela <c>previsoes_da_entrega</c>.</summary>
internal sealed class PrevisaoDaEntregaConfiguracao : IEntityTypeConfiguration<PrevisaoDaEntrega>
{
    public void Configure(EntityTypeBuilder<PrevisaoDaEntrega> builder)
    {
        builder.ToTable("previsoes_da_entrega", tabela =>
        {
            tabela.HasCheckConstraint(
                "ck_previsoes_da_entrega_composicao_nao_negativa",
                "deslocamento_em_segundos >= 0 AND distancia_em_metros >= 0 AND paradas_antes >= 0 "
                + "AND tempo_das_paradas_antes_em_segundos >= 0 AND tempo_por_parada_em_segundos >= 0");
            tabela.HasCheckConstraint("ck_previsoes_da_entrega_janela", "janela_fim > janela_inicio");
            tabela.HasCheckConstraint(
                "ck_previsoes_da_entrega_limiares",
                "limiar_de_risco_em_segundos >= 0 AND limiar_de_atencao_em_segundos > limiar_de_risco_em_segundos");
        });

        builder.HasKey(previsao => previsao.EntregaId).HasName(NomesDeRestricoes.PrevisaoDaEntrega);
        builder.Property(previsao => previsao.EntregaId).ValueGeneratedNever();
        builder.Property(previsao => previsao.Versao).IsRowVersion();

        builder.Property(previsao => previsao.Situacao).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(previsao => previsao.MotivoDaSituacao).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(previsao => previsao.MotivoSemChegadaPrevista).HasConversion<string>().HasMaxLength(40);
        builder.Property(previsao => previsao.Fonte).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(previsao => previsao.MotivoDaContingencia).HasConversion<string>().HasMaxLength(20);
        builder.Property(previsao => previsao.Provedor).HasMaxLength(60);

        builder.HasOne<Entrega>().WithOne().HasForeignKey<PrevisaoDaEntrega>(previsao => previsao.EntregaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Organizacao>().WithMany().HasForeignKey(previsao => previsao.OrganizacaoId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Rota>().WithMany().HasForeignKey(previsao => previsao.RotaId).OnDelete(DeleteBehavior.Restrict);

        // A reavaliação periódica e o recálculo por rota procuram previsões ainda ativas.
        builder.HasIndex(previsao => previsao.RotaId).HasDatabaseName("ix_previsoes_da_entrega_rota_id_ativa").HasFilter("ativa");
        builder.HasIndex(previsao => previsao.OrganizacaoId);
    }
}

/// <summary>Mapeamento de <see cref="RegistroDePrevisao"/> — tabela <c>registros_de_previsao</c>.</summary>
internal sealed class RegistroDePrevisaoConfiguracao : IEntityTypeConfiguration<RegistroDePrevisao>
{
    public void Configure(EntityTypeBuilder<RegistroDePrevisao> builder)
    {
        builder.ToTable("registros_de_previsao", tabela =>
        {
            tabela.HasCheckConstraint("ck_registros_de_previsao_sequencia_positiva", "sequencia > 0");
            tabela.HasCheckConstraint("ck_registros_de_previsao_janela", "janela_fim > janela_inicio");
        });

        builder.HasKey(registro => registro.Id);
        builder.Property(registro => registro.Id).ValueGeneratedNever();

        builder.Property(registro => registro.Tipo).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(registro => registro.SituacaoAnterior).HasConversion<string>().HasMaxLength(20);
        builder.Property(registro => registro.Situacao).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(registro => registro.MotivoDaSituacao).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(registro => registro.MotivoSemChegadaPrevista).HasConversion<string>().HasMaxLength(40);
        builder.Property(registro => registro.Fonte).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(registro => registro.MotivoDaContingencia).HasConversion<string>().HasMaxLength(20);
        builder.Property(registro => registro.StatusDaEntrega).HasConversion<string>().HasMaxLength(30);
        builder.Property(registro => registro.Provedor).HasMaxLength(60);

        builder.HasOne<Entrega>().WithMany().HasForeignKey(registro => registro.EntregaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Organizacao>().WithMany().HasForeignKey(registro => registro.OrganizacaoId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Rota>().WithMany().HasForeignKey(registro => registro.RotaId).OnDelete(DeleteBehavior.Restrict);

        // Dois recálculos concorrentes da mesma entrega não gravam a mesma posição do histórico.
        builder.HasIndex(registro => new { registro.EntregaId, registro.Sequencia })
            .IsUnique()
            .HasDatabaseName(NomesDeRestricoes.SequenciaDoRegistroDePrevisao);
        builder.HasIndex(registro => registro.OrganizacaoId);
        builder.HasIndex(registro => registro.RotaId);
    }
}
