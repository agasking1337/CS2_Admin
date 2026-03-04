using FluentMigrator;

namespace CS2_Admin.Database.Migrations;

[Migration(2026030205)]
public class AddAdminPlaytimeTable : Migration
{
    public override void Up()
    {
        if (Schema.Table("admin_playtime").Exists())
        {
            return;
        }

        Create.Table("admin_playtime")
            .WithColumn("id").AsInt64().PrimaryKey().Identity().NotNullable()
            .WithColumn("steamid").AsInt64().Unique().NotNullable()
            .WithColumn("player_name").AsString(64).NotNullable().WithDefaultValue("")
            .WithColumn("playtime_minutes").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("created_at").AsDateTime().NotNullable().WithDefaultValue(SystemMethods.CurrentDateTime)
            .WithColumn("updated_at").AsDateTime().NotNullable().WithDefaultValue(SystemMethods.CurrentDateTime);

        Create.Index("idx_admin_playtime_playtime_minutes")
            .OnTable("admin_playtime")
            .OnColumn("playtime_minutes");
    }

    public override void Down()
    {
        Delete.Table("admin_playtime");
    }
}
