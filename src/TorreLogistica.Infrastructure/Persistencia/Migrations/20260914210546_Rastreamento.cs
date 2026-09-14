using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace TorreLogistica.Infrastructure.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class Rastreamento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "posicoes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organizacao_id = table.Column<Guid>(type: "uuid", nullable: false),
                    motorista_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rota_id = table.Column<Guid>(type: "uuid", nullable: true),
                    evento_de_localizacao_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequencia = table.Column<long>(type: "bigint", nullable: false),
                    localizacao = table.Column<Point>(type: "geography (point, 4326)", nullable: false),
                    precisao_em_metros = table.Column<double>(type: "double precision", nullable: false),
                    velocidade_em_metros_por_segundo = table.Column<double>(type: "double precision", nullable: true),
                    direcao_em_graus = table.Column<double>(type: "double precision", nullable: true),
                    capturada_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    recebida_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    qualidade = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_posicoes", x => x.id);
                    table.CheckConstraint("ck_posicoes_precisao_positiva", "precisao_em_metros > 0");
                    table.CheckConstraint("ck_posicoes_sequencia_nao_negativa", "sequencia >= 0");
                    table.ForeignKey(
                        name: "fk_posicoes_motoristas_motorista_id",
                        column: x => x.motorista_id,
                        principalTable: "motoristas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_posicoes_organizacoes_organizacao_id",
                        column: x => x.organizacao_id,
                        principalTable: "organizacoes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_posicoes_rotas_rota_id",
                        column: x => x.rota_id,
                        principalTable: "rotas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "posicoes_atuais",
                columns: table => new
                {
                    motorista_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organizacao_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rota_id = table.Column<Guid>(type: "uuid", nullable: true),
                    evento_de_localizacao_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequencia = table.Column<long>(type: "bigint", nullable: false),
                    localizacao = table.Column<Point>(type: "geography (point, 4326)", nullable: false),
                    precisao_em_metros = table.Column<double>(type: "double precision", nullable: false),
                    velocidade_em_metros_por_segundo = table.Column<double>(type: "double precision", nullable: true),
                    direcao_em_graus = table.Column<double>(type: "double precision", nullable: true),
                    capturada_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    recebida_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    atualizada_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_posicoes_atuais", x => x.motorista_id);
                    table.CheckConstraint("ck_posicoes_atuais_precisao_positiva", "precisao_em_metros > 0");
                    table.ForeignKey(
                        name: "fk_posicoes_atuais_motoristas_motorista_id",
                        column: x => x.motorista_id,
                        principalTable: "motoristas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_posicoes_atuais_organizacoes_organizacao_id",
                        column: x => x.organizacao_id,
                        principalTable: "organizacoes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_posicoes_atuais_rotas_rota_id",
                        column: x => x.rota_id,
                        principalTable: "rotas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_posicoes_motorista_id_capturada_em",
                table: "posicoes",
                columns: new[] { "motorista_id", "capturada_em" });

            migrationBuilder.CreateIndex(
                name: "ix_posicoes_rota_id",
                table: "posicoes",
                column: "rota_id");

            migrationBuilder.CreateIndex(
                name: "ux_posicoes_evento_de_localizacao",
                table: "posicoes",
                columns: new[] { "organizacao_id", "motorista_id", "evento_de_localizacao_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_posicoes_atuais_localizacao",
                table: "posicoes_atuais",
                column: "localizacao")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_posicoes_atuais_organizacao_id",
                table: "posicoes_atuais",
                column: "organizacao_id");

            migrationBuilder.CreateIndex(
                name: "ix_posicoes_atuais_rota_id",
                table: "posicoes_atuais",
                column: "rota_id");

            // O que foi capturado não é reescrito: UPDATE falha no histórico de posições. DELETE segue
            // permitido de propósito — a limpeza por retenção (CLAUDE.md, seção 22) precisa dele.
            migrationBuilder.Sql("""
                CREATE FUNCTION impedir_alteracao_de_posicao() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'O histórico de posições não aceita alteração.'
                        USING ERRCODE = 'insufficient_privilege';
                END;
                $$;

                CREATE TRIGGER trg_posicoes_sem_update
                    BEFORE UPDATE ON posicoes
                    FOR EACH ROW EXECUTE FUNCTION impedir_alteracao_de_posicao();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_posicoes_sem_update ON posicoes;
                DROP FUNCTION IF EXISTS impedir_alteracao_de_posicao();
                """);

            migrationBuilder.DropTable(
                name: "posicoes");

            migrationBuilder.DropTable(
                name: "posicoes_atuais");
        }
    }
}
