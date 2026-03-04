using FluentMigrator;

namespace CS2_Admin.Database.Migrations;

[Migration(2026030202)]
public class AddAdminLogsTable : Migration
{
    public override void Up()
    {
        if (Schema.Table("admin_log").Exists())
        {
            return;
        }

        Create.Table("admin_log")
            .WithColumn("id").AsInt64().PrimaryKey().Identity().NotNullable()
            .WithColumn("admin_name").AsString(64).NotNullable()
            .WithColumn("admin_steamid").AsInt64().NotNullable()
            .WithColumn("action").AsString(64).NotNullable()
            .WithColumn("target_steamid").AsInt64().Nullable()
            .WithColumn("target_ip").AsString(64).Nullable()
            .WithColumn("details").AsString(4096).NotNullable()
            .WithColumn("server_id").AsString(128).NotNullable()
            .WithColumn("server_ip").AsString(64).NotNullable()
            .WithColumn("server_port").AsInt32().NotNullable()
            .WithColumn("created_at").AsDateTime().NotNullable().WithDefaultValue(SystemMethods.CurrentDateTime);

        Create.Index("idx_admin_log_created_at").OnTable("admin_log").OnColumn("created_at").Descending();
    }

    public override void Down()
    {
        Delete.Table("admin_log");
    }
}
