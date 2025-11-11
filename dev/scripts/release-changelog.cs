#!/usr/bin/dotnet run

#:package System.CommandLine@2.0.0-rc.2.25502.107

using System.CommandLine;
using System.Text.RegularExpressions;

const string githubComparePath = "/compare/";
const string githubReleasePath = "/releases/tag/";

var pathToChangeLogArgument = new Argument<FileInfo>("change-log-file-path")
{
    Description = "Path to CHANGELOG.md file"
}.AcceptExistingOnly();

var newVersionArgument = new Argument<string>("new-version")
{
    Description = "Version to release"
};

var keepUnreleasedChangesOption = new Option<bool>("--keep-unreleased-changes")
{
    Description = "Keep unreleased changes in the changelog",
    DefaultValueFactory = _ => false
};

var printChangesToStdOption = new Option<bool>("--print-changes-to-stdout")
{
    Description = "Print result to stdout",
    DefaultValueFactory = _ => true
};

var dryRunOption = new Option<bool>("--dry-run")
{
    Description = "Run script without modify change log file",
    DefaultValueFactory = _ => false
};

var rootCommand = new RootCommand("Update Changelog when release new version");
rootCommand.Arguments.Add(pathToChangeLogArgument);
rootCommand.Arguments.Add(newVersionArgument);
rootCommand.Options.Add(printChangesToStdOption);
rootCommand.Options.Add(dryRunOption);
rootCommand.Options.Add(keepUnreleasedChangesOption);

rootCommand.SetAction(async parseResult =>
{
    var changeLogFileInfo = parseResult.GetValue(pathToChangeLogArgument) ?? throw new Exception("path-to-change-log-file file info is null");
    var newVersion = parseResult.GetValue(newVersionArgument) ?? throw new Exception("new-version is null");
    var printChangesToStd = parseResult.GetValue(printChangesToStdOption);
    var keepUnreleasedChanges = parseResult.GetValue(keepUnreleasedChangesOption);
    var dryRun = parseResult.GetValue(dryRunOption);

    var changeLogFileName = changeLogFileInfo.FullName;

    var date = DateOnly.FromDateTime(DateTime.Now);
    var githubRepoUrl = "https://github.com/olivierlacan/keep-a-changelog";

    var changeLogRaw = await File.ReadAllTextAsync(changeLogFileName);

    var changes = ExtractUnreleaseChanges(changeLogRaw);

    changeLogRaw = UpdateReleasesLink(changeLogRaw, newVersion, githubRepoUrl);
    changeLogRaw = InsertNewVersionHeading(changeLogRaw, newVersion, date);

    if (keepUnreleasedChanges)
    {
        var unreleasedHeadingEndIndex = GetUnreleasedHeadingEndIndex(changeLogRaw);
        changeLogRaw = changeLogRaw.Insert(unreleasedHeadingEndIndex, $"\n\n{changes}");
    }

    if (printChangesToStd)
        Console.Write(changes);
    else
        Console.Write(changeLogRaw);

    if (dryRun)
        return;

    await File.WriteAllTextAsync(changeLogFileName, changeLogRaw);
});

return await rootCommand.Parse(args).InvokeAsync();

static string ExtractUnreleaseChanges(string input)
{
    var unreleasedHeadingEndIndex = GetUnreleasedHeadingEndIndex(input);
    var latestReleaseHeadingStartIndex = GetLastetReleaseHeadingStartIndex(input);

    var changelog = input;
    if (latestReleaseHeadingStartIndex > 0)
    {
        changelog = input.Remove(latestReleaseHeadingStartIndex, input.Length - latestReleaseHeadingStartIndex);
    }
    else
    {
        var unreleasedLinkIndex = GetUnreleasedLinkIndex(changelog);
        changelog = input.Remove(unreleasedLinkIndex, input.Length - unreleasedLinkIndex);
    }

    changelog = changelog.Remove(0, unreleasedHeadingEndIndex);

    return changelog.TrimStart().TrimEnd();
}

static int GetLastetReleaseHeadingStartIndex(string input)
{
    var regex = ReleasesHeadingRegex();
    return regex.Match(input).Groups[0].Index;
}

static int GetUnreleasedLinkIndex(string input)
{
    var regex = UnreleasedLinkRegex();
    return regex.Match(input).Groups[0].Index;
}

static string UpdateReleasesLink(string input, string newVersion, string githubRepoUrl)
{
    var changeLog = input;

    var regex = UnreleasedLinkRegex();
    var unreleasedLinkGroup = regex.Match(changeLog).Groups[0];
    var unreleasedLinkGroupEndIndex = unreleasedLinkGroup.Index + unreleasedLinkGroup.Length;

    var latestVersion = GetLastetVersion(changeLog);

    var compareLink = latestVersion is null ?
        githubRepoUrl + githubReleasePath + $"v{newVersion}" :
        githubRepoUrl + githubComparePath + $"v{latestVersion}...v{newVersion}";

    var unreleasedCompareLink = githubRepoUrl + githubComparePath + $"v{newVersion}...HEAD";

    changeLog = changeLog.Insert(unreleasedLinkGroupEndIndex, $"[{newVersion}]: {compareLink}");
    changeLog = changeLog.Insert(unreleasedLinkGroupEndIndex, $"[unreleased]: {unreleasedCompareLink}\n");

    changeLog = changeLog.Remove(unreleasedLinkGroup.Index, unreleasedLinkGroup.Length);

    return changeLog;
}

static string? GetLastetVersion(string input)
{
    var regex = ReleasesHeadingRegex();
    var matchGroups = regex.Match(input).Groups;
    if (matchGroups.Count == 0)
        return null;

    var captures = matchGroups[1].Captures;

    if (captures.Count == 0)
        return null;

    return captures[0].Value;
}

static string InsertNewVersionHeading(string input, string version, DateOnly date)
{
    var unreleasedHeadingEndIndex = GetUnreleasedHeadingEndIndex(input);

    if (unreleasedHeadingEndIndex <= 0)
        throw new Exception("Unable to locate Unreleased Heading Posistion");

    return input.Insert(unreleasedHeadingEndIndex, $"\n\n## [{version}] - {date:O}");
}

static int GetUnreleasedHeadingEndIndex(string input)
{
    var unreleasedHeadingRegex = UnreleasedHeadingRegex();

    var unreleasedHeadingGroup = unreleasedHeadingRegex.Match(input).Groups[0];
    var unreleasedHeadingEndIndex = unreleasedHeadingGroup.Index + unreleasedHeadingGroup.Length;

    return unreleasedHeadingEndIndex;
}

partial class Program
{
    [GeneratedRegex(@"^## \[Unreleased\]", RegexOptions.Multiline)]
    private static partial Regex UnreleasedHeadingRegex();

    [GeneratedRegex(@"^\[unreleased\]: .+", RegexOptions.Multiline)]
    private static partial Regex UnreleasedLinkRegex();

    [GeneratedRegex(@"^## \[(?<Version>.+?)\] - (?<Date>\d\d\d\d-\d\d-\d\d)", RegexOptions.Multiline)]
    private static partial Regex ReleasesHeadingRegex();
}