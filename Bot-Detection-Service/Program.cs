using Bot_Detection_Service.Config;
using Bot_Detection_Service.Services;
using BotDetection;
using Microsoft.Extensions.Options;

namespace Bot_Detection_Service
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddOptions<RiskScoringOptions>()
                .Bind(builder.Configuration.GetSection(RiskScoringOptions.SectionName))
                .Validate(o => o.ChallengeThreshold is >= 0 and <= 1, "RiskScoring:ChallengeThreshold must be between 0 and 1.")
                .Validate(o => o.BlockThreshold is >= 0 and <= 1, "RiskScoring:BlockThreshold must be between 0 and 1.")
                .Validate(o => o.ChallengeThreshold <= o.BlockThreshold, "RiskScoring:ChallengeThreshold must not exceed BlockThreshold.")
                .ValidateOnStart();

            builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<RiskScoringOptions>>().Value);
            builder.Services.AddSingleton<RiskCalculator>();

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
                var req = ctx.GetArgument<ScoreRequest?>(0);
                var errors = ScoreRequestValidator.Validate(req);
                if (errors.Count > 0)
                {
                    return Results.ValidationProblem(errors);
                }

                return await next(ctx);
            });

            app.UseExceptionHandler(new ExceptionHandlerOptions
            {
                StatusCodeSelector = ex => ex is BadHttpRequestException badRequest
                    ? badRequest.StatusCode
                    : StatusCodes.Status500InternalServerError,
            });

            // Liveness probe for the container orchestrator. Kept trivial on purpose.
            app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

            app.Run();
        }
    }

    public sealed record ScoreRequest(string SessionId, BotFeatures Features);
}
