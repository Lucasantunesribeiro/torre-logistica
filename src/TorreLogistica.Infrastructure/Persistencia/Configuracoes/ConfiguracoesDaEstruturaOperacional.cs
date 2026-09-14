using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NetTopologySuite.Geometries;
using TorreLogistica.Application.Abstracoes.Persistencia;
using TorreLogistica.Domain.Clientes;
using TorreLogistica.Domain.Comum;
using TorreLogistica.Domain.Frota;
using TorreLogistica.Domain.Identidade;
using TorreLogistica.Domain.Operacao;

namespace TorreLogistica.Infrastructure.Persistencia.Configuracoes;

/// <summary>
/// Conversões compartilhadas pelos mapeamentos dos cadastros.
/// </summary>
internal static class MapeamentoComum
{
    /// <summary>SRID do WGS 84, o sistema de coordenadas do GPS.</summary>
    public const int Wgs84 = 4326;

    /// <summary>Tipo de coluna geográfica usado para pontos.</summary>
    public const string ColunaDePonto = "geography (point, 4326)";

    /// <summary>
    /// Converte a coordenada do domínio no <see cref="Point"/> do PostGIS e de volta.
    /// </summary>
    /// <remarks>
    /// Atenção à ordem: <see cref="Point"/> recebe (X, Y), ou seja, (longitude, latitude).
    /// Inverter as duas é o erro geoespacial mais comum e produz pontos válidos no lugar
    /// errado — por isso há teste de integração lendo <c>ST_X</c> e <c>ST_Y</c> do banco.
    /// </remarks>
    public static readonly ValueConverter<CoordenadaGeografica, Point> Coordenada = new(
        coordenada => new Point(coordenada.Longitude, coordenada.Latitude) { SRID = Wgs84 },
        ponto => CoordenadaGeografica.Criar(ponto.Y, ponto.X));

    /// <summary>Mesma conversão, para coordenada opcional.</summary>
    public static readonly ValueConverter<CoordenadaGeografica?, Point?> CoordenadaOpcional = new(
        coordenada => coordenada == null ? null : new Point(coordenada.Longitude, coordenada.Latitude) { SRID = Wgs84 },
        ponto => ponto == null ? null : CoordenadaGeografica.Criar(ponto.Y, ponto.X));

    /// <summary>Endereço como colunas da própria tabela.</summary>
    public static void MapearEndereco<T>(ComplexPropertyBuilder<Endereco> endereco)
    {
        endereco.Property(item => item.Logradouro).HasMaxLength(Endereco.TamanhoMaximoDoLogradouro).IsRequired();
        endereco.Property(item => item.Numero).HasMaxLength(Endereco.TamanhoMaximoDoNumero).IsRequired();
        endereco.Property(item => item.Complemento).HasMaxLength(Endereco.TamanhoMaximoDeTextoCurto);
        endereco.Property(item => item.Bairro).HasMaxLength(Endereco.TamanhoMaximoDeTextoCurto).IsRequired();
        endereco.Property(item => item.Cidade).HasMaxLength(Endereco.TamanhoMaximoDeTextoCurto).IsRequired();
        endereco.Property(item => item.Uf).HasMaxLength(2).IsFixedLength().IsRequired();
        endereco.Property(item => item.Cep).HasMaxLength(8).IsFixedLength().IsRequired();
    }
}

/// <summary>Mapeamento de <see cref="Motorista"/>.</summary>
internal sealed class MotoristaConfiguracao : IEntityTypeConfiguration<Motorista>
{
    public void Configure(EntityTypeBuilder<Motorista> builder)
    {
        builder.ToTable("motoristas");
        builder.HasKey(motorista => motorista.Id);
        builder.Property(motorista => motorista.Id).ValueGeneratedNever();
        builder.Property(motorista => motorista.Nome).HasMaxLength(Motorista.TamanhoMaximoDoNome).IsRequired();
        builder.Property(motorista => motorista.NomeNormalizado).HasMaxLength(Motorista.TamanhoMaximoDoNome).IsRequired();
        builder.Property(motorista => motorista.Telefone).HasMaxLength(16);

        // xmin do PostgreSQL: muda a cada gravação da linha, sem coluna extra.
        builder.Property(motorista => motorista.Versao).IsRowVersion();

        builder.HasOne<Organizacao>().WithMany().HasForeignKey(motorista => motorista.OrganizacaoId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Usuario>().WithMany().HasForeignKey(motorista => motorista.UsuarioId).OnDelete(DeleteBehavior.Restrict);

        // Uma conta de acesso vale para um motorista só. Parcial: motorista sem conta não conta.
        builder.HasIndex(motorista => motorista.UsuarioId)
            .IsUnique()
            .HasFilter("usuario_id IS NOT NULL")
            .HasDatabaseName(NomesDeRestricoes.ContaDoMotorista);

        builder.HasIndex(motorista => new { motorista.OrganizacaoId, motorista.NomeNormalizado });
    }
}

/// <summary>Mapeamento de <see cref="Veiculo"/>.</summary>
internal sealed class VeiculoConfiguracao : IEntityTypeConfiguration<Veiculo>
{
    public void Configure(EntityTypeBuilder<Veiculo> builder)
    {
        builder.ToTable("veiculos", tabela =>
            tabela.HasCheckConstraint("ck_veiculos_capacidade_em_kg_positiva", "capacidade_em_kg IS NULL OR capacidade_em_kg > 0"));

        builder.HasKey(veiculo => veiculo.Id);
        builder.Property(veiculo => veiculo.Id).ValueGeneratedNever();
        builder.Property(veiculo => veiculo.Placa).HasMaxLength(7).IsFixedLength().IsRequired();
        builder.Property(veiculo => veiculo.Identificacao).HasMaxLength(Veiculo.TamanhoMaximoDaIdentificacao).IsRequired();
        builder.Property(veiculo => veiculo.IdentificacaoNormalizada).HasMaxLength(Veiculo.TamanhoMaximoDaIdentificacao).IsRequired();
        builder.Property(veiculo => veiculo.Tipo).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(veiculo => veiculo.Versao).IsRowVersion();

        builder.HasOne<Organizacao>().WithMany().HasForeignKey(veiculo => veiculo.OrganizacaoId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(veiculo => new { veiculo.OrganizacaoId, veiculo.Placa })
            .IsUnique()
            .HasDatabaseName(NomesDeRestricoes.PlacaDoVeiculoPorOrganizacao);
    }
}

/// <summary>Mapeamento de <see cref="Hub"/>.</summary>
internal sealed class HubConfiguracao : IEntityTypeConfiguration<Hub>
{
    public void Configure(EntityTypeBuilder<Hub> builder)
    {
        builder.ToTable("hubs");
        builder.HasKey(hub => hub.Id);
        builder.Property(hub => hub.Id).ValueGeneratedNever();
        builder.Property(hub => hub.Nome).HasMaxLength(Hub.TamanhoMaximoDoNome).IsRequired();
        builder.Property(hub => hub.NomeNormalizado).HasMaxLength(Hub.TamanhoMaximoDoNome).IsRequired();
        builder.Property(hub => hub.Versao).IsRowVersion();

        builder.ComplexProperty(hub => hub.Endereco, MapeamentoComum.MapearEndereco<Hub>);

        builder.Property(hub => hub.Localizacao)
            .HasConversion(MapeamentoComum.Coordenada)
            .HasColumnType(MapeamentoComum.ColunaDePonto)
            .IsRequired();

        builder.HasOne<Organizacao>().WithMany().HasForeignKey(hub => hub.OrganizacaoId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(hub => new { hub.OrganizacaoId, hub.NomeNormalizado })
            .IsUnique()
            .HasDatabaseName(NomesDeRestricoes.NomeDoHubPorOrganizacao);

        // Índice espacial: a partir da Fase 7, "hub mais próximo" e raio de geofence usam isto.
        builder.HasIndex(hub => hub.Localizacao).HasMethod("gist");
    }
}

/// <summary>Mapeamento de <see cref="Cliente"/>.</summary>
internal sealed class ClienteConfiguracao : IEntityTypeConfiguration<Cliente>
{
    public void Configure(EntityTypeBuilder<Cliente> builder)
    {
        builder.ToTable("clientes");
        builder.HasKey(cliente => cliente.Id);
        builder.Property(cliente => cliente.Id).ValueGeneratedNever();
        builder.Property(cliente => cliente.Nome).HasMaxLength(Cliente.TamanhoMaximoDoNome).IsRequired();
        builder.Property(cliente => cliente.NomeNormalizado).HasMaxLength(Cliente.TamanhoMaximoDoNome).IsRequired();
        builder.Property(cliente => cliente.Cnpj).HasMaxLength(14).IsFixedLength();
        builder.Property(cliente => cliente.Versao).IsRowVersion();

        builder.HasOne<Organizacao>().WithMany().HasForeignKey(cliente => cliente.OrganizacaoId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(cliente => new { cliente.OrganizacaoId, cliente.Cnpj })
            .IsUnique()
            .HasFilter("cnpj IS NOT NULL")
            .HasDatabaseName(NomesDeRestricoes.CnpjDoClientePorOrganizacao);

        builder.HasIndex(cliente => new { cliente.OrganizacaoId, cliente.NomeNormalizado });
    }
}

/// <summary>Mapeamento de <see cref="Destinatario"/>.</summary>
internal sealed class DestinatarioConfiguracao : IEntityTypeConfiguration<Destinatario>
{
    public void Configure(EntityTypeBuilder<Destinatario> builder)
    {
        builder.ToTable("destinatarios");
        builder.HasKey(destinatario => destinatario.Id);
        builder.Property(destinatario => destinatario.Id).ValueGeneratedNever();
        builder.Property(destinatario => destinatario.Nome).HasMaxLength(Destinatario.TamanhoMaximoDoNome).IsRequired();
        builder.Property(destinatario => destinatario.NomeNormalizado).HasMaxLength(Destinatario.TamanhoMaximoDoNome).IsRequired();
        builder.Property(destinatario => destinatario.Telefone).HasMaxLength(16);
        builder.Property(destinatario => destinatario.InstrucoesDeEntrega).HasMaxLength(Destinatario.TamanhoMaximoDasInstrucoes);
        builder.Property(destinatario => destinatario.Versao).IsRowVersion();

        builder.ComplexProperty(destinatario => destinatario.Endereco, MapeamentoComum.MapearEndereco<Destinatario>);

        builder.Property(destinatario => destinatario.Localizacao)
            .HasConversion(MapeamentoComum.CoordenadaOpcional)
            .HasColumnType(MapeamentoComum.ColunaDePonto);

        builder.HasOne<Organizacao>().WithMany().HasForeignKey(destinatario => destinatario.OrganizacaoId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(destinatario => new { destinatario.OrganizacaoId, destinatario.NomeNormalizado });
        builder.HasIndex(destinatario => destinatario.Localizacao).HasMethod("gist");
    }
}
