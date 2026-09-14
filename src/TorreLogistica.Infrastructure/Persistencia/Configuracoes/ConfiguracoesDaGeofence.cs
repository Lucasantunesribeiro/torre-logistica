using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TorreLogistica.Domain.Entregas;
using TorreLogistica.Domain.Frota;
using TorreLogistica.Domain.Identidade;
using TorreLogistica.Domain.Rastreamento;

namespace TorreLogistica.Infrastructure.Persistencia.Configuracoes;

/// <summary>Mapeamento de <see cref="EstadoDeGeofence"/> — tabela <c>estados_de_geofence</c>.</summary>
internal sealed class EstadoDeGeofenceConfiguracao : IEntityTypeConfiguration<EstadoDeGeofence>
{
    public void Configure(EntityTypeBuilder<EstadoDeGeofence> builder)
    {
        builder.ToTable("estados_de_geofence", tabela =>
        {
            tabela.HasCheckConstraint("ck_estados_de_geofence_raio_positivo", "raio_em_metros > 0");
            tabela.HasCheckConstraint("ck_estados_de_geofence_distancia_nao_negativa", "distancia_em_metros >= 0");
            tabela.HasCheckConstraint("ck_estados_de_geofence_entradas_nao_negativas", "entradas >= 0");
        });

        builder.HasKey(estado => estado.EntregaId);
        builder.Property(estado => estado.EntregaId).ValueGeneratedNever();
        builder.Property(estado => estado.Versao).IsRowVersion();

        builder.HasOne<Entrega>().WithOne().HasForeignKey<EstadoDeGeofence>(estado => estado.EntregaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Organizacao>().WithMany().HasForeignKey(estado => estado.OrganizacaoId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Motorista>().WithMany().HasForeignKey(estado => estado.MotoristaId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Mapeamento de <see cref="EventoDeGeofence"/> — tabela <c>eventos_de_geofence</c>.</summary>
internal sealed class EventoDeGeofenceConfiguracao : IEntityTypeConfiguration<EventoDeGeofence>
{
    public void Configure(EntityTypeBuilder<EventoDeGeofence> builder)
    {
        builder.ToTable("eventos_de_geofence");

        builder.HasKey(evento => evento.Id);
        builder.Property(evento => evento.Id).ValueGeneratedNever();
        builder.Property(evento => evento.Tipo).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.HasOne<Entrega>().WithMany().HasForeignKey(evento => evento.EntregaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Organizacao>().WithMany().HasForeignKey(evento => evento.OrganizacaoId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Motorista>().WithMany().HasForeignKey(evento => evento.MotoristaId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(evento => new { evento.EntregaId, evento.OcorridoEm });
    }
}
