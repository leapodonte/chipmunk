using System.Text.Json.Nodes;

namespace Chipmunk.Platform;

public static class Seed
{
    public static async Task Initialize(Store store)
    {
        await store.Execute("INSERT INTO platform_tenant VALUES('tenant_demo','花栗鼠演示租户') ON CONFLICT DO NOTHING; INSERT INTO platform_organization VALUES('org_demo','tenant_demo','演示机构') ON CONFLICT DO NOTHING; INSERT INTO platform_clinic VALUES('clinic_demo','org_demo','演示门诊') ON CONFLICT DO NOTHING;");
        var user = new User("", "tenant_demo", "clinic_demo", "", "");
        if ((await store.List(user, "doctor", true)).Count == 0)
        {
            for (var i = 1; i <= 2; i++) await store.Add(user, "doctor", new JsonObject {
                ["name"] = i == 1 ? "林医生（演示）" : "陈医生（演示）", ["title"] = "演示医生", ["years"] = 8,
                ["hospital"] = "花栗鼠演示门诊", ["avatar"] = "", ["verified"] = false,
                ["honorBadges"] = new JsonArray(), ["stats"] = new JsonObject { ["fans"] = 0, ["rating"] = 0, ["served"] = 0 },
                ["specialtyTags"] = new JsonArray("正畸", "保持器"), ["specialtyText"] = "开发演示资料", ["highlight"] = "测试数据", ["isDemo"] = true
            }, true, "d_00" + i);
        }
        if ((await store.List(user, "post", true)).Count == 0)
            foreach (var topic in new[] { "doctor", "patient", "science", "case", "diary", "mutual" }) await store.Add(user, "post", new JsonObject {
                ["title"] = "花栗鼠演示内容 · " + topic, ["cover"] = "", ["author"] = new JsonObject { ["name"] = "演示账号", ["avatar"] = "" },
                ["likes"] = 0, ["topic"] = topic, ["content"] = "这是开发演示内容。", ["isDemo"] = true
            }, true, "p_" + topic);
    }
}
