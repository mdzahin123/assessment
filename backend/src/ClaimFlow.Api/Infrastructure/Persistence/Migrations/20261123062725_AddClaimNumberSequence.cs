using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClaimFlow.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddClaimNumberSequence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "ClaimNumbers",
                startValue: 1000L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropSequence(
                name: "ClaimNumbers");
        }
    }
}
