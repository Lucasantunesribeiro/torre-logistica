using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TorreLogistica.Infrastructure.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class Geofence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "estados_de_geofence",
                columns: table => new
                {
                    entrega_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organizacao_id = table.Column<Guid>(type: "uuid", nullable: false),
                    motorista_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dentro = table.Column<bool>(type: "boolean", nullable: false),
                    raio_em_metros = table.Column<double>(type: "double precision", nullable: false),
                    distancia_em_metros = table.Column<double>(type: "double precision", nullable: false),
                    ultima_captura_avaliada_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    ultima_sequencia_avaliada = table.Column<long>(type: "bigint", nullable: false),
                    entradas = table.Column<int>(type: "integer", nullable: false),
                    atualizada_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_estados_de_geofence", x => x.entrega_id);
                    table.CheckConstraint("ck_estados_de_geofence_distancia_nao_negativa", "distancia_em_metros >= 0");
                    table.CheckConstraint("ck_estados_de_geofence_entradas_nao_negativas", "entradas >= 0");
                    table.CheckConstraint("ck_estados_de_geofence_raio_positivo", "raio_em_metros > 0");
                    table.ForeignKey(
                        name: "fk_estados_de_geofence_entregas_entrega_id",
                        column: x => x.entrega_id,
                        principalTable: "entregas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_estados_de_geofence_motoristas_motorista_id",
                        column: x => x.motorista_id,
                        principalTable: "motoristas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_estados_de_geofence_organizacoes_organizacao_id",
                        column: x => x.organizacao_id,
                        principalTable: "organizacoes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "eventos_de_geofence",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organizacao_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entrega_id = table.Column<Guid>(type: "uuid", nullable: false),
                    motorista_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    distancia_em_metros = table.Column<double>(type: "double precision", nullable: false),
                    raio_em_metros = table.Column<double>(type: "double precision", nullable: false),
                    evento_de_localizacao_id = table.Column<Guid>(type: "uuid", nullable: false),
                    capturada_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    ocorrido_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_eventos_de_geofence", x => x.id);
                    table.ForeignKey(
                        name: "fk_eventos_de_geofence_entregas_entrega_id",
                        column: x => x.entrega_id,
                        principalTable: "entregas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_eventos_de_geofence_motoristas_motorista_id",
                        column: x => x.motorista_id,
                        principalTable: "motoristas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_eventos_de_geofence_organizacoes_organizacao_id",
                        column: x => x.organizacao_id,
                        principalTable: "organizacoes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_estados_de_geofence_motorista_id",
                table: "estados_de_geofence",
                column: "motorista_id");

            migrationBuilder.CreateIndex(
                name: "ix_estados_de_geofence_organizacao_id",
                table: "estados_de_geofence",
                column: "organizacao_id");

            migrationBuilder.CreateIndex(
                name: "ix_eventos_de_geofence_entrega_id_ocorrido_em",
                table: "eventos_de_geofence",
                columns: new[] { "entrega_id", "ocorrido_em" });

            migrationBuilder.CreateIndex(
                name: "ix_eventos_de_geofence_motorista_id",
                table: "eventos_de_geofence",
                column: "motorista_id");

            migrationBuilder.CreateIndex(
                name: "ix_eventos_de_geofence_organizacao_id",
                table: "eventos_de_geofence",
                column: "organizacao_id");

            // Entradas e saídas explicam alertas e transições: somente-inserção, garantido pelo banco.
            migrationBuilder.Sql("""
                CREATE FUNCTION impedir_alteracao_de_evento_de_geofence() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'Os eventos de geofence são somente-inserção.'
                        USING ERRCODE = 'insufficient_privilege';
                END;
                $$;

                CREATE TRIGGER trg_eventos_de_geofence_sem_update_ou_delete
                    BEFORE UPDATE OR DELETE ON eventos_de_geofence
                    FOR EACH ROW EXECUTE FUNCTION impedir_alteracao_de_evento_de_geofence();

                CREATE TRIGGER trg_eventos_de_geofence_sem_truncate
                    BEFORE TRUNCATE ON eventos_de_geofence
                    FOR EACH STATEMENT EXECUTE FUNCTION impedir_alteracao_de_evento_de_geofence();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_eventos_de_geofence_sem_truncate ON eventos_de_geofence;
                DROP TRIGGER IF EXISTS trg_eventos_de_geofence_sem_update_ou_delete ON eventos_de_geofence;
                DROP FUNCTION IF EXISTS impedir_alteracao_de_evento_de_geofence();
                """);

            migrationBuilder.DropTable(
                name: "estados_de_geofence");

            migrationBuilder.DropTable(
                name: "eventos_de_geofence");
        }
    }
}
