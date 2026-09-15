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

Store the PostgreSQL connection string in User Secrets, then apply the Entity Framework migrations:

```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<PostgreSQL connection string>"
```

```bash
dotnet ef database update
```

## Initial Admin Account

On first startup, `DbSeeder` creates exactly one account — `admin@trailguard.com` with the Admin role — and nothing else (no Organizer/Participant sample accounts, no Trails, no Events). It reads the account's password from configuration only; there is no hardcoded or default password. Set it before the first run:

```bash
dotnet user-secrets set "SeedAdmin:Password" "<choose-a-strong-password>"
```

For a deployment environment that configures settings via environment variables instead of User Secrets, use the double-underscore form of the same key:

```
SeedAdmin__Password=<choose-a-strong-password>
```

This setting is only read the first time the seeder needs to create the Admin account. It is never required again, and startup never resets an existing account's password, role, or active status — every run after the first is a no-op for seeding.

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
