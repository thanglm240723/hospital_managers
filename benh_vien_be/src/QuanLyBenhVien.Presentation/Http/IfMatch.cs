using Microsoft.AspNetCore.Http;

namespace QuanLyBenhVien.Presentation.Http;

/// Đọc header If-Match chứa RowVersion (số nguyên không dấu, có thể bọc dấu nháy kép).
internal static class IfMatch
{
    public static bool TryRead(HttpContext http, out uint version)
    {
        var raw = http.Request.Headers.IfMatch.ToString().Trim().Trim('"');
        return uint.TryParse(raw, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out version);
    }

    public static IResult Invalid(HttpContext http, string resourceLabel)
        => ProblemResponses.Create(http, StatusCodes.Status400BadRequest, "invalid_if_match",
            $"Thiếu hoặc sai định dạng header If-Match (phiên bản {resourceLabel}).");
}
