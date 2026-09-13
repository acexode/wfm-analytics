using System.Globalization;
using System.Security.Claims;
using Npgsql;
using WfmAnalytics.Server.Database;

namespace WfmAnalytics.Server.Modules.Demo;

public static class DailyReportEndpoints
{
    public static void MapDailyReportEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/v1/teams/{teamId}/daily", async (string teamId, string? date, ClaimsPrincipal user, PostgresDatabase database, HttpContext context, CancellationToken token) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var reportDate))
                return Results.BadRequest(new { code = "invalid_date" });
            try
            {
                await using var connection = database.CreateConnection();
                await connection.OpenAsync(token);
                await using var grant = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM platform.access_grants WHERE principal_id=$1 AND team_id=$2 AND permission='analytics:daily:read' AND valid_from <= CURRENT_TIMESTAMP AND (valid_until IS NULL OR valid_until > CURRENT_TIMESTAMP))", connection);
                grant.Parameters.AddWithValue(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
                grant.Parameters.AddWithValue(teamId);
                if (await grant.ExecuteScalarAsync(token) is not true) return Results.Forbid();
                await using var query = new NpgsqlCommand("SELECT payload::text FROM analytics.synthetic_daily_reports WHERE team_id=$1 AND report_date=$2", connection);
                query.Parameters.AddWithValue(teamId);
                query.Parameters.AddWithValue(reportDate);
                var payload = await query.ExecuteScalarAsync(token) as string;
                return payload is null ? Results.NotFound(new { code = "report_not_found" }) : Results.Content(payload, "application/json");
            }
            catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException or ArgumentException)
            {
                return Results.Json(new { code = "database_unavailable" }, statusCode: 503);
            }
        }).RequireAuthorization();
    }
}
