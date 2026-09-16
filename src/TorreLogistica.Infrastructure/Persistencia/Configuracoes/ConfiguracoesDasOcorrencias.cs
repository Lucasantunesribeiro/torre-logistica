using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TorreLogistica.Domain.Entregas;
using TorreLogistica.Domain.Frota;
using TorreLogistica.Domain.Identidade;
using TorreLogistica.Domain.Ocorrencias;
using TorreLogistica.Domain.Rotas;

namespace TorreLogistica.Infrastructure.Persistencia.Configuracoes;

/// <summary>Mapeamento de <see cref="Ocorrencia"/> — tabela <c>ocorrencias</c>, somente-inserção.</summary>
internal sealed class OcorrenciaConfiguracao : IEntityTypeConfiguration<Ocorrencia>
{
    public void Configure(EntityTypeBuilder<Ocorrencia> builder)
    {
        builder.ToTable("ocorrencias", tabela =>
        {
            // O vocabulário fechado manda: tentativa tem motivo, o resto não tem.
            tabela.HasCheckConstraint(
                "ck_ocorrencias_motivo_coerente_com_tipo",
                "(tipo = 'TentativaDeEntrega' AND motivo_da_tentativa IS NOT NULL) "
                + "OR (tipo <> 'TentativaDeEntrega' AND motivo_da_tentativa IS NULL)");

            // Texto livre é complemento, e só é exigido quando o vocabulário diz "Outro".
            tabela.HasCheckConstraint(
                "ck_ocorrencias_observacao_quando_outro",
                "(tipo <> 'Outro' AND motivo_da_tentativa IS DISTINCT FROM 'Outro') OR observacao IS NOT NULL");
        });

        builder.HasKey(ocorrencia => ocorrencia.Id);
        builder.Property(ocorrencia => ocorrencia.Id).ValueGeneratedNever();
        builder.Property(ocorrencia => ocorrencia.Tipo).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(ocorrencia => ocorrencia.Severidade).HasConversion<string>().HasMaxLength(10).IsRequired();
        builder.Property(ocorrencia => ocorrencia.Origem).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(ocorrencia => ocorrencia.MotivoDaTentativa).HasConversion<string>().HasMaxLength(40);
        builder.Property(ocorrencia => ocorrencia.Observacao).HasMaxLength(Ocorrencia.TamanhoMaximoDaObservacao);

        builder.Property(ocorrencia => ocorrencia.Localizacao)
            .HasConversion(MapeamentoComum.CoordenadaOpcional)
            .HasColumnType(MapeamentoComum.ColunaDePonto);

        builder.HasOne<Organizacao>().WithMany().HasForeignKey(ocorrencia => ocorrencia.OrganizacaoId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Entrega>().WithMany().HasForeignKey(ocorrencia => ocorrencia.EntregaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Rota>().WithMany().HasForeignKey(ocorrencia => ocorrencia.RotaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Motorista>().WithMany().HasForeignKey(ocorrencia => ocorrencia.MotoristaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Usuario>().WithMany().HasForeignKey(ocorrencia => ocorrencia.AutorUsuarioId).OnDelete(DeleteBehavior.Restrict);

        // A lista do console: da organização, mais recentes primeiro.
        builder.HasIndex(ocorrencia => new { ocorrencia.OrganizacaoId, ocorrencia.OcorridaEm });

        // As da entrega, em ordem; e as críticas ainda em avaliação pelo motor de alertas.
        builder.HasIndex(ocorrencia => new { ocorrencia.EntregaId, ocorrencia.OcorridaEm });
        builder.HasIndex(ocorrencia => ocorrencia.EntregaId)
            .HasDatabaseName("ix_ocorrencias_entrega_id_critica")
            .HasFilter("severidade = 'Critica'");
        builder.HasIndex(ocorrencia => ocorrencia.RotaId);
        builder.HasIndex(ocorrencia => ocorrencia.MotoristaId);
    }
}
