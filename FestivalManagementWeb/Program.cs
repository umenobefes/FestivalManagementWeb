using AspNetCore.Identity.MongoDbCore.Models;
using FestivalManagementWeb.Models;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using MongoDB.Driver;
using System;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

// Configure MongoDB settings
var mongoDbSettings = builder.Configuration.GetSection("MongoDbSettings").Get<FestivalManagementWeb.Models.MongoDbSettings>()!;
builder.Services.AddSingleton(mongoDbSettings);

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


builder.Services.AddControllersWithViews();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// Create Unique Indexes for Keys
using (var scope = app.Services.CreateScope())
{
    var database = scope.ServiceProvider.GetRequiredService<IMongoDatabase>();
    var textKeyValues = database.GetCollection<TextKeyValue>("TextKeyValues");
    var imageKeyValues = database.GetCollection<ImageKeyValue>("ImageKeyValues");

    var textKeyIndex = new CreateIndexModel<TextKeyValue>(Builders<TextKeyValue>.IndexKeys.Ascending(x => x.Key), new CreateIndexOptions { Unique = true });
    var imageKeyIndex = new CreateIndexModel<ImageKeyValue>(Builders<ImageKeyValue>.IndexKeys.Ascending(x => x.Key), new CreateIndexOptions { Unique = true });

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
