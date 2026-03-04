using FluentMigrator;

namespace CS2_Admin.Database.Migrations;

[Migration(2026030203)]
public class AddServersTable : Migration
{
    public override void Up()
    {
        if (Schema.Table("admin_servers").Exists())
        {
            return;
        }

        Create.Table("admin_servers")
            .WithColumn("id").AsInt64().PrimaryKey().Identity().NotNullable()
            .WithColumn("server_id").AsString(128).Unique().NotNullable()
            .WithColumn("server_ip").AsString(64).NotNullable()
            .WithColumn("server_port").AsInt32().NotNullable()
            .WithColumn("last_seen_at").AsDateTime().NotNullable().WithDefaultValue(SystemMethods.CurrentDateTime);
    }

    public override void Down()
    {
        Delete.Table("admin_servers");
    }
}
