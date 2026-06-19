using System;

namespace POTimeTracker.Models
{
    public class JiraConfig
    {
        public string BaseUrl { get; set; } = "";
        public string Email { get; set; } = "";
        public string DefaultProjectKey { get; set; } = "";
        public bool Enabled { get; set; } = false;
    }

    public class JiraProject
    {
        public string Id { get; set; } = "";
        public string Key { get; set; } = "";
        public string Name { get; set; } = "";
        public override string ToString() => $"[{Key}] {Name}";
    }

    public class JiraIssue
    {
        public string Id { get; set; } = "";
        public string Key { get; set; } = "";
        public string Summary { get; set; } = "";
        public string Status { get; set; } = "";
        public string StatusCategory { get; set; } = "";
        public string IssueType { get; set; } = "";
        public string Assignee { get; set; } = "";
        public override string ToString() => $"{Key}: {Summary}";
    }

    public class JiraWorklogEntry
    {
        public string IssueKey       { get; set; } = "";
        public string Summary        { get; set; } = "";
        public string StatusCategory { get; set; } = "";
        public double Hours          { get; set; }
    }

    public class JiraTransition
    {
        public string Id               { get; set; } = "";
        public string Name             { get; set; } = "";
        public string ToStatusName     { get; set; } = "";
        public string ToStatusCategory { get; set; } = "";
        public bool   IsAvailable      { get; set; } = true;
        public bool   HasScreen        { get; set; } = false;
    }
}
