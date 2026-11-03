using ArnoxPay.Data;
using ArnoxPay.Infrastructure;
using ArnoxPay.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

var builder = WebApplication.CreateBuilder(args);

var zaCulture = new CultureInfo("en-ZA");
CultureInfo.DefaultThreadCurrentCulture = zaCulture;
CultureInfo.DefaultThreadCurrentUICulture = zaCulture;

// MVC
builder.Services.AddControllersWithViews();
builder.Services.AddHttpContextAccessor();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(2);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});
builder.Services.AddControllers(); // JSON API for mobile clients
builder.Services.AddCors(options =>
{
    options.AddPolicy("MobileApp", policy =>
        policy.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin());
});

// Database
builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"));
    options.ConfigureWarnings(w =>
        w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
});

// Application services
builder.Services.AddScoped<IRepositoryWrapper, RepositoryWrapper>();
builder.Services.Configure<NotificationSettings>(
    builder.Configuration.GetSection("NotificationSettings"));
builder.Services.Configure<OneSignalSettings>(
    builder.Configuration.GetSection("OneSignal"));
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddHttpClient<IOneSignalPushService, OneSignalPushService>(client =>
    client.Timeout = TimeSpan.FromSeconds(8));
builder.Services.AddScoped<IEmailDeliveryService, EmailDeliveryService>();
builder.Services.AddScoped<ITransactionService, TransactionService>();
builder.Services.AddScoped<IFileStorageService, FileStorageService>();
builder.Services.AddScoped<IPaymentGateway, InternalPaymentProcessor>();
builder.Services.AddScoped<ICheckoutService, CheckoutService>();
builder.Services.AddScoped<ICampusAccountService, CampusAccountService>();
builder.Services.AddScoped<IFinancialReportService, FinancialReportService>();
builder.Services.Configure<FeeSettings>(builder.Configuration.GetSection("FeeSettings"));
builder.Services.AddScoped<IServiceCartStore, ServiceCartStore>();

// Routing (lowercase URLs)
builder.Services.AddRouting(options =>
{
    options.LowercaseUrls = true;
    options.AppendTrailingSlash = true;
});

// ASP.NET Identity
builder.Services
    .AddIdentity<AppUser, IdentityRole>(options =>
    {
        options.Password.RequiredLength = 8;
        options.Password.RequireDigit = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireNonAlphanumeric = false;

        options.User.RequireUniqueEmail = true;
        // Student/Staff numbers are used as usernames
        options.User.AllowedUserNameCharacters =
            "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._@+";

        options.SignIn.RequireConfirmedAccount = false;
        options.SignIn.RequireConfirmedEmail = false;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders()
    .AddUserValidator<StaffEmailDomainValidator>();

// Required by the mobile bearer-token issuer and validator.
builder.Services.AddDataProtection();
builder.Services.AddSingleton<MobileTokenService>();

// Cookie paths must match lowercase + trailing-slash routing
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/account/login/";
    options.AccessDeniedPath = "/account/accessdenied/";
    options.LogoutPath = "/account/logout/";
    options.SlidingExpiration = true;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
});


var app = builder.Build();

var localization = new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture(zaCulture),
    SupportedCultures = [zaCulture],
    SupportedUICultures = [zaCulture]
};
app.UseRequestLocalization(localization);

// Apply migrations + seed roles/admin (safe if DB is not ready yet)
// Apply SQLite migrations and seed the administrator
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider
        .GetRequiredService<ILoggerFactory>()
        .CreateLogger("Startup");

    try
    {
        var db = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        await db.Database.MigrateAsync();

        logger.LogInformation(
            "SQLite database migrations completed successfully.");
    }
    catch (Exception ex)
    {
        logger.LogError(
            ex,
            "SQLite database migration failed.");
    }

    try
    {
        await AdministratorAccountSeeder.SeedAsync(
            scope.ServiceProvider);
    }
    catch (Exception ex)
    {
        logger.LogWarning(
            ex,
            "Administrator seeding failed.");
    }
}
// Pipeline
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/home/error/");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseCors("MobileApp");
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
