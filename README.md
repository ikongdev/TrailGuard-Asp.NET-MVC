# TrailGuard

## Project Proposal

### Problem Statement
Organizing hiking events requires more than simply accepting participants and assigning them to available trails. Currently, participant screening is often handled manually or informally, making it difficult for organizers to consistently determine if a participant is suitable for a specific trail. Hiking safety depends heavily on a participant’s readiness (fitness, experience, equipment) matching the objective difficulty of the trail (distance, elevation, terrain). Without an integrated system, organizers experience inefficient record handling, inconsistent applicant screening, and difficulty making safety-related decisions.

### Objectives
- Provide a centralized web platform that streamlines the management of hiking events and trails.
- Replace manual screening with a structured evaluation system that matches a participant's physical readiness against the objective difficulty of a trail.
- Improve overall hiking safety by providing organizers with data-driven insights (e.g., automated difficulty calculations, weather tracking) before approving registrations.

### Target Users
- **System Administrators:** Manage overall system access and accounts.
- **Hiking Organizers:** Create events, manage trails, and review participant assessment results.
- **Participants:** Browse available hikes, submit fitness assessments, and track their registration statuses.

---

# System Overview

TrailGuard is a comprehensive ASP.NET Core MVC web application tailored for outdoor recreation management.

## Features and Functionality

### Role-Based Access Control
Secure, separate dashboards and workflows for Administrators, Organizers, and Participants.

### Trail & Event Management
Organizers can create detailed trail profiles, upload photos, manage trail information, and schedule hiking events.

### Automated Difficulty Calculator
`DifficultyCalculator` computes a geometry-based Event Difficulty display from an Event's captured trail data. A Trail's technical class is metadata; the organizer remains responsible for the registration decision.

### Weather Integration
A built-in `WeatherService` allows organizers to monitor weather conditions for scheduled events.

### Structured Participant Assessments
Participants complete a readiness assessment during registration, providing information regarding fitness, experience, and equipment.

### Event Comparison & Matching
Organizers can compare trail difficulty against participant assessment results to support informed approval decisions.

### Post-Event Feedback
Participants can submit feedback after events to help organizers improve future hiking activities.

---

# Technology Stack


--------------------------------------------------------------
|      Component       |             Technology              |
|----------------------|-------------------------------------|
| Backend Framework    | ASP.NET Core MVC                    |
| Programming Language | C#                                  |
| Database & ORM       | Entity Framework Core               |
| Authentication       | ASP.NET Core Identity               |
| Frontend             | Razor Views (.cshtml), Tailwind CSS |
| Tooling              | Node.js, npm, nodemon               |
--------------------------------------------------------------

# Installation / Setup Instructions

## Prerequisites

- .NET 10 SDK
- Python 3.14
- Node.js and npm
- Visual Studio 2022, Visual Studio Code, or any preferred IDE

## Clone the Repository

```bash
git clone https://github.com/ikongdev/trailguard-asp.net-mvc.git
cd trailguard-asp.net-mvc
```

## Install Frontend Dependencies

```bash
npm install
```

## Restore Backend Dependencies

```bash
dotnet restore
```

## Install ML Service Dependencies

From the repository root, install the dependencies used by the v3 FastAPI service:

```bash
python -m pip install fastapi "uvicorn[standard]" xgboost shap numpy pandas scikit-learn
```

## Database Setup

TrailGuard can run against either a local PostgreSQL install or a cloud Supabase PostgreSQL instance. Which one is active is controlled by a single setting, `Database:Target`, which accepts `Local` (the default) or `Supabase`:

| Setting | Used when | Holds |
|---|---|---|
| `ConnectionStrings:DefaultConnection` | `Database:Target` is `Local` or unset | Local PostgreSQL connection string |
| `ConnectionStrings:SupabaseConnection` | `Database:Target` is `Supabase` | Supabase session-pooler connection string (`SSL Mode=VerifyFull`) |

Store both connection strings in User Secrets — never in `appsettings.json`:

```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<local PostgreSQL connection string>"
dotnet user-secrets set "ConnectionStrings:SupabaseConnection" "<Supabase connection string>"
```

**Choosing a target for `dotnet run` or `dotnet ef`:** both read the same `Database:Target` setting, resolved in one shared place (`Services/DatabaseTargetResolver.cs`) so they can never disagree. Leaving the key unset (or set to `Local`) uses local PostgreSQL, matching current behavior with no changes required. To point either command at Supabase instead:

```bash
dotnet user-secrets set "Database:Target" "Supabase"
```

Set it back to `"Local"` (or remove the key) to switch back. **The application must be restarted after changing this setting** — it is only read at startup, not re-checked while running. An unsupported or blank `Database:Target` value fails startup immediately with a clear configuration error rather than silently defaulting.

**Switching targets selects a separate database — it does not transfer, sync, or copy any records.** Local and Supabase are independent databases with independent schemas and data; migrations must be applied to each target separately, and rows created against one are never visible from the other. For this project's Supabase instance, the database name is `postgres` (Supabase's session pooler for this project routes connections there) — that's expected for this connection string specifically, not something to change to `trailguard_db` to "match" Local; a different Supabase project could be configured with a different database name.

A malformed `ConnectionStrings:DefaultConnection`/`SupabaseConnection` value, or one missing a `Host` or `Database`, fails startup immediately with a clear, credential-free error — it is never passed through to a Npgsql connection attempt that could surface a less clear failure later.

Apply migrations to whichever target is currently selected:

```bash
dotnet ef database update
```

For a deployment environment that configures settings via environment variables instead of User Secrets, use the double-underscore form of the same keys:

```
Database__Target=Supabase
ConnectionStrings__DefaultConnection=<local PostgreSQL connection string>
ConnectionStrings__SupabaseConnection=<Supabase connection string>
```

## Initial Admin Account

On first startup, `DbSeeder` creates exactly one account — `admin@trailguard.com` with the Admin role — and nothing else (no Organizer/Participant sample accounts, no Trails, no Events). It reads the account's password from configuration only; there is no hardcoded or default password. Which setting is read depends on the selected `Database:Target`, so a Local seed password is never reused as a Supabase fallback:

| `Database:Target` | Password setting |
|---|---|
| `Local` (or unset) | `SeedAdmin:Password` |
| `Supabase` | `SeedAdmin:SupabasePassword` |

Set the one that matches your selected target before the first run against that database:

```bash
dotnet user-secrets set "SeedAdmin:Password" "<choose-a-strong-password>"
dotnet user-secrets set "SeedAdmin:SupabasePassword" "<choose-a-different-strong-password>"
```

For a deployment environment that configures settings via environment variables instead of User Secrets, use the double-underscore form of the matching key:

```
SeedAdmin__Password=<choose-a-strong-password>
SeedAdmin__SupabasePassword=<choose-a-different-strong-password>
```

Each setting is only read the first time its target's database needs to create the Admin account. It is never required again once that account exists, and startup never resets an existing account's password, role, or active status — a startup is a no-op only for whatever already exists; if a previous run skipped Admin creation (e.g. the password setting wasn't set yet), the next startup against that same database retries it rather than skipping forever.

Because Local and Supabase are separate databases (see "Database Setup" above), each one independently gets its own `admin@trailguard.com` account the first time it's seeded, using that target's own password setting. **Both databases legitimately containing an account with the same email is expected, not a conflict** — they are two distinct accounts that can (and, per the guidance above, should) have different passwords. When verifying a login against Supabase, use the Supabase target's own password (`SeedAdmin:SupabasePassword`) and confirm you're actually connected to Supabase (check the startup log's `Database target:` line) — don't assume success on one target confirms the other, and don't reuse the Local password when testing Supabase.

## Run the Application

Start the ML service **first**. It must run on the same address as `MlApi:BaseUrl` in `appsettings.json` (the committed setting is `http://127.0.0.1:8000`):

```bash
cd trailguard-ml-v2
python -m uvicorn main:app --reload --port 8000
```

Then, in a second terminal at the repository root, start the web application:

```bash
dotnet run
```

Assessments require the ML service. If it is unavailable or running at a different address, assessment submission returns an error and saves no result.

> Note: If you modify Tailwind CSS files, rebuild or watch the CSS assets as required by your project configuration.

---

# User Guide

## For Administrators

1. Log in using Administrator credentials.
2. Navigate to the **Accounts** section.
3. Add, edit, disable, or manage user accounts and roles.

## For Organizers

1. Log in and access the Organizer Dashboard.
2. Create and manage trail information under **Trails**.
3. Enter trail metrics to enable automatic difficulty calculation.
4. Create hiking events under **Events**.
5. Review participant applications under **Registrations**.
6. Use the Event Comparison feature to evaluate participant suitability before approval.

## For Participants

1. Create an account and complete your profile.
2. Browse available hiking events.
3. Register for an event and complete the readiness assessment.
4. Track registration status through **My Registrations**.
5. Participate in events and submit feedback afterward.

---

# License

This project is developed as an academic capstone project and is intended for educational purposes.
