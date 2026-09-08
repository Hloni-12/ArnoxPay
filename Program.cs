using ArnoxPay.Data;
using ArnoxPay.Infrastructure;
using ArnoxPay.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// MVC
builder.Services.AddControllersWithViews();

// Database
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

// Application services
builder.Services.AddScoped<IRepositoryWrapper, RepositoryWrapper>();
builder.Services.Configure<NotificationSettings>(
    builder.Configuration.GetSection("NotificationSettings"));
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<ITransactionService, TransactionService>();
builder.Services.AddScoped<IFileStorageService, FileStorageService>();
builder.Services.AddScoped<IPaymentGateway, DemoPaymentGateway>();
builder.Services.AddScoped<ICheckoutService, CheckoutService>();

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

// Seed roles + default administrator (safe if DB is not ready yet)
using (var scope = app.Services.CreateScope())
{
    try
    {
        await AdministratorAccountSeeder.SeedAsync(scope.ServiceProvider);
    }
    catch (Exception ex)
    {
        var logger = scope.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Startup");
        logger.LogWarning(
            ex,
            "Database seeding was skipped. Create ArnoxPayDB and apply migrations, then restart.");
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
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
