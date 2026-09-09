

using Admin.Api.Services;
using Admin.Services;
using Admin.Utils;
using FreeRedis;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Reflection.PortableExecutable;
using System.Text.Json.Serialization;

if (args.Length > 0)
{
    if (args[0] == "--init")
    {
        try
        {
            var cfg = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json")
                .Build();

            string? constr = cfg["Database"];
            if (constr == null)
            {
                throw new Exception("初始化失败: 数据库配置文件错误");
            }
            IFreeSql fsql = new FreeSql.FreeSqlBuilder()
                .UseConnectionString(FreeSql.DataType.PostgreSQL, constr)
                //.CreateDatabaseIfNotExists()
                .CreateDatabaseIfNotExistsPgSql()
                .Build();
            //Mysql不要映射成枚举类型
            fsql.Aop.ConfigEntityProperty += (s, e) =>
            {
                if (e.Property.PropertyType.IsEnum)
                    e.ModifyResult.MapType = typeof(int);
            };
            fsql.UseJsonMap();

            SystemService svr = new SystemService(fsql, new EncryptorService(
                cfg["Security:SignKey"],
                cfg["Security:ClientPwdKey"],
                cfg["Security:ServerPwdKey"],
                cfg["Security:RequestKey"]));
            await svr.InitSystemAsync();
            Console.WriteLine("初始化完成");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"初始化失败:{ex.Message}");
        }
    }
}
else
{

    var builder = WebApplication.CreateBuilder(args);

    builder.Services.Configure<RequestLocalizationOptions>(options => {

        var supportedCultures = new List<CultureInfo>
        {
            new CultureInfo("en-US"),
            new CultureInfo("zh-Hans"),
        };
        options.DefaultRequestCulture = new RequestCulture(supportedCultures[0]);
        options.SupportedCultures = supportedCultures;
        options.SupportedUICultures = supportedCultures;
    });

    builder.Services.AddLocalization();




    // Add services to the container.
    builder.Services.AddControllers().AddJsonOptions(opts => opts.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull);

    builder.Services.AddSingleton(sp =>
    {
        var cfg = sp.GetRequiredService<IConfiguration>();
        string? constr = cfg["Database"];
        IFreeSql fsql = new FreeSql.FreeSqlBuilder()
        .UseConnectionString(FreeSql.DataType.PostgreSQL, constr)
        .UseAutoSyncStructure(false)
        .Build();
        //Mysql不要映射成枚举类型
        fsql.Aop.ConfigEntityProperty += (s, e) =>
        {
            if (e.Property.PropertyType.IsEnum)
                e.ModifyResult.MapType = typeof(int);
        };
        fsql.UseJsonMap();
        return fsql;
    });

    builder.Services.AddSingleton<RedisClient>(sp => {
        var cfg = sp.GetRequiredService<IConfiguration>();
        var redis = cfg.GetSection("Redis");
        var master = redis.GetValue<string>("MasterSlave:Master");
        var slavesNode = redis.GetSection("MasterSlave:Slaves").GetChildren();
        var slaves = new List<ConnectionStringBuilder>(slavesNode.Count());
        foreach(var slave in slavesNode)
        {
            if(!string.IsNullOrWhiteSpace(slave.Value))
            {
                slaves.Add(slave.Value);
            }
        }
        return new RedisClient(master, slaves.ToArray());
    });

    builder.Services.AddSingleton<AdminLogService>();
    builder.Services.AddSingleton(sp =>
    {
        var cfg = sp.GetRequiredService<IConfiguration>();
        return new EncryptorService(
            cfg["Security:SignKey"],
            cfg["Security:ClientPwdKey"],
            cfg["Security:ServerPwdKey"],
            cfg["Security:RequestKey"]);
    });
    builder.Services.AddScoped<SystemOptionService>();
    builder.Services.AddScoped<AccountService>();
    builder.Services.AddScoped<AdminSessionService>();
    builder.Services.AddScoped<AppSessionService>();
    builder.Services.AddScoped<ModuleService>();
    builder.Services.AddScoped<RoleService>();
    builder.Services.AddScoped<CorpService>();
    builder.Services.AddScoped<WechatService>();
    builder.Services.AddScoped<UserService>();

    var app = builder.Build();

    var localizeOptions = app.Services.GetService<IOptions<RequestLocalizationOptions>>();
    app.UseRequestLocalization(localizeOptions.Value);

    // Configure the HTTP request pipeline.
    var headers = app.Configuration.GetSection("HttpHeaders").GetChildren();
    app.Use(async (ctx, next) =>
    {
        foreach (var hd in headers)
        {
            ctx.Response.Headers?.TryAdd(hd.Key, hd.Value);
        }

        if (HttpMethods.IsOptions(ctx.Request.Method))
        {
            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsync("");
        }
        else
        {
            await next();
        }
    });

    app.UseAuthorization();

    app.MapControllers();



    app.Run();
}

