using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using TrailGuard.Models;
using TrailGuard.Services;

namespace TrailGuard.Data
{
    public static class DbSeeder
    {
        // Seeds only the three operational roles and the initial Admin
        // account (admin@trailguard.com). No Organizer/Participant sample
        // accounts, Trails, Events, or registrations are seeded.
        public static async Task SeedRolesAndAdminAsync(IServiceProvider serviceProvider)
        {
            var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var userManager = serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var roleAssignmentService = serviceProvider.GetRequiredService<RoleAssignmentService>();
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();

            Console.WriteLine("=========================================");
            Console.WriteLine("STARTING DATABASE SEEDING");
            Console.WriteLine("=========================================");

            // ============================================
            // 1. SEED OPERATIONAL ROLES
            // ============================================
            Console.WriteLine("Seeding roles...");
            foreach (var roleName in OperationalRolePolicy.AllowedRoles)
            {
                if (!await roleManager.RoleExistsAsync(roleName))
                {
                    var roleResult = await roleManager.CreateAsync(new IdentityRole(roleName));
                    if (roleResult.Succeeded)
                    {
                        Console.WriteLine($"    Role '{roleName}' created");
                    }
                    else
                    {
                        // IdentityResult descriptions are already safe, user-facing
                        // text - never a raw exception or credential.
                        Console.WriteLine($"Failed to create role '{roleName}': " +
                            string.Join("; ", roleResult.Errors.Select(e => e.Description)));
                        Console.WriteLine("Stopping this seeding attempt before Admin creation - required roles are incomplete.");
                        return;
                    }
                }
                else
                {
                    Console.WriteLine($"   Role '{roleName}' already exists");
                }
            }

            // ============================================
            // 2. SEED INITIAL ADMIN ACCOUNT
            // ============================================
            Console.WriteLine("Seeding initial Admin account...");
            const string adminEmail = "admin@trailguard.com";
            var existingAdmin = await userManager.FindByEmailAsync(adminEmail);
            if (existingAdmin != null)
            {
                // Existing account: never touched. Its password, roles, and
                // active status are left exactly as they are - this seeder
                // only ever creates the account once.
                Console.WriteLine("Admin account already exists - skipping (no changes made).");
            }
            else
            {
                // Required only for this creation path - an already-existing
                // Admin account never needs it, so a missing setting on a
                // later, already-seeded startup is not an error.
                var adminPassword = configuration["SeedAdmin:Password"];
                if (string.IsNullOrEmpty(adminPassword))
                {
                    Console.WriteLine("SeedAdmin:Password is not configured. Skipping initial Admin account creation.");
                    Console.WriteLine("Set it via 'dotnet user-secrets set \"SeedAdmin:Password\" \"<password>\"' (local) " +
                        "or the SeedAdmin__Password environment variable (deployment), then restart the application.");
                }
                else
                {
                    var newAdmin = new ApplicationUser
                    {
                        UserName = adminEmail,
                        Email = adminEmail,
                        FirstName = "System",
                        LastName = "Admin",
                        IsActive = true,
                        DateCreated = DateTime.Now,
                        EmailConfirmed = true
                    };

                    var result = await roleAssignmentService.CreateAccountWithRoleAsync(newAdmin, adminPassword, "Admin");
                    if (result.Succeeded)
                    {
                        Console.WriteLine("Admin account created.");
                    }
                    else if (result.IdentityErrors.Count > 0)
                    {
                        // IdentityResult descriptions are already safe, user-facing
                        // text (e.g. password policy violations) - never the
                        // submitted password itself.
                        Console.WriteLine("Failed to create Admin account: " + string.Join("; ", result.IdentityErrors));
                    }
                    else
                    {
                        Console.WriteLine("Failed to create Admin account: " + (result.GenericError ?? "Unknown error."));
                    }
                }
            }

            Console.WriteLine("=========================================");
            Console.WriteLine("DATABASE SEEDING ATTEMPT COMPLETE");
            Console.WriteLine("=========================================");
        }
    }
}
