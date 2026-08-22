using DatingApp.Llm;
using DatingApp.Shared.Middleware;
using FluentValidation;
using MessagingService.Data;
using MessagingService.Extensions;
using MessagingService.Hubs;
using MessagingService.Middleware;
using MessagingService.Services;
using MessagingService.Common;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Threading.Tasks;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithEnvironmentName()
    .Enrich.WithMachineName()
    .Enrich.WithCorrelationId()
    .Enrich.WithProperty("ServiceName", "MessagingService")
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
    .MinimumLevel.Override("System", LogEventLevel.Warning)
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{ServiceName}] [{CorrelationId}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        path: "logs/messaging-service-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 7,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{ServiceName}] [{CorrelationId}] {Message:lj}{NewLine}{Exception}"
    ));

// Add services to the container.
var messagingDbConnectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(messagingDbConnectionString))
{
    throw new InvalidOperationException(
        "MessagingService requires a configured ConnectionStrings:DefaultConnection (MySQL).");
}
builder.Services.AddDbContext<MessagingDbContext>(options =>
    options.UseMySql(
        messagingDbConnectionString,
        new MySqlServerVersion(new Version(8, 0, 25))
    )
);

// Add Authentication
builder.Services.AddKeycloakAuthentication(builder.Configuration, options =>
{
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) && (path.StartsWithSegments("/hubs/messages") || path.StartsWithSegments("/messagingHub")))
            {
                context.Token = accessToken;
            }
            return Task.CompletedTask;
        }
    };

    options.TokenValidationParameters.ClockSkew = TimeSpan.Zero;
});

// Add Authorization
builder.Services.AddAuthorization();

// Add Controllers
builder.Services.AddControllers();

// Add Health Checks
builder.Services.AddHealthChecks();

// Add SignalR
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = true;
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
});

// Add Custom Services
builder.Services.AddScoped<IMessageService, MessageService>();
builder.Services.AddScoped<IMessageServiceSpec, MessageServiceSpec>();
builder.Services.AddScoped<IContentModerationService, ContentModerationService>();
builder.Services.AddScoped<ISpamDetectionService, SpamDetectionService>();
builder.Services.AddScoped<IPersonalInfoDetectionService, PersonalInfoDetectionService>();
builder.Services.AddScoped<IRateLimitingService, RateLimitingService>();
builder.Services.AddScoped<IReportingService, ReportingService>();
builder.Services.AddScoped<IMatchValidationService, MatchValidationService>();
builder.Services.AddSingleton<IUserIdentityResolver, UserIdentityResolver>();
builder.Services.AddCorrelationIds();
builder.Services.AddSingleton<MessagingService.Services.IPresenceTracker, MessagingService.Services.InMemoryPresenceTracker>();

// LLM providers and router
builder.Services.AddLlm(builder.Configuration);
builder.Services.AddScoped<ISafetyAgentService, SafetyAgentService>();

// Internal API Key Authentication for service-to-service calls
builder.Services.AddScoped<InternalApiKeyAuthFilter>();
builder.Services.AddTransient<InternalApiKeyAuthHandler>();

// Add HttpClient for Safety Service
builder.Services.AddHttpClient<ISafetyServiceClient, SafetyServiceClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Gateway:BaseUrl"] ?? "http://dejting-yarp:8080");
})
.AddHttpMessageHandler<InternalApiKeyAuthHandler>();

// Add HttpClient for MessageServiceSpec (to call MatchmakingService)
builder.Services.AddHttpClient<MessageServiceSpec>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Gateway:BaseUrl"] ?? "http://dejting-yarp:8080");
})
.AddHttpMessageHandler<InternalApiKeyAuthHandler>();

// Add HttpClient for GhostDetectionService (to call ReputationService)
builder.Services.AddHttpClient("ReputationService", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Gateway:BaseUrl"] ?? "http://dejting-yarp:8080");
})
.AddHttpMessageHandler<InternalApiKeyAuthHandler>();

// Add ghost detection background service
builder.Services.AddHostedService<GhostDetectionService>();

// Add HttpClient for MatchValidationService (to call SwipeService)
builder.Services.AddHttpClient("SwipeService", client =>
{
    client.BaseAddress = new Uri("http://localhost:8087");
    client.Timeout = TimeSpan.FromSeconds(5);
})
.AddHttpMessageHandler<InternalApiKeyAuthHandler>();

// Add MediatR for CQRS
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(Program).Assembly));

// Add FluentValidation
builder.Services.AddValidatorsFromAssembly(typeof(Program).Assembly);

// Add Memory Cache for rate limiting and content caching
builder.Services.AddMemoryCache();

// CORS: config-driven origins — localhost-only in dev, restricted in staging/production
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowSpecificOrigins", policy =>
    {
        if (allowedOrigins != null && allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyMethod()
                  .AllowAnyHeader()
                  .AllowCredentials();
        }
        else
        {
            policy.SetIsOriginAllowed(origin => new Uri(origin).Host == "localhost")
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        }
    });
});

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Dating App Messaging Service API",
        Version = "v1",
        Description = "Real-time messaging service with proactive safety features including content moderation, spam detection, and personal information protection."
    });

    // JWT Authentication
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using Bearer scheme. Enter 'Bearer' [space] and then your token.",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });

    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        c.IncludeXmlComments(xmlPath);
    }
});

// Configure OpenTelemetry for metrics and distributed tracing
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService(serviceName: "messaging-service",
                    serviceVersion: "1.0.0"))
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddMeter("MessagingService")
        .AddPrometheusExporter())
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation(options =>
        {
            options.RecordException = true;
            options.Filter = (httpContext) =>
            {
                // Don't trace health checks and metrics endpoints
                var path = httpContext.Request.Path.ToString();
                return !path.Contains("/health") && !path.Contains("/metrics");
            };
        })
        .AddHttpClientInstrumentation()
        .AddEntityFrameworkCoreInstrumentation(options =>
        {
            options.SetDbStatementForText = true;
            options.EnrichWithIDbCommand = (activity, command) =>
            {
                activity.SetTag("db.query", command.CommandText);
            };
        }));

// Create custom meters for business metrics

// Register injectable business metrics
builder.Services.AddSingleton<MessagingService.Metrics.MessagingServiceMetrics>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseCors("AllowSpecificOrigins");

app.UseCorrelationIds();

// Add custom rate limiting middleware
app.UseMiddleware<RateLimitingMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapHealthChecks("/health");

// Prometheus metrics endpoint
app.MapPrometheusScrapingEndpoint("/metrics");

// Map SignalR hub - Use the spec-compliant hub
app.MapHub<MessagingHubSpec>("/hubs/messages");
app.MapHub<MessagingHubSpec>("/messagingHub"); // Flutter compat alias

// Ensure database is created
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<MessagingDbContext>();
    context.Database.EnsureCreated();

    // EnsureCreated is a no-op on existing DBs, so add the bot-flag column idempotently.
    // This lets the targeted bot-data purge (DELETE /api/admin/bot-messages) work on
    // databases that were created before the IsBotGenerated column existed.
    try
    {
        await context.Database.ExecuteSqlRawAsync(@"
            SET @col := (SELECT COUNT(*) FROM information_schema.COLUMNS
                WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'Messages' AND COLUMN_NAME = 'IsBotGenerated');
            SET @ddl := IF(@col = 0,
                'ALTER TABLE Messages ADD COLUMN IsBotGenerated TINYINT(1) NOT NULL DEFAULT 0',
                'SELECT 1');
            PREPARE s FROM @ddl; EXECUTE s; DEALLOCATE PREPARE s;");
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Could not apply additive IsBotGenerated column (may already exist)");
    }

    // Create the MessageReactions table (message likes) idempotently.
    try
    {
        await context.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS MessageReactions (
                Id INT AUTO_INCREMENT PRIMARY KEY,
                MessageId INT NOT NULL,
                UserId VARCHAR(36) NOT NULL,
                Reaction VARCHAR(20) NOT NULL DEFAULT 'like',
                CreatedAt DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                UNIQUE KEY UX_MessageReactions_Message_User (MessageId, UserId),
                KEY IX_MessageReactions_MessageId (MessageId)
            );");
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Could not create MessageReactions table");
    }
}

app.Run();
