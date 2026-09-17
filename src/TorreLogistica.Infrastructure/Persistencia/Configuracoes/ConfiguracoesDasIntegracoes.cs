using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TorreLogistica.Application.Abstracoes.Persistencia;
using TorreLogistica.Domain.Entregas;
using TorreLogistica.Domain.Identidade;
using TorreLogistica.Domain.Integracoes;

namespace TorreLogistica.Infrastructure.Persistencia.Configuracoes;

/// <summary>Mapeamento de <see cref="Integracao"/>.</summary>
internal sealed class IntegracaoConfiguracao : IEntityTypeConfiguration<Integracao>
{
    public void Configure(EntityTypeBuilder<Integracao> builder)
    {
        builder.ToTable("integracoes", tabela =>
        {
            tabela.HasCheckConstraint("ck_integracoes_hash_tem_32_bytes", "octet_length(hash_do_segredo) = 32");
        });

        builder.HasKey(integracao => integracao.Id);
        builder.Property(integracao => integracao.Id).ValueGeneratedNever();
        builder.Property(integracao => integracao.Nome)
            .HasMaxLength(PoliticaDeIntegracao.TamanhoMaximoDoNome)
            .IsRequired();
        builder.Property(integracao => integracao.IdentificadorPublico)
            .HasMaxLength(PoliticaDeIntegracao.TamanhoDoIdentificador)
            .IsRequired();
        builder.Property(integracao => integracao.HashDoSegredo).IsRequired();

        builder.HasOne<Organizacao>()
            .WithMany()
            .HasForeignKey(integracao => integracao.OrganizacaoId)
            .OnDelete(DeleteBehavior.Restrict);

        // Único global, e não por organização: é a chave apresentada que decide a organização, então
        // duas organizações com o mesmo identificador tornariam a credencial ambígua.
        builder.HasIndex(integracao => integracao.IdentificadorPublico)
            .IsUnique()
            .HasDatabaseName(NomesDeRestricoes.IdentificadorPublicoDaIntegracao);

        builder.HasIndex(integracao => integracao.OrganizacaoId);
    }
}

/// <summary>Mapeamento de <see cref="RequisicaoDeIntegracao"/>.</summary>
internal sealed class RequisicaoDeIntegracaoConfiguracao : IEntityTypeConfiguration<RequisicaoDeIntegracao>
{
    public void Configure(EntityTypeBuilder<RequisicaoDeIntegracao> builder)
    {
        builder.ToTable("requisicoes_de_integracao", tabela =>
        {
            tabela.HasCheckConstraint("ck_requisicoes_de_integracao_hash_tem_32_bytes", "octet_length(hash_da_requisicao) = 32");
        });

        builder.HasKey(requisicao => requisicao.Id);
        builder.Property(requisicao => requisicao.Id).ValueGeneratedNever();
        builder.Property(requisicao => requisicao.Chave)
            .HasMaxLength(PoliticaDeIntegracao.TamanhoMaximoDaChaveDeIdempotencia)
            .IsRequired();
        builder.Property(requisicao => requisicao.HashDaRequisicao).IsRequired();
        builder.Property(requisicao => requisicao.Recurso).HasMaxLength(40).IsRequired();

        builder.HasOne<Integracao>()
            .WithMany()
            .HasForeignKey(requisicao => requisicao.IntegracaoId)
            .OnDelete(DeleteBehavior.Restrict);

        // A chave é do cliente: duas integrações podem escolher a mesma sem se atrapalhar.
        builder.HasIndex(requisicao => new { requisicao.OrganizacaoId, requisicao.IntegracaoId, requisicao.Chave })
            .IsUnique()
            .HasDatabaseName(NomesDeRestricoes.ChaveDeIdempotenciaPorIntegracao);
    }
}

/// <summary>Mapeamento de <see cref="ReferenciaExternaDaEntrega"/>.</summary>
internal sealed class ReferenciaExternaDaEntregaConfiguracao : IEntityTypeConfiguration<ReferenciaExternaDaEntrega>
{
    public void Configure(EntityTypeBuilder<ReferenciaExternaDaEntrega> builder)
    {
        builder.ToTable("referencias_externas_de_entrega");

        builder.HasKey(referencia => referencia.Id);
        builder.Property(referencia => referencia.Id).ValueGeneratedNever();
        builder.Property(referencia => referencia.IdentificadorExterno)
            .HasMaxLength(PoliticaDeIntegracao.TamanhoMaximoDoIdentificadorExterno)
            .IsRequired();

        builder.HasOne<Entrega>()
            .WithMany()
            .HasForeignKey(referencia => referencia.EntregaId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Integracao>()
            .WithMany()
            .HasForeignKey(referencia => referencia.IntegracaoId)
            .OnDelete(DeleteBehavior.Restrict);

        // O mesmo pedido do ERP não vira duas entregas, mesmo que a chave de idempotência mude.
        builder.HasIndex(referencia => new { referencia.OrganizacaoId, referencia.IntegracaoId, referencia.IdentificadorExterno })
            .IsUnique()
            .HasDatabaseName(NomesDeRestricoes.ReferenciaExternaPorIntegracao);

        builder.HasIndex(referencia => referencia.EntregaId);
    }
}
