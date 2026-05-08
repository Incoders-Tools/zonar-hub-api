using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using ZonarHub.ApiService.Endpoints.Admin.Onboarding;
using ZonarHub.ApiService.Endpoints.Admin.Dashboard;
using ZonarHub.ApiService.Endpoints.Admin.Catalog.Categories;
using ZonarHub.ApiService.Endpoints.Admin.Catalog.Complexes;
using ZonarHub.ApiService.Endpoints.Admin.Catalog.Courts;
using ZonarHub.ApiService.Endpoints.Admin.Catalog.Genders;
using ZonarHub.ApiService.Endpoints.Admin.Catalog.Sports;
using ZonarHub.ApiService.Endpoints.Admin.Catalog.TournamentModalities;
using ZonarHub.ApiService.Endpoints.Admin.Catalog.TournamentStatuses;
using ZonarHub.ApiService.Endpoints.Admin.System.EmailTemplates;
using ZonarHub.ApiService.Endpoints.Admin.System.Organizations;
using ZonarHub.ApiService.Endpoints.Admin.System.Permissions;
using ZonarHub.ApiService.Endpoints.Admin.System.Roles;
using ZonarHub.ApiService.Endpoints.Admin.System.Tenants;
using ZonarHub.ApiService.Endpoints.Admin.System.Users;
using ZonarHub.ApiService.Endpoints.Admin.Circuit.Tournaments;
using ZonarHub.ApiService.Endpoints.Auth;
using ZonarHub.ApiService.Endpoints.Common;
using ZonarHub.ApiService.Endpoints.External;
using ZonarHub.ApiService.Endpoints.SystemSettings;
using ZonarHub.ApiService.Endpoints.UserPreferences;
using ZonarHub.Application;
using ZonarHub.Infrastructure.Auth;
using ZonarHub.Infrastructure.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ValidationExceptionHandler>();
builder.Services.AddOpenApi();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(
                  "http://localhost:4200", "https://localhost:4200",
                  "http://127.0.0.1:4200", "https://127.0.0.1:4200"
              )
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

var jwtSection = builder.Configuration.GetSection(JwtOptions.SectionName);
var jwtSecret = jwtSection["Secret"] ?? string.Empty;

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opts =>
    {
        opts.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSection["Issuer"] ?? "ZonarHub",
            ValidAudience = jwtSection["Audience"] ?? "ZonarHub",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization(opts =>
{
    opts.AddPolicy("AdminOrAbove", policy =>
        policy.RequireClaim(System.Security.Claims.ClaimTypes.Role, "admin", "system_admin"));

    opts.AddPolicy("SystemAdminOnly", policy =>
        policy.RequireClaim(System.Security.Claims.ClaimTypes.Role, "system_admin"));
});

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/", () => "API service is running.");

app.MapAuthEndpoints();
app.MapSystemSettingsEndpoints();
app.MapUserPreferencesEndpoints();
app.MapExternalEndpoints();
app.MapOrganizationsEndpoints();
app.MapOrganizationSportsEndpoints();
app.MapTenantSportsEndpoints();
app.MapSportsEndpoints();
app.MapAdminUsersEndpoints();
app.MapAdminPermissionsEndpoints();
app.MapOnboardingEndpoints();
app.MapEmailTemplatesEndpoints();
app.MapCourtsEndpoints();
app.MapComplexesEndpoints();
app.MapTournamentStatusesEndpoints();
app.MapTournamentModalitiesEndpoints();
app.MapAdminTournamentsEndpoints();
app.MapAdminRolesEndpoints();
app.MapAdminDashboardEndpoints();
app.MapCatalogGendersEndpoints();
app.MapCatalogCategoriesEndpoints();

app.MapDefaultEndpoints();

app.Run();
