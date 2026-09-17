using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TorreLogistica.Application.Abstracoes.Persistencia;
using TorreLogistica.Domain.Entregas;
using TorreLogistica.Domain.Rastreamento;

namespace TorreLogistica.Infrastructure.Persistencia.Configuracoes;

/// <summary>Mapeamento de <see cref="TokenDeRastreamento"/>.</summary>
internal sealed class TokenDeRastreamentoConfiguracao : IEntityTypeConfiguration<TokenDeRastreamento>
{
    public void Configure(EntityTypeBuilder<TokenDeRastreamento> builder)
    {
        builder.ToTable("tokens_de_rastreamento", tabela =>
        {
            tabela.HasCheckConstraint("ck_tokens_de_rastreamento_hash_tem_32_bytes", "octet_length(hash_do_token) = 32");
            tabela.HasCheckConstraint("ck_tokens_de_rastreamento_validade", "expira_em > emitido_em");
        });

        builder.HasKey(token => token.Id);
        builder.Property(token => token.Id).ValueGeneratedNever();
        builder.Property(token => token.HashDoToken).IsRequired();

        builder.HasOne<Entrega>()
            .WithMany()
            .HasForeignKey(token => token.EntregaId)
            .OnDelete(DeleteBehavior.Restrict);

        // O lookup público é feito só por este índice: um token apresentado é convertido em hash e
        // procurado aqui. Sem acerto, nada é consultado — nem a entrega, nem a organização.
        builder.HasIndex(token => token.HashDoToken)
            .IsUnique()
            .HasDatabaseName(NomesDeRestricoes.HashDoTokenDeRastreamento);

        // Um link ativo por entrega, garantido pelo banco: emitir de novo revoga o anterior no mesmo
        // commit, e duas emissões simultâneas não conseguem deixar dois links vivos.
        builder.HasIndex(token => token.EntregaId)
            .IsUnique()
            .HasFilter("revogado_em IS NULL")
            .HasDatabaseName(NomesDeRestricoes.TokenDeRastreamentoAtivoPorEntrega);

        builder.HasIndex(token => token.OrganizacaoId);
    }
}
