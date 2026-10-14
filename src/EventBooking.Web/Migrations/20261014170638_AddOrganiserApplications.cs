using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventBooking.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganiserApplications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrganiserApplications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    OrganisationName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ContactPhone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Website = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReviewedById = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RejectionReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganiserApplications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganiserApplications_AspNetUsers_ReviewedById",
                        column: x => x.ReviewedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrganiserApplications_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrganiserApplications_ReviewedById",
                table: "OrganiserApplications",
                column: "ReviewedById");

            migrationBuilder.CreateIndex(
                name: "IX_OrganiserApplications_Status_SubmittedAt",
                table: "OrganiserApplications",
                columns: new[] { "Status", "SubmittedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganiserApplications_UserId",
                table: "OrganiserApplications",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganiserApplications_UserId_Pending",
                table: "OrganiserApplications",
                column: "UserId",
                unique: true,
                filter: "[Status] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrganiserApplications");
        }
    }
}
