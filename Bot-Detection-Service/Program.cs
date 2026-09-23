using Bot_Detection_Service.Services;
using BotDetection;

namespace Bot_Detection_Service
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddSingleton<RiskCalculator>();
            builder.Services.AddControllers();

            builder.Services.AddProblemDetails();


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

            app.MapPost("/api/score", (ScoreRequest req, RiskCalculator scorer) =>
            {
                var result = scorer.Score(req.Features);

                return Results.Ok(new
                {
                    req.SessionId,
                    result.Score,
                    Action = result.Action.ToString(),
                    result.Reasons,
                });
            })
            .AddEndpointFilter(async (ctx, next) =>
            {
                // Runs after model binding, before the handler. A malformed or empty
                // JSON body is already rejected with a 400 by the framework before this
                // point; here we catch the structurally-valid-JSON-but-wrong-shape cases
                // (missing sessionId, null features, null feature sub-objects) that would
                // otherwise null-ref inside the scorer.
                var req = ctx.GetArgument<ScoreRequest?>(0);
                var errors = ScoreRequestValidator.Validate(req);
                if (errors.Count > 0)
                {
                    return Results.ValidationProblem(errors);
                }

                return await next(ctx);
            });

            // Liveness probe for the container orchestrator. Kept trivial on purpose.
            app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

            app.Run();
        }
    }

    public sealed record ScoreRequest(string SessionId, BotFeatures Features);
}
