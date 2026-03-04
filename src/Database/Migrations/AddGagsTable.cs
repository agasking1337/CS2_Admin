using FluentMigrator;

namespace CS2_Admin.Database.Migrations;

[Migration(2025123103)]
public class AddGagsTable : Migration
{
    public override void Up()
    {
        if (Schema.Table("admin_gags").Exists())
        {
            return;
        }

        Create.Table("admin_gags")
            .WithColumn("id").AsInt64().PrimaryKey().Identity().NotNullable()
            .WithColumn("steamid").AsInt64().NotNullable()
            .WithColumn("admin_name").AsString(64).NotNullable()
            .WithColumn("admin_steamid").AsInt64().NotNullable()
            .WithColumn("reason").AsString(2048).NotNullable()
            .WithColumn("created_at").AsDateTime().NotNullable().WithDefaultValue(SystemMethods.CurrentDateTime)
            .WithColumn("expires_at").AsDateTime().Nullable()
            .WithColumn("status").AsString(16).NotNullable().WithDefaultValue("active")
            .WithColumn("ungag_admin_name").AsString(64).Nullable()
            .WithColumn("ungag_admin_steamid").AsInt64().Nullable()
            .WithColumn("ungag_reason").AsString(2048).Nullable()
            .WithColumn("ungag_date").AsDateTime().Nullable();

        Create.Index("idx_admin_gags_steamid_status").OnTable("admin_gags").OnColumn("steamid").Ascending().OnColumn("status").Ascending();
        Create.Index("idx_admin_gags_expires_status").OnTable("admin_gags").OnColumn("expires_at").Ascending().OnColumn("status").Ascending();
        Create.Index("idx_admin_gags_status").OnTable("admin_gags").OnColumn("status");
    }

    public override void Down()
    {
        Delete.Table("admin_gags");
    }
}
