using FluentMigrator;

namespace CS2_Admin.Database.Migrations;

[Migration(2025123104)]
public class AddMutesTable : Migration
{
    public override void Up()
    {
        if (Schema.Table("admin_mutes").Exists())
        {
            return;
        }

        Create.Table("admin_mutes")
            .WithColumn("id").AsInt64().PrimaryKey().Identity().NotNullable()
            .WithColumn("steamid").AsInt64().NotNullable()
            .WithColumn("admin_name").AsString(64).NotNullable()
            .WithColumn("admin_steamid").AsInt64().NotNullable()
            .WithColumn("reason").AsString(2048).NotNullable()
            .WithColumn("created_at").AsDateTime().NotNullable().WithDefaultValue(SystemMethods.CurrentDateTime)
            .WithColumn("expires_at").AsDateTime().Nullable()
            .WithColumn("status").AsString(16).NotNullable().WithDefaultValue("active")
            .WithColumn("unmute_admin_name").AsString(64).Nullable()
            .WithColumn("unmute_admin_steamid").AsInt64().Nullable()
            .WithColumn("unmute_reason").AsString(2048).Nullable()
            .WithColumn("unmute_date").AsDateTime().Nullable();

        Create.Index("idx_admin_mutes_steamid_status").OnTable("admin_mutes").OnColumn("steamid").Ascending().OnColumn("status").Ascending();
        Create.Index("idx_admin_mutes_expires_status").OnTable("admin_mutes").OnColumn("expires_at").Ascending().OnColumn("status").Ascending();
        Create.Index("idx_admin_mutes_status").OnTable("admin_mutes").OnColumn("status");
    }

    public override void Down()
    {
        Delete.Table("admin_mutes");
    }
}
