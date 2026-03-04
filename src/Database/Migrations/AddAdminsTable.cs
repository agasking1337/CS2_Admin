using FluentMigrator;

namespace CS2_Admin.Database.Migrations;

[Migration(2025123101)]
public class AddAdminsTable : Migration
{
    public override void Up()
    {
        if (Schema.Table("admin_admins").Exists())
        {
            return;
        }

        Create.Table("admin_admins")
            .WithColumn("id").AsInt64().PrimaryKey().Identity().NotNullable()
            .WithColumn("steamid").AsInt64().Unique().NotNullable()
            .WithColumn("name").AsString(64).NotNullable()
            .WithColumn("flags").AsString(255).NotNullable()
            .WithColumn("groups").AsString(512).NotNullable().WithDefaultValue("")
            .WithColumn("immunity").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("created_at").AsDateTime().NotNullable().WithDefaultValue(SystemMethods.CurrentDateTime)
            .WithColumn("expires_at").AsDateTime().Nullable()
            .WithColumn("added_by").AsString(64).Nullable()
            .WithColumn("added_by_steamid").AsInt64().Nullable();

        Create.Index("idx_admin_admins_steamid").OnTable("admin_admins").OnColumn("steamid");
        Create.Index("idx_admin_admins_expires_at").OnTable("admin_admins").OnColumn("expires_at");
    }

    public override void Down()
    {
        Delete.Table("admin_admins");
    }
}
