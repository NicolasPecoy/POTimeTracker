using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using POTimeTracker.Models;

namespace POTimeTracker.Services
{
    /// <summary>
    /// Client for Project Open built on GeneXus C# 10.3.16.
    /// Page: ingresohorasxfecha.aspx — nested grids:
    ///   Fsgridproyectos  (grid 52) → vPROJECTID, vPROYECTODESCRIPCION
    ///   Fsgridhoras      (grid 71) → vTAREAID, vTAREADESCRIPCION, vTIMESHEETCANTHORAS, vTIMESHEETCOMENTARIOS
    ///   Fsgridhorasro    (grid 85) → read-only tasks from other projects
    /// Events: 'CONFIRMAR' (save), 'IMPUTAROTROSPROYECTOS', ENTER, CANCEL
    /// </summary>
    public class POApiService : IDisposable
    {
        private readonly HttpClient _client;
        private readonly CookieContainer _cookies;
        private string _serverUrl = "http://po.invenzis.com:8080";
        private bool _isLoggedIn;
        private string _gxState = "";
        private int _personaId;

        public bool IsLoggedIn => _isLoggedIn;
        public string ServerUrl => _serverUrl;
        public string? CurrentUser { get; private set; }

        public POApiService()
        {
            _cookies = new CookieContainer();
            var handler = new HttpClientHandler
            {
                CookieContainer = _cookies,
                UseCookies = true,
                AllowAutoRedirect = true
            };
            _client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
            _client.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/147.0.0.0 Safari/537.36");
            _client.DefaultRequestHeaders.Add("Accept",
                "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            _client.DefaultRequestHeaders.Add("Accept-Language", "es-UY,es;q=0.9,en;q=0.8");
        }

        // ═══════════════════════════════════════════════════════════
        // LOGIN
        // ═══════════════════════════════════════════════════════════

        public async Task<(bool Success, string Message)> LoginAsync(
            string serverUrl, string username, string password)
        {
            _serverUrl = serverUrl.TrimEnd('/');
            try
            {
                // 1. GET login page
                var loginUrl = $"{_serverUrl}/sgplogin.aspx";
                var html = await (await _client.GetAsync(loginUrl)).Content.ReadAsStringAsync();

                // 2. Collect every hidden + visible input on the page
                var form = ExtractAllInputs(html);

                // 3. Inject credentials into the right fields
                var userField = DetectField(html, "text",
                    new[] { "vUSUARIO","vUSERNAME","vLOGIN","vUSER","USUARIO","USERNAME" });
                var passField = DetectField(html, "password", null);

                if (userField != null) form[userField] = username;
                if (passField != null) form[passField] = password;

                // 4. GeneXus event target
                form["__EVENTTARGET"] = "";
                form["__EVENTARGUMENT"] = "";
                SetGeneXusEvent(form, "EENTER.");

                // Detect submit button
                var btnName = DetectSubmitButton(html);
                if (btnName != null) form[btnName] = "Login";

                // 5. POST
                var resp = await _client.PostAsync(loginUrl, new FormUrlEncodedContent(form));
                var postHtml = await resp.Content.ReadAsStringAsync();

                // 6. Verify by hitting the real page
                var verify = await _client.GetAsync(
                    $"{_serverUrl}/registrodehoras.aspx?{DateTime.Today:yyyyMMdd}");
                var verifyUrl = verify.RequestMessage?.RequestUri?.ToString() ?? "";
                var verifyHtml = await verify.Content.ReadAsStringAsync();

                bool ok = IsAuthenticatedTimeEntryPage(verifyUrl, verifyHtml);

                if (ok)
                {
                    _isLoggedIn = true;
                    CurrentUser = username;
                    CaptureGxMeta(verifyHtml);
                    return (true, "Login exitoso");
                }

                var error = ExtractErrorMessage(verifyHtml);
                if (string.IsNullOrWhiteSpace(error))
                    error = ExtractErrorMessage(postHtml);

                return (false, error.Length > 0
                    ? error : BuildLoginFailureMessage(verifyUrl, verifyHtml));
            }
            catch (HttpRequestException ex)
            {
                LogService.Warn($"Login: error de conexion a {_serverUrl}", ex);
                return (false, $"Error de conexión: {ex.Message}");
            }
            catch (TaskCanceledException ex)
            {
                LogService.Warn("Login: timeout al conectar con el servidor", ex);
                return (false, "Timeout: el servidor no respondió");
            }
            catch (Exception ex)
            {
                LogService.Error("Login: excepcion inesperada", ex);
                return (false, $"Error: {ex.Message}");
            }
        }

        // ═══════════════════════════════════════════════════════════
        // LOAD PROJECTS + TASKS
        // ═══════════════════════════════════════════════════════════

        public async Task<List<POProject>> GetProjectsAsync(DateTime date, bool showAllTasks = false)
        {
            var palette = new[] { "#6366F1","#8B5CF6","#EC4899","#F59E0B",
                                  "#10B981","#06B6D4","#F97316","#EF4444" };
            try
            {
                var url = $"{_serverUrl}/registrodehoras.aspx?{date:yyyyMMdd}";
                SetReferer(url);
                var html = await (await _client.GetAsync(url)).Content.ReadAsStringAsync();

                if (showAllTasks)
                    html = await SearchTasksAsync(url, html, showAllTasks);

                CaptureGxMeta(html);

                var projects = ParseProjects(html, palette);
                ParseTasks(html, projects);
                return projects;
            }
            catch (Exception ex)
            {
                LogService.Error("GetProjectsAsync: error al cargar proyectos", ex);
                return new List<POProject>();
            }
        }

        private async Task<string> SearchTasksAsync(string pageUrl, string html, bool showAllTasks)
        {
            var form = ExtractAllInputs(html);
            form["vVERTAREAS"] = showAllTasks ? "T" : "A";
            form["vSELPROJECTID"] = form.GetValueOrDefault("vSELPROJECTID", "0");
            form["__EVENTTARGET"] = "'BUSCAR'";
            form["__EVENTARGUMENT"] = "";
            SetGeneXusEvent(form, "E'BUSCAR'.");

            var response = await _client.PostAsync(pageUrl, new FormUrlEncodedContent(form));
            return await response.Content.ReadAsStringAsync();
        }

        /// <summary>
        /// Parse Fsgridproyectos rows.
        /// GX renders: id="vPROYECTODESCRIPCION_0001" value="Nombre Proyecto"
        ///             id="vPROJECTID_0001" value="42"
        /// OR span variants: id="span_vPROYECTODESCRIPCION_0001">Nombre</span>
        /// </summary>
        private static List<POProject> ParseProjects(string html, string[] palette)
        {
            var projects = new List<POProject>();

            projects = ParseProjectsFromGeneXusGridData(html, palette);
            if (projects.Count > 0)
                return projects;

            // Collect project descriptions (input value OR span text)
            var descRx = new Regex(
                @"id=""(?:span_)?vPROYECTODESCRIPCION_(\d{4})""[^>]*(?:value=""([^""]*)""|>([^<]+)<)",
                RegexOptions.IgnoreCase);
            var idRx = new Regex(
                @"id=""(?:span_)?vPROJECTID_(\d{4})""[^>]*(?:value=""(\d+)""|>(\d+)<)",
                RegexOptions.IgnoreCase);

            var ids = new Dictionary<string, string>();
            foreach (Match m in idRx.Matches(html))
            {
                var row = m.Groups[1].Value;
                var val = Coalesce(m.Groups[2].Value, m.Groups[3].Value);
                if (!string.IsNullOrEmpty(val)) ids[row] = val;
            }

            int ci = 0;
            foreach (Match m in descRx.Matches(html))
            {
                var row  = m.Groups[1].Value;
                var name = WebUtility.HtmlDecode(
                    Coalesce(m.Groups[2].Value, m.Groups[3].Value)).Trim();
                if (string.IsNullOrEmpty(name)) continue;

                projects.Add(new POProject
                {
                    Id       = ids.GetValueOrDefault(row, row),
                    Name     = name,
                    Color    = palette[ci % palette.Length],
                    GxRowId  = row
                });
                ci++;
            }
            return projects;
        }

        private static List<POProject> ParseProjectsFromGeneXusGridData(string html, string[] palette)
        {
            var projects = new List<POProject>();

            try
            {
                var inputs = ExtractAllInputs(html);
                if (!inputs.TryGetValue("W0071FsgridproyectosContainerDataV", out var json)
                    || string.IsNullOrWhiteSpace(json))
                    return projects;

                using var doc = JsonDocument.Parse(json);
                var ci = 0;
                foreach (var row in doc.RootElement.EnumerateArray())
                {
                    if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() <= 11)
                        continue;

                    var name = WebUtility.HtmlDecode(JsonString(row[10])).Trim();
                    var id = JsonString(row[11]).Trim();
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(id))
                        continue;

                    projects.Add(new POProject
                    {
                        Id = id,
                        Name = name,
                        Color = palette[ci % palette.Length],
                        GxRowId = (ci + 1).ToString("0000")
                    });
                    ci++;
                }
            }
            catch
            {
                return new List<POProject>();
            }

            return projects;
        }

        /// <summary>
        /// Parse Fsgridhoras (grid 71) nested inside each project row.
        /// Fields: vTAREAID_PPPP_TTTT, vTAREADESCRIPCION_PPPP_TTTT,
        ///         vTIMESHEETCANTHORAS_PPPP_TTTT, vTIMESHEETCOMENTARIOS_PPPP_TTTT
        /// </summary>
        private static void ParseTasks(string html, List<POProject> projects)
        {
            if (ParseTasksFromGeneXusGridData(html, projects))
                return;

            // Task descriptions
            var descRx = new Regex(
                @"id=""(?:span_)?vTAREADESCRIPCION_(\d{4})_(\d{4})""[^>]*(?:value=""([^""]*)""|>([^<]+)<)",
                RegexOptions.IgnoreCase);
            // Task IDs
            var idRx = new Regex(
                @"id=""(?:span_)?vTAREAID_(\d{4})_(\d{4})""[^>]*(?:value=""(\d+)""|>(\d+)<)",
                RegexOptions.IgnoreCase);
            // Existing hours
            var hrsRx = new Regex(
                @"id=""(?:span_)?vTIMESHEETCANTHORAS_(\d{4})_(\d{4})""[^>]*(?:value=""([^""]*)""|>([^<]*)<)",
                RegexOptions.IgnoreCase);

            var idMap = new Dictionary<(string,string), string>();
            foreach (Match m in idRx.Matches(html))
                idMap[(m.Groups[1].Value, m.Groups[2].Value)] =
                    Coalesce(m.Groups[3].Value, m.Groups[4].Value);

            var hrsMap = new Dictionary<(string,string), double>();
            foreach (Match m in hrsRx.Matches(html))
            {
                var v = Coalesce(m.Groups[3].Value, m.Groups[4].Value)
                    .Replace(",", ".").Trim();
                if (double.TryParse(v, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var h) && h > 0)
                    hrsMap[(m.Groups[1].Value, m.Groups[2].Value)] = h;
            }

            foreach (Match m in descRx.Matches(html))
            {
                var pRow = m.Groups[1].Value;
                var tRow = m.Groups[2].Value;
                var name = WebUtility.HtmlDecode(
                    Coalesce(m.Groups[3].Value, m.Groups[4].Value)).Trim();
                if (string.IsNullOrEmpty(name)) continue;

                var proj = projects.FirstOrDefault(p => p.GxRowId == pRow);
                if (proj == null) continue;

                var key = (pRow, tRow);
                proj.Tasks.Add(new POTask
                {
                    Id              = idMap.GetValueOrDefault(key, tRow),
                    Name            = name,
                    ProjectId       = proj.Id,
                    GxRowId         = tRow,
                    GxProjectRowId  = pRow,
                    ExistingHours   = hrsMap.GetValueOrDefault(key, 0)
                });
            }
        }

        private static bool ParseTasksFromGeneXusGridData(string html, List<POProject> projects)
        {
            var added = 0;

            try
            {
                var inputs = ExtractAllInputs(html);

                foreach (var project in projects)
                {
                    var key = $"W0071FsgridhorasContainerDataV_{project.GxRowId}";
                    if (!inputs.TryGetValue(key, out var json) || string.IsNullOrWhiteSpace(json))
                        continue;

                    using var doc = JsonDocument.Parse(json);
                    var taskIndex = 0;
                    foreach (var row in doc.RootElement.EnumerateArray())
                    {
                        if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() <= 10)
                            continue;

                        var id = JsonString(row[6]).Trim();
                        var name = WebUtility.HtmlDecode(JsonString(row[8])).Trim();
                        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
                            continue;

                        var hoursText = JsonString(row[10]).Replace(",", ".").Trim();
                        _ = double.TryParse(hoursText,
                            System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out var hours);

                        taskIndex++;
                        project.Tasks.Add(new POTask
                        {
                            Id = id,
                            Name = name,
                            ProjectId = project.Id,
                            GxRowId = taskIndex.ToString("0000"),
                            GxProjectRowId = project.GxRowId,
                            ExistingHours = hours
                        });
                        added++;
                    }
                }
            }
            catch
            {
                return added > 0;
            }

            return added > 0;
        }

        // ═══════════════════════════════════════════════════════════
        // SUBMIT TIME ENTRY  ('CONFIRMAR' server event)
        // ═══════════════════════════════════════════════════════════

        private static (string ProjectRow, string TaskRow) FindGeneXusRows(
            Dictionary<string, string> form, string taskId, string projectId)
        {
            var projectRow = "";

            if (form.TryGetValue("W0071FsgridproyectosContainerDataV", out var projectsJson))
            {
                try
                {
                    using var doc = JsonDocument.Parse(projectsJson);
                    var index = 0;
                    foreach (var row in doc.RootElement.EnumerateArray())
                    {
                        index++;
                        if (row.ValueKind == JsonValueKind.Array
                            && row.GetArrayLength() > 11
                            && JsonString(row[11]).Trim() == projectId)
                        {
                            projectRow = index.ToString("0000");
                            break;
                        }
                    }
                }
                catch { }
            }

            if (string.IsNullOrWhiteSpace(projectRow))
                return ("", "");

            var tasksKey = $"W0071FsgridhorasContainerDataV_{projectRow}";
            if (!form.TryGetValue(tasksKey, out var tasksJson))
                return (projectRow, "");

            try
            {
                using var doc = JsonDocument.Parse(tasksJson);
                var index = 0;
                foreach (var row in doc.RootElement.EnumerateArray())
                {
                    index++;
                    if (row.ValueKind == JsonValueKind.Array
                        && row.GetArrayLength() > 6
                        && JsonString(row[6]).Trim() == taskId)
                        return (projectRow, index.ToString("0000"));
                }
            }
            catch { }

            return (projectRow, "");
        }

        private static void UpdateGridDataV(
            Dictionary<string, string> form, string projectRow, string taskRow, string hours, string comments)
        {
            var key = $"W0071FsgridhorasContainerDataV_{projectRow}";
            if (!form.TryGetValue(key, out var json))
                return;

            try
            {
                var rows = JsonSerializer.Deserialize<List<List<string>>>(json);
                var rowIndex = int.Parse(taskRow) - 1;
                if (rows == null || rowIndex < 0 || rowIndex >= rows.Count)
                    return;

                while (rows[rowIndex].Count <= 12)
                    rows[rowIndex].Add("");

                rows[rowIndex][10] = hours;
                rows[rowIndex][12] = comments;
                form[key] = JsonSerializer.Serialize(rows);
            }
            catch { }
        }

        public async Task<(bool Success, string Message)> SubmitTimeEntryAsync(TimeEntry entry, bool showAllTasks = false)
        {
            try
            {
                var dateStr = entry.Date.ToString("yyyyMMdd");
                var pageUrl = $"{_serverUrl}/registrodehoras.aspx?{dateStr}";
                SetReferer(pageUrl);

                // 1. GET fresh page state
                var html = await (await _client.GetAsync(pageUrl)).Content.ReadAsStringAsync();
                if (showAllTasks)
                    html = await SearchTasksAsync(pageUrl, html, showAllTasks);

                CaptureGxMeta(html);

                // 2. Collect all form fields (hidden + grid inputs)
                var form = ExtractAllInputs(html);

                // 3. Find the exact grid cell for this task
                //    GX IDs: W0071vTIMESHEETCANTHORAS_TTTTPPPP
                var cellId = FindGridCell(html, entry.TaskId, entry.ProjectId);
                var projectRow = entry.GxProjectRowId;
                var taskRow = entry.GxTaskRowId;
                if (string.IsNullOrWhiteSpace(projectRow) || string.IsNullOrWhiteSpace(taskRow))
                    (projectRow, taskRow) = FindGeneXusRows(form, entry.TaskId, entry.ProjectId);

                var hoursValue = entry.Hours.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)
                    .Replace(".", ",");

                if (!string.IsNullOrWhiteSpace(projectRow) && !string.IsNullOrWhiteSpace(taskRow))
                {
                    var suffix = $"{taskRow}{projectRow}";
                    form[$"W0071vTAREAID_{suffix}"] = entry.TaskId;
                    form[$"W0071vTIMESHEETCANTHORAS_{suffix}"] = hoursValue;
                    form[$"W0071vTIMESHEETCOMENTARIOS_{suffix}"] = entry.Notes ?? "";
                    UpdateGridDataV(form, projectRow, taskRow, hoursValue, entry.Notes ?? "");
                }
                else if (cellId != null)
                {
                    // Set hours (GX uses comma decimal)
                    var hrsKey = cellId.Replace("vTAREAID", "vTIMESHEETCANTHORAS");
                    var cmtKey = cellId.Replace("vTAREAID", "vTIMESHEETCOMENTARIOS");
                    form[hrsKey] = hoursValue;
                    form[cmtKey] = entry.Notes ?? "";
                }
                else
                {
                    // Fallback: try flat field names
                    form["W0071vTIMESHEETCANTHORAS"] = hoursValue;
                    form["vTIMESHEETCOMENTARIOS"] = entry.Notes ?? "";
                    form["vTAREAID"] = entry.TaskId;
                }

                form["W0071vFECHA"] = entry.Date.ToString("dd/MM/yyyy");
                if (_personaId > 0) form["W0071vPERSONAID"] = _personaId.ToString();

                // 4. Fire 'CONFIRMAR'
                form["__EVENTTARGET"] = "W0071E'CONFIRMAR'.";
                form["__EVENTARGUMENT"] = "";
                SetGeneXusEvent(form, "W0071E'CONFIRMAR'.", "52");

                var resp = await _client.PostAsync(pageUrl, new FormUrlEncodedContent(form));
                var body = await resp.Content.ReadAsStringAsync();

                // 5. Check result
                bool err = body.Contains("no se pudo", StringComparison.OrdinalIgnoreCase)
                        || body.Contains("Error al", StringComparison.OrdinalIgnoreCase)
                        || body.Contains("fallo", StringComparison.OrdinalIgnoreCase);

                if (!err)
                    return (true, "Horas registradas en PO");

                var msg = ExtractErrorMessage(body);
                return (false, string.IsNullOrEmpty(msg) ? "Error al registrar" : msg);
            }
            catch (Exception ex)
            {
                LogService.Error("SubmitTimeEntryAsync: excepcion inesperada", ex);
                return (false, $"Error: {ex.Message}");
            }
        }

        // ═══════════════════════════════════════════════════════════
        // SESSION CHECK
        // ═══════════════════════════════════════════════════════════

        public async Task<bool> CheckSessionAsync()
        {
            try
            {
                var r = await _client.GetAsync(
                    $"{_serverUrl}/registrodehoras.aspx?{DateTime.Today:yyyyMMdd}");
                var u = r.RequestMessage?.RequestUri?.ToString() ?? "";
                var html = await r.Content.ReadAsStringAsync();
                return IsAuthenticatedTimeEntryPage(u, html);
            }
            catch { return false; }
        }

        public void Logout()
        {
            _isLoggedIn = false;
            CurrentUser = null;
            _gxState = "";
            _personaId = 0;
        }

        // ═══════════════════════════════════════════════════════════
        // HELPERS
        // ═══════════════════════════════════════════════════════════

        private static bool IsAuthenticatedTimeEntryPage(string url, string html)
        {
            if (IsLoginPage(url, html))
                return false;

            return url.Contains("registrodehoras", StringComparison.OrdinalIgnoreCase)
                || html.Contains("registrodehoras", StringComparison.OrdinalIgnoreCase)
                || html.Contains("ingresohorasxfecha", StringComparison.OrdinalIgnoreCase)
                || html.Contains("vFECHA", StringComparison.OrdinalIgnoreCase)
                || html.Contains("Fsgridproyectos", StringComparison.OrdinalIgnoreCase)
                || html.Contains("gridproyectos", StringComparison.OrdinalIgnoreCase)
                || html.Contains("vPROJECTID", StringComparison.OrdinalIgnoreCase)
                || html.Contains("vTAREADESCRIPCION", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsLoginPage(string url, string html)
        {
            if (url.Contains("login", StringComparison.OrdinalIgnoreCase)
                || url.Contains("sgplogin", StringComparison.OrdinalIgnoreCase))
                return true;

            return html.Contains("type=\"password\"", StringComparison.OrdinalIgnoreCase)
                && (html.Contains("sgplogin", StringComparison.OrdinalIgnoreCase)
                    || html.Contains("vUSUARIO", StringComparison.OrdinalIgnoreCase)
                    || html.Contains("usuario", StringComparison.OrdinalIgnoreCase));
        }

        private static string BuildLoginFailureMessage(string verifyUrl, string html)
        {
            var title = ExtractTitle(html);
            var detail = title.Length > 0
                ? $" Pagina recibida: {title}."
                : "";

            return $"No pude confirmar el login. URL final: {verifyUrl}.{detail}";
        }

        private static string ExtractTitle(string html)
        {
            var m = Regex.Match(html, @"<title[^>]*>(.*?)</title>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            return m.Success
                ? WebUtility.HtmlDecode(Regex.Replace(m.Groups[1].Value, @"\s+", " ")).Trim()
                : "";
        }

        private void CaptureGxMeta(string html)
        {
            // GXState hidden field
            var m = Regex.Match(html,
                @"(?:name|id)=""GXState""[^>]*value=""([^""]*)""|value=""([^""]+)""[^>]*(?:name|id)=""GXState""",
                RegexOptions.IgnoreCase);
            if (m.Success)
                _gxState = WebUtility.HtmlDecode(Coalesce(m.Groups[1].Value, m.Groups[2].Value));

            // PersonaId
            m = Regex.Match(html,
                @"(?:id|name)=""vPERSONAID""[^>]*value=""(\d+)""|value=""(\d+)""[^>]*(?:id|name)=""vPERSONAID""",
                RegexOptions.IgnoreCase);
            if (m.Success)
                int.TryParse(Coalesce(m.Groups[1].Value, m.Groups[2].Value), out _personaId);
        }

        private void SetReferer(string url)
        {
            _client.DefaultRequestHeaders.Remove("Referer");
            _client.DefaultRequestHeaders.Add("Referer", url);
        }

        /// <summary>
        /// Find the grid input ID for a specific task:
        ///   id="vTAREAID_0001_0002" value="123"
        /// </summary>
        private static string? FindGridCell(string html, string taskId, string projectId)
        {
            var rx = new Regex(
                $@"id=""(vTAREAID_\d{{4}}_\d{{4}})""[^>]*value=""{Regex.Escape(taskId)}""",
                RegexOptions.IgnoreCase);
            var m = rx.Match(html);
            return m.Success ? m.Groups[1].Value : null;
        }

        /// <summary>
        /// Extract every &lt;input&gt; on the page into a dictionary.
        /// Covers hidden fields, text inputs, and GX grid cells.
        /// </summary>
        private static Dictionary<string, string> ExtractAllInputs(string html)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Pattern: any <input> with name or id + value
            var rx = new Regex(
                @"<input[^>]+>", RegexOptions.IgnoreCase);

            foreach (Match m in rx.Matches(html))
            {
                var tag = m.Value;
                var name = AttrVal(tag, "name") ?? AttrVal(tag, "id");
                var val  = AttrVal(tag, "value") ?? "";
                if (name != null && !d.ContainsKey(name))
                    d[name] = WebUtility.HtmlDecode(val);
            }
            return d;
        }

        private static string? AttrVal(string tag, string attr)
        {
            var m = Regex.Match(tag,
                $@"{attr}\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+))",
                RegexOptions.IgnoreCase);
            return m.Success ? Coalesce(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value) : null;
        }

        private static string JsonString(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString() ?? "",
                JsonValueKind.Number => element.ToString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => ""
            };
        }

        private static string? DetectField(string html, string inputType, string[]? hints)
        {
            var rx = new Regex(
                $@"<input[^>]*type=""{inputType}""[^>]*(?:name|id)=""([^""]+)""[^>]*>|<input[^>]*(?:name|id)=""([^""]+)""[^>]*type=""{inputType}""[^>]*>",
                RegexOptions.IgnoreCase);

            foreach (Match m in rx.Matches(html))
            {
                var name = Coalesce(m.Groups[1].Value, m.Groups[2].Value);
                if (name.StartsWith("__") || name.StartsWith("GX", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (hints == null) return name;
                foreach (var h in hints)
                    if (name.Contains(h, StringComparison.OrdinalIgnoreCase))
                        return name;
            }

            // Fallback: for password, return first non-system match
            if (inputType == "password")
            {
                foreach (Match m in rx.Matches(html))
                {
                    var name = Coalesce(m.Groups[1].Value, m.Groups[2].Value);
                    if (!name.StartsWith("__") && !name.StartsWith("GX", StringComparison.OrdinalIgnoreCase))
                        return name;
                }
            }
            return null;
        }

        private static string? DetectSubmitButton(string html)
        {
            var candidates = new[] {
                "LOGIN","btnLogin","btnIngresar","btnEntrar","btnAceptar",
                "BTNLOGIN","BTNINGRESAR","btnlogin","btningresar" };
            foreach (var c in candidates)
                if (html.Contains(c, StringComparison.OrdinalIgnoreCase))
                    return c;
            // Try any submit/button input
            var m = Regex.Match(html,
                @"<input[^>]*type=""(?:submit|button)""[^>]*(?:name|id)=""([^""]+)""",
                RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value : null;
        }

        private static void SetGeneXusEvent(
            Dictionary<string, string> form, string eventName, string eventGridId = "", string eventRowId = "")
        {
            if (!form.TryGetValue("GXState", out var gxState) || string.IsNullOrWhiteSpace(gxState))
                return;

            try
            {
                var state = JsonSerializer.Deserialize<Dictionary<string, object?>>(gxState)
                    ?? new Dictionary<string, object?>();
                state["_EventName"] = eventName;
                state["_EventGridId"] = eventGridId;
                state["_EventRowId"] = eventRowId;
                form["GXState"] = JsonSerializer.Serialize(state);
            }
            catch
            {
                form["GXState"] = Regex.Replace(
                    gxState,
                    @"""_EventName""\s*:\s*""[^""]*""",
                    $@"""_EventName"":""{eventName}""",
                    RegexOptions.IgnoreCase);
            }
        }

        private static string ExtractErrorMessage(string html)
        {
            var patterns = new[]
            {
                @"<span[^>]*class=""[^""]*error[^""]*""[^>]*>([^<]+)</span>",
                @"<div[^>]*class=""[^""]*gx-warning-message[^""]*""[^>]*>([^<]+)</div>",
                @"<div[^>]*class=""[^""]*(?:error|alert)[^""]*""[^>]*>([^<]+)</div>",
                @"lblError[^>]*>([^<]+)<",
                @"lblMensaje[^>]*>([^<]+)<",
                @"""Texto""\s*:\s*""([^""]+)"""
            };
            foreach (var p in patterns)
            {
                var m = Regex.Match(html, p, RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    var msg = WebUtility.HtmlDecode(m.Groups[1].Value).Trim();
                    if (msg.Length > 3) return msg;
                }
            }
            return "";
        }

        private static string Coalesce(params string[] vals)
        {
            foreach (var v in vals)
                if (!string.IsNullOrEmpty(v)) return v;
            return "";
        }

        public void Dispose() => _client.Dispose();
    }
}
