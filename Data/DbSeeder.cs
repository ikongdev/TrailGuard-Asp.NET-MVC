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
                        // TrailClass is a placeholder pending organizer review, same as the
                        // rest of this seed data - not a substitute for a real PinoyMountaineer
                        // classification.
                        new Trail
                        {
                            Name = "Mt. Ulap",
                            Location = "Itogon, Benguet",
                            DistanceKm = 8.5,
                            ElevationGainMeters = 700,
                            Terrain = "Grassland, Pine Forest",
                            TrailClass = 2, // Hiking
                            Description = "A scenic trail with breathtaking views of the Cordillera mountains.",
                            IsActive = true,
                            DateAdded = DateTime.Now
                        },
                        new Trail
                        {
                            Name = "Mt. Pulag",
                            Location = "Kabayan, Benguet",
                            DistanceKm = 12.5,
                            ElevationGainMeters = 1200,
                            Terrain = "Mossy Forest, Grassland",
                            TrailClass = 2, // Hiking (Ambangeg trail)
                            Description = "The highest peak in Luzon. Known for its sea of clouds.",
                            IsActive = true,
                            DateAdded = DateTime.Now
                        },
                        new Trail
                        {
                            Name = "Mt. Batulao",
                            Location = "Nasugbu, Batangas",
                            DistanceKm = 6.5,
                            ElevationGainMeters = 550,
                            Terrain = "Grassland, Rocky",
                            TrailClass = 2, // Hiking
                            Description = "A popular day hike with panoramic views of Batangas.",
                            IsActive = true,
                            DateAdded = DateTime.Now
                        },
                        new Trail
                        {
                            Name = "Mt. Makiling",
                            Location = "Los Baños, Laguna",
                            DistanceKm = 10.0,
                            ElevationGainMeters = 800,
                            Terrain = "Forest, Rocky",
                            TrailClass = 2, // Hiking
                            Description = "A well-known trail with diverse flora and fauna.",
                            IsActive = true,
                            DateAdded = DateTime.Now
                        },
                        new Trail
                        {
                            Name = "Mt. Daraitan",
                            Location = "Tanay, Rizal",
                            DistanceKm = 5.5,
                            ElevationGainMeters = 400,
                            Terrain = "Forest, Rocky, River",
                            TrailClass = 2, // Hiking
                            Description = "Features a crystal-clear river and limestone formations.",
                            IsActive = true,
                            DateAdded = DateTime.Now
                        },
                        new Trail
                        {
                            Name = "Mt. Pinatubo",
                            Location = "Zambales",
                            DistanceKm = 7.0,
                            ElevationGainMeters = 300,
                            Terrain = "Lahar, Rocky",
                            TrailClass = 1, // Walking (flat lahar riverbed trek)
                            Description = "Famous for its crater lake and unique lahar landscape.",
                            IsActive = true,
                            DateAdded = DateTime.Now
                        },
                        new Trail
                        {
                            Name = "Mt. Arayat",
                            Location = "Arayat, Pampanga",
                            DistanceKm = 8.0,
                            ElevationGainMeters = 900,
                            Terrain = "Forest, Rocky",
                            TrailClass = 3, // Scrambling (rocky, root-climbing sections near the summit)
                            Description = "A dormant volcano with a challenging trail.",
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

            // ============================================
            // 6. SEED EVENTS (NASA EVENTS ANG DIFFICULTY)
            // ============================================
            Console.WriteLine("Seeding events...");
            try
            {
                var eventCount = await context.Events.CountAsync();
                Console.WriteLine($"   📊 Current events count: {eventCount}");

                if (eventCount == 0)
                {
                    var trails = await context.Trails.ToListAsync();
                    var organizer = await userManager.FindByEmailAsync("organizer@trailguard.com");
                    var organizerId = organizer?.Id;

                    if (organizerId != null && trails.Any())
                    {
                        var ulapTrail = trails.First(t => t.Name == "Mt. Ulap");
                        var pulagTrail = trails.First(t => t.Name == "Mt. Pulag");
                        var batulaoTrail = trails.First(t => t.Name == "Mt. Batulao");
                        var daraitanTrail = trails.First(t => t.Name == "Mt. Daraitan");

                        // TrailId/Location/Difficulty and every Trail*Snapshot field are
                        // captured via EventTrailSnapshotHelper immediately below, the
                        // same central helper Add Event/Edit Event use - see CLAUDE.md,
                        // "Event Trail Snapshot".
                        var events = new List<Event>
                        {
                            new Event
                            {
                                EventTitle = "Mt. Ulap Sunrise Hike",
                                Description = "Join us for an early morning hike to witness the beautiful sunrise at Mt. Ulap.",
                                EventDate = DateTime.Now.AddDays(14),
                                EventTime = new TimeSpan(4, 0, 0),
                                EstimatedDuration = 5,
                                Capacity = 20,
                                OrganizedBy = organizerId,
                                Status = "Upcoming",
                                WeatherForecastAdvisory = "Partly cloudy, 15-20°C",
                                NotesAndReminders = "Bring water and trail food. Meeting: 3:30 AM.",
                                PaymentDetails = "PHP 500. BDO: 1234567890",
                                PickupPoints = "1. McDonald's Trinoma - 2:30 AM\n2. Shell Balintawak - 3:00 AM",
                                DateCreated = DateTime.Now,
                                DateUpdated = DateTime.Now
                            },
                            new Event
                            {
                                EventTitle = "Mt. Pulag Weekend Climb",
                                Description = "A 2-day adventure to the highest peak in Luzon.",
                                EventDate = DateTime.Now.AddDays(21),
                                EventTime = new TimeSpan(6, 0, 0),
                                EstimatedDuration = 8,
                                Capacity = 15,
                                OrganizedBy = organizerId,
                                Status = "Upcoming",
                                WeatherForecastAdvisory = "Cold, 5-15°C",
                                NotesAndReminders = "Overnight camping. Bring tent and sleeping bag.",
                                PaymentDetails = "PHP 2,500. BDO: 1234567890",
                                PickupPoints = "1. Victory Liner Cubao - 4:00 AM",
                                DateCreated = DateTime.Now,
                                DateUpdated = DateTime.Now
                            },
                            new Event
                            {
                                EventTitle = "Mt. Batulao Day Hike",
                                Description = "A quick day hike perfect for beginners.",
                                EventDate = DateTime.Now.AddDays(7),
                                EventTime = new TimeSpan(5, 30, 0),
                                EstimatedDuration = 4,
                                Capacity = 25,
                                OrganizedBy = organizerId,
                                Status = "Upcoming",
                                WeatherForecastAdvisory = "Sunny, 25-30°C",
                                NotesAndReminders = "Bring trail food and 2L water.",
                                PaymentDetails = "PHP 350. BDO: 1234567890",
                                PickupPoints = "1. McDonald's Macapagal - 4:00 AM\n2. Shell SLEX - 4:30 AM",
                                DateCreated = DateTime.Now,
                                DateUpdated = DateTime.Now
                            },
                            new Event
                            {
                                EventTitle = "Mt. Daraitan River Trek",
                                Description = "Experience the scenic river and limestone formations.",
                                EventDate = DateTime.Now.AddDays(10),
                                EventTime = new TimeSpan(5, 0, 0),
                                EstimatedDuration = 4.5,
                                Capacity = 20,
                                OrganizedBy = organizerId,
                                Status = "Upcoming",
                                WeatherForecastAdvisory = "Fair weather, 22-28°C",
                                NotesAndReminders = "Bring extra clothes for river crossing.",
                                PaymentDetails = "PHP 400. BDO: 1234567890",
                                PickupPoints = "1. SM East Ortigas - 3:30 AM",
                                DateCreated = DateTime.Now,
                                DateUpdated = DateTime.Now
                            }
                        };

                        EventTrailSnapshotHelper.CaptureSnapshot(events[0], ulapTrail);
                        EventTrailSnapshotHelper.CaptureSnapshot(events[1], pulagTrail);
                        EventTrailSnapshotHelper.CaptureSnapshot(events[2], batulaoTrail);
                        EventTrailSnapshotHelper.CaptureSnapshot(events[3], daraitanTrail);

                        Console.WriteLine($"Adding {events.Count} events...");
                        await context.Events.AddRangeAsync(events);
                        await context.SaveChangesAsync();
                        Console.WriteLine($"{events.Count} events added successfully!");
                    }
                    else
                    {
                        Console.WriteLine("Cannot seed events: Missing organizer or trails");
                    }
                }
                else
                {
                    Console.WriteLine("Events already exist, skipping...");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error seeding events: {ex.Message}");
            }

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