using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TorreLogistica.Application.Abstracoes.Persistencia;
using TorreLogistica.Domain.Auditoria;
using TorreLogistica.Domain.Identidade;

namespace TorreLogistica.Infrastructure.Persistencia.Configuracoes;

/// <summary>Mapeamento de <see cref="Organizacao"/>.</summary>
internal sealed class OrganizacaoConfiguracao : IEntityTypeConfiguration<Organizacao>
{
    public void Configure(EntityTypeBuilder<Organizacao> builder)
    {
        builder.ToTable("organizacoes");
        builder.HasKey(organizacao => organizacao.Id);
        builder.Property(organizacao => organizacao.Id).ValueGeneratedNever();
        builder.Property(organizacao => organizacao.Nome).HasMaxLength(Organizacao.TamanhoMaximoDoNome).IsRequired();
        builder.Property(organizacao => organizacao.Slug).HasMaxLength(Organizacao.TamanhoMaximoDoSlug).IsRequired();

        builder.HasIndex(organizacao => organizacao.Slug)
            .IsUnique()
            .HasDatabaseName(NomesDeRestricoes.SlugDaOrganizacao);
    }
}

/// <summary>Mapeamento de <see cref="Usuario"/>.</summary>
internal sealed class UsuarioConfiguracao : IEntityTypeConfiguration<Usuario>
{
    public void Configure(EntityTypeBuilder<Usuario> builder)
    {
        builder.ToTable("usuarios", tabela =>
        {
            tabela.HasCheckConstraint(
                "ck_usuarios_tentativas_de_login_falhas_nao_negativas",
                "tentativas_de_login_falhas >= 0");
        });

        builder.HasKey(usuario => usuario.Id);
        builder.Property(usuario => usuario.Id).ValueGeneratedNever();
        builder.Property(usuario => usuario.Nome).HasMaxLength(Usuario.TamanhoMaximoDoNome).IsRequired();
        builder.Property(usuario => usuario.Email).HasMaxLength(EnderecoDeEmail.TamanhoMaximo).IsRequired();
        builder.Property(usuario => usuario.EmailNormalizado).HasMaxLength(EnderecoDeEmail.TamanhoMaximo).IsRequired();
        builder.Property(usuario => usuario.HashDaSenha).HasMaxLength(512).IsRequired();

        // Enum como texto: legível em consulta manual e imune a reordenação do código.
        builder.Property(usuario => usuario.Perfil).HasConversion<string>().HasMaxLength(30).IsRequired();

        builder.Ignore(usuario => usuario.Canal);

        builder.HasOne<Organizacao>()
            .WithMany()
            .HasForeignKey(usuario => usuario.OrganizacaoId)
            .OnDelete(DeleteBehavior.Restrict);

        // Unicidade por organização: é o que impede o conflito de e-mail de revelar a
        // existência de contas em outro tenant. Ver ADR 0010.
        builder.HasIndex(usuario => new { usuario.OrganizacaoId, usuario.EmailNormalizado })
            .IsUnique()
            .HasDatabaseName(NomesDeRestricoes.EmailDoUsuarioPorOrganizacao);
    }
}

/// <summary>Mapeamento de <see cref="Sessao"/>.</summary>
internal sealed class SessaoConfiguracao : IEntityTypeConfiguration<Sessao>
{
    public void Configure(EntityTypeBuilder<Sessao> builder)
    {
        builder.ToTable("sessoes");
        builder.HasKey(sessao => sessao.Id);
        builder.Property(sessao => sessao.Id).ValueGeneratedNever();
        builder.Property(sessao => sessao.Canal).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(sessao => sessao.MotivoDaRevogacao).HasConversion<string>().HasMaxLength(30);

        builder.HasOne<Usuario>()
            .WithMany()
            .HasForeignKey(sessao => sessao.UsuarioId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Organizacao>()
            .WithMany()
            .HasForeignKey(sessao => sessao.OrganizacaoId)
            .OnDelete(DeleteBehavior.Restrict);

        // Revogar as sessões abertas de uma conta procura exatamente por isto.
        builder.HasIndex(sessao => sessao.UsuarioId)
            .HasDatabaseName("ix_sessoes_usuario_id_abertas")
            .HasFilter("revogada_em IS NULL");
    }
}

/// <summary>Mapeamento de <see cref="TokenDeRenovacao"/>.</summary>
internal sealed class TokenDeRenovacaoConfiguracao : IEntityTypeConfiguration<TokenDeRenovacao>
{
    public void Configure(EntityTypeBuilder<TokenDeRenovacao> builder)
    {
        builder.ToTable("tokens_de_renovacao", tabela =>
        {
            tabela.HasCheckConstraint("ck_tokens_de_renovacao_hash_tem_32_bytes", "octet_length(hash_do_token) = 32");
        });

        builder.HasKey(token => token.Id);
        builder.Property(token => token.Id).ValueGeneratedNever();
        builder.Property(token => token.HashDoToken).IsRequired();

        builder.HasOne<Sessao>()
            .WithMany()
            .HasForeignKey(token => token.SessaoId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(token => token.HashDoToken)
            .IsUnique()
            .HasDatabaseName(NomesDeRestricoes.HashDoTokenDeRenovacao);

        builder.HasIndex(token => token.SessaoId);
    }
}

/// <summary>Mapeamento de <see cref="EventoDeAuditoria"/>.</summary>
internal sealed class EventoDeAuditoriaConfiguracao : IEntityTypeConfiguration<EventoDeAuditoria>
{
    public void Configure(EntityTypeBuilder<EventoDeAuditoria> builder)
    {
        builder.ToTable("eventos_de_auditoria");
        builder.HasKey(evento => evento.Id);
        builder.Property(evento => evento.Id).ValueGeneratedNever();
        builder.Property(evento => evento.Tipo).HasMaxLength(60).IsRequired();
        builder.Property(evento => evento.AlvoTipo).HasMaxLength(60).IsRequired();
        builder.Property(evento => evento.Dados).HasColumnType("jsonb").IsRequired();

        builder.HasOne<Organizacao>()
            .WithMany()
            .HasForeignKey(evento => evento.OrganizacaoId)
            .OnDelete(DeleteBehavior.Restrict);

        // A consulta natural da trilha é "o que aconteceu nesta organização, do mais recente".
        builder.HasIndex(evento => new { evento.OrganizacaoId, evento.OcorridoEm })
            .IsDescending(false, true);
    }
}
