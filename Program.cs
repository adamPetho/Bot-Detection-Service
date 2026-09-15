using Bot_Detection_Service.Models;
using Bot_Detection_Service.Services;

namespace Bot_Detection_Service
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddControllers();

            var app = builder.Build();

            app.MapPost("/api/bot-check", (BotCheckRequest req, BotScorer scorer) =>
            {
                var result = scorer.Score(req.Features);

                return Results.Ok(new
                {
                    req.SessionId,
                    result.Score,
                    Verdict = result.Verdict.ToString(),
                    result.Reasons,
                });
            });

            app.Run();
        }
    }

    public sealed record BotCheckRequest(string SessionId, BotFeatures Features);
}
