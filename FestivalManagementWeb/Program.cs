using AspNetCore.Identity.MongoDbCore.Models;
using FestivalManagementWeb.Models;
using FestivalManagementWeb.Repositories;
using FestivalManagementWeb.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using MongoDB.Driver;
using System;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

// Configure MongoDB settings
var mongoDbSettings = builder.Configuration.GetSection("MongoDbSettings").Get<FestivalManagementWeb.Models.MongoDbSettings>()!;
builder.Services.AddSingleton(mongoDbSettings);
builder.Services.AddHttpContextAccessor();

// Bind FreeTier settings and service
builder.Services.Configure<FestivalManagementWeb.Models.FreeTierSettings>(
    builder.Configuration.GetSection("FreeTier"));
builder.Services.AddSingleton<FestivalManagementWeb.Services.IFreeTierService, FestivalManagementWeb.Services.FreeTierService>();
builder.Services.Configure<FestivalManagementWeb.Models.AzureUsageSettings>(builder.Configuration.GetSection("AzureUsage"));
builder.Services.AddSingleton<FestivalManagementWeb.Services.IAutoUsageState, FestivalManagementWeb.Services.AutoUsageState>();
builder.Services.AddSingleton<FestivalManagementWeb.Services.IAzureUsageProvider, FestivalManagementWeb.Services.AzureUsageProvider>();
builder.Services.AddSingleton<FestivalManagementWeb.Services.ICosmosFreeTierProvider, FestivalManagementWeb.Services.CosmosFreeTierProvider>();
builder.Services.AddHostedService<FestivalManagementWeb.Services.AutoUsageRefreshHostedService>();
builder.Services.AddSingleton<FestivalManagementWeb.Services.IRequestQuotaService, FestivalManagementWeb.Services.RequestQuotaService>();

// Configure Identity with MongoDB
builder.Services.AddIdentity<ApplicationUser, MongoIdentityRole<Guid>>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
    })
    .AddMongoDbStores<ApplicationUser, MongoIdentityRole<Guid>, Guid>(mongoDbSettings.ConnectionString, mongoDbSettings.DatabaseName)
    .AddSignInManager()
    .AddDefaultTokenProviders();

// Configure Authentication
builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    })
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
    })
    .AddGoogle(options =>
    {
        options.ClientId = builder.Configuration["Authentication:Google:ClientId"];
        options.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];
        options.CallbackPath = new PathString("/signin-google/");

    });

// Add MongoDB Client to DI
builder.Services.AddSingleton<IMongoClient>(new MongoClient(mongoDbSettings.ConnectionString));
builder.Services.AddScoped(sp => sp.GetRequiredService<IMongoClient>().GetDatabase(mongoDbSettings.DatabaseName));
builder.Services.AddScoped<MongoDB.Driver.GridFS.IGridFSBucket>(sp =>
{
    var database = sp.GetRequiredService<IMongoDatabase>();
    return new MongoDB.Driver.GridFS.GridFSBucket(database);
});


builder.Services.AddScoped<ITextKeyValueRepository, TextKeyValueRepository>();
builder.Services.AddScoped<IImageKeyValueRepository, ImageKeyValueRepository>();
builder.Services.AddScoped<IGitService, GitService>();
builder.Services.AddScoped<IYearBranchService, YearBranchService>();

builder.Services.AddControllersWithViews();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// Respect reverse proxy headers in ACA/ingress (should run early)
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    KnownNetworks = { },
    KnownProxies = { }
});

app.UseHttpsRedirection();

// Enforce daily request cap before serving static files or MVC
app.UseMiddleware<FestivalManagementWeb.Middleware.RequestQuotaMiddleware>();

app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "home",
    pattern: "home/{action=Index}/{id?}",
    defaults: new { controller = "Home" });

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// Create Unique Indexes for Keys
using (var scope = app.Services.CreateScope())
{
    var database = scope.ServiceProvider.GetRequiredService<IMongoDatabase>();
    var textKeyValues = database.GetCollection<TextKeyValue>("TextKeyValues");
    var imageKeyValues = database.GetCollection<ImageKeyValue>("ImageKeyValues");

    try
    {
        textKeyValues.Indexes.DropOne("Key_1");
    }
    catch (MongoCommandException)
    {
        // Index may not exist in fresh databases; ignore.
    }

    try
    {
        imageKeyValues.Indexes.DropOne("Key_1");
    }
    catch (MongoCommandException)
    {
        // Index may not exist in fresh databases; ignore.
    }

    var currentYear = DateTime.UtcNow.Year;

    var textMissingYearFilter = Builders<TextKeyValue>.Filter.Or(
        Builders<TextKeyValue>.Filter.Exists(nameof(TextKeyValue.Year), false),
        Builders<TextKeyValue>.Filter.Eq(x => x.Year, 0));
    var textSetYearUpdate = Builders<TextKeyValue>.Update.Set(x => x.Year, currentYear);
    textKeyValues.UpdateMany(textMissingYearFilter, textSetYearUpdate);

    var imageMissingYearFilter = Builders<ImageKeyValue>.Filter.Or(
        Builders<ImageKeyValue>.Filter.Exists(nameof(ImageKeyValue.Year), false),
        Builders<ImageKeyValue>.Filter.Eq(x => x.Year, 0));
    var imageSetYearUpdate = Builders<ImageKeyValue>.Update.Set(x => x.Year, currentYear);
    imageKeyValues.UpdateMany(imageMissingYearFilter, imageSetYearUpdate);

    var textKeyIndex = new CreateIndexModel<TextKeyValue>(
        Builders<TextKeyValue>.IndexKeys.Ascending(x => x.Year).Ascending(x => x.Key),
        new CreateIndexOptions { Unique = true });
    var imageKeyIndex = new CreateIndexModel<ImageKeyValue>(
        Builders<ImageKeyValue>.IndexKeys.Ascending(x => x.Year).Ascending(x => x.Key),
        new CreateIndexOptions { Unique = true });

    textKeyValues.Indexes.CreateOne(textKeyIndex);
    imageKeyValues.Indexes.CreateOne(imageKeyIndex);
}

// Seed initial user
async Task SeedInitialUser(IHost app)
{
    using (var scope = app.Services.CreateScope())
    {
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var initialUserEmail = configuration["InitialUser:Email"];

        if (!string.IsNullOrEmpty(initialUserEmail))
        {
            var existingUser = await userManager.FindByEmailAsync(initialUserEmail);
            if (existingUser == null)
            {
                var newUser = new ApplicationUser
                {
                    UserName = initialUserEmail,
                    Email = initialUserEmail,
                    EmailConfirmed = true
                };
                await userManager.CreateAsync(newUser);
            }
        }
    }
}

await SeedInitialUser(app);

app.Run();



