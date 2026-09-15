using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TorreLogistica.Infrastructure.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class AlertasOperacionais : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "alertas_operacionais",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organizacao_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    severidade = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    chave = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    entrega_id = table.Column<Guid>(type: "uuid", nullable: true),
                    motorista_id = table.Column<Guid>(type: "uuid", nullable: true),
                    rota_id = table.Column<Guid>(type: "uuid", nullable: true),
                    estado = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    condicao_ativa = table.Column<bool>(type: "boolean", nullable: false),
                    evidencia_de_abertura = table.Column<string>(type: "jsonb", nullable: false),
                    ultima_evidencia = table.Column<string>(type: "jsonb", nullable: false),
                    aberto_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    ultima_constatacao_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    resolvido_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    forma_de_resolucao = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    resolvido_por_usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    observacao_da_resolucao = table.Column<string>(type: "character varying(280)", maxLength: 280, nullable: true),
                    reaberturas = table.Column<int>(type: "integer", nullable: false),
                    ultima_sequencia_de_evento = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_alertas_operacionais", x => x.id);
                    table.CheckConstraint("ck_alertas_operacionais_alvo", "entrega_id IS NOT NULL OR (motorista_id IS NOT NULL AND rota_id IS NOT NULL)");
                    table.CheckConstraint("ck_alertas_operacionais_reaberturas_nao_negativas", "reaberturas >= 0");
                    table.CheckConstraint("ck_alertas_operacionais_resolucao_coerente", "(estado = 'Aberto' AND resolvido_em IS NULL AND forma_de_resolucao IS NULL) OR (estado = 'Resolvido' AND resolvido_em IS NOT NULL AND forma_de_resolucao IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_alertas_operacionais_entregas_entrega_id",
                        column: x => x.entrega_id,
                        principalTable: "entregas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_alertas_operacionais_motoristas_motorista_id",
                        column: x => x.motorista_id,
                        principalTable: "motoristas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_alertas_operacionais_organizacoes_organizacao_id",
                        column: x => x.organizacao_id,
                        principalTable: "organizacoes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_alertas_operacionais_rotas_rota_id",
                        column: x => x.rota_id,
                        principalTable: "rotas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_alertas_operacionais_usuarios_resolvido_por_usuario_id",
                        column: x => x.resolvido_por_usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "eventos_de_alerta",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organizacao_id = table.Column<Guid>(type: "uuid", nullable: false),
                    alerta_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequencia = table.Column<int>(type: "integer", nullable: false),
                    tipo = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    evidencia = table.Column<string>(type: "jsonb", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    observacao = table.Column<string>(type: "character varying(280)", maxLength: 280, nullable: true),
                    ocorrido_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_eventos_de_alerta", x => x.id);
                    table.CheckConstraint("ck_eventos_de_alerta_sequencia_positiva", "sequencia > 0");
                    table.ForeignKey(
                        name: "fk_eventos_de_alerta_alertas_operacionais_alerta_id",
                        column: x => x.alerta_id,
                        principalTable: "alertas_operacionais",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_eventos_de_alerta_organizacoes_organizacao_id",
                        column: x => x.organizacao_id,
                        principalTable: "organizacoes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_eventos_de_alerta_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_alertas_operacionais_entrega_id",
                table: "alertas_operacionais",
                column: "entrega_id");

            migrationBuilder.CreateIndex(
                name: "ix_alertas_operacionais_motorista_id",
                table: "alertas_operacionais",
                column: "motorista_id");

            migrationBuilder.CreateIndex(
                name: "ix_alertas_operacionais_organizacao_id_chave_aberto_em",
                table: "alertas_operacionais",
                columns: new[] { "organizacao_id", "chave", "aberto_em" });

            migrationBuilder.CreateIndex(
                name: "ix_alertas_operacionais_organizacao_id_estado_aberto_em",
                table: "alertas_operacionais",
                columns: new[] { "organizacao_id", "estado", "aberto_em" });

            migrationBuilder.CreateIndex(
                name: "ix_alertas_operacionais_resolvido_por_usuario_id",
                table: "alertas_operacionais",
                column: "resolvido_por_usuario_id");

            migrationBuilder.CreateIndex(
                name: "ix_alertas_operacionais_rota_id_aberto",
                table: "alertas_operacionais",
                column: "rota_id",
                filter: "estado = 'Aberto'");

            migrationBuilder.CreateIndex(
                name: "ux_alertas_operacionais_chave_aberto",
                table: "alertas_operacionais",
                columns: new[] { "organizacao_id", "chave" },
                unique: true,
                filter: "estado = 'Aberto'");

            migrationBuilder.CreateIndex(
                name: "ix_eventos_de_alerta_organizacao_id",
                table: "eventos_de_alerta",
                column: "organizacao_id");

            migrationBuilder.CreateIndex(
                name: "ix_eventos_de_alerta_usuario_id",
                table: "eventos_de_alerta",
                column: "usuario_id");

            migrationBuilder.CreateIndex(
                name: "ux_eventos_de_alerta_alerta_id_sequencia",
                table: "eventos_de_alerta",
                columns: new[] { "alerta_id", "sequencia" },
                unique: true);

            // O ciclo de vida explica quem abriu, resolveu e reabriu cada alerta: somente-inserção.
            migrationBuilder.Sql("""
                CREATE FUNCTION impedir_alteracao_de_evento_de_alerta() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'O ciclo de vida dos alertas é somente-inserção.'
                        USING ERRCODE = 'insufficient_privilege';
                END;
                $$;

                CREATE TRIGGER trg_eventos_de_alerta_sem_update_ou_delete
                    BEFORE UPDATE OR DELETE ON eventos_de_alerta
                    FOR EACH ROW EXECUTE FUNCTION impedir_alteracao_de_evento_de_alerta();

                CREATE TRIGGER trg_eventos_de_alerta_sem_truncate
                    BEFORE TRUNCATE ON eventos_de_alerta
                    FOR EACH STATEMENT EXECUTE FUNCTION impedir_alteracao_de_evento_de_alerta();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_eventos_de_alerta_sem_truncate ON eventos_de_alerta;
                DROP TRIGGER IF EXISTS trg_eventos_de_alerta_sem_update_ou_delete ON eventos_de_alerta;
                DROP FUNCTION IF EXISTS impedir_alteracao_de_evento_de_alerta();
                """);

            migrationBuilder.DropTable(
                name: "eventos_de_alerta");

            migrationBuilder.DropTable(
                name: "alertas_operacionais");
        }
    }
}
