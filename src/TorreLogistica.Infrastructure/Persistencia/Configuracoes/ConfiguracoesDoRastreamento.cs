using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TorreLogistica.Application.Abstracoes.Persistencia;
using TorreLogistica.Domain.Frota;
using TorreLogistica.Domain.Identidade;
using TorreLogistica.Domain.Rastreamento;
using TorreLogistica.Domain.Rotas;

namespace TorreLogistica.Infrastructure.Persistencia.Configuracoes;

/// <summary>Mapeamento de <see cref="PosicaoDoMotorista"/> — tabela <c>posicoes</c>.</summary>
internal sealed class PosicaoDoMotoristaConfiguracao : IEntityTypeConfiguration<PosicaoDoMotorista>
{
    public void Configure(EntityTypeBuilder<PosicaoDoMotorista> builder)
    {
        builder.ToTable("posicoes", tabela =>
        {
            tabela.HasCheckConstraint("ck_posicoes_precisao_positiva", "precisao_em_metros > 0");
            tabela.HasCheckConstraint("ck_posicoes_sequencia_nao_negativa", "sequencia >= 0");
        });

        builder.HasKey(posicao => posicao.Id);
        builder.Property(posicao => posicao.Id).ValueGeneratedNever();
        builder.Property(posicao => posicao.Qualidade).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.Property(posicao => posicao.Localizacao)
            .HasConversion(MapeamentoComum.Coordenada)
            .HasColumnType(MapeamentoComum.ColunaDePonto)
            .IsRequired();

        builder.HasOne<Organizacao>().WithMany().HasForeignKey(posicao => posicao.OrganizacaoId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Motorista>().WithMany().HasForeignKey(posicao => posicao.MotoristaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Rota>().WithMany().HasForeignKey(posicao => posicao.RotaId).OnDelete(DeleteBehavior.Restrict);

        // Idempotência de localização (CLAUDE.md, seção 20): organização + motorista + evento.
        builder.HasIndex(posicao => new { posicao.OrganizacaoId, posicao.MotoristaId, posicao.EventoDeLocalizacaoId })
            .IsUnique()
            .HasDatabaseName(NomesDeRestricoes.EventoDeLocalizacaoDoMotorista);

        // O acesso ao histórico é sempre por motorista e período de captura.
        builder.HasIndex(posicao => new { posicao.MotoristaId, posicao.CapturadaEm });

        // Limpeza por retenção: o corte é pelo recebimento, e o índice deixa a varredura começar
        // exatamente na linha mais velha em vez de percorrer a tabela inteira a cada lote.
        builder.HasIndex(posicao => posicao.RecebidaEm).HasDatabaseName("ix_posicoes_recebida_em");
    }
}

/// <summary>Mapeamento de <see cref="PosicaoAtual"/> — tabela <c>posicoes_atuais</c>.</summary>
internal sealed class PosicaoAtualConfiguracao : IEntityTypeConfiguration<PosicaoAtual>
{
    public void Configure(EntityTypeBuilder<PosicaoAtual> builder)
    {
        builder.ToTable("posicoes_atuais", tabela =>
            tabela.HasCheckConstraint("ck_posicoes_atuais_precisao_positiva", "precisao_em_metros > 0"));

        builder.HasKey(posicao => posicao.MotoristaId);
        builder.Property(posicao => posicao.MotoristaId).ValueGeneratedNever();

        builder.Property(posicao => posicao.Localizacao)
            .HasConversion(MapeamentoComum.Coordenada)
            .HasColumnType(MapeamentoComum.ColunaDePonto)
            .IsRequired();

        builder.HasOne<Organizacao>().WithMany().HasForeignKey(posicao => posicao.OrganizacaoId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Motorista>().WithOne().HasForeignKey<PosicaoAtual>(posicao => posicao.MotoristaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Rota>().WithMany().HasForeignKey(posicao => posicao.RotaId).OnDelete(DeleteBehavior.Restrict);

        // O mapa da operação (Fase 8) abre por organização; o GiST serve às consultas espaciais da Fase 7.
        builder.HasIndex(posicao => posicao.OrganizacaoId);
        builder.HasIndex(posicao => posicao.Localizacao).HasMethod("gist");
    }
}
