using FluentMigrator;

namespace CS2_Admin.Database.Migrations;

[Migration(2026030301)]
public class AddPlayerIpsTable : Migration
{
    public override void Up()
    {
        if (Schema.Table("admin_player_ips").Exists())
        {
            return;
        }

        Create.Table("admin_player_ips")
            .WithColumn("id").AsInt64().PrimaryKey().Identity().NotNullable()
            .WithColumn("steamid").AsInt64().NotNullable().Unique()
            .WithColumn("player_name").AsString(64).NotNullable().WithDefaultValue("")
            .WithColumn("ip_address").AsString(64).NotNullable()
            .WithColumn("last_seen_at").AsDateTime().NotNullable().WithDefaultValue(SystemMethods.CurrentDateTime);

        Create.Index("idx_admin_player_ips_ip").OnTable("admin_player_ips").OnColumn("ip_address");
        Create.Index("idx_admin_player_ips_last_seen").OnTable("admin_player_ips").OnColumn("last_seen_at");
    }

    public override void Down()
    {
        Delete.Table("admin_player_ips");
    }
}
