using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Oaza.Domain.Constants;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;
using Oaza.Domain.Interfaces;
using Oaza.Functions.Attributes;

namespace Oaza.Functions.Endpoints;

public class SeedFunctions
{
    private readonly IUserRepository _userRepository;
    private readonly IHouseRepository _houseRepository;
    private readonly IWaterMeterRepository _meterRepository;
    private readonly ILogger<SeedFunctions> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly bool _seedEnabled;

    public SeedFunctions(
        IUserRepository userRepository,
        IHouseRepository houseRepository,
        IWaterMeterRepository meterRepository,
        ILogger<SeedFunctions> logger)
    {
        _userRepository = userRepository;
        _houseRepository = houseRepository;
        _meterRepository = meterRepository;
        _logger = logger;
        _seedEnabled = string.Equals(
            Environment.GetEnvironmentVariable("ENABLE_SEED"), "true",
            StringComparison.OrdinalIgnoreCase);
    }

    [Function("SeedData")]
    [RequireRole(UserRole.Admin)]
    public async Task<HttpResponseData> SeedDataAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "seed")] HttpRequestData req,
        FunctionContext context)
    {
        try
        {
            if (!_seedEnabled)
            {
                var disabledResponse = req.CreateResponse(HttpStatusCode.NotFound);
                return disabledResponse;
            }

            var existingHouses = await _houseRepository.GetByPartitionKeyAsync(PartitionKeys.House);
            if (existingHouses.Count > 0)
            {
                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new { message = "Data already seeded.", housesCount = existingHouses.Count });
                return response;
            }

            // Create 8 houses (Zadní Kopanina 142-149)
            var houses = new List<House>();
            var houseNames = new[]
            {
                ("Čendelínovi (142)", "Zadní Kopanina 142, Praha 5", "Rosťa Čendelín", "rostislav@cendelinovi.cz"),
                ("Novákovi (143)", "Zadní Kopanina 143, Praha 5", "Jan Novák", "novak@example.cz"),
                ("Svobodovi (144)", "Zadní Kopanina 144, Praha 5", "Petr Svoboda", "svoboda@example.cz"),
                ("Dvořákovi (145)", "Zadní Kopanina 145, Praha 5", "Marie Dvořáková", "dvorakova@example.cz"),
                ("Černí (146)", "Zadní Kopanina 146, Praha 5", "Tomáš Černý", "cerny@example.cz"),
                ("Procházkovi (147)", "Zadní Kopanina 147, Praha 5", "Eva Procházková", "prochazkova@example.cz"),
                ("Kučerovi (148)", "Zadní Kopanina 148, Praha 5", "Karel Kučera", "kucera@example.cz"),
                ("Veselí (149)", "Zadní Kopanina 149, Praha 5", "Lucie Veselá", "vesela@example.cz"),
            };

            foreach (var (name, address, contact, email) in houseNames)
            {
                var house = new House
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = name,
                    Address = address,
                    ContactPerson = contact,
                    Email = email,
                    IsActive = true,
                };
                await _houseRepository.UpsertAsync(house);
                houses.Add(house);
            }

            _logger.LogInformation("Seeded {Count} houses", houses.Count);

            // Create 1 main water meter
            var mainMeter = new WaterMeter
            {
                Id = Guid.NewGuid().ToString(),
                MeterNumber = "HV-001",
                Type = MeterType.Main,
                HouseId = null,
                InstallationDate = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            };
            await _meterRepository.UpsertAsync(mainMeter);

            // Create 8 individual meters (one per house)
            var meterIndex = 1;
            foreach (var house in houses)
            {
                var meter = new WaterMeter
                {
                    Id = Guid.NewGuid().ToString(),
                    MeterNumber = $"DV-{meterIndex:D3}",
                    Type = MeterType.Individual,
                    HouseId = house.Id,
                    InstallationDate = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                };
                await _meterRepository.UpsertAsync(meter);
                meterIndex++;
            }

            _logger.LogInformation("Seeded 9 water meters (1 main + 8 individual)");

            // Create admin user
            var adminUser = new User
            {
                Id = Guid.NewGuid().ToString(),
                Name = "Rosťa Čendelín",
                Email = "rostislav@cendelinovi.cz",
                Role = UserRole.Admin,
                HouseId = houses[0].Id,
                AuthMethod = AuthMethod.EntraId,
                NotificationsEnabled = true,
            };
            await _userRepository.UpsertAsync(adminUser);

            _logger.LogInformation("Seeded admin user: {Name}", adminUser.Name);

            var successResponse = req.CreateResponse(HttpStatusCode.Created);
            await successResponse.WriteAsJsonAsync(new
            {
                message = "Seed data created successfully.",
                houses = houses.Count,
                meters = meterIndex,
                adminUser = adminUser.Email,
            });
            return successResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Seed data failed");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "An unexpected error occurred." });
            return errorResponse;
        }
    }
}
