using System;
using System.Collections.Generic;

namespace POTimeTracker.Models
{
    public class POProject
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Color { get; set; } = "#6366F1";
        public List<POTask> Tasks { get; set; } = new();

        /// <summary>GeneXus grid row identifier (e.g. "0001")</summary>
        public string GxRowId { get; set; } = "";

        public override string ToString() => Name;
    }

    public class POTask
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string ProjectId { get; set; } = "";

        /// <summary>GX task grid row (e.g. "0002")</summary>
        public string GxRowId { get; set; } = "";
        /// <summary>GX parent project grid row (e.g. "0001")</summary>
        public string GxProjectRowId { get; set; } = "";

        /// <summary>Hours already registered for this task today</summary>
        public double ExistingHours { get; set; }

        public override string ToString() =>
            ExistingHours > 0 ? $"{Name}  [{ExistingHours:0.0}h]" : Name;
    }

    public class TimeEntry
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public DateTime Date { get; set; }
        public string ProjectId { get; set; } = "";
        public string ProjectName { get; set; } = "";
        public string ProjectColor { get; set; } = "#6366F1";
        public string TaskId { get; set; } = "";
        public string TaskName { get; set; } = "";
        public string GxTaskRowId { get; set; } = "";
        public string GxProjectRowId { get; set; } = "";
        public double Hours { get; set; }
        public string Notes { get; set; } = "";
        public bool Synced { get; set; }

        /// <summary>Optional Jira issue key linked to this entry (e.g. "PROJ-123").</summary>
        public string JiraIssueKey { get; set; } = "";
        /// <summary>True if hours were also logged to Jira.</summary>
        public bool JiraSynced { get; set; }
    }

    public class LoginCredentials
    {
        public string ServerUrl { get; set; } = "http://po.invenzis.com:8080";
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public bool RememberMe { get; set; } = true;
        public double WeeklyTarget { get; set; } = 40;
        public int ReminderHour { get; set; } = 17;
        public int ReminderMinute { get; set; } = 15;
        public bool ReminderOnSaturday { get; set; } = false;
        public bool ReminderOnSunday { get; set; } = false;
        public double ReloginIntervalHours { get; set; } = 3.0;
        public bool StartDateAsToday { get; set; } = true;
    }

    public class WeekDay
    {
        public DateTime Date { get; set; }
        public string DayLabel { get; set; } = "";
        public int DayNumber { get; set; }
        public double Hours { get; set; }
        public bool IsActive { get; set; }
        public bool HasHours => Hours > 0;
    }
}
