using FluentMigrator;

namespace CS2_Admin.Database.Migrations;

[Migration(2025123102)]
public class AddBansTable : Migration
{
    public override void Up()
    {
        if (Schema.Table("admin_bans").Exists())
        {
            return;
        }

        Create.Table("admin_bans")
            .WithColumn("id").AsInt64().PrimaryKey().Identity().NotNullable()
            .WithColumn("steamid").AsInt64().NotNullable()
            .WithColumn("target_name").AsString(64).NotNullable().WithDefaultValue("")
            .WithColumn("target_type").AsString(16).NotNullable().WithDefaultValue("steamid")
            .WithColumn("ip_address").AsString(64).Nullable()
            .WithColumn("admin_name").AsString(64).NotNullable()
            .WithColumn("admin_steamid").AsInt64().NotNullable()
            .WithColumn("reason").AsString(2048).NotNullable()
            .WithColumn("is_global").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("server_id").AsString(128).NotNullable().WithDefaultValue("")
            .WithColumn("server_ip").AsString(64).NotNullable().WithDefaultValue("")
            .WithColumn("server_port").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("created_at").AsDateTime().NotNullable().WithDefaultValue(SystemMethods.CurrentDateTime)
            .WithColumn("expires_at").AsDateTime().Nullable()
            .WithColumn("status").AsString(16).NotNullable().WithDefaultValue("active")
            .WithColumn("unban_admin_name").AsString(64).Nullable()
            .WithColumn("unban_admin_steamid").AsInt64().Nullable()
            .WithColumn("unban_reason").AsString(2048).Nullable()
            .WithColumn("unban_date").AsDateTime().Nullable();

        Create.Index("idx_admin_bans_steamid_status").OnTable("admin_bans").OnColumn("steamid").Ascending().OnColumn("status").Ascending();
        Create.Index("idx_admin_bans_ip_status").OnTable("admin_bans").OnColumn("ip_address").Ascending().OnColumn("status").Ascending();
        Create.Index("idx_admin_bans_expires_status").OnTable("admin_bans").OnColumn("expires_at").Ascending().OnColumn("status").Ascending();
        Create.Index("idx_admin_bans_created_at").OnTable("admin_bans").OnColumn("created_at");
        Create.Index("idx_admin_bans_status").OnTable("admin_bans").OnColumn("status");
    }

    public override void Down()
    {
        Delete.Table("admin_bans");
    }
}
