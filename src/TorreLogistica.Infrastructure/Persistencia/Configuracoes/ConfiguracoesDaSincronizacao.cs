using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TorreLogistica.Application.Abstracoes.Persistencia;
using TorreLogistica.Domain.Entregas;
using TorreLogistica.Domain.Frota;
using TorreLogistica.Domain.Identidade;
using TorreLogistica.Domain.Sincronizacao;

namespace TorreLogistica.Infrastructure.Persistencia.Configuracoes;

/// <summary>Mapeamento de <see cref="OperacaoDoCliente"/> — tabela <c>operacoes_do_cliente</c>.</summary>
internal sealed class OperacaoDoClienteConfiguracao : IEntityTypeConfiguration<OperacaoDoCliente>
{
    public void Configure(EntityTypeBuilder<OperacaoDoCliente> builder)
    {
        builder.ToTable("operacoes_do_cliente", tabela =>
            tabela.HasCheckConstraint(
                "ck_operacoes_do_cliente_codigo_do_desfecho",
                "(resultado = 'Aplicada' AND codigo IS NULL) OR (resultado <> 'Aplicada' AND codigo IS NOT NULL)"));

        builder.HasKey(operacao => operacao.Id);
        builder.Property(operacao => operacao.Id).ValueGeneratedNever();
        builder.Property(operacao => operacao.Tipo).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(operacao => operacao.Motivo).HasConversion<string>().HasMaxLength(40);
        builder.Property(operacao => operacao.Observacao).HasMaxLength(Entrega.TamanhoMaximoDaDescricaoDaTentativa);
        builder.Property(operacao => operacao.Resultado).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(operacao => operacao.Codigo).HasMaxLength(60);
        builder.Property(operacao => operacao.Mensagem).HasMaxLength(PoliticaDeOperacaoDoCliente.TamanhoMaximoDaMensagem);

        builder.HasOne<Organizacao>().WithMany().HasForeignKey(operacao => operacao.OrganizacaoId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Motorista>().WithMany().HasForeignKey(operacao => operacao.MotoristaId).OnDelete(DeleteBehavior.Restrict);

        // Exatamente uma vez: a segunda gravação da mesma operação do mesmo motorista falha e é desfeita inteira.
        builder.HasIndex(operacao => new { operacao.OrganizacaoId, operacao.MotoristaId, operacao.OperacaoDoClienteId })
            .IsUnique()
            .HasDatabaseName(NomesDeRestricoes.OperacaoDoCliente);
        builder.HasIndex(operacao => new { operacao.AlvoId, operacao.RecebidaEm });
        builder.HasIndex(operacao => operacao.MotoristaId);
    }
}
