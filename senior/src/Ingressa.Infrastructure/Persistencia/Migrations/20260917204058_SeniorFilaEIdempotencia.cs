using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Ingressa.Infrastructure.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class SeniorFilaEIdempotencia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "FilaVirtual",
                table: "Eventos",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "RequisicoesIdempotentes",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UsuarioId = table.Column<int>(type: "integer", nullable: false),
                    Chave = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Rota = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    HashDaRequisicao = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StatusHttp = table.Column<int>(type: "integer", nullable: true),
                    Resposta = table.Column<string>(type: "jsonb", nullable: true),
                    Local = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    CriadaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConcluidaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequisicoesIdempotentes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RequisicoesIdempotentes_CriadaEm",
                table: "RequisicoesIdempotentes",
                column: "CriadaEm");

            migrationBuilder.CreateIndex(
                name: "IX_RequisicoesIdempotentes_UsuarioId_Chave",
                table: "RequisicoesIdempotentes",
                columns: new[] { "UsuarioId", "Chave" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RequisicoesIdempotentes");

            migrationBuilder.DropColumn(
                name: "FilaVirtual",
                table: "Eventos");
        }
    }
}
