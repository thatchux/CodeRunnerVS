//------------------------------------------------------------------------------
// <copyright file="RunCommand.cs" company="Company">
//   Copyright (c) Company.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------
 
using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

// Disambiguate Task to the TPL Task to avoid collision with Microsoft.VisualStudio.Shell.Task
using Task = System.Threading.Tasks.Task;
// Disambiguate Process to the System.Diagnostics.Process to avoid collision with EnvDTE.Process
using Process = System.Diagnostics.Process;
 
namespace CodeRunner
{
  /// <summary>
  /// Command handler
  /// </summary>
  internal sealed class RunCommand
  {
    /// <summary>
    /// Command ID.
    /// </summary>
    public const int CommandId = 0x0100;
 
    /// <summary>
    /// Command menu group (command set GUID).
    /// </summary>
    public static readonly Guid CommandSet = new Guid("b62d762c-0f40-4249-94cb-7a09ca719bda");
 
    /// <summary>
    /// VS Package that provides this command, not null.
    /// </summary>
    private readonly Package package;
 
    /// <summary>
    /// Initializes a new instance of the <see cref="RunCommand"/> class.
    /// Adds our command handlers for menu (commands must exist in the command table file)
    /// </summary>
    /// <param name="package">Owner package, not null.</param>
    private RunCommand(Package package)
    {
      if (package == null)
      {
        throw new ArgumentNullException("package");
      }
 
      this.package = package;
 
      OleMenuCommandService commandService = this.ServiceProvider.GetService(typeof(IMenuCommandService)) as OleMenuCommandService;
      if (commandService != null)
      {
        var menuCommandID = new CommandID(CommandSet, CommandId);
        var menuItem = new MenuCommand(this.Run, menuCommandID);
        commandService.AddCommand(menuItem);
      }
    }
 
    /// <summary>
    /// Gets the instance of the command.
    /// </summary>
    public static RunCommand Instance
    {
      get;
      private set;
    }
 
    /// <summary>
    /// Gets the service provider from the owner package.
    /// </summary>
    private IServiceProvider ServiceProvider
    {
      get
      {
        return this.package;
      }
    }
 
    /// <summary>
    /// Initializes the singleton instance of the command.
    /// </summary>
    /// <param name="package">Owner package, not null.</param>
    public static void Initialize(Package package)
    {
      Instance = new RunCommand(package);
    }
 
    /// <summary>
    /// Helper to get or create the Output Window pane named "Code Runner".
    /// </summary>
    private OutputWindowPane GetOutputPane(DTE2 dte)
    {
      var ow = dte.ToolWindows.OutputWindow;
      try
      {
        return ow.OutputWindowPanes.Item("Code Runner");
      }
      catch
      {
        ow.OutputWindowPanes.Add("Code Runner");
        return ow.OutputWindowPanes.Item("Code Runner");
      }
    }
 
    /// <summary>
    /// Determines whether an executable is available on PATH by checking PATH entries for the specified filename.
    /// </summary>
    private static bool IsExecutableOnPath(string exeFileName)
    {
      if (string.IsNullOrEmpty(exeFileName))
        return false;
 
      // Ensure extension (on Windows)
      string fileName = exeFileName;
      if (Path.GetExtension(fileName) == string.Empty)
      {
        fileName = fileName + ".exe";
      }
 
      var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
      var paths = pathEnv.Split(Path.PathSeparator);
      foreach (var p in paths)
      {
        try
        {
          var candidate = Path.Combine(p, fileName);
          if (File.Exists(candidate))
            return true;
        }
        catch
        {
          // ignore invalid paths
        }
      }
 
      return false;
    }
 
    /// <summary>
    /// This function is the callback used to execute the command when the menu item is clicked.
    /// </summary>
    private void Run(object sender, EventArgs e)
    {
      var dte = ServiceProvider.GetService(typeof(DTE)) as DTE2;
      string selectedPath = null;
 
      if (dte.ActiveWindow.Type == vsWindowType.vsWindowTypeDocument)
      {
        selectedPath = dte.ActiveDocument.FullName;
      }
      else if (dte.SelectedItems.Count == 1)
      {
        selectedPath = dte.SelectedItems?.Item(1)?.ProjectItem?.FileNames[0];
      }
 
      if (string.IsNullOrEmpty(selectedPath))
        return;
 
      string ext = Path.GetExtension(selectedPath).ToLowerInvariant();
 
      // Telemetry call (no-op if key not configured)
      AppInsightsClient.trackEvent(ext);
 
      // Default mappings (without .ps1 — .ps1 handled specially when absent)
      var defaultMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
      {
        {".js", "node"},
        {".php", "php"},
        {".py", "python"},
        {".pl", "perl"},
        {".rb", "ruby"},
        {".go", "go run"},
        {".lua", "lua"},
        {".groovy", "groovy"},
        {".scala", "scala"},
        {".vbs", "cscript //Nologo"}
      };
 
      // Load user-provided mappings from Tools->Options page
      var extMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
      try
      {
        var options = this.package.GetDialogPage(typeof(RunOptionsPage)) as RunOptionsPage;
        if (options != null)
        {
          foreach (var kv in options.GetMappingsDictionary())
          {
            extMapping[kv.Key] = kv.Value;
          }
        }
      }
      catch
      {
        // ignore options load errors and fallback to defaults
      }
 
      // Merge defaults for keys not provided by user
      foreach (var kv in defaultMapping)
      {
        if (!extMapping.ContainsKey(kv.Key))
          extMapping[kv.Key] = kv.Value;
      }
 
      string mapping = null;
      if (ext == ".ps1")
      {
        if (extMapping.ContainsKey(".ps1"))
        {
          mapping = extMapping[".ps1"];
        }
        else
        {
          if (IsExecutableOnPath("pwsh"))
            mapping = "pwsh -NoProfile -ExecutionPolicy Bypass -File";
          else
            mapping = "powershell -NoProfile -ExecutionPolicy Bypass -File";
        }
      }
      else
      {
        if (!extMapping.ContainsKey(ext))
        {
          VsShellUtilities.ShowMessageBox(
            this.ServiceProvider,
            $"The file type \"{ext}\" is not supported.",
            "File type not supported",
            OLEMSGICON.OLEMSGICON_INFO,
            OLEMSGBUTTON.OLEMSGBUTTON_OK,
            OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
          return;
        }
 
        mapping = extMapping[ext];
      }
 
      // split mapping into executable and arguments (first space)
      string exe;
      string mappedArgs;
      int idx = mapping.IndexOf(' ');
      if (idx >= 0)
      {
        exe = mapping.Substring(0, idx);
        mappedArgs = mapping.Substring(idx + 1);
      }
      else
      {
        exe = mapping;
        mappedArgs = string.Empty;
      }
 
      // Use the absolute path to the file (quoted). Do NOT override WorkingDirectory so the process inherits the host/terminal cwd.
      string arguments = string.IsNullOrWhiteSpace(mappedArgs) ? $"\"{selectedPath}\"" : $"{mappedArgs} \"{selectedPath}\"";
 
      var startInfo = new ProcessStartInfo
      {
        FileName = exe,
        Arguments = arguments,
        // Do not set WorkingDirectory -> inherit whatever the host/terminal is set to
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
      };
 
      var pane = GetOutputPane(dte);
      // Write a header line (show current Environment.CurrentDirectory since WorkingDirectory is not forced)
      ThreadHelper.Generic.BeginInvoke(() => pane.OutputString($"--- Code Runner: `{exe} {arguments}` (cwd: {Environment.CurrentDirectory}) ---{Environment.NewLine}"));
 
      // Launch and stream output on a background task
      Task.Run(() =>
      {
        try
        {
          using (var process = new Process() { StartInfo = startInfo, EnableRaisingEvents = true })
          {
            process.OutputDataReceived += (s, ea) =>
            {
              if (ea.Data != null)
              {
                ThreadHelper.Generic.BeginInvoke(() => pane.OutputString(ea.Data + Environment.NewLine));
              }
            };
            process.ErrorDataReceived += (s, ea) =>
            {
              if (ea.Data != null)
              {
                ThreadHelper.Generic.BeginInvoke(() => pane.OutputString("[ERR] " + ea.Data + Environment.NewLine));
              }
            };
 
            bool started = process.Start();
            if (!started)
            {
              ThreadHelper.Generic.BeginInvoke(() =>
                VsShellUtilities.ShowMessageBox(
                  this.ServiceProvider,
                  $"Failed to start process: {exe}",
                  "Run Code Error",
                  OLEMSGICON.OLEMSGICON_CRITICAL,
                  OLEMSGBUTTON.OLEMSGBUTTON_OK,
                  OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST));
              return;
            }
 
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
 
            process.WaitForExit();
 
            ThreadHelper.Generic.BeginInvoke(() => pane.OutputString($"--- Process exited with code {process.ExitCode} ---{Environment.NewLine}"));
          }
        }
        catch (Exception ex)
        {
          ThreadHelper.Generic.BeginInvoke(() =>
          {
            VsShellUtilities.ShowMessageBox(
              this.ServiceProvider,
              $"Failed to start or monitor process: {ex.Message}",
              "Run Code Error",
              OLEMSGICON.OLEMSGICON_CRITICAL,
              OLEMSGBUTTON.OLEMSGBUTTON_OK,
              OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
          });
        }
      });
    }
  }
}
