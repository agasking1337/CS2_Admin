using FluentMigrator;

namespace CS2_Admin.Database.Migrations;

[Migration(2026030501)]
public class AddPlayerSessionsTable : Migration
{
    public override void Up()
    {
        if (Schema.Table("admin_player_sessions").Exists())
        {
            return;
        }

        Create.Table("admin_player_sessions")
            .WithColumn("id").AsInt64().PrimaryKey().Identity().NotNullable()
            .WithColumn("steamid").AsInt64().NotNullable()
            .WithColumn("player_name").AsString(64).NotNullable().WithDefaultValue("")
            .WithColumn("ip_address").AsString(64).NotNullable().WithDefaultValue("")
            .WithColumn("connected_at").AsDateTime().NotNullable().WithDefaultValue(SystemMethods.CurrentDateTime)
            .WithColumn("disconnected_at").AsDateTime().Nullable();

        Create.Index("idx_admin_player_sessions_steamid")
            .OnTable("admin_player_sessions")
            .OnColumn("steamid");

        Create.Index("idx_admin_player_sessions_disconnected_at")
            .OnTable("admin_player_sessions")
            .OnColumn("disconnected_at");
    }

    public override void Down()
    {
        Delete.Table("admin_player_sessions");
    }
}
