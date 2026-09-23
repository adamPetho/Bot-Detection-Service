using Bot_Detection_Service.Models;
using Bot_Detection_Service.Services;

namespace Bot_Detection_Service
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddSingleton<RiskCalculator>();
            builder.Services.AddControllers();

            // Wide-open CORS for testing.
            // Set up proper CORS on production.
#if DEBUG
            builder.Services.AddCors(options =>
            {
                options.AddPolicy("DevTestPage", policy =>
                    policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
            });
#endif

            var app = builder.Build();

#if DEBUG
            app.UseCors("DevTestPage");
#endif

            app.MapPost("/api/score", (BotCheckRequest req, RiskCalculator scorer) =>
            {
                var result = scorer.Score(req.Features);

                return Results.Ok(new
                {
                    req.SessionId,
                    result.Score,
                    Action = result.Action.ToString(),
                    result.Reasons,
                });
            });

            // Liveness probe for the container orchestrator. Kept trivial on purpose.
            app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

            app.Run();
        }
    }

    public sealed record BotCheckRequest(string SessionId, BotFeatures Features);
}
