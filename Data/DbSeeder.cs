using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using TrailGuard.Models;
using TrailGuard.Services;

namespace TrailGuard.Data
{
    public static class DbSeeder
    {



        public static async Task SeedRolesAndAdminAsync(IServiceProvider serviceProvider)
        {
            var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var userManager = serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var roleAssignmentService = serviceProvider.GetRequiredService<RoleAssignmentService>();
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();
            var databaseTarget = serviceProvider.GetRequiredService<ResolvedDatabaseTarget>();

            Console.WriteLine("=========================================");
            Console.WriteLine("STARTING DATABASE SEEDING");
            Console.WriteLine("=========================================");


            Console.WriteLine($"Database target: {databaseTarget.SafeEndpointDescription}");




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




            Console.WriteLine("Seeding initial Admin account...");
            const string adminEmail = "admin@trailguard.com";
            var existingAdmin = await userManager.FindByEmailAsync(adminEmail);
            if (existingAdmin != null)
            {



                Console.WriteLine("Admin account already exists - skipping (no changes made).");
            }
            else
            {






                var passwordKey = databaseTarget.SeedAdminPasswordKey;
                var adminPassword = configuration[passwordKey];
                if (string.IsNullOrEmpty(adminPassword))
                {
                    var envVarName = passwordKey.Replace(":", "__");
                    Console.WriteLine($"{passwordKey} is not configured. Skipping initial Admin account creation.");
                    Console.WriteLine($"Set it via 'dotnet user-secrets set \"{passwordKey}\" \"<password>\"' (local) " +
                        $"or the {envVarName} environment variable (deployment), then restart the application.");
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
