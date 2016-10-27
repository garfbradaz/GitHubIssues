﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using BugReport.Query;
using BugReport.DataModel;
using System.Text.RegularExpressions;

namespace BugReport.Reports
{
    public class AlertsReport
    {
        public AlertsReport(string alertsXmlFileName)
        {
            ConfigLoader loader = new ConfigLoader();
            Alerts = loader.Load(alertsXmlFileName);
        }

        public void SendEmails(IssueCollection issuesStart, IssueCollection issuesEnd, string htmlTemplateFileName)
        {
            string htmlTemplate = File.ReadAllText(htmlTemplateFileName);

            SmtpClient smtpClient = new SmtpClient("smtphost");
            smtpClient.UseDefaultCredentials = true;

            foreach (Alert alert in Alerts)
            {
                alert.Query.Validate(issuesEnd);
                IEnumerable<Issue> queryStart = alert.Query.Evaluate(issuesStart);
                IEnumerable<Issue> queryEnd = alert.Query.Evaluate(issuesEnd);

                IEnumerable<Issue> goneIssues = queryStart.Except(queryEnd);
                IEnumerable<Issue> newIssues = queryEnd.Except(queryStart);

                if (!goneIssues.Any() && !newIssues.Any())
                {
                    continue;
                }

                MailMessage message = new MailMessage();
                message.From = new MailAddress(Environment.UserName + "@microsoft.com");
                foreach (Alert.User user in alert.Owners)
                {
                    message.To.Add(user.EmailAddress);
                }
                foreach (Alert.User user in alert.CCs)
                {
                    message.To.Add(user.EmailAddress);
                }
                message.Subject = "Test email from my tool";
                message.IsBodyHtml = true;

                //string text = htmlTemplate.Replace

                //htmlText.Replace()

                //StringBuilder text = new StringBuilder(htmlTemplate);
                string text = htmlTemplate;
                text = text.Replace("%ALERT_NAME%", alert.Name);

                if (!goneIssues.Any() || !newIssues.Any())
                {
                    Regex regex = new Regex("%ALL_ISSUES_START%.*%ALL_ISSUES_END%");
                    text = regex.Replace(text, "");

                    if (!goneIssues.Any())
                    {
                        regex = new Regex("%GONE_ISSUES_START%.*%GONE_ISSUES_END%");
                        text = regex.Replace(text, "");
                    }
                    if (!newIssues.Any())
                    {
                        regex = new Regex("%NEW_ISSUES_START%.*%NEW_ISSUES_END%");
                        text = regex.Replace(text, "");
                    }
                }
                text = text.Replace("%ALL_ISSUES_START%", "");
                text = text.Replace("%ALL_ISSUES_END%", "");
                text = text.Replace("%GONE_ISSUES_START%", "");
                text = text.Replace("%GONE_ISSUES_END%", "");
                text = text.Replace("%NEW_ISSUES_START%", "");
                text = text.Replace("%NEW_ISSUES_END%", "");

                text = text.Replace("%ALL_ISSUES_LINK%", GitHubQuery.GetHyperLink(newIssues.Concat(goneIssues)));
                text = text.Replace("%ALL_ISSUES_COUNT%", (goneIssues.Count() + newIssues.Count()).ToString());
                text = text.Replace("%GONE_ISSUES_LINK%", GitHubQuery.GetHyperLink(goneIssues));
                text = text.Replace("%GONE_ISSUES_COUNT%", goneIssues.Count().ToString());
                text = text.Replace("%NEW_ISSUES_LINK%", GitHubQuery.GetHyperLink(newIssues));
                text = text.Replace("%NEW_ISSUES_COUNT%", newIssues.Count().ToString());

                IEnumerable<IssueEntry> newIssueEntries = newIssues.Select(issue => new IssueEntry(issue));
                text = text.Replace("%NEW_ISSUES_TABLE%", FormatIssueTable(newIssueEntries));
                IEnumerable<IssueEntry> goneIssueEntries = goneIssues.Select(issue =>
                    {
                        Issue newIssue = issuesEnd.GetIssue(issue.Number);
                        if (newIssue == null)
                        {   // Closed issue
                            return new IssueEntry(issue, "Closed");
                        }
                        return new IssueEntry(newIssue);
                    });
                text = text.Replace("%GONE_ISSUES_TABLE%", FormatIssueTable(goneIssueEntries));

                message.Body = text;

                try
                {
                    smtpClient.Send(message);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("ERROR sending alert {0}", alert.Name);
                    Console.WriteLine(ex);
                }

                Console.WriteLine("Alert {0}", alert.Name);
                Console.WriteLine("    Onwers:");
                foreach (Alert.User user in alert.Owners)
                {
                    Console.WriteLine("        {0} - {1}", user.Name, user.EmailAddress);
                }
                Console.WriteLine("    CC:");
                foreach (Alert.User user in alert.CCs)
                {
                    Console.WriteLine("        {0} - {1}", user.Name, user.EmailAddress);
                }
                Console.Write(text.Replace("<br/>", ""));
            }
        }

        struct IssueEntry
        {
            public string IssueId;
            public string Title;
            public string LabelsText;
            public string AssignedToText;

            public IssueEntry(Issue issue, string assignedToOverride = null)
            {
                string idPrefix = "";
                if (issue.IsPullRequest)
                {
                    idPrefix = "PR ";
                }
                IssueId = string.Format("{0}#<a href=\"{1}\">{2}</a>", idPrefix, issue.HtmlUrl, issue.Number);
                Title = issue.Title;
                LabelsText = string.Join(", ", issue.Labels.Select(l => l.Name));
                if (assignedToOverride != null)
                {
                    AssignedToText = assignedToOverride;
                }
                else if (issue.Assignee != null)
                {
                    AssignedToText = string.Format("<a href=\"{0}\">@{1}</a>", issue.Assignee.HtmlUrl, issue.Assignee.Login);
                }
                else
                {
                    AssignedToText = "";
                }
            }
        }

        string FormatIssueTable(IEnumerable<IssueEntry> issues)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine("<table>");
            text.AppendLine("  <tr>");
            text.AppendLine("    <th>Issue #</th>");
            text.AppendLine("    <th>Title</th>");
            text.AppendLine("    <th>Assigned To</th>");
            text.AppendLine("  </tr>");
            foreach (IssueEntry issue in issues)
            {
                text.AppendLine(  "  <tr>");
                text.AppendFormat("    <td>{0}</td>", issue.IssueId).AppendLine();
                text.AppendLine(  "    <td>");
                text.AppendFormat("      {0}", issue.Title).AppendLine();
                if (issue.LabelsText != null)
                {
                    text.AppendFormat("      <ul><li>{0}</li></ul>", issue.LabelsText).AppendLine();
                }
                text.AppendLine(  "    </td>");
                text.AppendFormat("    <td>{0}</td>", issue.AssignedToText).AppendLine();
                text.AppendLine(  "  </tr>");
            }
            text.AppendLine("</table>");

            return text.ToString();
        }

        IEnumerable<Alert> Alerts = new List<Alert>();

        private class ConfigLoader
        {
            List<Alert.User> Users = new List<Alert.User>();

            public IEnumerable<Alert> Load(string alertsXmlFileName)
            {
                XElement root = XElement.Load(alertsXmlFileName);

                foreach (XElement usersNode in root.Descendants("users"))
                {
                    string defaultEmailServer = null;
                    XAttribute defaultEmailServerAttribute = usersNode.Attribute("default-email-server");
                    if (defaultEmailServerAttribute != null)
                    {
                        defaultEmailServer = defaultEmailServerAttribute.Value;
                        if (!defaultEmailServer.Contains('@'))
                        {
                            defaultEmailServer = "@" + defaultEmailServer;
                        }
                    }

                    foreach (XElement userNode in usersNode.Descendants("user"))
                    {
                        string name = userNode.Attribute("name").Value;
                        string emailAlias = userNode.Attribute("alias").Value;
                        string gitHubLogin = userNode.Attribute("github").Value;

                        if (!gitHubLogin.StartsWith("@"))
                        {
                            throw new InvalidDataException("GitHub login expected to start with @: " + gitHubLogin);
                        }
                        if (emailAlias.StartsWith("@"))
                        {
                            throw new InvalidDataException("Alias cannot start with @: " + emailAlias);
                        }

                        if (FindUser(gitHubLogin) != null)
                        {
                            throw new InvalidDataException("Duplicate user defined with GitHub login: " + gitHubLogin);
                        }
                        if (FindUser(emailAlias) != null)
                        {
                            throw new InvalidDataException("Duplicate user defined with alias: " + emailAlias);
                        }

                        string email;
                        if (emailAlias.Contains('@'))
                        {
                            email = emailAlias;
                            emailAlias = null;
                        }
                        else
                        {
                            email = emailAlias + defaultEmailServer;
                        }

                        Users.Add(new Alert.User(name, email, emailAlias, gitHubLogin));
                    }
                }

                foreach (XElement alertsNode in root.Descendants("alerts"))
                {
                    foreach (XElement alertNode in alertsNode.Descendants("alert"))
                    {
                        string alertName = alertNode.Attribute("name").Value;

                        string query = alertNode.Descendants("query").First().Value;
                        IEnumerable<Alert.User> owners = alertNode.Descendants("owner").Select(e => FindUserOrThrow(e.Value));
                        IEnumerable<Alert.User> ccUsers = alertNode.Descendants("cc").Select(e => FindUserOrThrow(e.Value));

                        Alert alert;
                        try
                        {
                            alert = new Alert(alertName, query, owners, ccUsers);
                        }
                        catch (InvalidQueryException ex)
                        {
                            throw new InvalidDataException("Invalid query in alert: " + alertName, ex);
                        }
                        yield return alert;
                    }
                }
            }

            Alert.User FindUser(string id)
            {
                foreach (Alert.User user in Users)
                {
                    if ((user.EmailAlias == id) || (user.GitHubLogin == id) || (user.EmailAddress == id))
                    {
                        return user;
                    }
                }
                return null;
            }
            Alert.User FindUserOrThrow(string id)
            {
                Alert.User user = FindUser(id);
                if (user == null)
                {
                    throw new InvalidDataException("Cannot find user: " + id);
                }
                return user;
            }
        }
    }
}
