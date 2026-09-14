using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace TorreLogistica.Infrastructure.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class EstruturaOperacional : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "clientes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organizacao_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    nome_normalizado = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    cnpj = table.Column<string>(type: "character(14)", fixedLength: true, maxLength: 14, nullable: true),
                    ativo = table.Column<bool>(type: "boolean", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_clientes", x => x.id);
                    table.ForeignKey(
                        name: "fk_clientes_organizacoes_organizacao_id",
                        column: x => x.organizacao_id,
                        principalTable: "organizacoes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "destinatarios",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organizacao_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    nome_normalizado = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    telefone = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    localizacao = table.Column<Point>(type: "geography (point, 4326)", nullable: true),
                    instrucoes_de_entrega = table.Column<string>(type: "character varying(280)", maxLength: 280, nullable: true),
                    ativo = table.Column<bool>(type: "boolean", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    endereco_bairro = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    endereco_cep = table.Column<string>(type: "character(8)", fixedLength: true, maxLength: 8, nullable: false),
                    endereco_cidade = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    endereco_complemento = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    endereco_logradouro = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    endereco_numero = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    endereco_uf = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_destinatarios", x => x.id);
                    table.ForeignKey(
                        name: "fk_destinatarios_organizacoes_organizacao_id",
                        column: x => x.organizacao_id,
                        principalTable: "organizacoes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "hubs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organizacao_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    nome_normalizado = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    localizacao = table.Column<Point>(type: "geography (point, 4326)", nullable: false),
                    ativo = table.Column<bool>(type: "boolean", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    endereco_bairro = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    endereco_cep = table.Column<string>(type: "character(8)", fixedLength: true, maxLength: 8, nullable: false),
                    endereco_cidade = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    endereco_complemento = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    endereco_logradouro = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    endereco_numero = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    endereco_uf = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hubs", x => x.id);
                    table.ForeignKey(
                        name: "fk_hubs_organizacoes_organizacao_id",
                        column: x => x.organizacao_id,
                        principalTable: "organizacoes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "motoristas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organizacao_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    nome_normalizado = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    telefone = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ativo = table.Column<bool>(type: "boolean", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_motoristas", x => x.id);
                    table.ForeignKey(
                        name: "fk_motoristas_organizacoes_organizacao_id",
                        column: x => x.organizacao_id,
                        principalTable: "organizacoes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_motoristas_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "veiculos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organizacao_id = table.Column<Guid>(type: "uuid", nullable: false),
                    placa = table.Column<string>(type: "character(7)", fixedLength: true, maxLength: 7, nullable: false),
                    identificacao = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    identificacao_normalizada = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    tipo = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    capacidade_em_kg = table.Column<int>(type: "integer", nullable: true),
                    ativo = table.Column<bool>(type: "boolean", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_veiculos", x => x.id);
                    table.CheckConstraint("ck_veiculos_capacidade_em_kg_positiva", "capacidade_em_kg IS NULL OR capacidade_em_kg > 0");
                    table.ForeignKey(
                        name: "fk_veiculos_organizacoes_organizacao_id",
                        column: x => x.organizacao_id,
                        principalTable: "organizacoes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_clientes_organizacao_id_nome_normalizado",
                table: "clientes",
                columns: new[] { "organizacao_id", "nome_normalizado" });

            migrationBuilder.CreateIndex(
                name: "ux_clientes_organizacao_id_cnpj",
                table: "clientes",
                columns: new[] { "organizacao_id", "cnpj" },
                unique: true,
                filter: "cnpj IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_destinatarios_localizacao",
                table: "destinatarios",
                column: "localizacao")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_destinatarios_organizacao_id_nome_normalizado",
                table: "destinatarios",
                columns: new[] { "organizacao_id", "nome_normalizado" });

            migrationBuilder.CreateIndex(
                name: "ix_hubs_localizacao",
                table: "hubs",
                column: "localizacao")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ux_hubs_organizacao_id_nome_normalizado",
                table: "hubs",
                columns: new[] { "organizacao_id", "nome_normalizado" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_motoristas_organizacao_id_nome_normalizado",
                table: "motoristas",
                columns: new[] { "organizacao_id", "nome_normalizado" });

            migrationBuilder.CreateIndex(
                name: "ux_motoristas_usuario_id",
                table: "motoristas",
                column: "usuario_id",
                unique: true,
                filter: "usuario_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_veiculos_organizacao_id_placa",
                table: "veiculos",
                columns: new[] { "organizacao_id", "placa" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "clientes");

            migrationBuilder.DropTable(
                name: "destinatarios");

            migrationBuilder.DropTable(
                name: "hubs");

            migrationBuilder.DropTable(
                name: "motoristas");

            migrationBuilder.DropTable(
                name: "veiculos");
        }
    }
}
