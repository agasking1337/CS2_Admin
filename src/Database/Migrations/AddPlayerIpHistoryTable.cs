using FluentMigrator;

namespace CS2_Admin.Database.Migrations;

[Migration(2026030401)]
public class AddPlayerIpHistoryTable : Migration
{
    public override void Up()
    {
        if (Schema.Table("admin_player_ip_history").Exists())
        {
            return;
        }

        Create.Table("admin_player_ip_history")
            .WithColumn("id").AsInt64().PrimaryKey().Identity().NotNullable()
            .WithColumn("steamid").AsInt64().NotNullable()
            .WithColumn("player_name").AsString(64).NotNullable().WithDefaultValue("")
            .WithColumn("ip_address").AsString(64).NotNullable()
            .WithColumn("first_seen_at").AsDateTime().NotNullable().WithDefaultValue(SystemMethods.CurrentDateTime)
            .WithColumn("last_seen_at").AsDateTime().NotNullable().WithDefaultValue(SystemMethods.CurrentDateTime);

        Create.Index("idx_admin_player_ip_history_steamid")
            .OnTable("admin_player_ip_history")
            .OnColumn("steamid");

        Create.Index("idx_admin_player_ip_history_ip")
            .OnTable("admin_player_ip_history")
            .OnColumn("ip_address");

        Create.Index("idx_admin_player_ip_history_steamid_ip")
            .OnTable("admin_player_ip_history")
            .OnColumn("steamid").Ascending()
            .OnColumn("ip_address").Ascending();

        Create.Index("idx_admin_player_ip_history_last_seen")
            .OnTable("admin_player_ip_history")
            .OnColumn("last_seen_at");
    }

    public override void Down()
    {
        Delete.Table("admin_player_ip_history");
    }
}
