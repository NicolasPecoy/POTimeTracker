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

        public string? CurrentUser          { get; private set; }
        public string? CurrentUserAccountId { get; private set; }
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

                CurrentUser          = name;
                CurrentUserAccountId = GetString(json, "accountId");
                return (true, name, "Conexion exitosa");
            }
            catch (TaskCanceledException ex)
            {
                LogService.Warn("JiraApiService.TestConnectionAsync: timeout", ex);
                return (false, "", "Timeout: Jira no respondio");
            }
            catch (Exception ex)
            {
                LogService.Error("JiraApiService.TestConnectionAsync: error de conexion", ex);
                return (false, "", $"Error de conexion: {ex.Message}");
            }
        }

        // ═══════════════════════════════════════════════════════════
        // PROJECTS
        // ═══════════════════════════════════════════════════════════

        public async Task<List<JiraProject>> GetProjectsAsync()
        {
            try
            {
                var projects = new List<JiraProject>();
                var startAt  = 0;
                const int pageSize = 50;

                while (true)
                {
                    var response = await _client.GetAsync(
                        $"{_baseUrl}/rest/api/3/project/search?maxResults={pageSize}&startAt={startAt}&orderBy=name");
                    var body = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode) break;

                    var json = JsonSerializer.Deserialize<JsonElement>(body);
                    if (!json.TryGetProperty("values", out var values)) break;

                    var pageCount = 0;
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
                        pageCount++;
                    }

                    // Si la página vino incompleta, ya llegamos al final
                    if (pageCount < pageSize) break;
                    startAt += pageSize;
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

        /// <summary>
        /// Fetch a single page of issues. Returns the page and a nextPageToken (null = no more pages).
        /// Use this for progressive / background loading.
        /// </summary>
        public async Task<(List<JiraIssue> Issues, string? NextPageToken)> SearchIssuesPageAsync(
            string jql, int pageSize = 100, string? pageToken = null)
        {
            try
            {
                var url = $"{_baseUrl}/rest/api/3/search/jql"
                        + $"?jql={Uri.EscapeDataString(jql)}"
                        + $"&maxResults={Math.Min(pageSize, 100)}"
                        + "&fields=summary,status,issuetype,assignee,project";
                if (pageToken != null)
                    url += $"&nextPageToken={Uri.EscapeDataString(pageToken)}";

                var response = await _client.GetAsync(url);
                var body     = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode) return (new(), null);

                var json   = JsonSerializer.Deserialize<JsonElement>(body);
                var issues = ParseIssueArray(json, "issues");

                // New endpoint uses nextPageToken; absence or null means last page
                string? next = null;
                if (json.TryGetProperty("nextPageToken", out var npt)
                    && npt.ValueKind != JsonValueKind.Null)
                {
                    var candidate = npt.GetString();
                    // Guard against infinite-loop: reject token equal to current one
                    if (!string.IsNullOrEmpty(candidate) && candidate != pageToken)
                        next = candidate;
                }

                // If the page is smaller than requested we are at the end
                if (issues.Count < Math.Min(pageSize, 100))
                    next = null;

                return (issues, next);
            }
            catch (Exception ex)
            {
                LogService.Error("JiraApiService.SearchIssuesPageAsync", ex);
                return (new(), null);
            }
        }

        /// <summary>Single-request search (fast). Explicit searches and worklog queries use this.</summary>
        public async Task<List<JiraIssue>> SearchIssuesAsync(string jql, int maxResults = 50)
        {
            var (issues, _) = await SearchIssuesPageAsync(jql, Math.Min(maxResults, 100));
            return issues;
        }

        /// <summary>Builds the JQL string for "my open issues" without executing it.</summary>
        public static string BuildMyIssuesJql(string projectKey = "", bool includeDone = false)
        {
            var jql = includeDone
                ? "assignee = currentUser() ORDER BY updated DESC"
                : "assignee = currentUser() AND statusCategory != Done ORDER BY updated DESC";
            if (!string.IsNullOrWhiteSpace(projectKey))
                jql = $"project = \"{projectKey}\" AND " + jql;
            return jql;
        }

        /// <summary>Returns first 100 issues immediately. Callers can continue loading via SearchIssuesPageAsync.</summary>
        public Task<(List<JiraIssue> Issues, string? NextPageToken)> GetMyIssuesPageAsync(
            string projectKey = "", bool includeDone = false)
            => SearchIssuesPageAsync(BuildMyIssuesJql(projectKey, includeDone), 100);

        // Keep for backward compat (worklog queries pass explicit limit)
        public Task<List<JiraIssue>> GetMyIssuesAsync(string projectKey = "", bool includeDone = false)
            => SearchIssuesAsync(BuildMyIssuesJql(projectKey, includeDone), 100);

        public async Task<JiraIssue?> GetIssueAsync(string issueKey)
        {
            try
            {
                var url = $"{_baseUrl}/rest/api/3/issue/{Uri.EscapeDataString(issueKey.Trim())}"
                        + "?fields=summary,status,issuetype,assignee,project";
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
            catch (TaskCanceledException ex)
            {
                LogService.Warn($"JiraApiService.LogWorkAsync({issueKey}): timeout", ex);
                return (false, "Timeout al conectar con Jira");
            }
            catch (Exception ex)
            {
                LogService.Error($"JiraApiService.LogWorkAsync({issueKey})", ex);
                return (false, $"Error: {ex.Message}");
            }
        }

        // ═══════════════════════════════════════════════════════════
        // TRANSITIONS (STATUS CHANGE)
        // ═══════════════════════════════════════════════════════════

        public async Task<List<JiraTransition>> GetTransitionsAsync(string issueKey)
        {
            try
            {
                // includeUnavailableTransitions=true ensures "Done" and other states
                // are returned even when workflow conditions aren't fully met.
                var url      = $"{_baseUrl}/rest/api/3/issue/{Uri.EscapeDataString(issueKey)}/transitions"
                             + "?includeUnavailableTransitions=true&sortByOpsBarAndStatus=true";
                var response = await _client.GetAsync(url);
                var body     = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode) return new();

                var json = JsonSerializer.Deserialize<JsonElement>(body);
                var list = new List<JiraTransition>();
                if (!json.TryGetProperty("transitions", out var arr)) return list;

                foreach (var t in arr.EnumerateArray())
                {
                    var id   = GetString(t, "id");
                    var name = GetString(t, "name");
                    if (string.IsNullOrEmpty(id)) continue;

                    var toStatusName = "";
                    var toCategory   = "";
                    if (t.TryGetProperty("to", out var to))
                    {
                        toStatusName = GetString(to, "name");
                        if (to.TryGetProperty("statusCategory", out var sc))
                            toCategory = GetString(sc, "key");
                    }

                    var isAvailable = !t.TryGetProperty("isAvailable", out var avProp) || avProp.GetBoolean();
                    var hasScreen   =  t.TryGetProperty("hasScreen",   out var hsProp) && hsProp.GetBoolean();

                    list.Add(new JiraTransition
                    {
                        Id               = id,
                        Name             = name,
                        ToStatusName     = toStatusName,
                        ToStatusCategory = toCategory,
                        IsAvailable      = isAvailable,
                        HasScreen        = hasScreen
                    });
                }
                return list;
            }
            catch (Exception ex)
            {
                LogService.Error($"JiraApiService.GetTransitionsAsync({issueKey})", ex);
                return new();
            }
        }

        public async Task<(bool Success, string Message)> TransitionIssueAsync(string issueKey, string transitionId)
        {
            try
            {
                var bodyObj = new { transition = new { id = transitionId } };
                var content = new StringContent(
                    JsonSerializer.Serialize(bodyObj),
                    Encoding.UTF8,
                    "application/json");

                var response = await _client.PostAsync(
                    $"{_baseUrl}/rest/api/3/issue/{Uri.EscapeDataString(issueKey)}/transitions",
                    content);

                var respBody = await response.Content.ReadAsStringAsync();
                return response.IsSuccessStatusCode
                    ? (true, "Estado cambiado")
                    : (false, $"Jira {(int)response.StatusCode}: {ExtractJiraError(respBody)}");
            }
            catch (TaskCanceledException ex)
            {
                LogService.Warn($"JiraApiService.TransitionIssueAsync({issueKey}): timeout", ex);
                return (false, "Timeout al conectar con Jira");
            }
            catch (Exception ex)
            {
                LogService.Error($"JiraApiService.TransitionIssueAsync({issueKey})", ex);
                return (false, $"Error: {ex.Message}");
            }
        }

        // ═══════════════════════════════════════════════════════════
        // WORKLOGS DEL DÍA
        // ═══════════════════════════════════════════════════════════

        public async Task<List<JiraWorklogEntry>> GetMyWorklogsForDateAsync(DateTime date)
        {
            try
            {
                var dateStr = date.ToString("yyyy-MM-dd");
                var jql     = $"worklogAuthor = currentUser() AND worklogDate = \"{dateStr}\"";
                var issues  = await SearchIssuesAsync(jql, 50);

                var result = new List<JiraWorklogEntry>();
                foreach (var issue in issues)
                {
                    double hours = await GetIssueWorklogHoursForDateAsync(issue.Key, date);
                    if (hours > 0)
                        result.Add(new JiraWorklogEntry
                        {
                            IssueKey       = issue.Key,
                            Summary        = issue.Summary,
                            StatusCategory = issue.StatusCategory,
                            Hours          = hours
                        });
                }
                return result;
            }
            catch (Exception ex)
            {
                LogService.Error("JiraApiService.GetMyWorklogsForDateAsync", ex);
                return new();
            }
        }

        private async Task<double> GetIssueWorklogHoursForDateAsync(string issueKey, DateTime date)
        {
            try
            {
                var url      = $"{_baseUrl}/rest/api/3/issue/{Uri.EscapeDataString(issueKey)}/worklog?maxResults=100";
                var response = await _client.GetAsync(url);
                var body     = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode) return 0;

                var json = JsonSerializer.Deserialize<JsonElement>(body);
                if (!json.TryGetProperty("worklogs", out var worklogs)) return 0;

                var datePrefix = date.ToString("yyyy-MM-dd");
                double totalSeconds = 0;

                foreach (var wl in worklogs.EnumerateArray())
                {
                    // Filter by author when account ID is known
                    if (!string.IsNullOrEmpty(CurrentUserAccountId))
                    {
                        if (!wl.TryGetProperty("author", out var author)) continue;
                        if (GetString(author, "accountId") != CurrentUserAccountId) continue;
                    }

                    // Filter by date (started is stored with the submitted timezone)
                    var started = GetString(wl, "started");
                    if (started.Length < 10 || started[..10] != datePrefix) continue;

                    if (wl.TryGetProperty("timeSpentSeconds", out var tss))
                        totalSeconds += tss.GetInt32();
                }

                return Math.Round(totalSeconds / 3600.0, 2);
            }
            catch (Exception ex)
            {
                LogService.Error($"JiraApiService.GetIssueWorklogHoursForDateAsync({issueKey})", ex);
                return 0;
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
            var issueType  = "";
            var assignee   = "";
            var projectKey = "";
            var projectName = "";

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

                if (fields.TryGetProperty("project", out var proj)
                    && proj.ValueKind != JsonValueKind.Null)
                {
                    projectKey  = GetString(proj, "key");
                    projectName = GetString(proj, "name");
                }
            }

            return new JiraIssue
            {
                Id             = id,
                Key            = key,
                Summary        = summary,
                Status         = status,
                StatusCategory = statusCategory,
                IssueType      = issueType,
                Assignee       = assignee,
                ProjectKey     = projectKey,
                ProjectName    = projectName
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
            catch (Exception ex)
            {
                LogService.Warn("JiraApiService.ExtractJiraError: no se pudo parsear el error JSON", ex);
            }
            return body.Length > 300 ? body[..300] : body;
        }

        private static string GetString(JsonElement el, string key) =>
            el.TryGetProperty(key, out var v) ? v.GetString() ?? "" : "";

        public void Disconnect() => CurrentUser = null;

        public void Dispose() => _client.Dispose();
    }
}
