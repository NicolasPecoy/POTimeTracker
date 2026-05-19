using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using POTimeTracker.Models;

namespace POTimeTracker.Services
{
    public class JiraApiService : IDisposable
    {
        private HttpClient _client;
        private string _baseUrl  = "";
        private string _lastCredentials = "";   // cached for proxy toggle

        public string? CurrentUser { get; private set; }
        public bool IsConnected => !string.IsNullOrEmpty(_baseUrl) && CurrentUser != null;
        public bool ProxyEnabled { get; private set; }

        public JiraApiService()
        {
            _client = BuildClient(proxyUrl: null);
        }

        // ═══════════════════════════════════════════════════════════
        // PROXY (for Fiddler / Charles / mitmproxy)
        // ═══════════════════════════════════════════════════════════

        /// <summary>Route all traffic through the given proxy (e.g. http://localhost:8888 for Fiddler).</summary>
        public void EnableProxy(string proxyUrl = "http://localhost:8888")
        {
            RebuildClient(proxyUrl);
            ProxyEnabled = true;
            LogService.Info($"JiraApiService: proxy habilitado en {proxyUrl}");
        }

        public void DisableProxy()
        {
            RebuildClient(proxyUrl: null);
            ProxyEnabled = false;
            LogService.Info("JiraApiService: proxy deshabilitado");
        }

        private void RebuildClient(string? proxyUrl)
        {
            var old = _client;
            _client = BuildClient(proxyUrl);

            // Re-apply auth header if we already had credentials
            if (!string.IsNullOrEmpty(_lastCredentials))
                _client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Basic", _lastCredentials);

            // Re-apply base URL (no-op if not yet configured)
            old.Dispose();
        }

        private static HttpClient BuildClient(string? proxyUrl)
        {
            HttpClientHandler handler;

            if (!string.IsNullOrEmpty(proxyUrl))
            {
                handler = new HttpClientHandler
                {
                    Proxy    = new WebProxy(proxyUrl) { BypassProxyOnLocal = false },
                    UseProxy = true,
                    // Trust Fiddler's self-signed root certificate
                    ServerCertificateCustomValidationCallback = (_, _, _, _) => true
                };
            }
            else
            {
                handler = new HttpClientHandler { UseProxy = false };
            }

            var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
            return client;
        }

        // ═══════════════════════════════════════════════════════════
        // CONFIGURE
        // ═══════════════════════════════════════════════════════════

        public void Configure(string baseUrl, string email, string apiToken)
        {
            _baseUrl = baseUrl.Trim().TrimEnd('/');
            _lastCredentials = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{email.Trim()}:{apiToken.Trim()}"));
            _client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Basic", _lastCredentials);
        }

        // ═══════════════════════════════════════════════════════════
        // CONNECTION TEST
        // ═══════════════════════════════════════════════════════════

        public async Task<(bool Success, string UserName, string Message)> TestConnectionAsync()
        {
            try
            {
                var response = await _client.GetAsync($"{_baseUrl}/rest/api/3/myself");
                var body = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                    return (false, "", $"Error {(int)response.StatusCode}: {ExtractJiraError(body)}");

                var json = JsonSerializer.Deserialize<JsonElement>(body);
                var displayName = GetString(json, "displayName");
                var email       = GetString(json, "emailAddress");
                var name        = displayName.Length > 0 ? displayName : email;

                CurrentUser = name;
                return (true, name, "Conexion exitosa");
            }
            catch (TaskCanceledException)
            {
                return (false, "", "Timeout: Jira no respondio");
            }
            catch (Exception ex)
            {
                return (false, "", $"Error de conexion: {ex.Message}");
            }
        }

        // ═══════════════════════════════════════════════════════════
        // PROJECTS
        // ═══════════════════════════════════════════════════════════

        public async Task<List<JiraProject>> GetProjectsAsync(int maxResults = 50)
        {
            try
            {
                var response = await _client.GetAsync(
                    $"{_baseUrl}/rest/api/3/project/search?maxResults={maxResults}&orderBy=name");
                var body = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode) return new();

                var json = JsonSerializer.Deserialize<JsonElement>(body);
                var projects = new List<JiraProject>();

                if (!json.TryGetProperty("values", out var values)) return projects;

                foreach (var item in values.EnumerateArray())
                {
                    var key = GetString(item, "key");
                    var name = GetString(item, "name");
                    if (string.IsNullOrEmpty(key)) continue;

                    projects.Add(new JiraProject
                    {
                        Id   = GetString(item, "id"),
                        Key  = key,
                        Name = name
                    });
                }

                return projects;
            }
            catch (Exception ex)
            {
                LogService.Error("JiraApiService.GetProjectsAsync", ex);
                return new();
            }
        }

        // ═══════════════════════════════════════════════════════════
        // ISSUES
        // ═══════════════════════════════════════════════════════════

        public async Task<List<JiraIssue>> SearchIssuesAsync(string jql, int maxResults = 50)
        {
            try
            {
                var url = $"{_baseUrl}/rest/api/3/search/jql"
                        + $"?jql={Uri.EscapeDataString(jql)}"
                        + $"&maxResults={maxResults}"
                        + "&fields=summary,status,issuetype,assignee";

                var response = await _client.GetAsync(url);
                var body = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode) return new();

                var json = JsonSerializer.Deserialize<JsonElement>(body);
                return ParseIssueArray(json, "issues");
            }
            catch (Exception ex)
            {
                LogService.Error("JiraApiService.SearchIssuesAsync", ex);
                return new();
            }
        }

        public Task<List<JiraIssue>> GetMyIssuesAsync(string projectKey = "", int maxResults = 50)
        {
            var jql = "assignee = currentUser() AND statusCategory != Done ORDER BY updated DESC";
            if (!string.IsNullOrWhiteSpace(projectKey))
                jql = $"project = \"{projectKey}\" AND " + jql;
            return SearchIssuesAsync(jql, maxResults);
        }

        public async Task<JiraIssue?> GetIssueAsync(string issueKey)
        {
            try
            {
                var url = $"{_baseUrl}/rest/api/3/issue/{Uri.EscapeDataString(issueKey.Trim())}"
                        + "?fields=summary,status,issuetype,assignee";
                var response = await _client.GetAsync(url);
                var body = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode) return null;

                var json = JsonSerializer.Deserialize<JsonElement>(body);
                return ParseSingleIssue(json);
            }
            catch (Exception ex)
            {
                LogService.Error($"JiraApiService.GetIssueAsync({issueKey})", ex);
                return null;
            }
        }

        // ═══════════════════════════════════════════════════════════
        // WORKLOG
        // ═══════════════════════════════════════════════════════════

        public async Task<(bool Success, string Message)> LogWorkAsync(
            string issueKey, double hours, DateTime date, string comment = "")
        {
            try
            {
                var timeSpentSeconds = (int)Math.Round(hours * 3600);
                var started = FormatJiraDateTime(date);

                var bodyObj = new
                {
                    timeSpentSeconds,
                    started,
                    comment = BuildAdfComment(
                        string.IsNullOrWhiteSpace(comment)
                            ? "Horas registradas desde PO Time Tracker"
                            : comment)
                };

                var content = new StringContent(
                    JsonSerializer.Serialize(bodyObj),
                    Encoding.UTF8,
                    "application/json");

                var response = await _client.PostAsync(
                    $"{_baseUrl}/rest/api/3/issue/{Uri.EscapeDataString(issueKey)}/worklog",
                    content);

                var respBody = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                    return (true, $"Worklog creado en {issueKey}");

                return (false, $"Jira {(int)response.StatusCode}: {ExtractJiraError(respBody)}");
            }
            catch (TaskCanceledException)
            {
                return (false, "Timeout al conectar con Jira");
            }
            catch (Exception ex)
            {
                LogService.Error($"JiraApiService.LogWorkAsync({issueKey})", ex);
                return (false, $"Error: {ex.Message}");
            }
        }

        // ═══════════════════════════════════════════════════════════
        // HELPERS
        // ═══════════════════════════════════════════════════════════

        private static string FormatJiraDateTime(DateTime date)
        {
            // Use 9:00 AM on the target date, in local timezone
            var localTime = date.Date.AddHours(9);
            var offset    = TimeZoneInfo.Local.GetUtcOffset(localTime);
            var sign      = offset >= TimeSpan.Zero ? "+" : "-";
            var abs       = offset.Duration();
            return $"{localTime:yyyy-MM-ddTHH:mm:ss.fff}{sign}{abs.Hours:00}{abs.Minutes:00}";
        }

        private static object BuildAdfComment(string text) =>
            new
            {
                type    = "doc",
                version = 1,
                content = new[]
                {
                    new
                    {
                        type    = "paragraph",
                        content = new[]
                        {
                            new { type = "text", text }
                        }
                    }
                }
            };

        private static List<JiraIssue> ParseIssueArray(JsonElement root, string arrayKey)
        {
            var list = new List<JiraIssue>();
            if (!root.TryGetProperty(arrayKey, out var arr)) return list;
            foreach (var item in arr.EnumerateArray())
                list.Add(ParseSingleIssue(item));
            return list;
        }

        private static JiraIssue ParseSingleIssue(JsonElement item)
        {
            var key     = GetString(item, "key");
            var id      = GetString(item, "id");
            var summary = "";
            var status  = "";
            var statusCategory = "";
            var issueType = "";
            var assignee  = "";

            if (item.TryGetProperty("fields", out var fields))
            {
                summary = GetString(fields, "summary");

                if (fields.TryGetProperty("status", out var st))
                {
                    status = GetString(st, "name");
                    if (st.TryGetProperty("statusCategory", out var sc))
                        statusCategory = GetString(sc, "key");
                }

                if (fields.TryGetProperty("issuetype", out var it))
                    issueType = GetString(it, "name");

                if (fields.TryGetProperty("assignee", out var asn)
                    && asn.ValueKind != JsonValueKind.Null)
                    assignee = GetString(asn, "displayName");
            }

            return new JiraIssue
            {
                Id             = id,
                Key            = key,
                Summary        = summary,
                Status         = status,
                StatusCategory = statusCategory,
                IssueType      = issueType,
                Assignee       = assignee
            };
        }

        private static string ExtractJiraError(string body)
        {
            try
            {
                var json = JsonSerializer.Deserialize<JsonElement>(body);

                if (json.TryGetProperty("errorMessages", out var msgs))
                {
                    var parts = new List<string>();
                    foreach (var m in msgs.EnumerateArray())
                        parts.Add(m.GetString() ?? "");
                    if (parts.Count > 0) return string.Join(", ", parts);
                }

                if (json.TryGetProperty("message", out var msg))
                    return msg.GetString() ?? body;

                if (json.TryGetProperty("errors", out var errs)
                    && errs.ValueKind == JsonValueKind.Object)
                {
                    var parts = new List<string>();
                    foreach (var prop in errs.EnumerateObject())
                        parts.Add($"{prop.Name}: {prop.Value}");
                    if (parts.Count > 0) return string.Join(", ", parts);
                }
            }
            catch { }
            return body.Length > 300 ? body[..300] : body;
        }

        private static string GetString(JsonElement el, string key) =>
            el.TryGetProperty(key, out var v) ? v.GetString() ?? "" : "";

        public void Disconnect() => CurrentUser = null;

        public void Dispose() => _client.Dispose();
    }
}
