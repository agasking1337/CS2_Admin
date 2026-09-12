using FluentMigrator;

namespace CS2_Admin.Database.Migrations;

[Migration(2026091201)]
public class AddDiscordLinksTable : Migration
{
    public override void Up()
    {
        if (Schema.Table("admin_discord_links").Exists())
        {
            return;
        }

        Create.Table("admin_discord_links")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("steam_id").AsInt64().NotNullable()
            .WithColumn("discord_id").AsInt64().NotNullable()
            .WithColumn("discord_name").AsString(128).NotNullable().WithDefaultValue("")
            .WithColumn("server_id").AsString(128).NotNullable().WithDefaultValue("")
            .WithColumn("linked_at").AsDateTime().NotNullable().WithDefaultValue(SystemMethods.CurrentDateTime);

        Create.Index("idx_admin_discord_links_steam_id")
            .OnTable("admin_discord_links")
            .OnColumn("steam_id").Unique();

        Create.Index("idx_admin_discord_links_discord_id")
            .OnTable("admin_discord_links")
            .OnColumn("discord_id").Unique();
    }

    public override void Down()
    {
        Delete.Table("admin_discord_links");
    }
}
