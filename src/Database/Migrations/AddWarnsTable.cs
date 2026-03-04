using FluentMigrator;

namespace CS2_Admin.Database.Migrations;

[Migration(2026030204)]
public class AddWarnsTable : Migration
{
    public override void Up()
    {
        if (Schema.Table("admin_warns").Exists())
        {
            return;
        }

        Create.Table("admin_warns")
            .WithColumn("id").AsInt64().PrimaryKey().Identity().NotNullable()
            .WithColumn("steamid").AsInt64().NotNullable()
            .WithColumn("admin_name").AsString(64).NotNullable()
            .WithColumn("admin_steamid").AsInt64().NotNullable()
            .WithColumn("reason").AsString(2048).NotNullable()
            .WithColumn("created_at").AsDateTime().NotNullable().WithDefaultValue(SystemMethods.CurrentDateTime)
            .WithColumn("expires_at").AsDateTime().Nullable()
            .WithColumn("status").AsString(16).NotNullable().WithDefaultValue("active")
            .WithColumn("unwarn_admin_name").AsString(64).Nullable()
            .WithColumn("unwarn_admin_steamid").AsInt64().Nullable()
            .WithColumn("unwarn_reason").AsString(2048).Nullable()
            .WithColumn("unwarn_date").AsDateTime().Nullable();

        Create.Index("idx_admin_warns_steamid_status").OnTable("admin_warns").OnColumn("steamid").Ascending().OnColumn("status").Ascending();
        Create.Index("idx_admin_warns_expires_status").OnTable("admin_warns").OnColumn("expires_at").Ascending().OnColumn("status").Ascending();
        Create.Index("idx_admin_warns_status").OnTable("admin_warns").OnColumn("status");
        Create.Index("idx_admin_warns_created_at").OnTable("admin_warns").OnColumn("created_at").Descending();
    }

    public override void Down()
    {
        Delete.Table("admin_warns");
    }
}
