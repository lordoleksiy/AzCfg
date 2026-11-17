using AzCfg.Cli;

// Debug harness for the azcfg CLI: F5 here, then type commands exactly as after "azcfg".
//   azcfg> pull --json      run a command in-process (breakpoints in Cli/Core/Infrastructure work)
//   azcfg> cd <path>        change the working directory (the project azcfg should operate on)
//   azcfg> exit
// Arguments given on startup run once and exit. Set the start directory via the first
// argument "--cwd <path>" or the AZCFG_DEV_CWD environment variable.
var startArgs = args;
var cwd = Environment.GetEnvironmentVariable("AZCFG_DEV_CWD");
if (startArgs is ["--cwd", var path, .. var rest])
{
    cwd = path;
    startArgs = rest;
}

if (!string.IsNullOrWhiteSpace(cwd))
{
    if (Directory.Exists(cwd))
    {
        Directory.SetCurrentDirectory(cwd);
    }
    else
    {
        Console.Error.WriteLine($"Start directory not found, staying in {Directory.GetCurrentDirectory()}: {cwd}");
    }
}

if (startArgs.Length > 0)
{
    return await CliApp.RunAsync(startArgs);
}

while (true)
{
    Console.Write($"{Directory.GetCurrentDirectory()}{Environment.NewLine}azcfg> ");
    var line = Console.ReadLine();
    if (line is null || line.Trim() is "exit" or "quit")
    {
        return 0;
    }

    var tokens = Tokenize(line);
    if (tokens.Count == 0)
    {
        continue;
    }

    if (tokens[0] == "cd")
    {
        try
        {
            Directory.SetCurrentDirectory(tokens.Count > 1 ? tokens[1] : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Console.Error.WriteLine(ex.Message);
        }

        continue;
    }

    var exitCode = await CliApp.RunAsync([.. tokens]);
    Console.WriteLine($"[exit {exitCode}]");
}

static List<string> Tokenize(string line)
{
    var tokens = new List<string>();
    var current = new System.Text.StringBuilder();
    var quoted = false;
    var has = false;
    foreach (var ch in line)
    {
        if (ch == '"')
        {
            quoted = !quoted;
            has = true;
        }
        else if (char.IsWhiteSpace(ch) && !quoted)
        {
            if (has)
            {
                tokens.Add(current.ToString());
                current.Clear();
                has = false;
            }
        }
        else
        {
            current.Append(ch);
            has = true;
        }
    }

    if (has)
    {
        tokens.Add(current.ToString());
    }

    return tokens;
}
