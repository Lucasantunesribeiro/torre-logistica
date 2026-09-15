using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TorreLogistica.Infrastructure.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class PrevisaoDeChegada : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "previsoes_da_entrega",
                columns: table => new
                {
                    entrega_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organizacao_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rota_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ativa = table.Column<bool>(type: "boolean", nullable: false),
                    situacao = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    motivo_da_situacao = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    chegada_prevista_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    motivo_sem_chegada_prevista = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ja_no_destino = table.Column<bool>(type: "boolean", nullable: false),
                    folga_em_segundos = table.Column<int>(type: "integer", nullable: true),
                    deslocamento_em_segundos = table.Column<int>(type: "integer", nullable: false),
                    distancia_em_metros = table.Column<double>(type: "double precision", nullable: false),
                    paradas_antes = table.Column<int>(type: "integer", nullable: false),
                    tempo_das_paradas_antes_em_segundos = table.Column<int>(type: "integer", nullable: false),
                    tempo_por_parada_em_segundos = table.Column<int>(type: "integer", nullable: false),
                    fonte = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    provedor = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    motivo_da_contingencia = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    janela_inicio = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    janela_fim = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    limiar_de_atencao_em_segundos = table.Column<int>(type: "integer", nullable: false),
                    limiar_de_risco_em_segundos = table.Column<int>(type: "integer", nullable: false),
                    posicao_capturada_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    calculada_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    ultima_sequencia_de_registro = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_previsoes_da_entrega", x => x.entrega_id);
                    table.CheckConstraint("ck_previsoes_da_entrega_composicao_nao_negativa", "deslocamento_em_segundos >= 0 AND distancia_em_metros >= 0 AND paradas_antes >= 0 AND tempo_das_paradas_antes_em_segundos >= 0 AND tempo_por_parada_em_segundos >= 0");
                    table.CheckConstraint("ck_previsoes_da_entrega_janela", "janela_fim > janela_inicio");
                    table.CheckConstraint("ck_previsoes_da_entrega_limiares", "limiar_de_risco_em_segundos >= 0 AND limiar_de_atencao_em_segundos > limiar_de_risco_em_segundos");
                    table.ForeignKey(
                        name: "fk_previsoes_da_entrega_entregas_entrega_id",
                        column: x => x.entrega_id,
                        principalTable: "entregas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_previsoes_da_entrega_organizacoes_organizacao_id",
                        column: x => x.organizacao_id,
                        principalTable: "organizacoes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_previsoes_da_entrega_rotas_rota_id",
                        column: x => x.rota_id,
                        principalTable: "rotas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "registros_de_previsao",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organizacao_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entrega_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rota_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequencia = table.Column<int>(type: "integer", nullable: false),
                    tipo = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    situacao_anterior = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    situacao = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    motivo_da_situacao = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    chegada_prevista_anterior_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    chegada_prevista_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    motivo_sem_chegada_prevista = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ja_no_destino = table.Column<bool>(type: "boolean", nullable: false),
                    folga_em_segundos = table.Column<int>(type: "integer", nullable: true),
                    deslocamento_em_segundos = table.Column<int>(type: "integer", nullable: false),
                    distancia_em_metros = table.Column<double>(type: "double precision", nullable: false),
                    paradas_antes = table.Column<int>(type: "integer", nullable: false),
                    tempo_das_paradas_antes_em_segundos = table.Column<int>(type: "integer", nullable: false),
                    tempo_por_parada_em_segundos = table.Column<int>(type: "integer", nullable: false),
                    fonte = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    provedor = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    motivo_da_contingencia = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    janela_inicio = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    janela_fim = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    limiar_de_atencao_em_segundos = table.Column<int>(type: "integer", nullable: false),
                    limiar_de_risco_em_segundos = table.Column<int>(type: "integer", nullable: false),
                    posicao_capturada_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    status_da_entrega = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    calculada_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    registrado_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_registros_de_previsao", x => x.id);
                    table.CheckConstraint("ck_registros_de_previsao_janela", "janela_fim > janela_inicio");
                    table.CheckConstraint("ck_registros_de_previsao_sequencia_positiva", "sequencia > 0");
                    table.ForeignKey(
                        name: "fk_registros_de_previsao_entregas_entrega_id",
                        column: x => x.entrega_id,
                        principalTable: "entregas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_registros_de_previsao_organizacoes_organizacao_id",
                        column: x => x.organizacao_id,
                        principalTable: "organizacoes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_registros_de_previsao_rotas_rota_id",
                        column: x => x.rota_id,
                        principalTable: "rotas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_previsoes_da_entrega_organizacao_id",
                table: "previsoes_da_entrega",
                column: "organizacao_id");

            migrationBuilder.CreateIndex(
                name: "ix_previsoes_da_entrega_rota_id_ativa",
                table: "previsoes_da_entrega",
                column: "rota_id",
                filter: "ativa");

            migrationBuilder.CreateIndex(
                name: "ix_registros_de_previsao_organizacao_id",
                table: "registros_de_previsao",
                column: "organizacao_id");

            migrationBuilder.CreateIndex(
                name: "ix_registros_de_previsao_rota_id",
                table: "registros_de_previsao",
                column: "rota_id");

            migrationBuilder.CreateIndex(
                name: "ux_registros_de_previsao_entrega_id_sequencia",
                table: "registros_de_previsao",
                columns: new[] { "entrega_id", "sequencia" },
                unique: true);

            // O histórico explica por que a entrega entrou em risco: somente-inserção, garantido pelo banco.
            migrationBuilder.Sql("""
                CREATE FUNCTION impedir_alteracao_de_registro_de_previsao() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'O histórico de previsões é somente-inserção.'
                        USING ERRCODE = 'insufficient_privilege';
                END;
                $$;

                CREATE TRIGGER trg_registros_de_previsao_sem_update_ou_delete
                    BEFORE UPDATE OR DELETE ON registros_de_previsao
                    FOR EACH ROW EXECUTE FUNCTION impedir_alteracao_de_registro_de_previsao();

                CREATE TRIGGER trg_registros_de_previsao_sem_truncate
                    BEFORE TRUNCATE ON registros_de_previsao
                    FOR EACH STATEMENT EXECUTE FUNCTION impedir_alteracao_de_registro_de_previsao();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_registros_de_previsao_sem_truncate ON registros_de_previsao;
                DROP TRIGGER IF EXISTS trg_registros_de_previsao_sem_update_ou_delete ON registros_de_previsao;
                DROP FUNCTION IF EXISTS impedir_alteracao_de_registro_de_previsao();
                """);

            migrationBuilder.DropTable(
                name: "previsoes_da_entrega");

            migrationBuilder.DropTable(
                name: "registros_de_previsao");
        }
    }
}
