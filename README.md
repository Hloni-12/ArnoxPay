# ArnoxPay

**One platform. Every payment. Every service.**

ArnoxPay is a university-focused payment and campus-services platform developed as a Software Engineering project. It brings campus payments, accommodation workflows, parking, account information and staff administration into one web application, with REST API endpoints intended for a mobile client.

## Features

- **Accounts and access control** – registration and sign-in powered by ASP.NET Core Identity, with Administrator, Financial Officer and Customer roles.
- **Payments and transactions** – payment checkout, payment records, transaction history and receipts.
- **Campus account** – campus balance and account-funding workflows.
- **Campus services** – rent, water, electricity, refuse, parking and related service payments.
- **Accommodation** – browse accommodation, submit and track assignment requests, manage accommodation information and report issues.
- **Parking and vehicles** – vehicle registration, parking permits and parking administration.
- **Staff operations** – payment review, customer and financial-officer administration, reports, announcements, notifications and staff communication.
- **Mobile API** – JSON endpoints under `api/mobile` that use protected bearer tokens.
- **South African formatting** – the application uses the `en-ZA` culture and South African rand (ZAR) formatting.

> The repository supplied for this README contains the ASP.NET Core web application and its mobile API. It does not include a separate native Android application project.

## Technology stack

- C# and ASP.NET Core MVC on **.NET 9**
- Entity Framework Core 9
- ASP.NET Core Identity
- Microsoft SQL Server (configured through `DefaultConnection`)
- Bootstrap 5.3 and Font Awesome 7.3
- Google Cloud Firestore integration components
- ClosedXML for Excel report generation

## Repository structure

```text
ArnoxPay/
├── Controllers/       MVC controllers and mobile API controllers
├── Data/              EF Core DbContext, repositories and account seeding
├── Infrastructure/    Payment, checkout, notification and application services
├── Migrations/        Entity Framework Core migrations
├── Models/            Domain models and view models
├── Views/             Razor views
├── wwwroot/           CSS, images, libraries and uploaded-file storage
├── Program.cs         Application configuration and middleware pipeline
└── ArnoxPay.csproj    Project file and NuGet dependencies
```

## Prerequisites

- .NET 9 SDK
- SQL Server instance and a database/user with permission to create and update the application's schema
- Visual Studio 2022 (with ASP.NET and web development workload) or another .NET-compatible editor

## Getting started

1. Clone the repository:

   ```bash
   git clone https://github.com/Hloni-12/ArnoxPay.git
   cd ArnoxPay/ArnoxPay
   ```

   If the project files are at the repository root in your checkout, omit the final `/ArnoxPay` directory.

2. Restore dependencies:

   ```bash
   dotnet restore
   ```

3. Configure the database connection without putting credentials in source control. The application reads the connection string named `DefaultConnection`:

   ```bash
   dotnet user-secrets init
   dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=YOUR_SERVER;Database=ArnoxPayDB;User Id=YOUR_USER;Password=YOUR_PASSWORD;TrustServerCertificate=True;"
   ```

   Replace the example values with your own SQL Server details. For deployment, use the hosting provider's protected environment-variable or secret-configuration facility instead of committing credentials.

4. Apply the Entity Framework Core migrations. The current startup code also attempts to apply migrations when the application starts, but applying them explicitly makes database setup easier to diagnose:

   ```bash
   dotnet ef database update
   ```

   If the EF command is unavailable, install the EF Core CLI tool:

   ```bash
   dotnet tool install --global dotnet-ef --version 9.0.0
   ```

5. Run the application:

   ```bash
   dotnet run
   ```

6. Open the local URL printed in the terminal.

## Configuration

Keep environment-specific values outside the repository. Common settings include:

| Setting | Purpose |
| --- | --- |
| `ConnectionStrings:DefaultConnection` | SQL Server connection string |
| `NotificationSettings` | Email and SMS provider configuration |
| `OneSignal` | Push-notification provider configuration |
| `FeeSettings` | Configurable campus/service fee values |

Email, SMS and push notifications require valid provider credentials and appropriate provider-side configuration. Leave integrations disabled until they are configured.

## Database and administrator setup

- EF Core migrations define the database schema. Review migrations before applying them to a production database.
- The application attempts database migration and initial administrator/role seeding during startup. Check application logs if database initialization fails.
- The initial administrator account is defined in `Data/AdministratorAccountSeeder.cs`. **Change the seeded credentials and ensure the account uses a strong, unique password before deploying or exposing the application.** Do not publish default credentials in documentation or source control.
- Back up the database before applying schema changes to an existing deployment.

## Security notes

- Never commit database passwords, API keys, SMTP credentials, push-notification secrets, publishing-profile credentials, or production data.
- Use .NET User Secrets for local development and protected environment variables/secret stores for hosting.
- If a credential has ever been committed or shared in an archive, treat it as compromised: rotate it with the relevant provider and remove it from the repository's tracked configuration and history where appropriate.
- Do not commit local database files, build output, IDE state, or user-specific publish settings.
- Review authentication, authorization, CORS and payment-processing settings before production deployment. The current mobile API CORS policy is permissive and should be restricted to trusted origins where applicable.

## Build

```bash
dotnet build --configuration Release
```

## Project status

ArnoxPay is an academic software project and is under active development. Some integrations and workflows may require environment-specific setup and testing. Do not use it to process real financial transactions without a full security, reliability and compliance review.

## License

No license file is currently specified in this repository. Unless a license is added, the source code is not granted an open-source license by default.
