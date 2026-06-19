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
The system uses a custom `DifficultyCalculator` service to objectively rate trails based on trail metrics such as distance and elevation gain.

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

- .NET SDK (compatible with ASP.NET Core)
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

## Database Setup

Update the connection string in `appsettings.json` if necessary, then apply the Entity Framework migrations:

```bash
dotnet ef database update
```

## Run the Application

```bash
dotnet run
```

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
