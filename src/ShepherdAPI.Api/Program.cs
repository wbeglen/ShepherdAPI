using Microsoft.OpenApi;
using ShepherdAPI.Api.Data;
using ShepherdAPI.Api.Infrastructure;
using ShepherdAPI.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "ShepherdAPI",
        Version = "v1",
        Description = "Durable API boundary for Good Shepherd Alpha: recording and retrieval of " +
                       "experiment scores, notes, and testing evidence metadata over the existing " +
                       "Experiments database. See docs/ARCHITECTURE.md for the approved architecture.",
    });
});

builder.Services.AddSingleton<ISqlConnectionFactory, SqlConnectionFactory>();
builder.Services.AddScoped<ILookupRepository, LookupRepository>();
builder.Services.AddScoped<IExperimentDesignRepository, ExperimentDesignRepository>();
builder.Services.AddScoped<IRecordingRepository, RecordingRepository>();
builder.Services.AddScoped<ILockGuardQueries, LockGuardQueries>();
builder.Services.AddScoped<IExperimentDesignService, ExperimentDesignService>();
builder.Services.AddScoped<IRecordingService, RecordingService>();

var app = builder.Build();

app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.MapControllers();

app.Run();

// Exposed for WebApplicationFactory<Program> in integration tests.
public partial class Program;
