using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aqr.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "sync");

            migrationBuilder.EnsureSchema(
                name: "zone");

            migrationBuilder.EnsureSchema(
                name: "access");

            migrationBuilder.EnsureSchema(
                name: "quota");

            migrationBuilder.EnsureSchema(
                name: "dim");

            migrationBuilder.CreateTable(
                name: "Coverage",
                schema: "sync",
                columns: table => new
                {
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Stage = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SubscriptionId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Error = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Coverage", x => new { x.RunId, x.Stage, x.SubscriptionId });
                });

            migrationBuilder.CreateTable(
                name: "FamilyZoneAccess",
                schema: "zone",
                columns: table => new
                {
                    SubscriptionId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Region = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    FamilyId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ZoneStatus = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    SkusTotal = table.Column<int>(type: "int", nullable: false),
                    SkusRegionOpen = table.Column<int>(type: "int", nullable: false),
                    OfferedZonesLogical = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    OpenZonesLogical = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    PartialZonesLogical = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    RestrictedZonesLogical = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    OfferedZonesPhysical = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    OpenZonesPhysical = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    PartialZonesPhysical = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    RestrictedZonesPhysical = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ReasonCodes = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ValidFrom = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodStartColumn", true),
                    ValidTo = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodEndColumn", true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FamilyZoneAccess", x => new { x.SubscriptionId, x.Region, x.FamilyId });
                })
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "FamilyZoneAccessHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", "zone")
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "ValidTo")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "ValidFrom");

            migrationBuilder.CreateTable(
                name: "Grant",
                schema: "access",
                columns: table => new
                {
                    GrantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PrincipalObjectId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PrincipalType = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    PrincipalDisplayName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    TargetType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    TargetId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    ChangedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ValidFrom = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodStartColumn", true),
                    ValidTo = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodEndColumn", true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Grant", x => x.GrantId);
                })
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "GrantHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", "access")
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "ValidTo")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "ValidFrom");

            migrationBuilder.CreateTable(
                name: "GroupAllocation",
                schema: "quota",
                columns: table => new
                {
                    ManagementGroupId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    GroupName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    SubscriptionId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Region = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    FamilyId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    QuotaAllocated = table.Column<long>(type: "bigint", nullable: false),
                    ValidFrom = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodStartColumn", true),
                    ValidTo = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodEndColumn", true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupAllocation", x => new { x.ManagementGroupId, x.GroupName, x.SubscriptionId, x.Region, x.FamilyId });
                })
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "GroupAllocationHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", "quota")
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "ValidTo")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "ValidFrom");

            migrationBuilder.CreateTable(
                name: "GroupQuota",
                schema: "quota",
                columns: table => new
                {
                    ManagementGroupId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    GroupName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Region = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    FamilyId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    GroupLimit = table.Column<long>(type: "bigint", nullable: false),
                    AvailableLimit = table.Column<long>(type: "bigint", nullable: false),
                    AllocatedTotal = table.Column<long>(type: "bigint", nullable: false),
                    GroupUsage = table.Column<long>(type: "bigint", nullable: true),
                    ValidFrom = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodStartColumn", true),
                    ValidTo = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodEndColumn", true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupQuota", x => new { x.ManagementGroupId, x.GroupName, x.Region, x.FamilyId });
                })
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "GroupQuotaHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", "quota")
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "ValidTo")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "ValidFrom");

            migrationBuilder.CreateTable(
                name: "QuotaGroup",
                schema: "quota",
                columns: table => new
                {
                    ManagementGroupId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    GroupName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ValidFrom = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodStartColumn", true),
                    ValidTo = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodEndColumn", true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuotaGroup", x => new { x.ManagementGroupId, x.GroupName });
                })
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "QuotaGroupHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", "quota")
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "ValidTo")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "ValidFrom");

            migrationBuilder.CreateTable(
                name: "QuotaGroupMember",
                schema: "quota",
                columns: table => new
                {
                    ManagementGroupId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    GroupName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    SubscriptionId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ValidFrom = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodStartColumn", true),
                    ValidTo = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodEndColumn", true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuotaGroupMember", x => new { x.ManagementGroupId, x.GroupName, x.SubscriptionId });
                })
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "QuotaGroupMemberHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", "quota")
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "ValidTo")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "ValidFrom");

            migrationBuilder.CreateTable(
                name: "Region",
                schema: "dim",
                columns: table => new
                {
                    Name = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Geography = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    HasZones = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Region", x => x.Name);
                });

            migrationBuilder.CreateTable(
                name: "Run",
                schema: "sync",
                columns: table => new
                {
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Stage = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Trigger = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    StartedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    SubsExpected = table.Column<int>(type: "int", nullable: false),
                    SubsCovered = table.Column<int>(type: "int", nullable: false),
                    KeysSeen = table.Column<int>(type: "int", nullable: false),
                    Inserted = table.Column<int>(type: "int", nullable: false),
                    Updated = table.Column<int>(type: "int", nullable: false),
                    Deleted = table.Column<int>(type: "int", nullable: false),
                    Throttled = table.Column<int>(type: "int", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Run", x => new { x.RunId, x.Stage });
                });

            migrationBuilder.CreateTable(
                name: "SkuRestriction",
                schema: "zone",
                columns: table => new
                {
                    SubscriptionId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Region = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Sku = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    FamilyId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RestrictionType = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ZonesLogical = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ZonesPhysical = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ReasonCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ValidFrom = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodStartColumn", true),
                    ValidTo = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodEndColumn", true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SkuRestriction", x => new { x.SubscriptionId, x.Region, x.Sku });
                })
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "SkuRestrictionHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", "zone")
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "ValidTo")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "ValidFrom");

            migrationBuilder.CreateTable(
                name: "Subscription",
                schema: "dim",
                columns: table => new
                {
                    SubscriptionId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ManagementGroupPath = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    State = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ValidFrom = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodStartColumn", true),
                    ValidTo = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodEndColumn", true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subscription", x => x.SubscriptionId);
                })
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "SubscriptionHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", "dim")
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "ValidTo")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "ValidFrom");

            migrationBuilder.CreateTable(
                name: "SubscriptionQuota",
                schema: "quota",
                columns: table => new
                {
                    SubscriptionId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Region = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    QuotaName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    FamilyId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    LocalizedName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Usage = table.Column<long>(type: "bigint", nullable: false),
                    Limit = table.Column<long>(type: "bigint", nullable: false),
                    ValidFrom = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodStartColumn", true),
                    ValidTo = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodEndColumn", true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionQuota", x => new { x.SubscriptionId, x.Region, x.QuotaName });
                })
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "SubscriptionQuotaHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", "quota")
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "ValidTo")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "ValidFrom");

            migrationBuilder.CreateTable(
                name: "VmFamily",
                schema: "dim",
                columns: table => new
                {
                    FamilyId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    QuotaName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    LocalizedName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Series = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CpuManufacturer = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Architecture = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    AcceleratorType = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    AcceleratorVendor = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    AcceleratorModel = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Generation = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    Features = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Lifecycle = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    MinVcpu = table.Column<int>(type: "int", nullable: false),
                    MaxVcpu = table.Column<int>(type: "int", nullable: false),
                    SkuCount = table.Column<int>(type: "int", nullable: false),
                    Classification = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    SourceUrl = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    ValidFrom = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodStartColumn", true),
                    ValidTo = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodEndColumn", true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VmFamily", x => x.FamilyId);
                })
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "VmFamilyHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", "dim")
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "ValidTo")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "ValidFrom");

            migrationBuilder.CreateTable(
                name: "ZoneMapping",
                schema: "zone",
                columns: table => new
                {
                    SubscriptionId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Region = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    LogicalZone = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    PhysicalZone = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ValidFrom = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodStartColumn", true),
                    ValidTo = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodEndColumn", true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ZoneMapping", x => new { x.SubscriptionId, x.Region, x.LogicalZone });
                })
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "ZoneMappingHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", "zone")
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "ValidTo")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "ValidFrom");

            migrationBuilder.CreateIndex(
                name: "IX_Grant_PrincipalObjectId_TargetType_TargetId",
                schema: "access",
                table: "Grant",
                columns: new[] { "PrincipalObjectId", "TargetType", "TargetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Run_StartedUtc",
                schema: "sync",
                table: "Run",
                column: "StartedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionQuota_FamilyId_Region",
                schema: "quota",
                table: "SubscriptionQuota",
                columns: new[] { "FamilyId", "Region" });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionQuota_Region_Kind",
                schema: "quota",
                table: "SubscriptionQuota",
                columns: new[] { "Region", "Kind" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Coverage",
                schema: "sync");

            migrationBuilder.DropTable(
                name: "FamilyZoneAccess",
                schema: "zone")
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "FamilyZoneAccessHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", "zone")
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "ValidTo")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "ValidFrom");

            migrationBuilder.DropTable(
                name: "Grant",
                schema: "access")
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "GrantHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", "access")
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "ValidTo")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "ValidFrom");

            migrationBuilder.DropTable(
                name: "GroupAllocation",
                schema: "quota")
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "GroupAllocationHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", "quota")
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "ValidTo")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "ValidFrom");

            migrationBuilder.DropTable(
                name: "GroupQuota",
                schema: "quota")
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "GroupQuotaHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", "quota")
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "ValidTo")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "ValidFrom");

            migrationBuilder.DropTable(
                name: "QuotaGroup",
                schema: "quota")
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "QuotaGroupHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", "quota")
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "ValidTo")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "ValidFrom");

            migrationBuilder.DropTable(
                name: "QuotaGroupMember",
                schema: "quota")
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "QuotaGroupMemberHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", "quota")
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "ValidTo")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "ValidFrom");

            migrationBuilder.DropTable(
                name: "Region",
                schema: "dim");

            migrationBuilder.DropTable(
                name: "Run",
                schema: "sync");

            migrationBuilder.DropTable(
                name: "SkuRestriction",
                schema: "zone")
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "SkuRestrictionHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", "zone")
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "ValidTo")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "ValidFrom");

            migrationBuilder.DropTable(
                name: "Subscription",
                schema: "dim")
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "SubscriptionHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", "dim")
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "ValidTo")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "ValidFrom");

            migrationBuilder.DropTable(
                name: "SubscriptionQuota",
                schema: "quota")
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "SubscriptionQuotaHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", "quota")
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "ValidTo")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "ValidFrom");

            migrationBuilder.DropTable(
                name: "VmFamily",
                schema: "dim")
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "VmFamilyHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", "dim")
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "ValidTo")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "ValidFrom");

            migrationBuilder.DropTable(
                name: "ZoneMapping",
                schema: "zone")
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "ZoneMappingHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", "zone")
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "ValidTo")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "ValidFrom");
        }
    }
}
