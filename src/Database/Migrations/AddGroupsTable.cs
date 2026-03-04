using FluentMigrator;

namespace CS2_Admin.Database.Migrations;

[Migration(2026030201)]
public class AddGroupsTable : Migration
{
    public override void Up()
    {
        if (Schema.Table("admin_groups").Exists())
        {
            return;
        }

        Create.Table("admin_groups")
            .WithColumn("id").AsInt64().PrimaryKey().Identity().NotNullable()
            .WithColumn("name").AsString(64).Unique().NotNullable()
            .WithColumn("flags").AsString(512).NotNullable()
            .WithColumn("immunity").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("created_at").AsDateTime().NotNullable().WithDefaultValue(SystemMethods.CurrentDateTime)
            .WithColumn("updated_at").AsDateTime().NotNullable().WithDefaultValue(SystemMethods.CurrentDateTime);

        Create.Index("idx_admin_groups_name").OnTable("admin_groups").OnColumn("name").Ascending();
    }

    public override void Down()
    {
        Delete.Table("admin_groups");
    }
}
