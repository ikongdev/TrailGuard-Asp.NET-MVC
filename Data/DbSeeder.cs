using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
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
            var context = serviceProvider.GetRequiredService<ApplicationDbContext>();

            Console.WriteLine("=========================================");
            Console.WriteLine("STARTING DATABASE SEEDING");
            Console.WriteLine($"Database: {context.Database.GetDbConnection().Database}");
            Console.WriteLine("=========================================");

            Console.WriteLine("📌 Seeding roles...");
            string[] roleNames = { "Admin", "Organizer", "Participant" };
            foreach (var roleName in roleNames)
            {
                if (!await roleManager.RoleExistsAsync(roleName))
                {
                    await roleManager.CreateAsync(new IdentityRole(roleName));
                    Console.WriteLine($"    Role '{roleName}' created");
                }
                else
                {
                    Console.WriteLine($"   ⏭ Role '{roleName}' already exists");
                }
            }

            // ============================================
            // 2. SEED ADMIN USER
            // ============================================
            Console.WriteLine("Seeding admin user...");
            string adminEmail = "admin@trailguard.com";
            var adminUser = await userManager.FindByEmailAsync(adminEmail);
            if (adminUser == null)
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
                var result = await userManager.CreateAsync(newAdmin, "Admin@123!");
                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(newAdmin, "Admin");
                    Console.WriteLine("Admin user created");
                }
                else
                {
                    Console.WriteLine("Failed to create admin user");
                }
            }
            else
            {
                Console.WriteLine("Admin user already exists");
            }

            // ============================================
            // 3. SEED ORGANIZER USER
            // ============================================
            Console.WriteLine("Seeding organizer user...");
            string orgEmail = "organizer@trailguard.com";
            var orgUser = await userManager.FindByEmailAsync(orgEmail);
            if (orgUser == null)
            {
                var newOrg = new ApplicationUser
                {
                    UserName = orgEmail,
                    Email = orgEmail,
                    FirstName = "Maria",
                    LastName = "Santos",
                    IsActive = true,
                    DateCreated = DateTime.Now,
                    EmailConfirmed = true,
                    PhoneNumber = "09171234567"
                };
                var result = await userManager.CreateAsync(newOrg, "Organizer@123!");
                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(newOrg, "Organizer");
                    Console.WriteLine(" Organizer user created");
                }
                else
                {
                    Console.WriteLine("Failed to create organizer user");
                }
            }
            else
            {
                Console.WriteLine("Organizer user already exists");
            }

            // ============================================
            // 4. SEED PARTICIPANT USER
            // ============================================
            Console.WriteLine("Seeding participant user...");
            string participantEmail = "participant@trailguard.com";
            var participantUser = await userManager.FindByEmailAsync(participantEmail);
            if (participantUser == null)
            {
                var newParticipant = new ApplicationUser
                {
                    UserName = participantEmail,
                    Email = participantEmail,
                    FirstName = "Juan",
                    LastName = "Dela Cruz",
                    IsActive = true,
                    DateCreated = DateTime.Now,
                    EmailConfirmed = true
                };
                var result = await userManager.CreateAsync(newParticipant, "Participant@123!");
                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(newParticipant, "Participant");
                    Console.WriteLine("Participant user created");
                }
                else
                {
                    Console.WriteLine("Failed to create participant user");
                }
            }
            else
            {
                Console.WriteLine("Participant user already exists");
            }

            // ============================================
            // 5. SEED TRAILS (WALANG DIFFICULTY)
            // ============================================
            Console.WriteLine("Seeding trails...");
            try
            {
                var trailCount = await context.Trails.CountAsync();
                Console.WriteLine($"Current trails count: {trailCount}");

                if (trailCount == 0)
                {
                    var trails = new List<Trail>
                    {
                        // Numeric values are the agency's Stage 1 reference table in PLAN.md.
                        // Terrain/description were not supplied; do not infer terrain from TrailClass.
                        new Trail
                        {
                            Name = "Mt. Pulag (Ambangeg)",
                            Location = "Kabayan, Benguet",
                            DistanceKm = 14.65,
                            ElevationGainMeters = 830,
                            TrailClass = 2,
                            TypicalDurationHours = 5.47m,
                            Terrain = "Not recorded",
                            Description = "Agency reference trail; description not recorded.",
                            IsActive = true,
                            DateAdded = DateTime.Now
                        },
                        new Trail
                        {
                            Name = "Mt. Pinatubo (crater)",
                            Location = "Capas, Tarlac",
                            DistanceKm = 11.27,
                            ElevationGainMeters = 625,
                            TrailClass = 1,
                            TypicalDurationHours = 4.17m,
                            Terrain = "Not recorded",
                            Description = "Agency reference trail; description not recorded.",
                            IsActive = true,
                            DateAdded = DateTime.Now
                        },
                        new Trail
                        {
                            Name = "Mt. Tapulao (Dampay)",
                            Location = "Palauig, Zambales",
                            DistanceKm = 29.13,
                            ElevationGainMeters = 2036,
                            TrailClass = 3,
                            TypicalDurationHours = 12.25m,
                            Terrain = "Not recorded",
                            Description = "Agency reference trail; description not recorded.",
                            IsActive = true,
                            DateAdded = DateTime.Now
                        },
                        new Trail
                        {
                            Name = "Mt. Talamitam",
                            Location = "Nasugbu, Batangas",
                            DistanceKm = 7.08,
                            ElevationGainMeters = 471,
                            TrailClass = 3,
                            TypicalDurationHours = 2.90m,
                            Terrain = "Not recorded",
                            Description = "Agency reference trail; description not recorded.",
                            IsActive = true,
                            DateAdded = DateTime.Now
                        },
                        new Trail
                        {
                            Name = "Mt. Makiling (UPLB)",
                            Location = "Los Baños, Laguna",
                            DistanceKm = 17.38,
                            ElevationGainMeters = 971,
                            TrailClass = 3,
                            TypicalDurationHours = 6.45m,
                            Terrain = "Not recorded",
                            Description = "Agency reference trail; description not recorded.",
                            IsActive = true,
                            DateAdded = DateTime.Now
                        },
                        new Trail
                        {
                            Name = "Mt. Ayaas (Mascap)",
                            Location = "Rodriguez, Rizal",
                            DistanceKm = 10.78,
                            ElevationGainMeters = 617,
                            TrailClass = 3,
                            TypicalDurationHours = 4.05m,
                            Terrain = "Not recorded",
                            Description = "Agency reference trail; description not recorded.",
                            IsActive = true,
                            DateAdded = DateTime.Now
                        },
                        new Trail
                        {
                            Name = "Mt. Pamitinan (Wawa)",
                            Location = "Rodriguez, Rizal",
                            DistanceKm = 3.06,
                            ElevationGainMeters = 308,
                            TrailClass = 4,
                            TypicalDurationHours = 1.62m,
                            Terrain = "Not recorded",
                            Description = "Agency reference trail; description not recorded.",
                            IsActive = true,
                            DateAdded = DateTime.Now
                        },
                        new Trail
                        {
                            Name = "Mt. Hapunang Banoi (Wawa)",
                            Location = "Rodriguez, Rizal",
                            DistanceKm = 3.54,
                            ElevationGainMeters = 431,
                            TrailClass = 4,
                            TypicalDurationHours = 2.15m,
                            Terrain = "Not recorded",
                            Description = "Agency reference trail; description not recorded.",
                            IsActive = true,
                            DateAdded = DateTime.Now
                        },
                        new Trail
                        {
                            Name = "Mt. Manalmon (Madlum)",
                            Location = "San Miguel, Bulacan",
                            DistanceKm = 3.70,
                            ElevationGainMeters = 150,
                            TrailClass = 2,
                            TypicalDurationHours = 1.17m,
                            Terrain = "Not recorded",
                            Description = "Agency reference trail; description not recorded.",
                            IsActive = true,
                            DateAdded = DateTime.Now
                        },
                        new Trail
                        {
                            Name = "Mt. Kitanglad (Intavas)",
                            Location = "Impasugong, Bukidnon",
                            DistanceKm = 16.09,
                            ElevationGainMeters = 1551,
                            TrailClass = 4,
                            TypicalDurationHours = 8.27m,
                            Terrain = "Not recorded",
                            Description = "Agency reference trail; description not recorded.",
                            IsActive = true,
                            DateAdded = DateTime.Now
                        },
                        new Trail
                        {
                            Name = "Mt. Dulang-Dulang (Bol-ogan)",
                            Location = "Lantapan, Bukidnon",
                            DistanceKm = 16.09,
                            ElevationGainMeters = 1543,
                            TrailClass = 4,
                            TypicalDurationHours = 8.23m,
                            Terrain = "Not recorded",
                            Description = "Agency reference trail; description not recorded.",
                            IsActive = true,
                            DateAdded = DateTime.Now
                        },
                        new Trail
                        {
                            Name = "Mt. Tagapo (Janosa)",
                            Location = "Binangonan, Rizal",
                            DistanceKm = 5.47,
                            ElevationGainMeters = 402,
                            TrailClass = 2,
                            TypicalDurationHours = 2.37m,
                            Terrain = "Not recorded",
                            Description = "Agency reference trail; description not recorded.",
                            IsActive = true,
                            DateAdded = DateTime.Now
                        }
                    };

                    Console.WriteLine($"Adding {trails.Count} trails...");
                    await context.Trails.AddRangeAsync(trails);
                    await context.SaveChangesAsync();
                    Console.WriteLine($"{trails.Count} trails added successfully!");
                }
                else
                {
                    Console.WriteLine("Trails already exist, skipping...");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error seeding trails: {ex.Message}");
            }

            // The former sample Events referenced the retired development catalog.
            // Create Events through Add Event after reseeding the agency trails.

            // // ============================================
            // // 7. SEED REGISTRATIONS
            // // ============================================
            // Console.WriteLine("Seeding registrations...");
            // try
            // {
            //     var regCount = await context.EventRegistrations.CountAsync();
            //     Console.WriteLine($"Current registrations count: {regCount}");

            //     if (regCount == 0)
            //     {
            //         var events = await context.Events.ToListAsync();
            //         var participant = await userManager.FindByEmailAsync("participant@trailguard.com");

            //         if (events.Any() && participant != null)
            //         {
            //             var registrations = new List<EventRegistration>();
            //             var random = new Random();

            //             foreach (var ev in events.Take(2))
            //             {
            //                 registrations.Add(new EventRegistration
            //                 {
            //                     EventId = ev.Id,
            //                     UserId = participant.Id,
            //                     ParticipantName = $"{participant.FirstName} {participant.LastName}",
            //                     PickupPoint = ev.PickupPoints?.Split('\n').FirstOrDefault()?.Trim() ?? "Main Pickup",
            //                     IsPaid = random.Next(0, 2) == 1,
            //                     Status = "Accepted",
            //                     RegisteredAt = DateTime.Now.AddDays(-random.Next(1, 5)),
            //                     EmergencyContactName = "Emergency Contact",
            //                     EmergencyContactNumber = "09171234567"
            //                 });
            //             }

            //             Console.WriteLine($"Adding {registrations.Count} registrations...");
            //             await context.EventRegistrations.AddRangeAsync(registrations);
            //             await context.SaveChangesAsync();
            //             Console.WriteLine($"{registrations.Count} registrations added successfully!");
            //         }
            //         else
            //         {
            //             Console.WriteLine("Cannot seed registrations: Missing events or participant");
            //         }
            //     }
            //     else
            //     {
            //         Console.WriteLine("Registrations already exist, skipping...");
            //     }
            // }
            // catch (Exception ex)
            // {
            //     Console.WriteLine($"Error seeding registrations: {ex.Message}");
            // }

            Console.WriteLine("=========================================");
            Console.WriteLine("✅ DATABASE SEEDING COMPLETED!");
            Console.WriteLine("=========================================");
        }
    }
}