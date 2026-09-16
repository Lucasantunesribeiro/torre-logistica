using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TorreLogistica.Application.Abstracoes.Persistencia;
using TorreLogistica.Domain.Comprovantes;
using TorreLogistica.Domain.Entregas;
using TorreLogistica.Domain.Frota;
using TorreLogistica.Domain.Identidade;
using TorreLogistica.Domain.Rotas;

namespace TorreLogistica.Infrastructure.Persistencia.Configuracoes;

/// <summary>Mapeamento de <see cref="Comprovante"/> — tabela <c>comprovantes</c>, somente-inserção.</summary>
internal sealed class ComprovanteConfiguracao : IEntityTypeConfiguration<Comprovante>
{
    public void Configure(EntityTypeBuilder<Comprovante> builder)
    {
        builder.ToTable("comprovantes");

        builder.HasKey(comprovante => comprovante.Id);
        builder.Property(comprovante => comprovante.Id).ValueGeneratedNever();
        builder.Property(comprovante => comprovante.RecebidoPor).HasMaxLength(PoliticaDeComprovante.TamanhoMaximoDoRecebedor).IsRequired();
        builder.Property(comprovante => comprovante.Observacao).HasMaxLength(PoliticaDeComprovante.TamanhoMaximoDaObservacao);

        builder.Property(comprovante => comprovante.Localizacao)
            .HasConversion(MapeamentoComum.CoordenadaOpcional)
            .HasColumnType(MapeamentoComum.ColunaDePonto);

        builder.HasOne<Organizacao>().WithMany().HasForeignKey(comprovante => comprovante.OrganizacaoId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Entrega>().WithMany().HasForeignKey(comprovante => comprovante.EntregaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Rota>().WithMany().HasForeignKey(comprovante => comprovante.RotaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Motorista>().WithMany().HasForeignKey(comprovante => comprovante.MotoristaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Usuario>().WithMany().HasForeignKey(comprovante => comprovante.AutorUsuarioId).OnDelete(DeleteBehavior.Restrict);

        // Uma entrega, um comprovante: repetir a conclusão não cria outra prova.
        builder.HasIndex(comprovante => comprovante.EntregaId)
            .IsUnique()
            .HasDatabaseName(NomesDeRestricoes.ComprovantePorEntrega);

        builder.HasIndex(comprovante => new { comprovante.OrganizacaoId, comprovante.RegistradoEm });

        builder.HasMany(comprovante => comprovante.Arquivos)
            .WithOne()
            .HasForeignKey(arquivo => arquivo.ComprovanteId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(comprovante => comprovante.Arquivos).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

/// <summary>Mapeamento de <see cref="ArquivoDoComprovante"/> — tabela <c>arquivos_do_comprovante</c>.</summary>
internal sealed class ArquivoDoComprovanteConfiguracao : IEntityTypeConfiguration<ArquivoDoComprovante>
{
    public void Configure(EntityTypeBuilder<ArquivoDoComprovante> builder)
    {
        builder.ToTable("arquivos_do_comprovante", tabela =>
        {
            tabela.HasCheckConstraint("ck_arquivos_do_comprovante_tamanho_positivo", "tamanho_em_bytes > 0");
            tabela.HasCheckConstraint(
                "ck_arquivos_do_comprovante_tamanho_maximo",
                $"tamanho_em_bytes <= {PoliticaDeComprovante.TamanhoMaximoEmBytes}");
        });

        builder.HasKey(arquivo => arquivo.Id);
        builder.Property(arquivo => arquivo.Id).ValueGeneratedNever();
        builder.Property(arquivo => arquivo.Tipo).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(arquivo => arquivo.Chave).HasMaxLength(PoliticaDeComprovante.TamanhoMaximoDaChave).IsRequired();
        builder.Property(arquivo => arquivo.TipoDeConteudo).HasMaxLength(60).IsRequired();
        builder.Property(arquivo => arquivo.HashSha256).HasMaxLength(64).IsRequired();

        builder.HasOne<Organizacao>().WithMany().HasForeignKey(arquivo => arquivo.OrganizacaoId).OnDelete(DeleteBehavior.Restrict);

        // A chave do objeto é única: o mesmo arquivo não é registrado duas vezes.
        builder.HasIndex(arquivo => arquivo.Chave)
            .IsUnique()
            .HasDatabaseName(NomesDeRestricoes.ChaveDoArquivoDoComprovante);
        builder.HasIndex(arquivo => arquivo.ComprovanteId);
    }
}
