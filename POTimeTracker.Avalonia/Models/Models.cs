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
        public string GxRowId { get; set; } = "";
        public override string ToString() => Name;
    }

    public class POTask
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string ProjectId { get; set; } = "";
        public string GxRowId { get; set; } = "";
        public string GxProjectRowId { get; set; } = "";
        public double ExistingHours { get; set; }
        public override string ToString() => ExistingHours > 0 ? $"{Name}  [{ExistingHours:0.0}h]" : Name;
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
    }

    public class LoginCredentials
    {
        public string ServerUrl { get; set; } = "http://po.invenzis.com:8080";
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public bool RememberMe { get; set; } = true;
        public double WeeklyTarget { get; set; } = 40;
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
