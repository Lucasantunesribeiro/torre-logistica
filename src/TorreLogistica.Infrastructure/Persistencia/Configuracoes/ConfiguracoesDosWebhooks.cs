using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TorreLogistica.Application.Abstracoes.Persistencia;
using TorreLogistica.Domain.Entregas;
using TorreLogistica.Domain.Identidade;
using TorreLogistica.Domain.Webhooks;

namespace TorreLogistica.Infrastructure.Persistencia.Configuracoes;

/// <summary>Mapeamento de <see cref="MensagemDoOutbox"/>.</summary>
internal sealed class MensagemDoOutboxConfiguracao : IEntityTypeConfiguration<MensagemDoOutbox>
{
    public void Configure(EntityTypeBuilder<MensagemDoOutbox> builder)
    {
        builder.ToTable("outbox");

        builder.HasKey(mensagem => mensagem.Id);
        builder.Property(mensagem => mensagem.Id).ValueGeneratedNever();
        builder.Property(mensagem => mensagem.Tipo).HasMaxLength(60).IsRequired();
        builder.Property(mensagem => mensagem.Conteudo).HasColumnType("jsonb").IsRequired();
        builder.Property(mensagem => mensagem.UltimoErro).HasMaxLength(PoliticaDeWebhook.TamanhoMaximoDoErro);

        builder.HasOne<Entrega>()
            .WithMany()
            .HasForeignKey(mensagem => mensagem.EntregaId)
            .OnDelete(DeleteBehavior.Restrict);

        // O despachante procura sempre a mesma coisa: o que ainda não saiu e já pode sair. O índice
        // parcial mantém essa varredura barata mesmo com a tabela crescendo com o histórico.
        builder.HasIndex(mensagem => mensagem.DisponivelEm)
            .HasFilter("despachada_em IS NULL")
            .HasDatabaseName("ix_outbox_pendentes");

        builder.HasIndex(mensagem => mensagem.EntregaId);
    }
}

/// <summary>Mapeamento de <see cref="AssinaturaDeWebhook"/>.</summary>
internal sealed class AssinaturaDeWebhookConfiguracao : IEntityTypeConfiguration<AssinaturaDeWebhook>
{
    public void Configure(EntityTypeBuilder<AssinaturaDeWebhook> builder)
    {
        builder.ToTable("assinaturas_de_webhook");

        builder.HasKey(assinatura => assinatura.Id);
        builder.Property(assinatura => assinatura.Id).ValueGeneratedNever();
        builder.Property(assinatura => assinatura.Nome).HasMaxLength(PoliticaDeWebhook.TamanhoMaximoDoNome).IsRequired();
        builder.Property(assinatura => assinatura.Url).HasMaxLength(PoliticaDeWebhook.TamanhoMaximoDaUrl).IsRequired();
        builder.Property(assinatura => assinatura.SegredoCifrado).IsRequired();

        // Lista de nomes de evento vira text[] no PostgreSQL: é dado do próprio registro, não entidade,
        // e tabela filha para três strings só acrescentaria junção.
        builder.PrimitiveCollection<List<string>>("_eventos")
            .HasColumnName("eventos")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasOne<Organizacao>()
            .WithMany()
            .HasForeignKey(assinatura => assinatura.OrganizacaoId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(assinatura => assinatura.OrganizacaoId);
    }
}

/// <summary>Mapeamento de <see cref="EntregaDeWebhook"/>.</summary>
internal sealed class EntregaDeWebhookConfiguracao : IEntityTypeConfiguration<EntregaDeWebhook>
{
    public void Configure(EntityTypeBuilder<EntregaDeWebhook> builder)
    {
        builder.ToTable("entregas_de_webhook");

        builder.HasKey(entrega => entrega.Id);
        builder.Property(entrega => entrega.Id).ValueGeneratedNever();
        builder.Property(entrega => entrega.Tipo).HasMaxLength(60).IsRequired();
        builder.Property(entrega => entrega.Conteudo).HasColumnType("jsonb").IsRequired();
        builder.Property(entrega => entrega.Url).HasMaxLength(PoliticaDeWebhook.TamanhoMaximoDaUrl).IsRequired();
        builder.Property(entrega => entrega.UltimoErro).HasMaxLength(PoliticaDeWebhook.TamanhoMaximoDoErro);
        builder.Property(entrega => entrega.Estado).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.HasOne<AssinaturaDeWebhook>()
            .WithMany()
            .HasForeignKey(entrega => entrega.AssinaturaId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<MensagemDoOutbox>()
            .WithMany()
            .HasForeignKey(entrega => entrega.MensagemId)
            .OnDelete(DeleteBehavior.Restrict);

        // A dedução do consumidor mora aqui: o despachante pode processar a mesma mensagem duas vezes,
        // e o assinante ainda recebe o evento uma vez só.
        builder.HasIndex(entrega => new { entrega.AssinaturaId, entrega.MensagemId })
            .IsUnique()
            .HasDatabaseName(NomesDeRestricoes.EntregaDeWebhookPorMensagem);

        builder.HasIndex(entrega => entrega.DisponivelEm)
            .HasFilter("estado = 'Pendente'")
            .HasDatabaseName("ix_entregas_de_webhook_pendentes");

        builder.HasIndex(entrega => new { entrega.OrganizacaoId, entrega.Estado });
    }
}

/// <summary>Mapeamento de <see cref="TentativaDeWebhook"/>.</summary>
internal sealed class TentativaDeWebhookConfiguracao : IEntityTypeConfiguration<TentativaDeWebhook>
{
    public void Configure(EntityTypeBuilder<TentativaDeWebhook> builder)
    {
        builder.ToTable("tentativas_de_webhook");

        builder.HasKey(tentativa => tentativa.Id);
        builder.Property(tentativa => tentativa.Id).ValueGeneratedNever();
        builder.Property(tentativa => tentativa.Erro).HasMaxLength(PoliticaDeWebhook.TamanhoMaximoDoErro);

        builder.HasOne<EntregaDeWebhook>()
            .WithMany()
            .HasForeignKey(tentativa => tentativa.EntregaDeWebhookId)
            .OnDelete(DeleteBehavior.Restrict);

        // Índice de leitura, e não restrição de unicidade: a entrega é no mínimo-uma-vez, e o mesmo número
        // pode se repetir quando o arrendamento vence ou o processo cai depois do POST e antes de gravar.
        // Essa repetição é justamente o que o histórico precisa mostrar, não um erro a impedir.
        builder.HasIndex(tentativa => new { tentativa.EntregaDeWebhookId, tentativa.Numero })
            .HasDatabaseName("ix_tentativas_de_webhook_entrega_numero");
    }
}
