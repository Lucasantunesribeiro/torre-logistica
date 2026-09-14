using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TorreLogistica.Infrastructure.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class Rotas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "rotas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organizacao_id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    data = table.Column<DateOnly>(type: "date", nullable: false),
                    hub_id = table.Column<Guid>(type: "uuid", nullable: true),
                    motorista_id = table.Column<Guid>(type: "uuid", nullable: true),
                    veiculo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    saida_planejada = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    versao_da_ordem = table.Column<int>(type: "integer", nullable: false),
                    criada_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    atualizada_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    planejada_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    cancelada_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    ultima_sequencia_de_evento = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rotas", x => x.id);
                    table.CheckConstraint("ck_rotas_ultima_sequencia_de_evento_positiva", "ultima_sequencia_de_evento > 0");
                    table.CheckConstraint("ck_rotas_versao_da_ordem_nao_negativa", "versao_da_ordem >= 0");
                    table.ForeignKey(
                        name: "fk_rotas_hubs_hub_id",
                        column: x => x.hub_id,
                        principalTable: "hubs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_rotas_motoristas_motorista_id",
                        column: x => x.motorista_id,
                        principalTable: "motoristas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_rotas_organizacoes_organizacao_id",
                        column: x => x.organizacao_id,
                        principalTable: "organizacoes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_rotas_veiculos_veiculo_id",
                        column: x => x.veiculo_id,
                        principalTable: "veiculos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "eventos_da_rota",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organizacao_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rota_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequencia = table.Column<int>(type: "integer", nullable: false),
                    tipo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    status_resultante = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    autor_usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    dados = table.Column<string>(type: "jsonb", nullable: false),
                    ocorrido_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_eventos_da_rota", x => x.id);
                    table.CheckConstraint("ck_eventos_da_rota_sequencia_positiva", "sequencia > 0");
                    table.ForeignKey(
                        name: "fk_eventos_da_rota_organizacoes_organizacao_id",
                        column: x => x.organizacao_id,
                        principalTable: "organizacoes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_eventos_da_rota_rotas_rota_id",
                        column: x => x.rota_id,
                        principalTable: "rotas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_eventos_da_rota_usuarios_autor_usuario_id",
                        column: x => x.autor_usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "paradas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organizacao_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rota_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entrega_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequencia = table.Column<int>(type: "integer", nullable: false),
                    ativa = table.Column<bool>(type: "boolean", nullable: false),
                    adicionada_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    removida_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    motivo_da_remocao = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_paradas", x => x.id);
                    table.CheckConstraint("ck_paradas_sequencia_positiva", "sequencia > 0");
                    table.ForeignKey(
                        name: "fk_paradas_entregas_entrega_id",
                        column: x => x.entrega_id,
                        principalTable: "entregas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_paradas_organizacoes_organizacao_id",
                        column: x => x.organizacao_id,
                        principalTable: "organizacoes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_paradas_rotas_rota_id",
                        column: x => x.rota_id,
                        principalTable: "rotas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_eventos_da_rota_autor_usuario_id",
                table: "eventos_da_rota",
                column: "autor_usuario_id");

            migrationBuilder.CreateIndex(
                name: "ix_eventos_da_rota_organizacao_id",
                table: "eventos_da_rota",
                column: "organizacao_id");

            migrationBuilder.CreateIndex(
                name: "ux_eventos_da_rota_rota_id_sequencia",
                table: "eventos_da_rota",
                columns: new[] { "rota_id", "sequencia" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_paradas_organizacao_id",
                table: "paradas",
                column: "organizacao_id");

            migrationBuilder.CreateIndex(
                name: "ix_paradas_rota_id_ativa_sequencia",
                table: "paradas",
                columns: new[] { "rota_id", "ativa", "sequencia" });

            migrationBuilder.CreateIndex(
                name: "ux_paradas_entrega_id_ativa",
                table: "paradas",
                column: "entrega_id",
                unique: true,
                filter: "ativa");

            migrationBuilder.CreateIndex(
                name: "ix_rotas_hub_id",
                table: "rotas",
                column: "hub_id");

            migrationBuilder.CreateIndex(
                name: "ix_rotas_motorista_id",
                table: "rotas",
                column: "motorista_id");

            migrationBuilder.CreateIndex(
                name: "ix_rotas_organizacao_id_data_status",
                table: "rotas",
                columns: new[] { "organizacao_id", "data", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_rotas_veiculo_id",
                table: "rotas",
                column: "veiculo_id");

            migrationBuilder.CreateIndex(
                name: "ux_rotas_motorista_por_data_ativa",
                table: "rotas",
                columns: new[] { "organizacao_id", "motorista_id", "data" },
                unique: true,
                filter: "motorista_id IS NOT NULL AND status IN ('EmMontagem', 'Planejada', 'EmAndamento')");

            migrationBuilder.CreateIndex(
                name: "ux_rotas_organizacao_id_codigo",
                table: "rotas",
                columns: new[] { "organizacao_id", "codigo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_rotas_veiculo_por_data_ativa",
                table: "rotas",
                columns: new[] { "organizacao_id", "veiculo_id", "data" },
                unique: true,
                filter: "veiculo_id IS NOT NULL AND status IN ('EmMontagem', 'Planejada', 'EmAndamento')");

            // Timeline da rota somente-inserção, garantida pelo banco — mesmo desenho da
            // timeline da entrega (ADR 0012).
            migrationBuilder.Sql("""
                CREATE FUNCTION impedir_alteracao_da_timeline_da_rota() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'A timeline da rota é somente-inserção.'
                        USING ERRCODE = 'insufficient_privilege';
                END;
                $$;

                CREATE TRIGGER trg_eventos_da_rota_sem_update_ou_delete
                    BEFORE UPDATE OR DELETE ON eventos_da_rota
                    FOR EACH ROW EXECUTE FUNCTION impedir_alteracao_da_timeline_da_rota();

                CREATE TRIGGER trg_eventos_da_rota_sem_truncate
                    BEFORE TRUNCATE ON eventos_da_rota
                    FOR EACH STATEMENT EXECUTE FUNCTION impedir_alteracao_da_timeline_da_rota();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_eventos_da_rota_sem_truncate ON eventos_da_rota;
                DROP TRIGGER IF EXISTS trg_eventos_da_rota_sem_update_ou_delete ON eventos_da_rota;
                DROP FUNCTION IF EXISTS impedir_alteracao_da_timeline_da_rota();
                """);

            migrationBuilder.DropTable(
                name: "eventos_da_rota");

            migrationBuilder.DropTable(
                name: "paradas");

            migrationBuilder.DropTable(
                name: "rotas");
        }
    }
}
