using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TorreLogistica.Infrastructure.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class ExecucaoDaEntrega : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "concluida_em",
                table: "rotas",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "iniciada_em",
                table: "rotas",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "chegada_registrada_em",
                table: "entregas",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "entregue_em",
                table: "entregas",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "motivo_da_ultima_tentativa",
                table: "entregas",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "motorista_id",
                table: "entregas",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "saiu_para_rota_em",
                table: "entregas",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "tentativas_frustradas",
                table: "entregas",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ultima_tentativa_frustrada_em",
                table: "entregas",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_entregas_motorista_id",
                table: "entregas",
                column: "motorista_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_entregas_tentativas_frustradas_nao_negativas",
                table: "entregas",
                sql: "tentativas_frustradas >= 0");

            migrationBuilder.AddForeignKey(
                name: "fk_entregas_motoristas_motorista_id",
                table: "entregas",
                column: "motorista_id",
                principalTable: "motoristas",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_entregas_motoristas_motorista_id",
                table: "entregas");

            migrationBuilder.DropIndex(
                name: "ix_entregas_motorista_id",
                table: "entregas");

            migrationBuilder.DropCheckConstraint(
                name: "ck_entregas_tentativas_frustradas_nao_negativas",
                table: "entregas");

            migrationBuilder.DropColumn(
                name: "concluida_em",
                table: "rotas");

            migrationBuilder.DropColumn(
                name: "iniciada_em",
                table: "rotas");

            migrationBuilder.DropColumn(
                name: "chegada_registrada_em",
                table: "entregas");

            migrationBuilder.DropColumn(
                name: "entregue_em",
                table: "entregas");

            migrationBuilder.DropColumn(
                name: "motivo_da_ultima_tentativa",
                table: "entregas");

            migrationBuilder.DropColumn(
                name: "motorista_id",
                table: "entregas");

            migrationBuilder.DropColumn(
                name: "saiu_para_rota_em",
                table: "entregas");

            migrationBuilder.DropColumn(
                name: "tentativas_frustradas",
                table: "entregas");

            migrationBuilder.DropColumn(
                name: "ultima_tentativa_frustrada_em",
                table: "entregas");
        }
    }
}
