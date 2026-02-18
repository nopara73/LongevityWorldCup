# Longevity World Cup - Claude Code Project Guide

## Project Overview
Longevity World Cup is a competitive longevity platform where athletes compete based on health/longevity metrics. Season-based competition with applications, leagues, badges, events, and Bitcoin integration.

**Live site:** https://www.longevityworldcup.com/

## Tech Stack
- **Framework:** ASP.NET 8.0 MVC (C#)
- **Database:** SQLite (via Microsoft.Data.Sqlite.Core + SQLitePCLRaw)
- **Job Scheduling:** Quartz.NET
- **Email:** MailKit + Gmail Auth
- **Image Processing:** SixLabors.ImageSharp
- **Auth:** Google.Apis.Auth
- **Integrations:** Slack webhooks, Bitcoin
- **CI/CD:** GitHub Actions (`.github/workflows/deploy.yml`, `.github/workflows/dotnet.yml`)
- **Default branch:** `master`

## Solution Structure
```
LongevityWorldCup.sln
├── LongevityWorldCup.Website/          # Main ASP.NET web application
│   ├── Controllers/                     # MVC Controllers
│   │   ├── ApplicationController.cs     # Athlete application flow
│   │   ├── AthleteController.cs         # Athlete profiles
│   │   ├── BitcoinController.cs         # Bitcoin/payment integration
│   │   ├── DataController.cs            # Data API endpoints
│   │   ├── ErrorController.cs           # Error handling
│   │   ├── EventsController.cs          # Events management
│   │   ├── GuessController.cs           # Prediction/guessing game
│   │   ├── HomeController.cs            # Landing/home pages
│   │   ├── LeagueController.cs          # League standings
│   │   └── MediaController.cs           # Media content
│   ├── Business/                        # Business logic & services
│   │   ├── ApplicantData.cs             # Applicant data model
│   │   ├── AthleteDataService.cs        # Athlete CRUD & queries
│   │   ├── BadgeDataService.cs          # Badge system
│   │   ├── BitcoinDataService.cs        # Bitcoin transactions
│   │   ├── DatabaseManager.cs           # SQLite DB management
│   │   ├── Divisions.cs                 # Competition divisions
│   │   ├── EventDataService.cs          # Events data
│   │   ├── Flags.cs                     # Country flags
│   │   ├── NewsletterService.cs         # Email newsletters
│   │   ├── SeasonFinalizerService.cs    # End-of-season logic
│   │   ├── SlackEventService.cs         # Slack notifications
│   │   └── SlackWebhookClient.cs        # Slack API client
│   ├── Jobs/                            # Quartz.NET scheduled jobs
│   ├── Middleware/                       # ASP.NET middleware
│   ├── Scripts/                         # Shell scripts (e.g., custom_event.sh)
│   ├── Tools/                           # Utility tools
│   ├── wwwroot/                         # Static files (CSS, JS, images, fonts)
│   ├── Config.cs                        # App configuration
│   ├── GmailAuth.cs                     # Gmail authentication
│   ├── Program.cs                       # App entry point & DI setup
│   └── appsettings.json                 # App settings
├── LongevityWorldCup.ApplicationReviewer/ # Console app for reviewing applications
│   └── Program.cs
└── LongevityWorldCup.Documentation/     # Project docs
    ├── GameNights.md
    └── ServerDeployment.md
```

## Build & Run Commands
```bash
# Restore dependencies
dotnet restore

# Build solution
dotnet build

# Run the website (development)
dotnet run --project LongevityWorldCup.Website

# Run in release mode
dotnet run --project LongevityWorldCup.Website -c Release

# Run the application reviewer
dotnet run --project LongevityWorldCup.ApplicationReviewer
```

## Development Rules

### Code Style
- Use C# nullable reference types (`<Nullable>enable</Nullable>`)
- Use implicit usings (`<ImplicitUsings>enable</ImplicitUsings>`)
- Follow existing MVC patterns: Controllers are thin, business logic lives in `Business/` services
- Database access goes through `DatabaseManager.cs`
- Keep controller actions focused; delegate to services

### Architecture Patterns
- **Controllers** handle HTTP concerns only (routing, model binding, response)
- **Business services** (*DataService.cs) contain all data access and business logic
- **Jobs** use Quartz.NET `IJob` interface for scheduled tasks
- **Middleware** for cross-cutting concerns
- SQLite database with raw SQL (no ORM) - follow existing query patterns in DataService files

### Security
- Never commit `appsettings.Production.json` or any secrets
- Gmail credentials handled via `GmailAuth.cs` - never hardcode
- Validate all user input in controllers before passing to services
- Use parameterized queries for all SQL (see `DatabaseManager.cs` patterns)

### Git Workflow
- Default branch: `master`
- Create feature branches from `master`
- CI runs on push via `.github/workflows/dotnet.yml`
- Deployment via `.github/workflows/deploy.yml`
